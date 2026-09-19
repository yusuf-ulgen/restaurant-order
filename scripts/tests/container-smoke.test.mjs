import test from 'node:test';
import assert from 'node:assert/strict';
import { execFileSync, spawnSync } from 'node:child_process';
import http from 'node:http';

function isDockerAvailable() {
  try {
    const result = spawnSync('docker', ['info'], { timeout: 5000, stdio: 'pipe' });
    return result.status === 0;
  } catch {
    return false;
  }
}

function fetchHttp(url) {
  return new Promise((resolve, reject) => {
    const req = http.get(url, (res) => {
      let data = '';
      res.on('data', (chunk) => { data += chunk; });
      res.on('end', () => {
        resolve({
          statusCode: res.statusCode,
          headers: res.headers,
          body: data,
        });
      });
    });
    req.on('error', reject);
    req.setTimeout(5000, () => {
      req.destroy();
      reject(new Error(`Timeout fetching ${url}`));
    });
  });
}

const dockerRunning = isDockerAvailable();

test('Container Smoke Tests Suite', { skip: !dockerRunning ? 'Docker daemon is not running in this environment' : false }, async (t) => {
  const disposableApiTag = 'restaurant-order-test-api:disposable';
  const disposableWorkerTag = 'restaurant-order-test-worker:disposable';
  const disposableCustomerTag = 'restaurant-order-test-customer:disposable';

  await t.test('1. Build API container and verify non-root unprivileged execution', async () => {
    execFileSync('docker', ['build', '-f', 'deploy/docker/Dockerfile.api', '-t', disposableApiTag, '.'], {
      stdio: 'pipe',
      timeout: 180000,
    });

    const userCheck = execFileSync('docker', ['run', '--rm', '--entrypoint', 'whoami', disposableApiTag], {
      encoding: 'utf8',
      timeout: 10000,
    }).trim();

    assert.equal(userCheck, 'appuser', 'API container must run as unprivileged appuser');

    const uidCheck = execFileSync('docker', ['run', '--rm', '--entrypoint', 'id', disposableApiTag, '-u'], {
      encoding: 'utf8',
      timeout: 10000,
    }).trim();

    assert.notEqual(uidCheck, '0', 'API container must not run as root (UID 0)');
    assert.equal(uidCheck, '10001', 'API container must run as UID 10001');
  });

  await t.test('2. Run API container, verify health endpoint, and clean up', async () => {
    const containerName = 'test-api-probe-' + Date.now();
    try {
      execFileSync('docker', [
        'run', '-d', '--name', containerName,
        '--read-only', '--tmpfs', '/tmp',
        '-p', '5099:5000',
        '-e', 'ASPNETCORE_ENVIRONMENT=Development',
        disposableApiTag,
      ], { stdio: 'pipe' });

      // Wait up to 10s for API to be ready
      let healthy = false;
      for (let i = 0; i < 20; i++) {
        try {
          const res = await fetchHttp('http://127.0.0.1:5099/health/live');
          if (res.statusCode === 200) {
            healthy = true;
            break;
          }
        } catch {
          await new Promise((r) => setTimeout(r, 500));
        }
      }

      assert.equal(healthy, true, 'API /health/live probe must return 200 OK');
    } finally {
      spawnSync('docker', ['rm', '-f', containerName], { stdio: 'pipe' });
      spawnSync('docker', ['rmi', disposableApiTag], { stdio: 'pipe' });
    }
  });

  await t.test('3. Build Worker container and verify non-root execution and fail-closed exit', async () => {
    execFileSync('docker', ['build', '-f', 'deploy/docker/Dockerfile.worker', '-t', disposableWorkerTag, '.'], {
      stdio: 'pipe',
      timeout: 180000,
    });

    const userCheck = execFileSync('docker', ['run', '--rm', '--entrypoint', 'whoami', disposableWorkerTag], {
      encoding: 'utf8',
      timeout: 10000,
    }).trim();

    assert.equal(userCheck, 'appuser', 'Worker container must run as unprivileged appuser');

    const uidCheck = execFileSync('docker', ['run', '--rm', '--entrypoint', 'id', disposableWorkerTag, '-u'], {
      encoding: 'utf8',
      timeout: 10000,
    }).trim();

    assert.notEqual(uidCheck, '0', 'Worker container must not run as root (UID 0)');
    assert.equal(uidCheck, '10001', 'Worker container must run as UID 10001');

    // Run worker in Production mode without required Redis/DB -> must fail-closed immediately (exit code != 0)
    const runResult = spawnSync('docker', [
      'run', '--rm',
      '-e', 'ASPNETCORE_ENVIRONMENT=Production',
      disposableWorkerTag,
    ], { timeout: 15000 });

    assert.notEqual(runResult.status, 0, 'Worker without config in Production must fail-closed with non-zero exit code');
    spawnSync('docker', ['rmi', disposableWorkerTag], { stdio: 'pipe' });
  });

  await t.test('4. Build Customer Web container and verify non-root execution', async () => {
    execFileSync('docker', [
      'build',
      '-f', 'deploy/docker/Dockerfile.web',
      '--build-arg', 'APP_NAME=customer-web',
      '-t', disposableCustomerTag,
      '.',
    ], { stdio: 'pipe', timeout: 180000 });

    const userCheck = execFileSync('docker', ['run', '--rm', '--entrypoint', 'whoami', disposableCustomerTag], {
      encoding: 'utf8',
      timeout: 10000,
    }).trim();

    assert.equal(userCheck, 'nginx', 'Web container must run as unprivileged nginx user');

    const uidCheck = execFileSync('docker', ['run', '--rm', '--entrypoint', 'id', disposableCustomerTag, '-u'], {
      encoding: 'utf8',
      timeout: 10000,
    }).trim();

    assert.notEqual(uidCheck, '0', 'Web container must not run as root (UID 0)');
  });

  await t.test('5. Verify Web container on 8080, health probe, SPA fallback, and cache headers', async () => {
    const containerName = 'test-web-probe-' + Date.now();
    try {
      execFileSync('docker', [
        'run', '-d', '--name', containerName,
        '--read-only', '--tmpfs', '/tmp',
        '-p', '8088:8080',
        disposableCustomerTag,
      ], { stdio: 'pipe' });

      // Wait for nginx to accept connections
      let ready = false;
      for (let i = 0; i < 20; i++) {
        try {
          const res = await fetchHttp('http://127.0.0.1:8088/health');
          if (res.statusCode === 200) {
            ready = true;
            break;
          }
        } catch {
          await new Promise((r) => setTimeout(r, 500));
        }
      }

      assert.equal(ready, true, 'Nginx must accept connections on port 8080');

      // A. Health probe
      const healthRes = await fetchHttp('http://127.0.0.1:8088/health');
      assert.equal(healthRes.statusCode, 200);
      assert.equal(healthRes.body.trim(), 'healthy');

      // B. Root index.html + no-cache headers
      const indexRes = await fetchHttp('http://127.0.0.1:8088/');
      assert.equal(indexRes.statusCode, 200);
      assert.match(indexRes.body, /<html/i, 'Index route must serve HTML');
      const indexCacheControl = indexRes.headers['cache-control'] || '';
      assert.match(indexCacheControl, /no-cache|no-store/i, 'index.html must have no-cache headers');

      // C. SPA Route fallback (deep link must NOT 404)
      const spaRes = await fetchHttp('http://127.0.0.1:8088/tables/12/order');
      assert.equal(spaRes.statusCode, 200, 'Deep SPA route must return 200 OK via try_files fallback');
      assert.match(spaRes.body, /<html/i, 'Deep SPA route must return index.html content');
    } finally {
      spawnSync('docker', ['rm', '-f', containerName], { stdio: 'pipe' });
      spawnSync('docker', ['rmi', disposableCustomerTag], { stdio: 'pipe' });
    }
  });
});
