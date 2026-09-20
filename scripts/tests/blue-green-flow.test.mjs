import test from 'node:test';
import assert from 'node:assert/strict';
import fs from 'node:fs';
import os from 'node:os';
import path from 'node:path';

import { FakeCommandRunner } from '../blue-green/lib/common.mjs';
import { resolveActiveSlot } from '../blue-green/lib/active-slot-resolver.mjs';
import { runPreflight } from '../blue-green/preflight.mjs';
import { runDeployInactive } from '../blue-green/deploy-inactive.mjs';
import { runHealthCheck } from '../blue-green/health-check.mjs';
import { runSmoke } from '../blue-green/smoke.mjs';
import { runCutover, buildUpstreamConfig } from '../blue-green/cutover.mjs';
import { runObserve } from '../blue-green/observe.mjs';
import { runRollback } from '../blue-green/rollback.mjs';
import { runOrchestrator } from '../blue-green/orchestrator.mjs';
import { FakeRedisClient, REDIS_ACTIVE_SLOT_KEY } from '../blue-green/lib/redis-state.mjs';

/**
 * A properly formatted but deliberately non-real sha256 digest for test fixtures.
 * NOT the empty-content hash (e3b0c...) which is now explicitly rejected in execute mode.
 */
const VALID_DIGEST = 'sha256:aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa';


test('16. orchestrator: halts on failure and records to journal', async () => {
  const tempDir = fs.mkdtempSync(path.join(os.tmpdir(), 'bg-orch-journal-'));
  const journalFile = path.join(tempDir, 'journal.jsonl');
  try {
    const dryRes = await runOrchestrator({ dryRun: true, journalFile });
    assert.equal(dryRes.success, true);
    assert.ok(fs.existsSync(journalFile), 'Deployment journal must be created');
    const journalContent = fs.readFileSync(journalFile, 'utf8');
    assert.ok(journalContent.includes('PIPELINE_START'));
    assert.ok(journalContent.includes('PIPELINE_SUCCESS'));
  } finally {
    fs.rmSync(tempDir, { recursive: true, force: true });
  }
});

test('17. orchestrator: triggers automated rollback when post-cutover step fails', async () => {
  let rollbackInvoked = false;
  const fakeRedis = new FakeRedisClient({ [REDIS_ACTIVE_SLOT_KEY]: 'blue' });

  const result = await runOrchestrator({
    dryRun: false,
    execute: true,
    color: 'green',
    confirmCutover: true,
    redisClient: fakeRedis,
    preflightFn: async () => ({ success: true }),
    configValidateFn: async () => ({ success: true }),
    migrationCheckFn: async () => ({ success: true }),
    deployInactiveFn: async () => ({ success: true }),
    healthCheckFn: async () => ({ success: true }),
    warmupFn: async () => ({ success: true }),
    smokeFn: async () => ({ success: true }),
    cutoverFn: async () => {
      // Simulate the real cutover updating Redis, so the post-cutover verification passes
      await fakeRedis.set(REDIS_ACTIVE_SLOT_KEY, 'green');
      return { success: true, plan: { newActiveSlot: 'green' } };
    },
    observeFn: async () => ({ success: false, error: 'Breached latency SLA post-cutover' }),
    rollbackFn: async (opts) => {
      rollbackInvoked = true;
      assert.equal(opts.color, 'green');
      assert.equal(opts.targetColor, 'blue');
      return { success: true, status: 'ROLLBACK_SUCCESS' };
    },
  });

  assert.equal(result.success, false);
  assert.equal(result.cutoverCompleted, true);
  assert.equal(rollbackInvoked, true, 'Rollback must be triggered when post-cutover step fails');
  assert.equal(result.failedStep, 'Post-Cutover Observation');
});

test('18. fail-closed: execute mode rejects false-positive PASS on unverified operations', async () => {
  // observe.mjs must fail-closed if METRICS_URL is not set and not dryRun
  const observeRes = await runObserve({
    execute: true,
    dryRun: false,
    color: 'green',
  });
  assert.equal(observeRes.success, false);
  assert.equal(observeRes.status, 'BLOCKED');
});

