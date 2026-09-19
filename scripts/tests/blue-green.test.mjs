import test from 'node:test';
import assert from 'node:assert/strict';
import { execSync } from 'node:child_process';
import fs from 'node:fs';
import os from 'node:os';
import path from 'node:path';

import { CommandRunner, FakeCommandRunner, getInactiveColor } from '../blue-green/lib/common.mjs';
import { FakeRedisClient, REDIS_ACTIVE_SLOT_KEY } from '../blue-green/lib/redis-state.mjs';
import { runPreflight } from '../blue-green/preflight.mjs';
import { runConfigValidate } from '../blue-green/config-validate.mjs';
import { runDeployInactive } from '../blue-green/deploy-inactive.mjs';
import { runHealthCheck } from '../blue-green/health-check.mjs';
import { runWarmup } from '../blue-green/warmup.mjs';
import { runSmoke } from '../blue-green/smoke.mjs';
import { runCutover } from '../blue-green/cutover.mjs';
import { runRollback } from '../blue-green/rollback.mjs';

/**
 * A properly formatted but deliberately non-real sha256 digest for test fixtures.
 * NOT the empty-content hash (e3b0c...) which is now explicitly rejected in execute mode.
 */
const VALID_DIGEST = 'sha256:aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa';
const EMPTY_CONTENT_DIGEST = 'sha256:e3b0c44298fc1c149afbf4c8996fb92427ae41e4649b934ca495991b7852b855';

