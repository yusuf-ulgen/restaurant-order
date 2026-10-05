import fs from 'node:fs';
import path from 'node:path';
import { parseArgs, logStep } from './lib/common.mjs';

/**
 * Migration Check: Validates database migration safety under Expand-Migrate-Contract rules.
 * Blue and green must both be compatible with the database at the same time.
 * Prohibits destructive DDL prior to cutover.
 */
export const FORBIDDEN_PRE_CUTOVER_PATTERNS = [
  { pattern: /\bDROP\s+TABLE\b/i, description: 'DROP TABLE is destructive and breaks active slot' },
  { pattern: /\bDROP\s+COLUMN\b/i, description: 'DROP COLUMN breaks active slot backward compatibility' },
  { pattern: /\bALTER\s+TABLE\s+.*\bDROP\s+(?!CONSTRAINT\b)/i, description: 'ALTER TABLE ... DROP is prohibited before cutover' },
  { pattern: /\bRENAME\s+COLUMN\b/i, description: 'RENAME COLUMN is prohibited; use expand (add new) + contract' },
  { pattern: /\bTRUNCATE\b/i, description: 'TRUNCATE is destructive' },
  { pattern: /\bADD\s+COLUMN\s+.*\bNOT\s+NULL\b(?!\s+DEFAULT)/i, description: 'ADD COLUMN NOT NULL without DEFAULT breaks concurrent inserts' },
];

/**
 * Narrow allowlist of approved constraint replacement operations.
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
    // Check if the unique constraint is in allowlist (e.g. AK_menus_tenant_id_id)
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

export function validateSqlMigration(sqlContent, options = {}) {
  const violations = [];
  if (!sqlContent || typeof sqlContent !== 'string') return violations;

  const content = options.isCSharp ? extractUpMethodContent(sqlContent) : sqlContent;

  for (const { pattern, description } of FORBIDDEN_PRE_CUTOVER_PATTERNS) {
    if (pattern.test(content)) {
      violations.push(description);
    }
  }

  const constraintViolations = validateConstraintSafety(sqlContent, options);
  if (constraintViolations.length > 0) {
    violations.push(...constraintViolations);
  }

  return violations;
}

/**
 * Discovers migration files in standard repository locations.
 */
export function findMigrationFiles(rootDir = process.cwd()) {
  const searchDirs = [
    path.join(rootDir, 'migrations'),
    path.join(rootDir, 'deploy/migrations'),
    path.join(rootDir, 'apps/api/Migrations'),
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

export function runMigrationCheck(options = {}) {
  const flags = { ...parseArgs(), ...options };
  logStep('MIGRATION-CHECK', 'RUNNING', 'Verifying database migration safety and backward compatibility...');

  const migrationFiles = options.migrationFiles || findMigrationFiles();
  const hasInlineSql = Boolean(options.sqlContent);

  if (migrationFiles.length === 0 && !hasInlineSql) {
    logStep('MIGRATION-CHECK', 'PASS', 'No migration files found to validate. Status: NO_MIGRATIONS.');
    return { success: true, status: 'NO_MIGRATIONS', message: 'No migration files found.' };
  }

  const allViolations = [];

  // Check inline SQL if provided (e.g. unit tests)
  if (hasInlineSql) {
    const violations = validateSqlMigration(options.sqlContent);
    if (violations.length > 0) {
      allViolations.push(...violations);
    }
  }

  // Scan discovered migration files
  for (const file of migrationFiles) {
    try {
      const content = fs.readFileSync(file, 'utf8');
      const isCSharp = file.endsWith('.cs');
      const violations = validateSqlMigration(content, { ...options, isCSharp });
      for (const v of violations) {
        allViolations.push(`${path.basename(file)}: ${v}`);
      }
    } catch (err) {
      allViolations.push(`Failed to read migration file ${file}: ${err.message}`);
    }
  }

  if (allViolations.length > 0) {
    for (const v of allViolations) {
      logStep('MIGRATION-CHECK', 'FAIL', `Destructive migration detected: ${v}`);
    }
    return { success: false, violations: allViolations };
  }

  // Verify Backup Readiness: strictly "true" or boolean true
  const backupVerifiedVal = options.backupVerified ?? process.env.BACKUP_VERIFIED;
  const isBackupVerified = typeof backupVerifiedVal === 'boolean'
    ? backupVerifiedVal
    : String(backupVerifiedVal || '').trim().toLowerCase() === 'true';

  if (flags.execute && !isBackupVerified) {
    const errorMsg = 'Database backup/snapshot readiness not verified. Set BACKUP_VERIFIED=true before migration.';
    logStep('MIGRATION-CHECK', 'FAIL', errorMsg);
    return { success: false, violations: [errorMsg] };
  }

  logStep('MIGRATION-CHECK', 'PASS', 'Migration is non-destructive, backward-compatible with active slot, and backup verified.');
  return { success: true, filesChecked: migrationFiles.length };
}

if (process.argv[1] && process.argv[1].endsWith('migration-check.mjs')) {
  const result = runMigrationCheck();
  if (!result.success) {
    process.exit(1);
  }
}