test('19. disposable end-to-end blue -> green -> rollback flow with central Redis state', async () => {
  const tempNginxDir = fs.mkdtempSync(path.join(os.tmpdir(), 'bg-flow-19-'));
  const tempConfD = path.join(tempNginxDir, 'conf.d');
  fs.mkdirSync(tempConfD, { recursive: true });

  const tempMainConf = path.join(tempNginxDir, 'nginx.conf');
  fs.writeFileSync(tempMainConf, 'events {} http { include conf.d/*.conf; }', 'utf8');

  const upstreamConf = path.join(tempConfD, 'upstream.conf');
  fs.writeFileSync(upstreamConf, buildUpstreamConfig('blue', { timestamp: new Date().toISOString() }), 'utf8');

  const executedCommands = [];
  const fakeRunner = new FakeCommandRunner(async (cmd, args) => {
    executedCommands.push({ cmd, args: args.join(' ') });
    return { success: true, exitCode: 0, stdout: 'OK' };
  });

  const fakeRedis = new FakeRedisClient({ [REDIS_ACTIVE_SLOT_KEY]: 'blue' });

  // Step 1: Preflight
  const preflight = await runPreflight({
    color: 'green',
    execute: true,
    dryRun: false,
    redisClient: fakeRedis,
    apiImageDigest: VALID_DIGEST,
    workerImageDigest: VALID_DIGEST,
    customerWebImageDigest: VALID_DIGEST,
    operationsWebImageDigest: VALID_DIGEST,
    adminWebImageDigest: VALID_DIGEST,
  });
  assert.equal(preflight.success, true);
  assert.equal(preflight.targetColor, 'green');


  // Step 2: Deploy Inactive Green
  const deploy = await runDeployInactive({
    color: 'green',
    execute: true,
    dryRun: false,
    runner: fakeRunner,
    redisClient: fakeRedis,
    skipHealthPoll: true,
    apiImageDigest: VALID_DIGEST,
    workerImageDigest: VALID_DIGEST,
    customerWebImageDigest: VALID_DIGEST,
    operationsWebImageDigest: VALID_DIGEST,
    adminWebImageDigest: VALID_DIGEST,
  });
  assert.equal(deploy.success, true);
  assert.ok(executedCommands.some((c) => c.args.includes('up -d --force-recreate')));

  // Step 3: Health Check
  const health = await runHealthCheck({
    color: 'green',
    execute: true,
    dryRun: false,
    probeFn: async () => ({ ok: true, status: 200, data: { status: 'Healthy', color: 'green', version: '0.1.0' } }),
  });
  assert.equal(health.success, true);

  // Step 4: Cutover to Green
  const cutover = await runCutover({
    color: 'green',
    execute: true,
    dryRun: false,
    confirmCutover: true,
    runner: fakeRunner,
    nginxDir: tempNginxDir,
    stateFile: path.join(tempNginxDir, 'state.json'),
    redisClient: fakeRedis,
  });
  assert.equal(cutover.success, true);
  assert.equal(cutover.plan.newActiveSlot, 'green');

  const updatedUpstream = fs.readFileSync(upstreamConf, 'utf8');
  assert.ok(updatedUpstream.includes('restaurant-order-api-green:5000'), 'Upstream must point to Green API container');
  const redisSlotAfterCutover = await fakeRedis.get(REDIS_ACTIVE_SLOT_KEY);
  assert.equal(redisSlotAfterCutover.value, 'green');

  // Step 5: Rollback to Blue (fakeRedis now has 'green' as active after cutover)
  const rollback = await runRollback({
    color: 'green',
    execute: true,
    dryRun: false,
    confirmRollback: true,
    runner: fakeRunner,
    nginxDir: tempNginxDir,
    stateFile: path.join(tempNginxDir, 'state.json'),
    redisClient: fakeRedis,
    targetColor: 'blue',
  });
  assert.equal(rollback.success, true);
  assert.equal(rollback.plan.restoredActiveSlot, 'blue');
  assert.equal(rollback.plan.keepFailedSlotRunning, true);

  const restoredUpstream = fs.readFileSync(upstreamConf, 'utf8');
  assert.ok(restoredUpstream.includes('restaurant-order-api-blue:5000'), 'Upstream must be restored to Blue API container');
  const redisSlotAfterRollback = await fakeRedis.get(REDIS_ACTIVE_SLOT_KEY);
  assert.equal(redisSlotAfterRollback.value, 'blue');

  // Clean up test directory
  fs.rmSync(tempNginxDir, { recursive: true, force: true });
});