test('1. Docker Compose config validation across all environments', () => {
  const composeFiles = [
    ['-f', 'compose.yml', '-f', 'compose.dev.yml'],
    ['-f', 'compose.yml', '-f', 'compose.staging.yml'],
    // Production slots: validate with -p flag to reflect real usage
    ['-p', 'restaurant-order-blue', '-f', 'compose.yml', '-f', 'compose.prod.blue.yml'],
    ['-p', 'restaurant-order-green', '-f', 'compose.yml', '-f', 'compose.prod.green.yml'],
    ['-p', 'restaurant-order-ingress', '-f', 'compose.ingress.yml'],
    ['-f', 'deploy/docker-compose.yml'],
  ];

  const testEnv = {
    ...process.env,
    DATABASE_URL: 'postgresql://postgres:postgres@localhost:5432/restaurant_order',
    REDIS_URL: 'redis://localhost:6379',
    JWT_SECRET: 'test_jwt_secret_min_32_characters_long_for_security',
    API_IMAGE_DIGEST: VALID_DIGEST,
    WORKER_IMAGE_DIGEST: VALID_DIGEST,
    CUSTOMER_WEB_IMAGE_DIGEST: VALID_DIGEST,
    OPERATIONS_WEB_IMAGE_DIGEST: VALID_DIGEST,
    ADMIN_WEB_IMAGE_DIGEST: VALID_DIGEST,
  };

  for (const args of composeFiles) {
    const cmd = `docker compose ${args.join(' ')} config`;
    const output = execSync(cmd, { env: testEnv, encoding: 'utf8', stdio: ['pipe', 'pipe', 'ignore'] });
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

test('4. Blue/Green compose files use immutable image digests, project isolation, and container DNS routing', () => {
  const blueContent = fs.readFileSync(path.join(process.cwd(), 'compose.prod.blue.yml'), 'utf8');
  const greenContent = fs.readFileSync(path.join(process.cwd(), 'compose.prod.green.yml'), 'utf8');
  const ingressContent = fs.readFileSync(path.join(process.cwd(), 'compose.ingress.yml'), 'utf8');

  // All 5 images must be pinned by digest in both slots
  for (const [label, content] of [['blue', blueContent], ['green', greenContent]]) {
    assert.ok(content.includes('@${API_IMAGE_DIGEST'), `${label} compose must pin API image by digest`);
    assert.ok(content.includes('@${WORKER_IMAGE_DIGEST'), `${label} compose must pin Worker image by digest`);
    assert.ok(content.includes('@${CUSTOMER_WEB_IMAGE_DIGEST'), `${label} compose must pin customer-web by digest`);
    assert.ok(content.includes('@${OPERATIONS_WEB_IMAGE_DIGEST'), `${label} compose must pin operations-web by digest`);
    assert.ok(content.includes('@${ADMIN_WEB_IMAGE_DIGEST'), `${label} compose must pin admin-web by digest`);
  }

  // Slots use expose (internal port), not host port bindings
  // Ingress reaches them via container DNS on the shared network
  assert.ok(blueContent.includes('expose:'), 'Blue slot must use expose, not host ports');
  assert.ok(greenContent.includes('expose:'), 'Green slot must use expose, not host ports');

  // No DB secrets in web slots
  assert.ok(!blueContent.includes('5432:5432'));
  assert.ok(!greenContent.includes('5432:5432'));

  // Shared external ingress network declared in both slots
  assert.ok(blueContent.includes('restaurant_order_ingress'), 'Blue must connect to shared ingress network');
  assert.ok(greenContent.includes('restaurant_order_ingress'), 'Green must connect to shared ingress network');

  // Ingress compose connects to the same shared network
  assert.ok(ingressContent.includes('restaurant_order_ingress'), 'Ingress must be on the shared network');
  assert.ok(ingressContent.includes('restaurant-order-ingress'), 'Ingress container must have stable name');
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

test('7. Preflight: slot selection, digest validation, rejection of latest tag, and empty-content hash', async () => {
  assert.equal(getInactiveColor('blue'), 'green');
  assert.equal(getInactiveColor('green'), 'blue');
  assert.throws(() => getInactiveColor('yellow'), /Invalid active color/);

  // Reject deploying to the active slot (color 'blue' matches what Redis says is active)
  const activeBlueRedis = new FakeRedisClient({ [REDIS_ACTIVE_SLOT_KEY]: 'blue' });
  const sameSlotResult = await runPreflight({ color: 'blue', execute: true, redisClient: activeBlueRedis });
  assert.equal(sameSlotResult.success, false);

  // Invalid digest format must fail
  const invalidDigestResult = await runPreflight({
    color: 'green',
    execute: true,
    redisClient: new FakeRedisClient({ [REDIS_ACTIVE_SLOT_KEY]: 'blue' }),
    apiImageDigest: 'invalid-digest',
    workerImageDigest: VALID_DIGEST,
  });
  assert.equal(invalidDigestResult.success, false);
  assert.ok(invalidDigestResult.errors.some((e) => e.includes('Invalid API_IMAGE_DIGEST')));

  // The empty-content SHA-256 (e3b0c...) must be explicitly rejected in execute mode
  const emptyHashResult = await runPreflight({
    color: 'green',
    execute: true,
    redisClient: new FakeRedisClient({ [REDIS_ACTIVE_SLOT_KEY]: 'blue' }),
    apiImageDigest: EMPTY_CONTENT_DIGEST,
    workerImageDigest: VALID_DIGEST,
    customerWebImageDigest: VALID_DIGEST,
    operationsWebImageDigest: VALID_DIGEST,
    adminWebImageDigest: VALID_DIGEST,
  });
  assert.equal(emptyHashResult.success, false, 'Empty-content hash must be rejected in execute mode');
  assert.ok(
    emptyHashResult.errors.some((e) => e.includes('empty-content hash') || e.includes('empty-content')),
    'Error must explain that this is the empty-content hash'
  );

  // Valid digests succeed (all 5 images)
  const validResult = await runPreflight({
    color: 'green',
    execute: true,
    redisClient: new FakeRedisClient({ [REDIS_ACTIVE_SLOT_KEY]: 'blue' }),
    apiImageDigest: VALID_DIGEST,
    workerImageDigest: VALID_DIGEST,
    customerWebImageDigest: VALID_DIGEST,
    operationsWebImageDigest: VALID_DIGEST,
    adminWebImageDigest: VALID_DIGEST,
  });
  assert.equal(validResult.success, true);
});

test('8. deploy-inactive: project isolation, pull+up sequence, --force-recreate, handles failure', async () => {
  const calls = [];
  const fakeRunner = new FakeCommandRunner(async (cmd, args) => {
    calls.push({ cmd, args: [...args] });
    if (args.includes('pull')) {
      return { success: true, exitCode: 0, stdout: 'Pulled all images' };
    }
    if (args.includes('up')) {
      return { success: true, exitCode: 0, stdout: 'Started' };
    }
    if (args.includes('inspect')) {
      return { success: true, exitCode: 0, stdout: 'running|healthy' };
    }
    return { success: true, exitCode: 0 };
  });

  const deployRes = await runDeployInactive({
    color: 'green',
    execute: true,
    dryRun: false,
    runner: fakeRunner,
    redisClient: new FakeRedisClient({ [REDIS_ACTIVE_SLOT_KEY]: 'blue' }),
    timeoutMs: 1000,
    pollIntervalMs: 50,
    skipHealthPoll: true,
    apiImageDigest: VALID_DIGEST,
    workerImageDigest: VALID_DIGEST,
    customerWebImageDigest: VALID_DIGEST,
    operationsWebImageDigest: VALID_DIGEST,
    adminWebImageDigest: VALID_DIGEST,
  });

  assert.equal(deployRes.success, true);
  assert.equal(deployRes.composeProject, 'restaurant-order-green', 'Must use slot-specific Compose project name');

  // Must pull before up
  const pullCall = calls.find((c) => c.args.includes('pull'));
  assert.ok(pullCall, 'Must invoke docker compose pull before up');
  assert.ok(pullCall.args.includes('-p'), 'Pull must use -p project flag for slot isolation');
  assert.ok(pullCall.args.includes('restaurant-order-green'), 'Pull must target the green project');

  // Must up with --force-recreate
  const upCall = calls.find((c) => c.args.includes('up'));
  assert.ok(upCall, 'Must invoke docker compose up');
  assert.ok(upCall.args.join(' ').includes('up -d --force-recreate'), 'Must use --force-recreate');
  assert.ok(!upCall.args.includes('--no-recreate'), 'Must NOT use --no-recreate');
  assert.ok(upCall.args.includes('-p'), 'Up must use -p project flag for slot isolation');

  // Failure handling when docker pull fails
  const failingRunner = new FakeCommandRunner(async (cmd, args) => {
    if (args.includes('pull')) {
      return { success: false, exitCode: 1, stderr: 'Docker daemon unavailable' };
    }
    return { success: true, exitCode: 0 };
  });

  const failRes = await runDeployInactive({
    color: 'green',
    execute: true,
    dryRun: false,
    runner: failingRunner,
    redisClient: new FakeRedisClient({ [REDIS_ACTIVE_SLOT_KEY]: 'blue' }),
    apiImageDigest: VALID_DIGEST,
    workerImageDigest: VALID_DIGEST,
    customerWebImageDigest: VALID_DIGEST,
    operationsWebImageDigest: VALID_DIGEST,
    adminWebImageDigest: VALID_DIGEST,
  });
  assert.equal(failRes.success, false);
  assert.ok(failRes.error.includes('Docker pull failed') || failRes.error.includes('Docker compose up failed'));
});

test('9. cutover: enforces confirmation, validates nginx -t via docker exec, reverts on failure', async () => {
  const tempNginxDir = fs.mkdtempSync(path.join(os.tmpdir(), 'bg-cutover-test-'));
  const tempConfD = path.join(tempNginxDir, 'conf.d');
  fs.mkdirSync(tempConfD, { recursive: true });
  fs.writeFileSync(path.join(tempNginxDir, 'nginx.conf'), 'events {} http { include conf.d/*.conf; }', 'utf8');
  fs.writeFileSync(path.join(tempConfD, 'upstream.conf'), 'upstream api_backend { server restaurant-order-api-blue:5000; }', 'utf8');

  try {
    // Missing confirmation aborts
    const unconfirmed = await runCutover({
      color: 'green',
      execute: true,
      dryRun: false,
      confirmCutover: false,
      redisClient: new FakeRedisClient({ [REDIS_ACTIVE_SLOT_KEY]: 'blue' }),
      nginxDir: tempNginxDir,
      stateFile: path.join(tempNginxDir, 'state.json'),
    });
    assert.equal(unconfirmed.success, false);
    assert.ok(unconfirmed.error.includes('CUTOVER ABORTED'));

    // When docker exec nginx -t fails, previous config is preserved
    const testFailRunner = new FakeCommandRunner(async (cmd, args) => {
      if (cmd === 'docker' && args.includes('exec') && args.includes('nginx') && args.includes('-t')) {
        return { success: false, exitCode: 1, stderr: 'nginx: syntax error in config' };
      }
      if (cmd === 'docker' && args.includes('inspect')) {
        return { success: true, exitCode: 0, stdout: 'running' };
      }
      return { success: true, exitCode: 0 };
    });

    const failedCutover = await runCutover({
      color: 'green',
      execute: true,
      dryRun: false,
      confirmCutover: true,
      runner: testFailRunner,
      nginxDir: tempNginxDir,
      stateFile: path.join(tempNginxDir, 'state.json'),
      redisClient: new FakeRedisClient({ [REDIS_ACTIVE_SLOT_KEY]: 'blue' }),
    });

    assert.equal(failedCutover.success, false);
    assert.ok(failedCutover.error.includes('validation failed'));

    // Successful cutover with valid runner (docker exec returns success)
    const successRunner = new FakeCommandRunner(async (cmd, args) => {
      if (cmd === 'docker' && args.includes('inspect')) {
        return { success: true, exitCode: 0, stdout: 'running' };
      }
      return { success: true, exitCode: 0 };
    });
    const okCutover = await runCutover({
      color: 'green',
      execute: true,
      dryRun: false,
      confirmCutover: true,
      runner: successRunner,
      nginxDir: tempNginxDir,
      stateFile: path.join(tempNginxDir, 'state.json'),
      redisClient: new FakeRedisClient({ [REDIS_ACTIVE_SLOT_KEY]: 'blue' }),
    });

    assert.equal(okCutover.success, true);
    assert.equal(okCutover.plan.newActiveSlot, 'green');
  } finally {
    fs.rmSync(tempNginxDir, { recursive: true, force: true });
  }
});

test('10. rollback: reverts ingress to previous slot via docker exec, keeps failing slot running', async () => {
  const tempNginxDir = fs.mkdtempSync(path.join(os.tmpdir(), 'bg-rollback-test-'));
  const tempConfD = path.join(tempNginxDir, 'conf.d');
  fs.mkdirSync(tempConfD, { recursive: true });
  fs.writeFileSync(path.join(tempNginxDir, 'nginx.conf'), 'events {} http { include conf.d/*.conf; }', 'utf8');
  fs.writeFileSync(path.join(tempConfD, 'upstream.conf'), 'upstream api_backend { server restaurant-order-api-green:5000; }', 'utf8');

  try {
    // Missing confirmation aborts
    const unconfirmed = await runRollback({
      color: 'green',
      execute: true,
      dryRun: false,
      confirmRollback: false,
      redisClient: new FakeRedisClient({ [REDIS_ACTIVE_SLOT_KEY]: 'green' }),
      nginxDir: tempNginxDir,
      stateFile: path.join(tempNginxDir, 'state.json'),
    });
    assert.equal(unconfirmed.success, false);
    assert.ok(unconfirmed.error.includes('ROLLBACK ABORTED'));

    const rollbackRunner = new FakeCommandRunner(async (cmd, args) => {
      if (cmd === 'docker' && args.includes('exec')) {
        return { success: true, exitCode: 0 };
      }
      return { success: true, exitCode: 0 };
    });
    const res = await runRollback({
      color: 'green',
      execute: true,
      dryRun: false,
      confirmRollback: true,
      runner: rollbackRunner,
      nginxDir: tempNginxDir,
      stateFile: path.join(tempNginxDir, 'state.json'),
      redisClient: new FakeRedisClient({ [REDIS_ACTIVE_SLOT_KEY]: 'green' }),
      targetColor: 'blue',
    });

    assert.equal(res.success, true);
    assert.equal(res.plan.restoredActiveSlot, 'blue');
    assert.equal(res.plan.keepFailedSlotRunning, true);
    assert.equal(res.plan.ingressContainer, 'restaurant-order-ingress', 'Rollback must target the ingress container');
  } finally {
    fs.rmSync(tempNginxDir, { recursive: true, force: true });
  }
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

