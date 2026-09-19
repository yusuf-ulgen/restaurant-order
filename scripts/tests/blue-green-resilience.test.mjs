import test from 'node:test';
import assert from 'node:assert/strict';
import fs from 'node:fs';
import os from 'node:os';
import path from 'node:path';

import { FakeCommandRunner } from '../blue-green/lib/common.mjs';
import { FakeRedisClient, REDIS_ACTIVE_SLOT_KEY } from '../blue-green/lib/redis-state.mjs';
import { runDrainOld } from '../blue-green/drain-old.mjs';
import { runCutover } from '../blue-green/cutover.mjs';
import { runRollback } from '../blue-green/rollback.mjs';

function createTempNginxDir() {
  const tempDir = fs.mkdtempSync(path.join(os.tmpdir(), 'bg-resilience-'));
  const confD = path.join(tempDir, 'conf.d');
  fs.mkdirSync(confD, { recursive: true });
  fs.writeFileSync(path.join(tempDir, 'nginx.conf'), 'events {} http { include conf.d/*.conf; }', 'utf8');
  fs.writeFileSync(
    path.join(confD, 'upstream.conf'),
    'upstream api_backend { server restaurant-order-api-blue:5000; }\n',
    'utf8'
  );
  return { tempDir, confD, upstreamConf: path.join(confD, 'upstream.conf') };
}

test('resilience: drain-old stops and verifies all 5 containers exited', async () => {
  const inspectCalls = [];
  const fakeRunner = new FakeCommandRunner(async (cmd, args) => {
    if (cmd === 'docker' && args[0] === 'compose' && args.includes('stop')) {
      return { success: true, exitCode: 0, stdout: 'Stopped' };
    }
    if (cmd === 'docker' && args[0] === 'inspect') {
      inspectCalls.push(args[args.length - 1]);
      return { success: true, exitCode: 0, stdout: 'exited\n' };
    }
    return { success: true, exitCode: 0 };
  });

  const result = await runDrainOld({
    color: 'blue',
    execute: true,
    dryRun: false,
    drainTimeoutSec: 0,
    runner: fakeRunner,
  });

  assert.equal(result.success, true);
  assert.equal(result.plan.drainingSlot, 'blue');
  assert.equal(result.plan.composeProject, 'restaurant-order-blue');
  assert.equal(inspectCalls.length, 5);
  assert.deepEqual(inspectCalls, [
    'restaurant-order-api-blue',
    'restaurant-order-worker-blue',
    'restaurant-order-customer-web-blue',
    'restaurant-order-operations-web-blue',
    'restaurant-order-admin-web-blue',
  ]);
});

test('resilience: drain-old fails-closed if any of the 5 containers is still running', async () => {
  const fakeRunner = new FakeCommandRunner(async (cmd, args) => {
    if (cmd === 'docker' && args[0] === 'compose' && args.includes('stop')) {
      return { success: true, exitCode: 0, stdout: 'Stopped' };
    }
    if (cmd === 'docker' && args[0] === 'inspect') {
      const target = args[args.length - 1];
      if (target === 'restaurant-order-worker-blue') {
        return { success: true, exitCode: 0, stdout: 'running\n' };
      }
      return { success: true, exitCode: 0, stdout: 'exited\n' };
    }
    return { success: true, exitCode: 0 };
  });

  const result = await runDrainOld({
    color: 'blue',
    execute: true,
    dryRun: false,
    drainTimeoutSec: 0,
    runner: fakeRunner,
  });

  assert.equal(result.success, false);
  assert.ok(result.error.includes('restaurant-order-worker-blue'));
  assert.ok(result.error.includes('still active'));
});

test('resilience: drain-old fails-closed if docker inspect fails', async () => {
  const fakeRunner = new FakeCommandRunner(async (cmd, args) => {
    if (cmd === 'docker' && args[0] === 'compose' && args.includes('stop')) {
      return { success: true, exitCode: 0 };
    }
    if (cmd === 'docker' && args[0] === 'inspect') {
      return { success: false, exitCode: 1, stderr: 'No such object: restaurant-order-api-blue' };
    }
    return { success: true, exitCode: 0 };
  });

  const result = await runDrainOld({
    color: 'blue',
    execute: true,
    dryRun: false,
    drainTimeoutSec: 0,
    runner: fakeRunner,
  });

  assert.equal(result.success, false);
  assert.ok(result.error.includes('Failed to inspect'));
});

test('resilience: drain-old preserves containers when keepFailedSlotRunning is set', async () => {
  let stopCalled = false;
  const fakeRunner = new FakeCommandRunner(async (cmd, args) => {
    if (cmd === 'docker' && args.includes('stop')) {
      stopCalled = true;
    }
    return { success: true, exitCode: 0 };
  });

  const result = await runDrainOld({
    color: 'blue',
    execute: true,
    dryRun: false,
    keepFailedSlotRunning: true,
    runner: fakeRunner,
  });

  assert.equal(result.success, true);
  assert.equal(result.preserved, true);
  assert.equal(stopCalled, false, 'docker stop must NOT be called when keeping failed slot');
});

