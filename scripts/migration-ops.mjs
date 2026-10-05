import fs from 'node:fs';
import path from 'node:path';
import { spawnSync } from 'node:child_process';

/**
 * Migration Operations & Safety Guardrails
 * Enforces:
 * 1. Safe, non-destructive migrations under expand-contract rules.
 * 2. Blocking of DROP TABLE, DROP COLUMN, TRUNCATE, and uncontrolled renames.
 * 3. Connection string masking in all logs and outputs.
 * 4. Separate operational steps for staging/production migrations (never on API startup).
 * 5. Local development migration application with environment guardrails.
 */

export const FORBIDDEN_DESTRUCTIVE_PATTERNS = [
  { pattern: /\bDROP\s+TABLE\b/i, description: 'DROP TABLE is destructive and breaks active slot' },
  { pattern: /\bDROP\s+COLUMN\b/i, description: 'DROP COLUMN breaks active slot backward compatibility' },
  { pattern: /\bALTER\s+TABLE\s+.*\bDROP\s+(?!CONSTRAINT\b)/i, description: 'ALTER TABLE ... DROP is prohibited before cutover' },
  { pattern: /\bRENAME\s+COLUMN\b/i, description: 'RENAME COLUMN is prohibited; use expand (add new) + contract' },
  { pattern: /\bTRUNCATE\b/i, description: 'TRUNCATE is destructive' },
  { pattern: /\bADD\s+COLUMN\s+.*\bNOT\s+NULL\b(?!\s+DEFAULT)/i, description: 'ADD COLUMN NOT NULL without DEFAULT breaks concurrent inserts' },
];

/**
 * Narrow allowlist of approved foreign key replacement operations.
 * Each entry requires verified replacement constraint in the same migration.
 */
export const ALLOWED_CONSTRAINT_REPLACEMENTS = [
  {
    droppedConstraint: 'fk_branch_item_availabilities_item_variants_variant_id',
    replacementConstraint: 'fk_branch_item_availabilities_variants_tenant_branch_variant',
    reason: 'Replaced legacy single-column FK with composite tenant/branch FK for cross-branch referential integrity (Phase 5.5)',
  },
  {
    droppedConstraint: 'fk_branch_item_availabilities_menu_items_item_id',
    replacementConstraint: 'fk_branch_item_availabilities_menu_items_tenant_branch_item',
    reason: 'Replaced legacy single-column FK with composite tenant/branch FK for cross-branch referential integrity (Phase 5.5)',
  },
  {
    droppedConstraint: 'fk_item_variants_menu_items_menu_item_id',
    replacementConstraint: 'fk_item_variants_menu_items_tenant_branch_item',
    reason: 'Replaced legacy single-column FK with composite tenant/branch FK for cross-branch referential integrity (Phase 5.5)',
  },
  {
    droppedConstraint: 'fk_item_variants_menus_tenant_id_menu_id',
    replacementConstraint: 'fk_item_variants_menus_tenant_branch_menu',
    reason: 'Replaced legacy tenant FK with composite tenant/branch FK for cross-branch referential integrity (Phase 5.5)',
  },
  {
    droppedConstraint: 'fk_menu_categories_menus_tenant_id_menu_id',
    replacementConstraint: 'fk_menu_categories_menus_tenant_branch_menu',
    reason: 'Replaced legacy tenant FK with composite tenant/branch FK for cross-branch referential integrity (Phase 5.5)',
  },
  {
    droppedConstraint: 'fk_item_modifier_assignments_menu_items_item_id',
    replacementConstraint: 'fk_item_modifier_assignments_menu_items_tenant_branch_item',
    reason: 'Replaced legacy single-column FK with composite tenant/branch FK for cross-branch referential integrity (Phase 5.5)',
  },
  {
    droppedConstraint: 'fk_item_modifier_assignments_modifier_groups_group_id',
    replacementConstraint: 'fk_item_modifier_assignments_groups_tenant_branch_group',
    reason: 'Replaced legacy single-column FK with composite tenant/branch FK for cross-branch referential integrity (Phase 5.5)',
  },
  {
    droppedConstraint: 'fk_menu_items_categories_tenant_id_menu_id_category_id',
    replacementConstraint: 'fk_menu_items_categories_tenant_branch_menu_cat',
    reason: 'Replaced legacy FK with composite tenant/branch FK for cross-branch referential integrity (Phase 5.5)',
  },
  {
    droppedConstraint: 'fk_menu_items_menus_tenant_id_menu_id',
    replacementConstraint: 'fk_menu_items_menus_tenant_branch_menu',
    reason: 'Replaced legacy tenant FK with composite tenant/branch FK for cross-branch referential integrity (Phase 5.5)',
  },
  {
    droppedConstraint: 'fk_menu_items_preparation_stations_station_id',
    replacementConstraint: 'fk_menu_items_prep_stations_tenant_branch_station',
    reason: 'Replaced legacy single-column FK with composite tenant/branch FK for cross-branch referential integrity (Phase 5.5)',
  },
  {
    droppedConstraint: 'fk_modifier_options_modifier_groups_group_id',
    replacementConstraint: 'fk_modifier_options_modifier_groups_tenant_branch_group',
    reason: 'Replaced legacy single-column FK with composite tenant/branch FK for cross-branch referential integrity (Phase 5.5)',
  },
  {
    droppedConstraint: 'AK_menus_tenant_id_id',
    replacementConstraint: 'AK_menus_tenant_id_branch_id_id',
    reason: 'Replaced legacy 2-column alternate key with 3-column composite alternate key for branch-scoped menu FKs (Phase 5.5)',
  },
  {
    droppedConstraint: 'fk_menu_items_prep_stations_tenant_branch_station',
    replacementConstraint: 'fk_menu_items_prep_stations_tenant_branch_station',
    reason: 'Replaced SetNull composite FK with Restrict delete behavior for preparation station constraint hardening (Phase 5 closing)',
  },
];

