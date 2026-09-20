import test from 'node:test';
import assert from 'node:assert/strict';
import fs from 'node:fs';
import os from 'node:os';
import path from 'node:path';

import { FakeCommandRunner, parseArgs } from '../blue-green/lib/common.mjs';
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
    'upstream api_backend { server restaurant-order-api-blue:5000; }\n' +
    'upstream customer_web_backend { server restaurant-order-customer-web-blue:8080; }\n' +
    'upstream operations_web_backend { server restaurant-order-operations-web-blue:8080; }\n' +
    'upstream admin_web_backend { server restaurant-order-admin-web-blue:8080; }\n',
    'utf8'
  );
  return { tempDir, confD, upstreamConf: path.join(confD, 'upstream.conf') };
}

async function withTempNginx(fn) {
  const { tempDir, confD, upstreamConf } = createTempNginxDir();
  const initialContent = fs.readFileSync(upstreamConf, 'utf8');
  try {
    await fn({ tempDir, confD, upstreamConf, initialContent });
  } finally {
    fs.rmSync(tempDir, { recursive: true, force: true });
  }
}

const cutoverOpts = (tempDir, extra = {}) => ({
  color: 'green', execute: true, dryRun: false, confirmCutover: true,
  nginxDir: tempDir, stateFile: path.join(tempDir, 'state.json'), ...extra,
});

test('resilience: drain-old stops and verifies all 5 containers exited', async () => {
  const inspectCalls = [];
  const fakeRunner = new FakeCommandRunner(async (cmd, args) => {
    if (cmd === 'docker' && args[0] === 'compose' && args.includes('stop')) return { success: true, exitCode: 0, stdout: 'Stopped' };
    if (cmd === 'docker' && args[0] === 'inspect') {
      inspectCalls.push(args[args.length - 1]);
      return { success: true, exitCode: 0, stdout: 'exited\n' };
    }
    return { success: true, exitCode: 0 };
  });

  const result = await runDrainOld({
    color: 'blue', execute: true, dryRun: false, drainTimeoutSec: 0, runner: fakeRunner,
  });

  assert.equal(result.success, true);
  assert.equal(result.plan.drainingSlot, 'blue');
  assert.equal(result.plan.composeProject, 'restaurant-order-blue');
  assert.equal(inspectCalls.length, 5);
});

test('resilience: drain-old fails-closed if any of the 5 containers is still running', async () => {
  const fakeRunner = new FakeCommandRunner(async (cmd, args) => {
    if (cmd === 'docker' && args[0] === 'compose' && args.includes('stop')) return { success: true, exitCode: 0, stdout: 'Stopped' };
    if (cmd === 'docker' && args[0] === 'inspect') {
      const target = args[args.length - 1];
      if (target === 'restaurant-order-worker-blue') return { success: true, exitCode: 0, stdout: 'running\n' };
      return { success: true, exitCode: 0, stdout: 'exited\n' };
    }
    return { success: true, exitCode: 0 };
  });

  const result = await runDrainOld({
    color: 'blue', execute: true, dryRun: false, drainTimeoutSec: 0, runner: fakeRunner,
  });

  assert.equal(result.success, false);
  assert.ok(result.error.includes('restaurant-order-worker-blue'));
  assert.ok(result.error.includes('still active'));
});

test('resilience: cutover fails-closed if no valid upstream.conf exists in execute mode', async () => {
  const tempDir = fs.mkdtempSync(path.join(os.tmpdir(), 'bg-cutover-no-upstream-'));
  try {
    const result = await runCutover(cutoverOpts(tempDir, {
      redisClient: new FakeRedisClient({ [REDIS_ACTIVE_SLOT_KEY]: 'blue' }),
    }));
    assert.equal(result.success, false);
    assert.equal(result.status, 'BOOTSTRAP_REQUIRED');
    assert.ok(result.error.includes('BOOTSTRAP_REQUIRED'));
  } finally {
    fs.rmSync(tempDir, { recursive: true, force: true });
  }
});