test('resilience: cutover candidate config failure leaves live upstream.conf untouched', async () => {
  const { tempDir, upstreamConf } = createTempNginxDir();
  const initialContent = fs.readFileSync(upstreamConf, 'utf8');

  try {
    const fakeRunner = new FakeCommandRunner(async (cmd, args) => {
      // Simulate inspect ok
      if (cmd === 'docker' && args[0] === 'inspect') {
        return { success: true, exitCode: 0, stdout: 'running\n' };
      }
      // Simulate candidate config nginx -t failing
      if (cmd === 'docker' && args.includes('exec') && args.includes('-t')) {
        return { success: false, exitCode: 1, stderr: 'nginx: [emerg] invalid parameter in candidate config' };
      }
      return { success: true, exitCode: 0 };
    });

    const result = await runCutover({
      color: 'green',
      execute: true,
      dryRun: false,
      confirmCutover: true,
      runner: fakeRunner,
      nginxDir: tempDir,
      stateFile: path.join(tempDir, 'state.json'),
      redisClient: new FakeRedisClient({ [REDIS_ACTIVE_SLOT_KEY]: 'blue' }),
    });

    assert.equal(result.success, false);
    assert.ok(result.error.includes('validation failed'));

    // Live upstream.conf must remain unchanged!
    const liveContent = fs.readFileSync(upstreamConf, 'utf8');
    assert.equal(liveContent, initialContent);
  } finally {
    fs.rmSync(tempDir, { recursive: true, force: true });
  }
});

test('resilience: cutover restores live upstream from backup when reload fails', async () => {
  const { tempDir, upstreamConf } = createTempNginxDir();
  const initialContent = fs.readFileSync(upstreamConf, 'utf8');

  try {
    const fakeRunner = new FakeCommandRunner(async (cmd, args) => {
      if (cmd === 'docker' && args[0] === 'inspect') {
        return { success: true, exitCode: 0, stdout: 'running\n' };
      }
      // Candidate nginx -t succeeds
      if (cmd === 'docker' && args.includes('-t')) {
        return { success: true, exitCode: 0, stdout: 'syntax is ok\n' };
      }
      // Reload fails
      if (cmd === 'docker' && args.includes('-s') && args.includes('reload')) {
        return { success: false, exitCode: 1, stderr: 'nginx reload failed' };
      }
      return { success: true, exitCode: 0 };
    });

    const result = await runCutover({
      color: 'green',
      execute: true,
      dryRun: false,
      confirmCutover: true,
      runner: fakeRunner,
      nginxDir: tempDir,
      stateFile: path.join(tempDir, 'state.json'),
      redisClient: new FakeRedisClient({ [REDIS_ACTIVE_SLOT_KEY]: 'blue' }),
    });

    assert.equal(result.success, false);
    assert.ok(result.error.includes('reload failed'));

    // Verify backup restoration
    const restoredContent = fs.readFileSync(upstreamConf, 'utf8');
    assert.equal(restoredContent, initialContent);
  } finally {
    fs.rmSync(tempDir, { recursive: true, force: true });
  }
});

test('resilience: rollback fails-closed without Redis unless emergency override is set', async () => {
  const { tempDir } = createTempNginxDir();

  try {
    const result = await runRollback({
      color: 'green',
      execute: true,
      dryRun: false,
      confirmRollback: true,
      nginxDir: tempDir,
      stateFile: path.join(tempDir, 'state.json'),
      redisClient: {
        get: async () => ({ success: false, error: 'Connection refused' }),
        set: async () => ({ success: false, error: 'Connection refused' }),
      },
    });

    assert.equal(result.success, false);
    assert.equal(result.status, 'BLOCKED_NO_CENTRAL_STATE');
    assert.ok(result.error.includes('--emergency-override'));
  } finally {
    fs.rmSync(tempDir, { recursive: true, force: true });
  }
});

test('resilience: rollback succeeds with --emergency-override even during Redis outage', async () => {
  const { tempDir, upstreamConf } = createTempNginxDir();
  const stateFile = path.join(tempDir, 'state.json');
  fs.writeFileSync(stateFile, JSON.stringify({ activeSlot: 'green', previousActiveSlot: 'blue' }), 'utf8');

  try {
    const fakeRunner = new FakeCommandRunner(async (cmd, args) => {
      if (cmd === 'docker' && args.includes('exec')) {
        return { success: true, exitCode: 0 };
      }
      return { success: true, exitCode: 0 };
    });

    const result = await runRollback({
      color: 'green',
      execute: true,
      dryRun: false,
      confirmRollback: true,
      emergencyOverride: true,
      runner: fakeRunner,
      nginxDir: tempDir,
      stateFile,
      targetColor: 'blue',
      redisClient: {
        get: async () => ({ success: false, error: 'Redis completely offline' }),
        set: async () => ({ success: false, error: 'Redis completely offline' }),
      },
    });

    assert.equal(result.success, true);
    assert.equal(result.status, 'EMERGENCY_OVERRIDE_SUCCESS');
    assert.equal(result.plan.restoredActiveSlot, 'blue');

    const updatedUpstream = fs.readFileSync(upstreamConf, 'utf8');
    assert.ok(updatedUpstream.includes('restaurant-order-api-blue:5000'));
  } finally {
    fs.rmSync(tempDir, { recursive: true, force: true });
  }
});