export const ALLOWED_FK_REPLACEMENTS = ALLOWED_CONSTRAINT_REPLACEMENTS;

export function extractUpMethodContent(content) {
  if (typeof content !== 'string') return '';
  const upMatch = content.match(/protected\s+override\s+void\s+Up\s*\(\s*MigrationBuilder\s+\w+\s*\)[\s\S]*?(?=protected\s+override\s+void\s+Down|\}\s*\}\s*$)/);
  return upMatch ? upMatch[0] : content;
}

export function validateConstraintSafety(rawContent, options = {}) {
  const violations = [];
  if (!rawContent || typeof rawContent !== 'string') return violations;

  const content = options.isCSharp ? extractUpMethodContent(rawContent) : rawContent;

  // Strict blocking of Primary Key, Unique, and Check constraint drops
  const pkDropRegex = /\b(?:DROP\s+PRIMARY\s+KEY|DROP\s+CONSTRAINT\s+(?:IF\s+EXISTS\s+)?["']?(?:pk_[a-zA-Z0-9_]*|[a-zA-Z0-9_]*_pkey)["']?)\b/i;
  if (pkDropRegex.test(content) || /migrationBuilder\.DropPrimaryKey\b/i.test(content)) {
    violations.push('DROP PRIMARY KEY/CONSTRAINT is strictly prohibited before cutover');
  }

  const uqDropRegex = /\b(?:DROP\s+CONSTRAINT\s+(?:IF\s+EXISTS\s+)?["']?(?:uq_[a-zA-Z0-9_]*|[a-zA-Z0-9_]*_key)["']?)\b/i;
  if (uqDropRegex.test(content) || /migrationBuilder\.DropUniqueConstraint\b/i.test(content)) {
    const uqMatch = content.match(/DropUniqueConstraint\s*\(\s*name:\s*["']([^"']+)["']/i) ||
                    content.match(/DROP\s+CONSTRAINT\s+(?:IF\s+EXISTS\s+)?["']?([^"'\s;]+)["']?/i);
    const uqName = uqMatch ? uqMatch[1] : '';
    const isAllowlisted = ALLOWED_CONSTRAINT_REPLACEMENTS.some(r => r.droppedConstraint.toLowerCase() === uqName.toLowerCase());
    if (!isAllowlisted) {
      violations.push('DROP UNIQUE CONSTRAINT is strictly prohibited before cutover');
    }
  }

  const ckDropRegex = /\b(?:DROP\s+CONSTRAINT\s+(?:IF\s+EXISTS\s+)?["']?(?:ck_[a-zA-Z0-9_]*|[a-zA-Z0-9_]*_check)["']?)\b/i;
  if (ckDropRegex.test(content) || /migrationBuilder\.DropCheckConstraint\b/i.test(content)) {
    violations.push('DROP CHECK CONSTRAINT is strictly prohibited before cutover');
  }

  // Extract all DROP CONSTRAINT / DropForeignKey / DropUniqueConstraint statements
  const sqlDropConstraintRegex = /ALTER\s+TABLE\s+(?:ONLY\s+)?(?:[a-zA-Z0-9_]+\.)?[a-zA-Z0-9_]+\s+DROP\s+CONSTRAINT\s+(?:IF\s+EXISTS\s+)?["']?([a-zA-Z0-9_]+)["']?/gi;
  const csDropFkRegex = /migrationBuilder\.DropForeignKey\s*\(\s*name:\s*["']([a-zA-Z0-9_]+)["']/gi;
  const csDropUqRegex = /migrationBuilder\.DropUniqueConstraint\s*\(\s*name:\s*["']([a-zA-Z0-9_]+)["']/gi;

  const droppedNames = [];
  let match;
  while ((match = sqlDropConstraintRegex.exec(content)) !== null) {
    droppedNames.push(match[1]);
  }
  while ((match = csDropFkRegex.exec(content)) !== null) {
    droppedNames.push(match[1]);
  }
  while ((match = csDropUqRegex.exec(content)) !== null) {
    droppedNames.push(match[1]);
  }

  for (const name of droppedNames) {
    const lowerName = name.toLowerCase();
    if (lowerName.startsWith('pk_') || lowerName.endsWith('_pkey') ||
        lowerName.startsWith('ck_') || lowerName.endsWith('_check')) {
      continue;
    }

    const allowEntry = ALLOWED_CONSTRAINT_REPLACEMENTS.find(r => r.droppedConstraint.toLowerCase() === lowerName);
    if (!allowEntry) {
      violations.push(`Dropping constraint '${name}' is prohibited before cutover (unauthorized constraint drop)`);
      continue;
    }

    const replacement = allowEntry.replacementConstraint.toLowerCase();
    const hasSqlReplacement = new RegExp(`ADD\\s+(?:CONSTRAINT\\s+)?["']?${replacement}["']?`, 'i').test(content);
    const hasCsReplacement = new RegExp(`(?:AddForeignKey|AddUniqueConstraint|name:)\\s*\\(?\\s*["']?${replacement}["']?`, 'i').test(content) ||
                             new RegExp(`name:\\s*["']${replacement}["']`, 'i').test(content);

    if (!hasSqlReplacement && !hasCsReplacement) {
      violations.push(`Dropping constraint '${name}' requires replacement '${allowEntry.replacementConstraint}' in the same migration, but replacement was not found.`);
    }
  }

  return violations;
}

/**
 * Masks passwords and secrets from connection strings before printing to logs.
 */
export function maskConnectionString(connStr) {
  if (!connStr || typeof connStr !== 'string') return '';

  // Mask URI format: postgresql://user:pass@host:5432/db
  let masked = connStr.replace(
    /(postgres(?:ql)?:\/\/[^:]+:)([^@]+)(@)/gi,
    '$1***$3'
  );

  // Mask ADO.NET format: Password=...; or Pwd=...;
  masked = masked.replace(
    /(Password|Pwd)\s*=\s*[^;]+/gi,
    '$1=***'
  );

  return masked;
}

/**
 * Validates migration SQL content against destructive patterns.
 */
export function validateMigrationSql(sqlContent, options = {}) {
  const violations = [];
  if (!sqlContent || typeof sqlContent !== 'string') return violations;

  const content = options.isCSharp ? extractUpMethodContent(sqlContent) : sqlContent;

  const allowDestructive = options.allowDestructive === true &&
    process.env.ALLOW_DESTRUCTIVE_MIGRATION === 'true';

  for (const { pattern, description } of FORBIDDEN_DESTRUCTIVE_PATTERNS) {
    if (pattern.test(content)) {
      if (allowDestructive) {
        violations.push({ description: `[OVERRIDDEN] ${description}`, isOverridden: true });
      } else {
        violations.push({ description, isOverridden: false });
      }
    }
  }

  const constraintViolations = validateConstraintSafety(sqlContent, options);
  for (const desc of constraintViolations) {
    violations.push({ description: desc, isOverridden: false });
  }

  return violations;
}

/**
 * Checks expand-contract compatibility between Blue and Green deployment slots.
 * Ensures schema changes are additive and backward-compatible with the running slot.
 */
export function checkExpandContractCompatibility(sqlContent) {
  const violations = validateMigrationSql(sqlContent, { allowDestructive: false });
  const blockingViolations = violations.filter(v => !v.isOverridden);

  return {
    isCompatible: blockingViolations.length === 0,
    violations: blockingViolations.map(v => v.description),
  };
}

/**
 * Discovers migration SQL and C# files.
 */
export function findMigrationFiles(rootDir = process.cwd()) {
  const searchDirs = [
    path.join(rootDir, 'deploy/migrations'),
    path.join(rootDir, 'packages/infrastructure/Persistence/Migrations'),
  ];

  const files = [];
  for (const dir of searchDirs) {
    if (fs.existsSync(dir)) {
      const entries = fs.readdirSync(dir, { recursive: true });
      for (const entry of entries) {
        const fullPath = path.join(dir, entry.toString());
        if (fs.statSync(fullPath).isFile() && (fullPath.endsWith('.sql') || fullPath.endsWith('.cs'))) {
          files.push(fullPath);
        }
      }
    }
  }
  return files;
}

/**
 * Validates all repository migration files.
 */
export function validateAllMigrations(rootDir = process.cwd(), options = {}) {
  const files = findMigrationFiles(rootDir);
  const allViolations = [];

  for (const file of files) {
    try {
      const content = fs.readFileSync(file, 'utf8');
      const isCSharp = file.endsWith('.cs');
      const violations = validateMigrationSql(content, { ...options, isCSharp });
      for (const v of violations) {
        if (!v.isOverridden) {
          allViolations.push(`${path.basename(file)}: ${v.description}`);
        }
      }
    } catch (err) {
      allViolations.push(`Failed to read ${file}: ${err.message}`);
    }
  }

  return {
    success: allViolations.length === 0,
    filesChecked: files.length,
    violations: allViolations,
  };
}

/**
 * Generates an idempotent SQL migration script.
 */
export function generateMigrationScript(options = {}) {
  const project = options.project || 'packages/infrastructure/RestaurantOrder.Infrastructure.csproj';
  const startupProject = options.startupProject || 'apps/api/RestaurantOrder.Api.csproj';
  const outputPath = options.outputPath || 'deploy/migrations/latest_bundle.sql';
  const idempotent = options.idempotent !== false;

  const args = [
    'ef', 'migrations', 'script',
    '--project', project,
    '--startup-project', startupProject,
    '--output', outputPath,
  ];

  if (idempotent) {
    args.push('--idempotent');
  }

  console.log(`[MIGRATION-OPS] Generating script: dotnet ${args.join(' ')}`);
  const result = spawnSync('dotnet', args, { stdio: 'inherit' });
  if (result.status !== 0) {
    throw new Error(`Migration script generation failed with code ${result.status}`);
  }

  // Validate the generated script
  if (fs.existsSync(outputPath)) {
    let scriptContent = fs.readFileSync(outputPath, 'utf8');
    const sanitized = scriptContent
      .split(/\r?\n/)
      .map(line => line.trimEnd())
      .join('\n')
      .trimEnd() + '\n';
    if (sanitized !== scriptContent) {
      fs.writeFileSync(outputPath, sanitized, 'utf8');
      scriptContent = sanitized;
    }
    const compatibility = checkExpandContractCompatibility(scriptContent);
    if (!compatibility.isCompatible) {
      throw new Error(`Generated migration script contains destructive operations:\n${compatibility.violations.join('\n')}`);
    }
  }

  console.log(`[MIGRATION-OPS] Generated and validated idempotent script at: ${outputPath}`);
}

/**
 * Applies migrations for local development environment only.
 */
export function applyLocalDevMigrations(options = {}) {
  const env = process.env.ASPNETCORE_ENVIRONMENT || process.env.DOTNET_ENVIRONMENT || 'Development';
  if (env.toLowerCase() === 'production' || env.toLowerCase() === 'staging') {
    throw new Error(
      `FATAL: applyLocalDevMigrations cannot be executed in '${env}'. Production/Staging migrations must be executed as a dedicated pipeline step using idempotent scripts.`
    );
  }

  const project = options.project || 'packages/infrastructure/RestaurantOrder.Infrastructure.csproj';
  const startupProject = options.startupProject || 'apps/api/RestaurantOrder.Api.csproj';

  console.log(`[MIGRATION-OPS] Applying migrations to local development database (Environment: ${env})...`);
  const result = spawnSync('dotnet', [
    'ef', 'database', 'update',
    '--project', project,
    '--startup-project', startupProject,
  ], { stdio: 'inherit' });

  if (result.status !== 0) {
    throw new Error(`Local development migration failed with exit code ${result.status}`);
  }
}

// CLI entry point
if (process.argv[1] && process.argv[1].endsWith('migration-ops.mjs')) {
  const command = process.argv[2];
  try {
    switch (command) {
      case 'validate': {
        const result = validateAllMigrations();
        if (!result.success) {
          console.error('[MIGRATION-OPS] Validation failed with violations:');
          result.violations.forEach(v => console.error(`  - ${v}`));
          process.exit(1);
        }
        console.log(`[MIGRATION-OPS] All ${result.filesChecked} migration files verified successfully (non-destructive).`);
        break;
      }
      case 'script': {
        const idempotent = !process.argv.includes('--non-idempotent');
        const outputIdx = process.argv.indexOf('--output');
        const outputPath = outputIdx !== -1 ? process.argv[outputIdx + 1] : undefined;
        generateMigrationScript({ idempotent, outputPath });
        break;
      }
      case 'apply-dev': {
        applyLocalDevMigrations();
        break;
      }
      default:
        console.log('Usage: node scripts/migration-ops.mjs [validate|script|apply-dev]');
        break;
    }
  } catch (err) {
    console.error(`[MIGRATION-OPS] Error: ${err.message}`);
    process.exit(1);
  }
}