test('resilience: cutover candidate config failure leaves live upstream.conf untouched', async () => {
  await withTempNginx(async ({ tempDir, upstreamConf, initialContent }) => {
    const fakeRunner = new FakeCommandRunner(async (cmd, args) => {
      if (cmd === 'docker' && args[0] === 'inspect') return { success: true, exitCode: 0, stdout: 'running\n' };
      if (cmd === 'docker' && args.includes('exec') && args.includes('-t')) {
        return { success: false, exitCode: 1, stderr: 'nginx: [emerg] invalid candidate' };
      }
      return { success: true, exitCode: 0 };
    });

    const result = await runCutover(cutoverOpts(tempDir, {
      runner: fakeRunner, redisClient: new FakeRedisClient({ [REDIS_ACTIVE_SLOT_KEY]: 'blue' }),
    }));

    assert.equal(result.success, false);
    assert.equal(fs.readFileSync(upstreamConf, 'utf8'), initialContent);
    assert.equal(fs.existsSync(path.join(tempDir, 'conf.d/upstream.conf.candidate')), false);
  });
});

test('resilience: cutover restores live upstream from backup when reload fails', async () => {
  await withTempNginx(async ({ tempDir, upstreamConf, initialContent }) => {
    let reloadCount = 0;
    const fakeRunner = new FakeCommandRunner(async (cmd, args) => {
      if (cmd === 'docker' && args[0] === 'inspect') return { success: true, exitCode: 0, stdout: 'running\n' };
      if (cmd === 'docker' && args.includes('-t')) return { success: true, exitCode: 0, stdout: 'ok\n' };
      if (cmd === 'docker' && args.includes('-s') && args.includes('reload')) {
        reloadCount++;
        if (reloadCount === 1) return { success: false, exitCode: 1, stderr: 'reload failed' };
        return { success: true, exitCode: 0 };
      }
      return { success: true, exitCode: 0 };
    });

    const result = await runCutover(cutoverOpts(tempDir, {
      runner: fakeRunner, redisClient: new FakeRedisClient({ [REDIS_ACTIVE_SLOT_KEY]: 'blue' }),
    }));

    assert.equal(result.success, false);
    assert.equal(result.status, 'RELOAD_FAILED');
    assert.equal(fs.readFileSync(upstreamConf, 'utf8'), initialContent);
    assert.equal(fs.existsSync(path.join(tempDir, 'conf.d/upstream.conf.backup')), false);
  });
});

test('resilience: cutover reconciles to previous slot when Redis SET fails', async () => {
  await withTempNginx(async ({ tempDir, upstreamConf, initialContent }) => {
    const fakeRunner = new FakeCommandRunner(async () => ({ success: true, exitCode: 0 }));
    let redisCalls = 0;
    const failingRedis = {
      get: async () => ({ success: true, value: 'blue' }),
      set: async (key, val) => {
        redisCalls++;
        if (val === 'green') return { success: false, error: 'Redis connection refused' };
        return { success: true };
      },
    };

    const result = await runCutover(cutoverOpts(tempDir, { runner: fakeRunner, redisClient: failingRedis }));

    assert.equal(result.success, false);
    assert.equal(result.status, 'RECONCILED_TO_PREVIOUS_SLOT');
    assert.equal(result.reconciled, true);
    assert.equal(fs.readFileSync(upstreamConf, 'utf8'), initialContent);
    assert.equal(fs.existsSync(path.join(tempDir, 'conf.d/upstream.conf.backup')), false);
    assert.equal(fs.existsSync(path.join(tempDir, 'conf.d/upstream.conf.candidate')), false);
  });
});

test('resilience: cutover reconciles to previous slot when Redis read-back mismatches', async () => {
  await withTempNginx(async ({ tempDir, upstreamConf, initialContent }) => {
    const fakeRunner = new FakeCommandRunner(async () => ({ success: true, exitCode: 0 }));
    let getCallCount = 0;
    const mismatchRedis = {
      get: async () => {
        getCallCount++;
        return { success: true, value: 'blue' };
      },
      set: async () => ({ success: true }),
    };

    const result = await runCutover(cutoverOpts(tempDir, { runner: fakeRunner, redisClient: mismatchRedis }));

    assert.equal(result.success, false);
    assert.equal(result.status, 'RECONCILED_TO_PREVIOUS_SLOT');
    assert.equal(result.reconciled, true);
    assert.equal(fs.readFileSync(upstreamConf, 'utf8'), initialContent);
    assert.equal(fs.existsSync(path.join(tempDir, 'conf.d/upstream.conf.backup')), false);
  });
});

