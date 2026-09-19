import test from 'node:test';
import assert from 'node:assert/strict';
import fs from 'node:fs';
import os from 'node:os';
import path from 'node:path';
import { execSync } from 'node:child_process';

/**
 * Pinned image versions (latest tag strictly prohibited).
 */
const NGINX_IMAGE = 'nginx:1.27-alpine';

function checkDockerRunning() {
  try {
    execSync('docker info', { stdio: 'ignore', timeout: 15000 });
    return true;
  } catch {
    return false;
  }
}

async function fetchWithRetry(url, expectedText, maxAttempts = 15, delayMs = 500) {
  for (let i = 0; i < maxAttempts; i++) {
    try {
      const res = await fetch(url);
      if (res.ok) {
        const text = await res.text();
        if (text.includes(expectedText)) {
          return text;
        }
      }
    } catch {
      // transient connection errors while nginx is reloading/starting
    }
    await new Promise((r) => setTimeout(r, delayMs));
  }
  // One final attempt to capture failure details
  const finalRes = await fetch(url);
  const finalText = await finalRes.text();
  assert.ok(
    finalText.includes(expectedText),
    `Expected response to include '${expectedText}', got '${finalText}' (status: ${finalRes.status})`
  );
  return finalText;
}

test('disposable docker blue-green: end-to-end blue -> green -> blue HTTP routing over dynamic ports', async (t) => {
  const isDocker = checkDockerRunning();

  if (!isDocker) {
    const isCi = process.env.CI === 'true' || process.env.GITHUB_ACTIONS === 'true' || process.env.CONTINUOUS_INTEGRATION === 'true';
    if (isCi) {
      assert.fail('Docker daemon is required in CI for disposable blue-green container integration tests, but is not running.');
    }
    const explicitSkip = process.env.SKIP_TESTCONTAINERS === 'true' || process.env.SKIP_DOCKER_FLOW === 'true';
    if (explicitSkip) {
      t.skip('Skipped: SKIP_TESTCONTAINERS or SKIP_DOCKER_FLOW is set and Docker daemon is not running.');
      return;
    }
    assert.fail('Docker daemon is not running. To run container flow tests, start Docker or set SKIP_TESTCONTAINERS=true or SKIP_DOCKER_FLOW=true.');
  }

  const uid = `${Date.now()}_${Math.random().toString(36).slice(2, 7)}`;
  const networkName = `bg_net_${uid}`;
  const blueContainer = `bg_blue_${uid}`;
  const greenContainer = `bg_green_${uid}`;
  const ingressContainer = `bg_ingress_${uid}`;

  const tempDir = fs.mkdtempSync(path.join(os.tmpdir(), `bg_dock_${uid}_`));
  const blueDir = path.join(tempDir, 'blue');
  const greenDir = path.join(tempDir, 'green');
  const ingressConfD = path.join(tempDir, 'ingress_conf_d');

  fs.mkdirSync(blueDir, { recursive: true });
  fs.mkdirSync(greenDir, { recursive: true });
  fs.mkdirSync(ingressConfD, { recursive: true });

  // Backend Nginx configs listening on internal port 5000
  fs.writeFileSync(
    path.join(blueDir, 'default.conf'),
    'server { listen 5000; location / { default_type text/plain; return 200 "HTTP_RESPONSE_BLUE\\n"; } }\n',
    'utf8'
  );

  fs.writeFileSync(
    path.join(greenDir, 'default.conf'),
    'server { listen 5000; location / { default_type text/plain; return 200 "HTTP_RESPONSE_GREEN\\n"; } }\n',
    'utf8'
  );

  // Initial Ingress configuration: routes to blue backend
  const upstreamConfPath = path.join(ingressConfD, 'upstream.conf');
  fs.writeFileSync(
    upstreamConfPath,
    `upstream api_backend {\n    server restaurant-order-api-blue:5000 max_fails=3 fail_timeout=10s;\n}\n`,
    'utf8'
  );

  fs.writeFileSync(
    path.join(ingressConfD, 'default.conf'),
    'server {\n' +
    '    listen 80;\n' +
    '    location / {\n' +
    '        proxy_pass http://api_backend;\n' +
    '        proxy_set_header Host $host;\n' +
    '    }\n' +
    '}\n',
    'utf8'
  );

  try {
    // 1. Create unique isolated Docker network
    execSync(`docker network create ${networkName}`, { stdio: 'ignore' });

    // 2. Start Blue backend container
    execSync(
      `docker run -d --name ${blueContainer} --network ${networkName} ` +
      `--network-alias restaurant-order-api-blue ` +
      `-v "${path.join(blueDir, 'default.conf')}:/etc/nginx/conf.d/default.conf:ro" ` +
      `${NGINX_IMAGE}`,
      { stdio: 'ignore' }
    );

    // 3. Start Green backend container
    execSync(
      `docker run -d --name ${greenContainer} --network ${networkName} ` +
      `--network-alias restaurant-order-api-green ` +
      `-v "${path.join(greenDir, 'default.conf')}:/etc/nginx/conf.d/default.conf:ro" ` +
      `${NGINX_IMAGE}`,
      { stdio: 'ignore' }
    );

    // 4. Start Ingress container with dynamic host port mapping (-p 0:80)
    execSync(
      `docker run -d --name ${ingressContainer} --network ${networkName} ` +
      `-p 0:80 ` +
      `-v "${ingressConfD}:/etc/nginx/conf.d" ` +
      `${NGINX_IMAGE}`,
      { stdio: 'ignore' }
    );

    // Verify all 3 containers are running
    const checkStatus = (c) => execSync(`docker inspect --format "{{.State.Status}}" ${c}`, { encoding: 'utf8' }).trim();
    assert.equal(checkStatus(blueContainer), 'running', 'Blue container must be running');
    assert.equal(checkStatus(greenContainer), 'running', 'Green container must be running');
    assert.equal(checkStatus(ingressContainer), 'running', 'Ingress container must be running');

    // Retrieve dynamically assigned host port
    const portMapping = execSync(`docker port ${ingressContainer} 80/tcp`, { encoding: 'utf8' }).trim();
    const hostPort = portMapping.split(':').pop().trim();
    assert.ok(hostPort && !isNaN(Number(hostPort)), `Expected dynamic port number, got '${hostPort}'`);

    const ingressUrl = `http://127.0.0.1:${hostPort}/`;

    // 5. Initial state verification: Ingress routes to Blue
    const initialBody = await fetchWithRetry(ingressUrl, 'HTTP_RESPONSE_BLUE');
    assert.ok(initialBody.includes('HTTP_RESPONSE_BLUE'), 'Initial traffic must route to Blue');

    // 6. Execute real Cutover to Green
    const candidatePath = path.join(ingressConfD, 'upstream.conf.candidate');
    fs.writeFileSync(
      candidatePath,
      `upstream api_backend {\n    server restaurant-order-api-green:5000 max_fails=3 fail_timeout=10s;\n}\n`,
      'utf8'
    );
    // Validate config inside ingress container
    execSync(`docker exec ${ingressContainer} nginx -t`, { stdio: 'ignore' });
    // Atomic rename
    fs.renameSync(candidatePath, upstreamConfPath);
    // Reload ingress Nginx
    execSync(`docker exec ${ingressContainer} nginx -s reload`, { stdio: 'ignore' });

    // Verify health after cutover
    assert.equal(checkStatus(blueContainer), 'running');
    assert.equal(checkStatus(greenContainer), 'running');
    assert.equal(checkStatus(ingressContainer), 'running');

    // 7. Post-cutover verification: Ingress routes to Green
    const greenBody = await fetchWithRetry(ingressUrl, 'HTTP_RESPONSE_GREEN');
    assert.ok(greenBody.includes('HTTP_RESPONSE_GREEN'), 'Post-cutover traffic must route to Green');

    // 8. Execute real Rollback to Blue
    const rollbackCandidatePath = path.join(ingressConfD, 'upstream.conf.rollback');
    fs.writeFileSync(
      rollbackCandidatePath,
      `upstream api_backend {\n    server restaurant-order-api-blue:5000 max_fails=3 fail_timeout=10s;\n}\n`,
      'utf8'
    );
    execSync(`docker exec ${ingressContainer} nginx -t`, { stdio: 'ignore' });
    fs.renameSync(rollbackCandidatePath, upstreamConfPath);
    execSync(`docker exec ${ingressContainer} nginx -s reload`, { stdio: 'ignore' });

    // Verify health after rollback
    assert.equal(checkStatus(blueContainer), 'running');
    assert.equal(checkStatus(greenContainer), 'running');
    assert.equal(checkStatus(ingressContainer), 'running');

    // 9. Post-rollback verification: Ingress routes back to Blue
    const rollbackBody = await fetchWithRetry(ingressUrl, 'HTTP_RESPONSE_BLUE');
    assert.ok(rollbackBody.includes('HTTP_RESPONSE_BLUE'), 'Rollback traffic must route back to Blue');
  } finally {
    // Deterministic cleanup in finally block
    try { execSync(`docker rm -f ${ingressContainer} ${blueContainer} ${greenContainer}`, { stdio: 'ignore' }); } catch {}
    try { execSync(`docker network rm ${networkName}`, { stdio: 'ignore' }); } catch {}
    try { fs.rmSync(tempDir, { recursive: true, force: true }); } catch {}
  }
});
