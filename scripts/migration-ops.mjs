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
  { pattern: /\bALTER\s+TABLE\s+.*\bDROP\b/i, description: 'ALTER TABLE ... DROP is prohibited before cutover' },
  { pattern: /\bRENAME\s+COLUMN\b/i, description: 'RENAME COLUMN is prohibited; use expand (add new) + contract' },
  { pattern: /\bTRUNCATE\b/i, description: 'TRUNCATE is destructive' },
  { pattern: /\bADD\s+COLUMN\s+.*\bNOT\s+NULL\b(?!\s+DEFAULT)/i, description: 'ADD COLUMN NOT NULL without DEFAULT breaks concurrent inserts' },
];

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

  const allowDestructive = options.allowDestructive === true &&
    process.env.ALLOW_DESTRUCTIVE_MIGRATION === 'true';

  for (const { pattern, description } of FORBIDDEN_DESTRUCTIVE_PATTERNS) {
    if (pattern.test(sqlContent)) {
      if (allowDestructive) {
        violations.push({ description: `[OVERRIDDEN] ${description}`, isOverridden: true });
      } else {
        violations.push({ description, isOverridden: false });
      }
    }
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
      const violations = validateMigrationSql(content, options);
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
    const scriptContent = fs.readFileSync(outputPath, 'utf8');
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
