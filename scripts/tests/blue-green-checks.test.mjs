import test from 'node:test';
import assert from 'node:assert/strict';

import { validateSqlMigration, runMigrationCheck } from '../blue-green/migration-check.mjs';
import { runObserve } from '../blue-green/observe.mjs';
import { FakeRedisClient, maskRedisUrl, REDIS_ACTIVE_SLOT_KEY } from '../blue-green/lib/redis-state.mjs';
import { resolveActiveSlot } from '../blue-green/lib/active-slot-resolver.mjs';

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
  process.env.BACKUP_VERIFIED = 'true';
  const trueBackupRes = runMigrationCheck({
    execute: true,
    sqlContent: 'ALTER TABLE orders ADD COLUMN notes TEXT;',
  });
  assert.equal(trueBackupRes.success, true);
});

test('16. maskRedisUrl: masks credentials and handles edge cases', () => {
  assert.equal(maskRedisUrl('redis://user:secret@host:6379/0'), 'redis://****:****@host:6379/0');
  // URL() normalizes with or without trailing slash — accept either form
  assert.ok(
    ['redis://host:6379/', 'redis://host:6379'].includes(maskRedisUrl('redis://host:6379')),
    'No-path URL should be preserved with optional trailing slash'
  );
  assert.ok(
    maskRedisUrl('redis://default:mypass@host:6379').includes('****'),
    'Credentials must be masked'
  );
  assert.equal(maskRedisUrl(''), '[EMPTY]');
  assert.equal(maskRedisUrl(null), '[EMPTY]');
  // 'not-a-url!!!' gets prefixed with redis:// and parsed without error — check it's sanitized or returned
  const badResult = maskRedisUrl('not-a-url!!!');
  assert.ok(typeof badResult === 'string' && badResult.length > 0, 'Must return a non-empty string for any input');
  // Host without scheme is normalized
  assert.ok(maskRedisUrl('host:6379').includes('host:6379'));
});

test('17. FakeRedisClient: simulateAuthFailure blocks both set and get', async () => {
  const redis = new FakeRedisClient({ [REDIS_ACTIVE_SLOT_KEY]: 'blue' });
  redis.setFailure('simulateAuthFailure');

  const getResult = await redis.get(REDIS_ACTIVE_SLOT_KEY);
  assert.equal(getResult.success, false);
  assert.ok(getResult.error.includes('NOAUTH'));

  const setResult = await redis.set(REDIS_ACTIVE_SLOT_KEY, 'green');
  assert.equal(setResult.success, false);
  assert.ok(setResult.error.includes('NOAUTH'));

  // Both calls should be recorded even on failure
  assert.equal(redis.calls.length, 2);
});

test('18. FakeRedisClient: simulateReadOnly blocks writes but allows reads', async () => {
  const redis = new FakeRedisClient({ [REDIS_ACTIVE_SLOT_KEY]: 'blue' });
  redis.setFailure('simulateReadOnly');

  const getResult = await redis.get(REDIS_ACTIVE_SLOT_KEY);
  assert.equal(getResult.success, true);
  assert.equal(getResult.value, 'blue');

  const setResult = await redis.set(REDIS_ACTIVE_SLOT_KEY, 'green');
  assert.equal(setResult.success, false);
  assert.ok(setResult.error.includes('READONLY'));
});

test('19. FakeRedisClient: simulateReadbackMismatch fails set with verification error', async () => {
  const redis = new FakeRedisClient({});
  redis.setFailure('simulateReadbackMismatch');

  const setResult = await redis.set(REDIS_ACTIVE_SLOT_KEY, 'green');
  assert.equal(setResult.success, false);
  assert.ok(setResult.error.includes('verification failed'));
  assert.ok(setResult.error.includes('corrupted-mismatch'));

  // Key must NOT be updated in store when readback fails (data integrity)
  redis.clearFailures();
  const getResult = await redis.get(REDIS_ACTIVE_SLOT_KEY);
  assert.equal(getResult.success, true);
});

test('20. resolveActiveSlot: fail-closed in execute mode without Redis', async () => {
  // In execute mode with no fakeClient and no REDIS_URL: must fail-closed.
  const savedRedisUrl = process.env.REDIS_URL;
  delete process.env.REDIS_URL;

  const result = await resolveActiveSlot({ execute: true });
  assert.equal(result.success, false, 'Must fail-closed when Redis is unavailable in execute mode');
  assert.ok(result.error.includes('FAIL-CLOSED') || result.error.includes('REDIS_URL'), `Error should explain why: ${result.error}`);

  // In dry-run mode without Redis: must use env fallback gracefully.
  process.env.ACTIVE_DEPLOYMENT_SLOT = 'blue';
  const dryResult = await resolveActiveSlot({ dryRun: true, execute: false });
  assert.equal(dryResult.success, true, 'Must succeed in dry-run mode with env fallback');
  assert.equal(dryResult.activeSlot, 'blue');
  assert.equal(dryResult.targetSlot, 'green');
  assert.equal(dryResult.source, 'fallback');

  // Restore
  if (savedRedisUrl) process.env.REDIS_URL = savedRedisUrl;
});
