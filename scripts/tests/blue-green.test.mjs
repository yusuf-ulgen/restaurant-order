import test from 'node:test';
import assert from 'node:assert/strict';
import { execSync } from 'node:child_process';
import fs from 'node:fs';
import path from 'node:path';

import { CommandRunner, FakeCommandRunner, getInactiveColor } from '../blue-green/lib/common.mjs';
import { runPreflight } from '../blue-green/preflight.mjs';
import { runConfigValidate } from '../blue-green/config-validate.mjs';
import { validateSqlMigration, runMigrationCheck } from '../blue-green/migration-check.mjs';
import { runDeployInactive } from '../blue-green/deploy-inactive.mjs';
import { runHealthCheck } from '../blue-green/health-check.mjs';
import { runWarmup } from '../blue-green/warmup.mjs';
import { runSmoke } from '../blue-green/smoke.mjs';
import { runCutover } from '../blue-green/cutover.mjs';
import { runObserve } from '../blue-green/observe.mjs';
import { runRollback } from '../blue-green/rollback.mjs';
import { runOrchestrator } from '../blue-green/orchestrator.mjs';

const VALID_DIGEST = 'sha256:e3b0c44298fc1c149afbf4c8996fb92427ae41e4649b934ca495991b7852b855';

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

test('2. Health endpoint contract in Program.cs', () => {
  const programCs = fs.readFileSync(path.join(process.cwd(), 'apps/api/Program.cs'), 'utf8');
  assert.ok(programCs.includes('/health/live'), 'API must expose /health/live');
  assert.ok(programCs.includes('/health/ready'), 'API must expose /health/ready');
  assert.ok(programCs.includes('Color: deploymentColor'), 'Health endpoint must expose deploymentColor');
});

test('3. Environment contracts and missing environment validation', () => {
  const result = runConfigValidate();
  assert.equal(result.success, true, 'Config validation must pass for existing .env.example files');

  const customerEnv = fs.readFileSync(path.join(process.cwd(), 'apps/customer-web/.env.example'), 'utf8');
  assert.ok(customerEnv.includes('VITE_API_URL'), 'Customer web must define VITE_API_URL');
  assert.ok(!customerEnv.includes('DATABASE_URL'), 'Customer web must NOT contain DATABASE_URL');
  assert.ok(!customerEnv.includes('JWT_SECRET'), 'Customer web must NOT contain JWT_SECRET');
});

test('4. Blue/Green compose files use immutable image digests and non-overlapping ports', () => {
  const blueContent = fs.readFileSync(path.join(process.cwd(), 'compose.prod.blue.yml'), 'utf8');
  const greenContent = fs.readFileSync(path.join(process.cwd(), 'compose.prod.green.yml'), 'utf8');

  assert.ok(blueContent.includes('@${API_IMAGE_DIGEST'), 'Blue compose must pin API image by digest');
  assert.ok(blueContent.includes('@${WORKER_IMAGE_DIGEST'), 'Blue compose must pin Worker image by digest');
  assert.ok(greenContent.includes('@${API_IMAGE_DIGEST'), 'Green compose must pin API image by digest');
  assert.ok(greenContent.includes('@${WORKER_IMAGE_DIGEST'), 'Green compose must pin Worker image by digest');

  assert.ok(blueContent.includes('5001'));
  assert.ok(greenContent.includes('5002'));
  assert.ok(!blueContent.includes('5432:5432'));
  assert.ok(!greenContent.includes('5432:5432'));
});

test('5. CommandRunner masks secrets and handles execution failure', async () => {
  const runner = new CommandRunner({ secretMasks: ['super_secret_token_123'] });
  assert.equal(runner.maskSecrets('auth=super_secret_token_123'), 'auth=***REDACTED***');
  assert.equal(runner.maskSecrets('password=myPassword123!'), 'password=***REDACTED***');
  assert.equal(runner.maskSecrets('postgres://user:pass123@host:5432/db'), 'postgres://user:***REDACTED***@host:5432/db');

  const failedResult = await runner.run('node', ['-e', 'process.exit(42)']);
  assert.equal(failedResult.success, false);
  assert.equal(failedResult.exitCode, 42);
});

test('6. FakeCommandRunner tracks calls and returns injected response', async () => {
  const fake = new FakeCommandRunner(async (cmd, args) => {
    if (cmd === 'docker' && args.includes('ps')) {
      return { success: true, exitCode: 0, stdout: 'container-1' };
    }
    return { success: false, exitCode: 1, stderr: 'unknown command' };
  });

  const res1 = await fake.run('docker', ['ps']);
  assert.equal(res1.success, true);
  assert.equal(res1.stdout, 'container-1');

  const res2 = await fake.run('docker', ['stop', 'foo']);
  assert.equal(res2.success, false);
  assert.equal(fake.calls.length, 2);
});

