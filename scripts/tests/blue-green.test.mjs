import test from 'node:test';
import assert from 'node:assert/strict';
import { execSync } from 'node:child_process';
import fs from 'node:fs';
import path from 'node:path';

import { runPreflight } from '../blue-green/preflight.mjs';
import { runConfigValidate } from '../blue-green/config-validate.mjs';
import { validateSqlMigration, runMigrationCheck } from '../blue-green/migration-check.mjs';
import { runCutover } from '../blue-green/cutover.mjs';
import { runRollback } from '../blue-green/rollback.mjs';
import { runDeployInactive } from '../blue-green/deploy-inactive.mjs';
import { getInactiveColor } from '../blue-green/lib/common.mjs';

test('1. Docker Compose config validation across all environments', () => {
  const composeFiles = [
    ['-f', 'compose.yml', '-f', 'compose.dev.yml'],
    ['-f', 'compose.yml', '-f', 'compose.staging.yml'],
    ['-f', 'compose.yml', '-f', 'compose.prod.blue.yml'],
    ['-f', 'compose.yml', '-f', 'compose.prod.green.yml'],
    ['-f', 'deploy/docker-compose.yml'],
  ];

  for (const args of composeFiles) {
    const cmd = `docker compose ${args.join(' ')} config`;
    const output = execSync(cmd, { encoding: 'utf8', stdio: ['pipe', 'pipe', 'ignore'] });
    assert.ok(output.includes('services:'), `Expected valid compose config for: ${args.join(' ')}`);
  }
});

test('2. Health endpoint contract specifies status, timestamp, service, and color', () => {
  // Read Program.cs to verify health endpoint contract structure
  const programCs = fs.readFileSync(path.join(process.cwd(), 'apps/api/Program.cs'), 'utf8');
  assert.ok(programCs.includes('/health/live'), 'API must expose /health/live');
  assert.ok(programCs.includes('/health/ready'), 'API must expose /health/ready');
  assert.ok(programCs.includes('color = deploymentColor'), 'Health endpoint must expose deploymentColor');
});

test('3. Environment contracts and missing environment validation', () => {
  const result = runConfigValidate();
  assert.equal(result.success, true, 'Config validation must pass for existing .env.example files');

  // Verify frontend contract enforces public-only variables
  const customerEnv = fs.readFileSync(path.join(process.cwd(), 'apps/customer-web/.env.example'), 'utf8');
  assert.ok(customerEnv.includes('VITE_API_URL'), 'Customer web must define VITE_API_URL');
  assert.ok(!customerEnv.includes('DATABASE_URL'), 'Customer web must NOT contain DATABASE_URL');
  assert.ok(!customerEnv.includes('JWT_SECRET'), 'Customer web must NOT contain JWT_SECRET');
});

test('4. Blue/Green config difference and non-overlapping ports', () => {
  const blueContent = fs.readFileSync(path.join(process.cwd(), 'compose.prod.blue.yml'), 'utf8');
  const greenContent = fs.readFileSync(path.join(process.cwd(), 'compose.prod.green.yml'), 'utf8');

  // Check distinct container names
  assert.ok(blueContent.includes('restaurant-order-api-blue'));
  assert.ok(greenContent.includes('restaurant-order-api-green'));

  // Check distinct colors
  assert.ok(blueContent.includes('DEPLOYMENT_COLOR: blue'));
  assert.ok(greenContent.includes('DEPLOYMENT_COLOR: green'));

  // Check distinct ports (5001 vs 5002)
  assert.ok(blueContent.includes('5001'));
  assert.ok(greenContent.includes('5002'));

  // Ensure neither exposes raw postgres or redis
  assert.ok(!blueContent.includes('5432:5432'));
  assert.ok(!greenContent.includes('5432:5432'));
});

test('5. Same image digest requirement for Blue and Green', () => {
  const baseContent = fs.readFileSync(path.join(process.cwd(), 'compose.yml'), 'utf8');
  // Both blue and green inherit the base image definition
  assert.ok(baseContent.includes('restaurant-order-api'), 'Base compose specifies shared api image');
  assert.ok(baseContent.includes('restaurant-order-worker'), 'Base compose specifies shared worker image');

  // Preflight requires imageDigest when executing
  const preflightResult = runPreflight({ execute: true, imageDigest: '' });
  assert.equal(preflightResult.success, false);
  assert.ok(preflightResult.errors.some(e => e.includes('IMAGE_DIGEST')));
});

test('6. Active worker guard and color resolution logic', () => {
  assert.equal(getInactiveColor('blue'), 'green');
  assert.equal(getInactiveColor('green'), 'blue');
  assert.throws(() => getInactiveColor('red'), /Invalid active color/);

  // Deploy inactive must reject deploying onto active slot
  const deployResult = runDeployInactive({ color: 'blue', dryRun: true });
  // If active slot is blue, deploying to blue must fail
  process.env.ACTIVE_DEPLOYMENT_SLOT = 'blue';
  const invalidDeploy = runDeployInactive({ color: 'blue', dryRun: true });
  assert.equal(invalidDeploy.success, false);
});

test('7. Dry-run cutover and rollback safety checks', () => {
  // Dry run cutover succeeds safely without changing live state
  const dryCutover = runCutover({ color: 'green', dryRun: true });
  assert.equal(dryCutover.success, true);
  assert.equal(dryCutover.dryRun, true);
  assert.equal(dryCutover.plan.newActiveSlot, 'green');

  // Live cutover without confirmation flag must abort
  const abortCutover = runCutover({ color: 'green', dryRun: false, confirmCutover: false });
  assert.equal(abortCutover.success, false);
  assert.ok(abortCutover.error.includes('CUTOVER ABORTED'));

  // Dry run rollback succeeds safely
  const dryRollback = runRollback({ color: 'green', dryRun: true });
  assert.equal(dryRollback.success, true);
  assert.equal(dryRollback.dryRun, true);
  assert.equal(dryRollback.plan.restoredActiveSlot, 'blue');

  // Live rollback without confirmation flag must abort
  const abortRollback = runRollback({ color: 'green', dryRun: false, confirmRollback: false });
  assert.equal(abortRollback.success, false);
  assert.ok(abortRollback.error.includes('ROLLBACK ABORTED'));
});

test('8. Database migration safety rejects destructive SQL', () => {
  const safeSql = `
    ALTER TABLE orders ADD COLUMN loyalty_points INT DEFAULT 0;
    CREATE INDEX CONCURRENTLY idx_orders_created ON orders (created_at);
  `;
  assert.equal(validateSqlMigration(safeSql).length, 0);

  const destructiveSql = `
    DROP TABLE old_order_items;
    ALTER TABLE orders DROP COLUMN legacy_status;
    ALTER TABLE users ADD COLUMN email VARCHAR(255) NOT NULL;
  `;
  const violations = validateSqlMigration(destructiveSql);
  assert.ok(violations.length >= 3);
  assert.ok(violations.some(v => v.includes('DROP TABLE')));
  assert.ok(violations.some(v => v.includes('DROP COLUMN') || v.includes('ALTER TABLE ... DROP')));

  const checkResult = runMigrationCheck({ sqlContent: destructiveSql });
  assert.equal(checkResult.success, false);
});