test('resilience: cutover emits CRITICAL_INCONSISTENT_STATE when Nginx restore validation fails', async () => {
  await withTempNginx(async ({ tempDir }) => {
    let validateCount = 0;
    const fakeRunner = new FakeCommandRunner(async (cmd, args) => {
      if (cmd === 'docker' && args.includes('-t')) {
        validateCount++;
        if (validateCount > 1) return { success: false, exitCode: 1, stderr: 'restore syntax error' };
        return { success: true, exitCode: 0 };
      }
      return { success: true, exitCode: 0 };
    });

    const failingRedis = {
      get: async () => ({ success: true, value: 'blue' }),
      set: async () => ({ success: false, error: 'Redis write error' }),
    };

    const result = await runCutover(cutoverOpts(tempDir, { runner: fakeRunner, redisClient: failingRedis }));

    assert.equal(result.success, false);
    assert.equal(result.status, 'CRITICAL_INCONSISTENT_STATE');
    assert.equal(result.requiresManualReconciliation, true);
  });
});

test('resilience: cutover emits CRITICAL_INCONSISTENT_STATE when Nginx restore reload fails', async () => {
  await withTempNginx(async ({ tempDir }) => {
    let reloadCount = 0;
    const fakeRunner = new FakeCommandRunner(async (cmd, args) => {
      if (cmd === 'docker' && args.includes('-s') && args.includes('reload')) {
        reloadCount++;
        if (reloadCount > 1) return { success: false, exitCode: 1, stderr: 'restore reload error' };
        return { success: true, exitCode: 0 };
      }
      return { success: true, exitCode: 0 };
    });

    const failingRedis = {
      get: async () => ({ success: true, value: 'blue' }),
      set: async () => ({ success: false, error: 'Redis write error' }),
    };

    const result = await runCutover(cutoverOpts(tempDir, { runner: fakeRunner, redisClient: failingRedis }));

    assert.equal(result.success, false);
    assert.equal(result.status, 'CRITICAL_INCONSISTENT_STATE');
    assert.equal(result.requiresManualReconciliation, true);
  });
});

test('resilience: cutover emits CRITICAL_INCONSISTENT_STATE when Redis revert to previous slot fails', async () => {
  await withTempNginx(async ({ tempDir }) => {
    const fakeRunner = new FakeCommandRunner(async () => ({ success: true, exitCode: 0 }));
    const failingRedis = {
      get: async () => ({ success: true, value: 'blue' }),
      set: async () => ({ success: false, error: 'Redis permanently offline' }),
    };

    const result = await runCutover(cutoverOpts(tempDir, { runner: fakeRunner, redisClient: failingRedis }));

    assert.equal(result.success, false);
    assert.equal(result.status, 'CRITICAL_INCONSISTENT_STATE');
    assert.equal(result.requiresManualReconciliation, true);
  });
});

test('resilience: cutover reports STATE_PERSISTENCE_FAILED when state temp write fails', async () => {
  await withTempNginx(async ({ tempDir }) => {
    const fakeRunner = new FakeCommandRunner(async () => ({ success: true, exitCode: 0 }));
    const fakeRedis = new FakeRedisClient({ [REDIS_ACTIVE_SLOT_KEY]: 'blue' });

    const result = await runCutover(cutoverOpts(tempDir, {
      runner: fakeRunner, redisClient: fakeRedis,
      stateWriteFn: () => { throw new Error('EACCES permission denied'); },
    }));

    assert.equal(result.success, false);
    assert.equal(result.status, 'STATE_PERSISTENCE_FAILED');
    assert.equal(result.requiresManualReconciliation, true);
    const activeInRedis = await fakeRedis.get(REDIS_ACTIVE_SLOT_KEY);
    assert.equal(activeInRedis.value, 'green');
  });
});

