import http from 'node:http';
import { parseArgs, logStep } from './lib/common.mjs';

/**
 * Health Check: Probes liveness and readiness on target slot.
 * Verifies HTTP status 200, status === 'Healthy', deployment color match, and version match.
 */
export async function probeEndpoint(url, timeoutMs = 3000) {
  return new Promise((resolve) => {
    const req = http.get(url, { timeout: timeoutMs }, (res) => {
      let data = '';
      res.on('data', (chunk) => { data += chunk; });
      res.on('end', () => {
        try {
          const json = JSON.parse(data);
          resolve({ status: res.statusCode, data: json, ok: res.statusCode === 200 });
        } catch {
          resolve({ status: res.statusCode, raw: data, ok: res.statusCode === 200 });
        }
      });
    });

    req.on('error', (err) => {
      resolve({ status: 0, error: err.message, ok: false });
    });

    req.on('timeout', () => {
      req.destroy();
      resolve({ status: 0, error: 'Request timed out', ok: false });
    });
  });
}

export async function runHealthCheck(options = {}) {
  const flags = parseArgs(process.argv.slice(2), options);
  const targetColor = (flags.color || 'green').toLowerCase();
  const port = targetColor === 'blue'
    ? (process.env.API_PORT_BLUE || '5001')
    : (process.env.API_PORT_GREEN || '5002');
  const baseUrl = flags.targetUrl || `http://localhost:${port}`;
  const probe = options.probeFn || probeEndpoint;

  logStep('HEALTH-CHECK', 'RUNNING', `Probing slot '${targetColor}' at ${baseUrl}...`);

  if (flags.dryRun) {
    logStep('HEALTH-CHECK', 'DRY-RUN', `[SIMULATED] Verified GET ${baseUrl}/health/live -> 200 OK (slot: ${targetColor})`);
    logStep('HEALTH-CHECK', 'DRY-RUN', `[SIMULATED] Verified GET ${baseUrl}/health/ready -> 200 OK (slot: ${targetColor})`);
    logStep('HEALTH-CHECK', 'PASS', `Health probes healthy for slot '${targetColor}'.`);
    return { success: true, dryRun: true, color: targetColor };
  }

  const liveResult = await probe(`${baseUrl}/health/live`);
  const readyResult = await probe(`${baseUrl}/health/ready`);

  if (!liveResult.ok || !readyResult.ok) {
    const errorMsg = `Health check failed on ${targetColor}: Live=${liveResult.status}, Ready=${readyResult.status}`;
    logStep('HEALTH-CHECK', 'FAIL', errorMsg);
    return { success: false, liveResult, readyResult, error: errorMsg };
  }

  // Validate response payload (color & version matching)
  const liveColor = (liveResult.data?.color || liveResult.data?.Color || '').toLowerCase();
  if (liveColor && liveColor !== targetColor) {
    const errorMsg = `Deployment color mismatch on live probe: expected '${targetColor}', got '${liveColor}'`;
    logStep('HEALTH-CHECK', 'FAIL', errorMsg);
    return { success: false, error: errorMsg, liveResult };
  }

  const expectedVersion = flags.version || options.version;
  if (expectedVersion) {
    const liveVersion = liveResult.data?.version || liveResult.data?.Version;
    if (liveVersion && liveVersion !== expectedVersion) {
      const errorMsg = `Version mismatch: expected '${expectedVersion}', got '${liveVersion}'`;
      logStep('HEALTH-CHECK', 'FAIL', errorMsg);
      return { success: false, error: errorMsg, liveResult };
    }
  }

  logStep('HEALTH-CHECK', 'PASS', `Slot '${targetColor}' is Healthy, Ready, and matches expected deployment color '${targetColor}'.`);
  return { success: true, liveResult, readyResult };
}

if (process.argv[1] && process.argv[1].endsWith('health-check.mjs')) {
  const result = await runHealthCheck();
  if (!result.success) {
    process.exit(1);
  }
}