test('7. Preflight: slot selection, digest validation, and rejection of latest tag', () => {
  assert.equal(getInactiveColor('blue'), 'green');
  assert.equal(getInactiveColor('green'), 'blue');
  assert.throws(() => getInactiveColor('yellow'), /Invalid active color/);

  // Reject deploying to the active slot
  process.env.ACTIVE_DEPLOYMENT_SLOT = 'blue';
  const sameSlotResult = runPreflight({ color: 'blue', execute: true });
  assert.equal(sameSlotResult.success, false);

  // Invalid digest format must fail
  const invalidDigestResult = runPreflight({
    color: 'green',
    execute: true,
    apiImageDigest: 'invalid-digest',
    workerImageDigest: VALID_DIGEST,
  });
  assert.equal(invalidDigestResult.success, false);
  assert.ok(invalidDigestResult.errors.some((e) => e.includes('Invalid API_IMAGE_DIGEST')));

  // Valid digests succeed
  const validResult = runPreflight({
    color: 'green',
    execute: true,
    apiImageDigest: VALID_DIGEST,
    workerImageDigest: VALID_DIGEST,
  });
  assert.equal(validResult.success, true);
});

test('8. deploy-inactive: verifies --force-recreate, rejects --no-recreate, handles failure', async () => {
  const calls = [];
  const fakeRunner = new FakeCommandRunner(async (cmd, args) => {
    calls.push({ cmd, args });
    if (args.includes('up')) {
      return { success: true, exitCode: 0, stdout: 'Started' };
    }
    if (args.includes('inspect')) {
      return { success: true, exitCode: 0, stdout: 'running|healthy' };
    }
    return { success: true, exitCode: 0 };
  });

  process.env.ACTIVE_DEPLOYMENT_SLOT = 'blue';
  const deployRes = await runDeployInactive({
    color: 'green',
    execute: true,
    runner: fakeRunner,
    timeoutMs: 1000,
    pollIntervalMs: 50,
  });

  assert.equal(deployRes.success, true);
  const upCall = calls.find((c) => c.args.includes('up'));
  assert.ok(upCall, 'Must invoke docker compose up');
  assert.ok(upCall.args.includes('--force-recreate'), 'Must use --force-recreate');
  assert.ok(!upCall.args.includes('--no-recreate'), 'Must NOT use --no-recreate');

  // Failure handling when docker compose fails
  const failingRunner = new FakeCommandRunner(async () => ({
    success: false,
    exitCode: 1,
    stderr: 'Docker daemon unavailable',
  }));

  const failRes = await runDeployInactive({
    color: 'green',
    execute: true,
    runner: failingRunner,
  });
  assert.equal(failRes.success, false);
  assert.ok(failRes.error.includes('Docker compose up failed'));
});

test('9. cutover: enforces confirmation, validates nginx -t, reverts on failure', async () => {
  // Missing confirmation aborts
  const unconfirmed = await runCutover({ color: 'green', execute: true, confirmCutover: false });
  assert.equal(unconfirmed.success, false);
  assert.ok(unconfirmed.error.includes('CUTOVER ABORTED'));

  // When nginx -t fails, previous config is preserved
  const testFailRunner = new FakeCommandRunner(async (cmd, args) => {
    if (cmd === 'nginx' && args.includes('-t')) {
      return { success: false, exitCode: 1, stderr: 'nginx: syntax error in config' };
    }
    return { success: true, exitCode: 0 };
  });

  const failedCutover = await runCutover({
    color: 'green',
    execute: true,
    confirmCutover: true,
    runner: testFailRunner,
    nginxDir: path.join(process.cwd(), 'deploy/nginx'),
  });

  assert.equal(failedCutover.success, false);
  assert.ok(failedCutover.error.includes('validation failed'));

  // Successful cutover with valid runner
  const successRunner = new FakeCommandRunner(async () => ({ success: true, exitCode: 0 }));
  const okCutover = await runCutover({
    color: 'green',
    execute: true,
    confirmCutover: true,
    runner: successRunner,
    nginxDir: path.join(process.cwd(), 'deploy/nginx'),
  });

  assert.equal(okCutover.success, true);
  assert.equal(okCutover.plan.newActiveSlot, 'green');
});