test('resilience: cutover reports STATE_PERSISTENCE_FAILED when state atomic rename fails', async () => {
  await withTempNginx(async ({ tempDir }) => {
    const fakeRunner = new FakeCommandRunner(async () => ({ success: true, exitCode: 0 }));
    const fakeRedis = new FakeRedisClient({ [REDIS_ACTIVE_SLOT_KEY]: 'blue' });

    const result = await runCutover(cutoverOpts(tempDir, {
      runner: fakeRunner, redisClient: fakeRedis,
      renameFn: (src, dest) => {
        if (src.includes('.tmp.')) throw new Error('EXDEV cross-device link');
        fs.renameSync(src, dest);
      },
    }));

    assert.equal(result.success, false);
    assert.equal(result.status, 'STATE_PERSISTENCE_FAILED');
    assert.equal(result.requiresManualReconciliation, true);
  });
});

test('resilience: rollback fails-closed without Redis unless emergency override is set', async () => {
  await withTempNginx(async ({ tempDir }) => {
    const result = await runRollback({
      color: 'green', execute: true, dryRun: false, confirmRollback: true,
      nginxDir: tempDir, stateFile: path.join(tempDir, 'state.json'),
      redisClient: {
        get: async () => ({ success: false, error: 'Connection refused' }),
        set: async () => ({ success: false, error: 'Connection refused' }),
      },
    });

    assert.equal(result.success, false);
    assert.equal(result.status, 'BLOCKED_NO_CENTRAL_STATE');
    assert.ok(result.error.includes('--emergency-override'));
  });
});

test('resilience: rollback fails-closed if no prior upstream config exists', async () => {
  const tempDir = fs.mkdtempSync(path.join(os.tmpdir(), 'bg-rollback-no-upstream-'));
  try {
    const result = await runRollback({
      color: 'green', execute: true, dryRun: false, confirmRollback: true,
      nginxDir: tempDir, redisClient: new FakeRedisClient({ [REDIS_ACTIVE_SLOT_KEY]: 'green' }),
    });
    assert.equal(result.success, false);
    assert.equal(result.status, 'BLOCKED_NO_UPSTREAM');
  } finally {
    fs.rmSync(tempDir, { recursive: true, force: true });
  }
});

test('resilience: rollback returns CRITICAL_INCONSISTENT_STATE on emergency override during Redis outage', async () => {
  await withTempNginx(async ({ tempDir, upstreamConf }) => {
    const stateFile = path.join(tempDir, 'state.json');
    fs.writeFileSync(stateFile, JSON.stringify({ activeSlot: 'green', previousActiveSlot: 'blue' }), 'utf8');
    const journalEntries = [];
    const fakeRunner = new FakeCommandRunner(async () => ({ success: true, exitCode: 0 }));

    const result = await runRollback({
      color: 'green', execute: true, dryRun: false, confirmRollback: true,
      emergencyOverride: true, runner: fakeRunner, nginxDir: tempDir, stateFile, targetColor: 'blue',
      appendJournalFn: (entry) => journalEntries.push(entry),
      redisClient: {
        get: async () => ({ success: false, error: 'Redis completely offline' }),
        set: async () => ({ success: false, error: 'Redis completely offline' }),
      },
    });

    assert.equal(result.success, false, 'Emergency override must NOT be reported as success');
    assert.equal(result.status, 'CRITICAL_INCONSISTENT_STATE');
    assert.equal(result.trafficRestored, true);
    assert.equal(result.redisReconciled, false);
    assert.equal(result.requiresManualReconciliation, true);
    assert.ok(result.reconciliationCommand.includes('cutover.mjs'));

    const stateContent = JSON.parse(fs.readFileSync(stateFile, 'utf8'));
    assert.equal(stateContent.activeSlot, 'green');
    assert.ok(journalEntries.some((e) => e.event === 'EMERGENCY_TRAFFIC_RESTORED_REDIS_UNVERIFIED'));

    const updatedUpstream = fs.readFileSync(upstreamConf, 'utf8');
    assert.ok(updatedUpstream.includes('restaurant-order-api-blue:5000'));
  });
});