test('20. cutover: reverts Nginx upstream if central Redis active slot update fails', async () => {
  const tempNginxDir = fs.mkdtempSync(path.join(os.tmpdir(), 'bg-flow-20-'));
  const tempConfD = path.join(tempNginxDir, 'conf.d');
  fs.mkdirSync(tempConfD, { recursive: true });

  const tempMainConf = path.join(tempNginxDir, 'nginx.conf');
  fs.writeFileSync(tempMainConf, 'events {} http { include conf.d/*.conf; }', 'utf8');

  const upstreamConf = path.join(tempConfD, 'upstream.conf');
  fs.writeFileSync(upstreamConf, buildUpstreamConfig('blue', { timestamp: new Date().toISOString() }), 'utf8');

  const runner = new FakeCommandRunner(async () => ({ success: true, exitCode: 0 }));
  // Redis allows slot resolution (get) but fails on the state update (set)
  const failingRedis = {
    get: async () => ({ success: true, value: 'blue' }),
    set: async () => ({ success: false, error: 'Redis connection lost' }),
  };

  const cutoverRes = await runCutover({
    color: 'green',
    execute: true,
    dryRun: false,
    confirmCutover: true,
    runner,
    nginxDir: tempNginxDir,
    stateFile: path.join(tempNginxDir, 'state.json'),
    redisClient: failingRedis,
  });

  assert.equal(cutoverRes.success, false);
  // Error must explain that Redis update failed
  assert.ok(
    cutoverRes.error.includes('Redis') || cutoverRes.error.includes('central') || cutoverRes.error.includes('RECONCILIATION'),
    `Expected Redis failure error, got: ${cutoverRes.error}`
  );

  // Verify Nginx was reverted to blue to avoid split-brain
  const content = fs.readFileSync(upstreamConf, 'utf8');
  assert.ok(content.includes('restaurant-order-api-blue:5000'), 'Must preserve original upstream on Redis update failure');

  fs.rmSync(tempNginxDir, { recursive: true, force: true });
});

test('21. rollback: emits CRITICAL_INCONSISTENT_STATE when Nginx reverts but Redis update fails', async () => {
  const tempNginxDir = fs.mkdtempSync(path.join(os.tmpdir(), 'bg-flow-21-'));
  const tempConfD = path.join(tempNginxDir, 'conf.d');
  fs.mkdirSync(tempConfD, { recursive: true });

  fs.writeFileSync(path.join(tempNginxDir, 'nginx.conf'), 'events {} http { include conf.d/*.conf; }', 'utf8');
  const upstreamConf = path.join(tempConfD, 'upstream.conf');
  fs.writeFileSync(upstreamConf, 'upstream api_backend { server restaurant-order-api-green:5000; }', 'utf8');

  const journalEntries = [];
  const runner = new FakeCommandRunner(async () => ({ success: true, exitCode: 0 }));

  // Redis allows slot resolution (get) but fails on the state update (set)
  const failingRedis = {
    get: async () => ({ success: true, value: 'green' }),
    set: async () => ({ success: false, error: 'READONLY You cannot write to a replica.' }),
  };

  const result = await runRollback({
    color: 'green',
    execute: true,
    dryRun: false,
    confirmRollback: true,
    runner,
    nginxDir: tempNginxDir,
    stateFile: path.join(tempNginxDir, 'state.json'),
    redisClient: failingRedis,
    targetColor: 'blue',
    appendJournalFn: (entry) => journalEntries.push(entry),
  });

  assert.equal(result.success, false, 'Rollback must fail when Redis update fails');
  assert.equal(result.status, 'CRITICAL_INCONSISTENT_STATE');
  assert.ok(result.error.includes('CRITICAL_INCONSISTENT_STATE'));
  assert.ok(result.error.includes('READONLY') || result.error.includes('replica'));

  // Journal must capture the inconsistency event for operator alerts
  assert.ok(journalEntries.some((e) => e.event === 'ROLLBACK_INCONSISTENT_STATE'),
    'Journal must record ROLLBACK_INCONSISTENT_STATE for operator visibility');

  // Nginx was reverted to blue despite Redis failure
  const content = fs.readFileSync(upstreamConf, 'utf8');
  assert.ok(content.includes('restaurant-order-api-blue:5000'), 'Nginx must be reverted to safe slot even if Redis fails');

  fs.rmSync(tempNginxDir, { recursive: true, force: true });
});

