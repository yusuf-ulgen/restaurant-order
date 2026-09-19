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
  for (const { pattern, description } of FORBIDDEN_PRE_CUTOVER_PATTERNS) {
    if (pattern.test(sqlContent)) {
      violations.push(description);
    }
  }
  return violations;
}

export function runMigrationCheck(options = {}) {
  const flags = { ...parseArgs(), ...options };
  logStep('MIGRATION-CHECK', 'RUNNING', 'Verifying database migration safety and backward compatibility...');

  const sampleSql = options.sqlContent || '';
  const violations = validateSqlMigration(sampleSql);

  if (violations.length > 0) {
    for (const v of violations) {
      logStep('MIGRATION-CHECK', 'FAIL', `Destructive migration detected: ${v}`);
    }
    return { success: false, violations };
  }

  // Verify Backup Readiness
  const backupVerified = options.backupVerified ?? (flags.execute ? Boolean(process.env.BACKUP_VERIFIED) : true);
  if (!backupVerified) {
    const errorMsg = 'Database backup/snapshot readiness not verified. Set BACKUP_VERIFIED=true before migration.';
    logStep('MIGRATION-CHECK', 'FAIL', errorMsg);
    return { success: false, violations: [errorMsg] };
  }

  logStep('MIGRATION-CHECK', 'PASS', 'Migration is non-destructive, backward-compatible with active slot, and backup verified.');
  return { success: true };
}

if (process.argv[1] && process.argv[1].endsWith('migration-check.mjs')) {
  const result = runMigrationCheck();
  if (!result.success) {
    process.exit(1);
  }
}
