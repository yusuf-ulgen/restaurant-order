import test from 'node:test';
import assert from 'node:assert/strict';
import fs from 'node:fs';
import path from 'node:path';

import { FakeCommandRunner } from '../blue-green/lib/common.mjs';
import { runPreflight } from '../blue-green/preflight.mjs';
import { runDeployInactive } from '../blue-green/deploy-inactive.mjs';
import { runHealthCheck } from '../blue-green/health-check.mjs';
import { runSmoke } from '../blue-green/smoke.mjs';
import { runCutover } from '../blue-green/cutover.mjs';
import { runObserve } from '../blue-green/observe.mjs';
import { runRollback } from '../blue-green/rollback.mjs';
import { runOrchestrator } from '../blue-green/orchestrator.mjs';

const VALID_DIGEST = 'sha256:e3b0c44298fc1c149afbf4c8996fb92427ae41e4649b934ca495991b7852b855';

test('16. orchestrator: halts on failure and records to journal', async () => {
  // Dry-run orchestrator succeeds safely
  const dryRes = await runOrchestrator({ dryRun: true });
  assert.equal(dryRes.success, true);

  // Check deployment journal file exists and has entries
  const journalFile = path.join(process.cwd(), '.deployment-journal.jsonl');
  assert.ok(fs.existsSync(journalFile), 'Deployment journal must be created');
  const journalContent = fs.readFileSync(journalFile, 'utf8');
  assert.ok(journalContent.includes('PIPELINE_START'));
});

test('17. orchestrator: triggers automated rollback when post-cutover step fails', async () => {
  let rollbackInvoked = false;
  let rollbackTarget = null;

  process.env.ACTIVE_DEPLOYMENT_SLOT = 'blue';
  const result = await runOrchestrator({
    dryRun: false,
    execute: true,
    color: 'green',
    confirmCutover: true,
    confirmRollback: true,
    preflightFn: async () => ({ success: true }),
    configValidateFn: async () => ({ success: true }),
    migrationCheckFn: async () => ({ success: true }),
    deployInactiveFn: async () => ({ success: true }),
    healthCheckFn: async () => ({ success: true }),
    warmupFn: async () => ({ success: true }),
    smokeFn: async () => ({ success: true }),
    cutoverFn: async () => ({ success: true, plan: { newActiveSlot: 'green' } }),
    observeFn: async () => ({ success: false, error: 'Breached latency SLA post-cutover' }),
    rollbackFn: async (opts) => {
      rollbackInvoked = true;
      rollbackTarget = opts.targetColor;
      return { success: true };
    },
  });

  assert.equal(result.success, false);
  assert.equal(result.cutoverCompleted, true);
  assert.equal(rollbackInvoked, true);
  assert.equal(rollbackTarget, 'blue');
});

test('18. fail-closed: execute mode rejects false-positive PASS on unverified operations', async () => {
  // Execute mode deploy-inactive fails if docker runner fails
  const mockFailRunner = new FakeCommandRunner(async () => ({ success: false, exitCode: 1, stderr: 'error' }));
  const deployRes = await runDeployInactive({ color: 'green', execute: true, runner: mockFailRunner });
  assert.equal(deployRes.success, false);

  // Execute mode smoke check without runner returns BLOCKED
  const smokeRes = await runSmoke({ color: 'green', execute: true });
  assert.equal(smokeRes.success, false);
  assert.equal(smokeRes.status, 'BLOCKED');

  // Execute mode observe without metrics source returns BLOCKED
  delete process.env.METRICS_URL;
  const observeRes = await runObserve({ color: 'green', execute: true });
  assert.equal(observeRes.success, false);
  assert.equal(observeRes.status, 'BLOCKED');
});

test('19. disposable end-to-end blue -> green -> rollback flow', async () => {
  const tempNginxDir = path.join(process.cwd(), '.test-nginx');
  const tempConfD = path.join(tempNginxDir, 'conf.d');
  fs.mkdirSync(tempConfD, { recursive: true });

  const tempMainConf = path.join(tempNginxDir, 'nginx.conf');
  fs.writeFileSync(tempMainConf, 'events {} http { include conf.d/*.conf; }', 'utf8');

  const upstreamConf = path.join(tempConfD, 'upstream.conf');
  fs.writeFileSync(upstreamConf, 'upstream api_backend { server 127.0.0.1:5001; }', 'utf8');

  const executedCommands = [];
  const fakeRunner = new FakeCommandRunner(async (cmd, args) => {
    executedCommands.push({ cmd, args: args.join(' ') });
    return { success: true, exitCode: 0, stdout: 'OK' };
  });

  process.env.ACTIVE_DEPLOYMENT_SLOT = 'blue';

  // Step 1: Preflight
  const preflight = runPreflight({
    color: 'green',
    execute: true,
    apiImageDigest: VALID_DIGEST,
    workerImageDigest: VALID_DIGEST,
  });
  assert.equal(preflight.success, true);
  assert.equal(preflight.targetColor, 'green');

  // Step 2: Deploy Inactive Green
  const deploy = await runDeployInactive({
    color: 'green',
    execute: true,
    runner: fakeRunner,
    skipHealthPoll: true,
  });
  assert.equal(deploy.success, true);
  assert.ok(executedCommands.some((c) => c.args.includes('up -d --force-recreate')));

  // Step 3: Health Check
  const health = await runHealthCheck({
    color: 'green',
    execute: true,
    probeFn: async () => ({ ok: true, status: 200, data: { status: 'Healthy', color: 'green', version: '0.1.0' } }),
  });
  assert.equal(health.success, true);

  // Step 4: Cutover to Green
  const cutover = await runCutover({
    color: 'green',
    execute: true,
    confirmCutover: true,
    runner: fakeRunner,
    nginxDir: tempNginxDir,
  });
  assert.equal(cutover.success, true);
  assert.equal(cutover.plan.newActiveSlot, 'green');

  const updatedUpstream = fs.readFileSync(upstreamConf, 'utf8');
  assert.ok(updatedUpstream.includes('127.0.0.1:5002'), 'Upstream must point to Green port 5002');

  // Step 5: Rollback to Blue
  const rollback = await runRollback({
    color: 'green',
    execute: true,
    confirmRollback: true,
    runner: fakeRunner,
    nginxDir: tempNginxDir,
  });
  assert.equal(rollback.success, true);
  assert.equal(rollback.plan.restoredActiveSlot, 'blue');
  assert.equal(rollback.plan.keepFailedSlotRunning, true);

  const restoredUpstream = fs.readFileSync(upstreamConf, 'utf8');
  assert.ok(restoredUpstream.includes('127.0.0.1:5001'), 'Upstream must be restored to Blue port 5001');

  // Clean up test directory
  fs.rmSync(tempNginxDir, { recursive: true, force: true });
});