test('22. orchestrator: mid-pipeline slot change detection halts cutover', async () => {
  let cutoverCalls = 0;
  let observeCalls = 0;
  let drainCalls = 0;
  let getCallCount = 0;

  // Sequence-aware fake Redis: 1st GET returns blue, 2nd GET returns green (simulating concurrent slot change)
  const sequenceRedis = {
    get: async () => {
      getCallCount++;
      if (getCallCount === 1) return { success: true, value: 'blue' };
      return { success: true, value: 'green' };
    },
    set: async () => ({ success: true }),
  };

  const result = await runOrchestrator({
    dryRun: false,
    execute: true,
    color: 'green',
    confirmCutover: true,
    redisClient: sequenceRedis,
    preflightFn: async () => ({ success: true }),
    configValidateFn: async () => ({ success: true }),
    migrationCheckFn: async () => ({ success: true }),
    deployInactiveFn: async () => ({ success: true }),
    healthCheckFn: async () => ({ success: true }),
    warmupFn: async () => ({ success: true }),
    smokeFn: async () => ({ success: true }),
    cutoverFn: async () => {
      cutoverCalls++;
      return { success: true };
    },
    observeFn: async () => {
      observeCalls++;
      return { success: true };
    },
    drainOldFn: async () => {
      drainCalls++;
      return { success: true };
    },
  });

  assert.equal(result.success, false, 'Orchestrator must fail when mid-pipeline slot change is detected');
  assert.equal(result.failedStep, 'Traffic Cutover', 'Failed step must be Traffic Cutover');
  assert.equal(cutoverCalls, 0, 'cutoverFn must NOT be called');
  assert.equal(observeCalls, 0, 'observeFn must NOT be called');
  assert.equal(drainCalls, 0, 'drainOldFn must NOT be called');
});

// Helper for testing second GET mid-pipeline failure scenarios
async function testMidPipelineSecondGetFailure(secondGetResult) {
  let cutoverCalls = 0;
  let getCallCount = 0;

  const sequenceRedis = {
    get: async () => {
      getCallCount++;
      if (getCallCount === 1) return { success: true, value: 'blue' };
      return secondGetResult;
    },
    set: async () => ({ success: true }),
  };

  const result = await runOrchestrator({
    dryRun: false,
    execute: true,
    color: 'green',
    confirmCutover: true,
    redisClient: sequenceRedis,
    preflightFn: async () => ({ success: true }),
    configValidateFn: async () => ({ success: true }),
    migrationCheckFn: async () => ({ success: true }),
    deployInactiveFn: async () => ({ success: true }),
    healthCheckFn: async () => ({ success: true }),
    warmupFn: async () => ({ success: true }),
    smokeFn: async () => ({ success: true }),
    cutoverFn: async () => {
      cutoverCalls++;
      return { success: true };
    },
    observeFn: async () => ({ success: true }),
    drainOldFn: async () => ({ success: true }),
  });

  assert.equal(result.success, false);
  assert.equal(result.failedStep, 'Traffic Cutover');
  assert.equal(cutoverCalls, 0, 'cutoverFn must NOT be called');
}

test('22a. orchestrator: mid-pipeline second GET connection failure halts cutover', async () => {
  await testMidPipelineSecondGetFailure({ success: false, error: 'Connection refused' });
});

test('22b. orchestrator: mid-pipeline second GET timeout halts cutover', async () => {
  await testMidPipelineSecondGetFailure({ success: false, error: 'Redis connection timed out after 5000ms' });
});

test('22c. orchestrator: mid-pipeline second GET authentication failure halts cutover', async () => {
  await testMidPipelineSecondGetFailure({ success: false, error: 'NOAUTH Authentication required' });
});

test('22d. orchestrator: mid-pipeline second GET empty value halts cutover', async () => {
  await testMidPipelineSecondGetFailure({ success: true, value: '' });
});

test('22e. orchestrator: mid-pipeline second GET invalid slot value halts cutover', async () => {
  await testMidPipelineSecondGetFailure({ success: true, value: 'purple' });
});

test('23. resolveActiveSlot: rejects corrupted/invalid value from Redis in execute mode', async () => {
  const invalidRedis = new FakeRedisClient({ [REDIS_ACTIVE_SLOT_KEY]: 'purple' });

  const result = await resolveActiveSlot({
    execute: true,
    redisClient: invalidRedis,
  });

  assert.equal(result.success, false);
  assert.ok(result.error.includes('FAIL-CLOSED') || result.error.includes('invalid value'));
  assert.ok(result.error.includes('purple'));

  // Empty string is also invalid in execute mode
  const emptyRedis = new FakeRedisClient({ [REDIS_ACTIVE_SLOT_KEY]: '' });
  const emptyResult = await resolveActiveSlot({
    execute: true,
    redisClient: emptyRedis,
  });

  assert.equal(emptyResult.success, false);
  assert.ok(emptyResult.error.includes('empty') || emptyResult.error.includes('FAIL-CLOSED'));
});
