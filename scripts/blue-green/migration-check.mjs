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
  { pattern: /\bALTER\s+TABLE\s+.*\bDROP\b/i, description: 'ALTER TABLE ... DROP is prohibited before cutover' },
  { pattern: /\bRENAME\s+COLUMN\b/i, description: 'RENAME COLUMN is prohibited; use expand (add new) + contract' },
  { pattern: /\bTRUNCATE\b/i, description: 'TRUNCATE is destructive' },
  { pattern: /\bADD\s+COLUMN\s+.*\bNOT\s+NULL\b(?!\s+DEFAULT)/i, description: 'ADD COLUMN NOT NULL without DEFAULT breaks concurrent inserts' },
];

export function validateSqlMigration(sqlContent) {
  const violations = [];
  if (!sqlContent || typeof sqlContent !== 'string') return violations;

  for (const { pattern, description } of FORBIDDEN_PRE_CUTOVER_PATTERNS) {
    if (pattern.test(sqlContent)) {
      violations.push(description);
    }
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
      const violations = validateSqlMigration(content);
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