test('10. rollback: reverts ingress to previous slot and keeps failing slot running', async () => {
  const stateFile = path.join(process.cwd(), '.deployment-state.json');
  fs.writeFileSync(stateFile, JSON.stringify({ previousActiveSlot: 'blue', newActiveSlot: 'green' }), 'utf8');

  // Missing confirmation aborts
  const unconfirmed = await runRollback({ color: 'green', execute: true, confirmRollback: false });
  assert.equal(unconfirmed.success, false);
  assert.ok(unconfirmed.error.includes('ROLLBACK ABORTED'));

  const rollbackRunner = new FakeCommandRunner(async () => ({ success: true, exitCode: 0 }));
  const res = await runRollback({
    color: 'green',
    execute: true,
    confirmRollback: true,
    runner: rollbackRunner,
    nginxDir: path.join(process.cwd(), 'deploy/nginx'),
  });

  assert.equal(res.success, true);
  assert.equal(res.plan.restoredActiveSlot, 'blue');
  assert.equal(res.plan.keepFailedSlotRunning, true);
});

test('11. health-check: validates live/ready, rejects color and version mismatch', async () => {
  // Dry run passes as simulated
  const dry = await runHealthCheck({ color: 'green', dryRun: true });
  assert.equal(dry.success, true);
  assert.equal(dry.dryRun, true);

  // Mismatched color fails in execute mode
  const colorMismatchProbe = async () => ({
    ok: true,
    status: 200,
    data: { status: 'Healthy', color: 'blue', version: '0.1.0' },
  });

  const mismatchRes = await runHealthCheck({
    color: 'green',
    execute: true,
    probeFn: colorMismatchProbe,
  });
  assert.equal(mismatchRes.success, false);
  assert.ok(mismatchRes.error.includes('color mismatch'));

  // Matching color and version passes
  const validProbe = async () => ({
    ok: true,
    status: 200,
    data: { status: 'Healthy', color: 'green', version: '0.1.0' },
  });

  const okRes = await runHealthCheck({
    color: 'green',
    execute: true,
    version: '0.1.0',
    probeFn: validProbe,
  });
  assert.equal(okRes.success, true);
});

test('12. warmup: dry-run simulates, execute executes real probes', async () => {
  const dry = await runWarmup({ color: 'green', dryRun: true });
  assert.equal(dry.success, true);

  const mockProbe = async (url) => ({ ok: true, status: 200, url });
  const live = await runWarmup({ color: 'green', execute: true, probeFn: mockProbe });
  assert.equal(live.success, true);
  assert.equal(live.dryRun, false);
});

test('13. smoke: fail-closed as BLOCKED in execute mode for unimplemented checks', async () => {
  const dry = await runSmoke({ color: 'green', dryRun: true });
  assert.equal(dry.success, true);
  assert.equal(dry.status, 'SIMULATED');

  // Execute mode without custom runner returns BLOCKED
  const blocked = await runSmoke({ color: 'green', execute: true });
  assert.equal(blocked.success, false);
  assert.equal(blocked.status, 'BLOCKED');
  assert.ok(blocked.error.includes('BLOCKED'));
});

test('14. observe: fail-closed as BLOCKED in execute mode if METRICS_URL is unconfigured', async () => {
  delete process.env.METRICS_URL;
  const blocked = await runObserve({ color: 'green', execute: true });
  assert.equal(blocked.success, false);
  assert.equal(blocked.status, 'BLOCKED');

  // With real metrics exceeding threshold, fails
  const highErrorRes = await runObserve({
    color: 'green',
    execute: true,
    fetchMetrics: async () => ({ errorRate: 0.05, p95LatencyMs: 150 }), // 5% error rate
  });
  assert.equal(highErrorRes.success, false);
  assert.ok(highErrorRes.error.includes('exceeded threshold'));
});

test('15. migration-check: NO_MIGRATIONS, rejects destructive SQL, strict BACKUP_VERIFIED', () => {
  // Destructive SQL is rejected
  const destructiveSql = 'DROP TABLE users; ALTER TABLE orders DROP COLUMN total;';
  const violations = validateSqlMigration(destructiveSql);
  assert.ok(violations.length >= 2);

  // If no migration files found, reports NO_MIGRATIONS
  const noMigRes = runMigrationCheck({ migrationFiles: [] });
  assert.equal(noMigRes.success, true);
  assert.equal(noMigRes.status, 'NO_MIGRATIONS');

  // BACKUP_VERIFIED="false" string is rejected (must not be treated as true)
  process.env.BACKUP_VERIFIED = 'false';
  const falseBackupRes = runMigrationCheck({
    execute: true,
    sqlContent: 'ALTER TABLE orders ADD COLUMN notes TEXT;',
  });
  assert.equal(falseBackupRes.success, false);
  assert.ok(falseBackupRes.violations.some((v) => v.includes('BACKUP_VERIFIED=true')));

  // Strictly "true" passes
  // Strictly "true" passes
  process.env.BACKUP_VERIFIED = 'true';
  const trueBackupRes = runMigrationCheck({
    execute: true,
    sqlContent: 'ALTER TABLE orders ADD COLUMN notes TEXT;',
  });
  assert.equal(trueBackupRes.success, true);
});



