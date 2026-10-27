import assert from 'node:assert/strict';
import { execFileSync } from 'node:child_process';
import { readFileSync } from 'node:fs';
import test from 'node:test';

const workflow = readFileSync(new URL('../../.github/workflows/ci.yml', import.meta.url), 'utf8').replaceAll('\r\n', '\n');
const syntheticWorkerPassword = ['ci', 'worker', 'only'].join('_');
const containerUrl = `Host=postgres;Port=5432;Database=restaurant_order_dev;Username=restaurant_worker_user;Password=${syntheticWorkerPassword}`;
const hostUrl = containerUrl.replace('Host=postgres;', 'Host=localhost;');

test('CI Compose değişkeni iş kapsamında ve host worker adresi ayrı olmalı', () => {
  const job = workflow.split('  quality-and-verification:\n')[1];
  assert.ok(job, 'CI doğrulama işi bulunamadı');
  const jobEnv = job.split('    steps:\n')[0];
  assert.match(jobEnv, /WORKER_DATABASE_URL:\s*Host=postgres;[^\n]*Username=restaurant_worker_user/);
  const e2e = job.split('      - name: E2E - Playwright Browser & Live API Probes\n')[1]
    ?.split('      - name: ')[0];
  assert.ok(e2e, 'E2E adımı bulunamadı');
  assert.match(e2e, /WORKER_DATABASE_URL:\s*Host=localhost;[^\n]*Username=restaurant_worker_user/);
  assert.match(job, /name: E2E Cleanup - Stop Disposable Containers and Volumes\n\s+if: always\(\)/);
});

test('Eksik worker URL reddedilir; sağlandığında dört Compose ortamı çözülür', () => {
  const baseEnv = {
    ...process.env,
    DATABASE_URL: 'Host=postgres;Port=5432;Database=restaurant_order_dev;Username=restaurant_app_user',
    REDIS_URL: 'redis:6379',
    JWT_SECRET: ['synthetic', 'ci', 'jwt', 'value', 'long', 'enough'].join('_'),
    POSTGRES_PASSWORD: ['synthetic', 'ci', 'postgres'].join('_'),
    API_IMAGE_DIGEST: `sha256:${'1'.repeat(64)}`,
    WORKER_IMAGE_DIGEST: `sha256:${'2'.repeat(64)}`,
    CUSTOMER_WEB_IMAGE_DIGEST: `sha256:${'3'.repeat(64)}`,
    OPERATIONS_WEB_IMAGE_DIGEST: `sha256:${'4'.repeat(64)}`,
    ADMIN_WEB_IMAGE_DIGEST: `sha256:${'5'.repeat(64)}`,
  };
  delete baseEnv.WORKER_DATABASE_URL;
  const compose = (file, env) => execFileSync('docker',
    ['compose', '-f', 'compose.yml', '-f', file, 'config', '--format', 'json'],
    { env, encoding: 'utf8', stdio: ['ignore', 'pipe', 'pipe'] });
  assert.throws(() => compose('compose.dev.yml', baseEnv), /WORKER_DATABASE_URL required/);
  for (const file of ['compose.dev.yml', 'compose.staging.yml', 'compose.prod.blue.yml', 'compose.prod.green.yml']) {
    const config = JSON.parse(compose(file, { ...baseEnv, WORKER_DATABASE_URL: containerUrl }));
    assert.equal(config.services.worker.environment.WORKER_DATABASE_URL, containerUrl, file);
    assert.notEqual(config.services.worker.environment.WORKER_DATABASE_URL, hostUrl, file);
  }
});