test('resilience: parseArgs correctly handles --emergency-override CLI flag', () => {
  const flags = parseArgs(['--execute', '--confirm-rollback', '--emergency-override']);
  assert.equal(flags.execute, true);
  assert.equal(flags.dryRun, false);
  assert.equal(flags.confirmRollback, true);
  assert.equal(flags.emergencyOverride, true);
});

test('resilience: rollback supports --emergency-override via real process.argv and writes journal to file', async () => {
  const originalArgv = [...process.argv];
  process.argv = [process.argv[0], 'scripts/blue-green/rollback.mjs', '--execute', '--confirm-rollback', '--emergency-override'];

  await withTempNginx(async ({ tempDir }) => {
    const stateFile = path.join(tempDir, 'state.json');
    const journalFile = path.join(tempDir, 'custom-journal.jsonl');
    fs.writeFileSync(stateFile, JSON.stringify({ activeSlot: 'green', previousActiveSlot: 'blue' }), 'utf8');
    const fakeRunner = new FakeCommandRunner(async () => ({ success: true, exitCode: 0 }));

    try {
      const result = await runRollback({
        color: 'green', runner: fakeRunner, nginxDir: tempDir, stateFile, journalFile, targetColor: 'blue',
        redisClient: {
          get: async () => ({ success: false, error: 'Connection refused to redis://user:secret123@redis.prod:6379' }),
          set: async () => ({ success: false, error: 'Connection refused to redis://user:secret123@redis.prod:6379' }),
        },
      });

      assert.notEqual(result.status, 'BLOCKED_NO_CENTRAL_STATE');
      assert.equal(result.success, false);
      assert.equal(result.status, 'CRITICAL_INCONSISTENT_STATE');
      assert.equal(result.trafficRestored, true);
      assert.equal(result.redisReconciled, false);
      assert.equal(result.requiresManualReconciliation, true);

      assert.ok(fs.existsSync(journalFile), 'Journal file must exist in temporary dir');
      const journalRaw = fs.readFileSync(journalFile, 'utf8');
      const lines = journalRaw.trim().split('\n').filter(Boolean);
      assert.equal(lines.length, 1, 'Exactly one emergency event must be written');

      const entry = JSON.parse(lines[0]);
      assert.equal(entry.event, 'EMERGENCY_TRAFFIC_RESTORED_REDIS_UNVERIFIED');
      assert.equal(entry.revertedToSlot, 'blue');
      assert.equal(entry.revertedFromSlot, 'green');
      assert.ok(entry.reconciliationCommand.includes('cutover.mjs') && entry.reconciliationCommand.includes('blue'));

      assert.ok(!journalRaw.includes('secret123'), 'Secret/password must not leak into journal');
      assert.ok(!journalRaw.includes('redis://user:'), 'Redis connection URL must not leak into journal');
    } finally {
      process.argv = originalArgv;
    }
  });
});

test('resilience: rollback without --emergency-override fails-closed without writing emergency journal', async () => {
  const originalArgv = [...process.argv];
  process.argv = [process.argv[0], 'scripts/blue-green/rollback.mjs', '--execute', '--confirm-rollback'];

  await withTempNginx(async ({ tempDir }) => {
    const stateFile = path.join(tempDir, 'state.json');
    const journalFile = path.join(tempDir, 'fail-closed-journal.jsonl');
    fs.writeFileSync(stateFile, JSON.stringify({ activeSlot: 'green', previousActiveSlot: 'blue' }), 'utf8');
    const fakeRunner = new FakeCommandRunner(async () => ({ success: true, exitCode: 0 }));

    try {
      const result = await runRollback({
        color: 'green', runner: fakeRunner, nginxDir: tempDir, stateFile, journalFile, targetColor: 'blue',
        redisClient: {
          get: async () => ({ success: false, error: 'Redis outage' }),
          set: async () => ({ success: false, error: 'Redis outage' }),
        },
      });

      assert.equal(result.success, false);
      assert.equal(result.status, 'BLOCKED_NO_CENTRAL_STATE');
      assert.notEqual(result.trafficRestored, true);
      assert.ok(!fs.existsSync(journalFile), 'Emergency journal must not be written during fail-closed abort');
    } finally {
      process.argv = originalArgv;
    }
  });
});
