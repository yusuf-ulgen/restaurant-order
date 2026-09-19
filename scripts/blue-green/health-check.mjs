import http from 'node:http';
import { parseArgs, logStep } from './lib/common.mjs';

/**
 * Health Check: Probes liveness and readiness on target slot.
 * Supports simulated probe for dry-run/test environments.
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
  const flags = { ...parseArgs(), ...options };
  const targetColor = flags.color || 'green';
  const port = targetColor === 'blue' ? (process.env.API_PORT_BLUE || '5001') : (process.env.API_PORT_GREEN || '5002');
  const baseUrl = flags.targetUrl || `http://localhost:${port}`;

  logStep('HEALTH-CHECK', 'RUNNING', `Probing slot '${targetColor}' at ${baseUrl}...`);

  if (flags.dryRun) {
    logStep('HEALTH-CHECK', 'DRY-RUN', `[SIMULATED] Verified GET ${baseUrl}/health/live -> 200 OK (slot: ${targetColor})`);
    logStep('HEALTH-CHECK', 'DRY-RUN', `[SIMULATED] Verified GET ${baseUrl}/health/ready -> 200 OK (slot: ${targetColor})`);
    logStep('HEALTH-CHECK', 'PASS', `Health probes healthy for slot '${targetColor}'.`);
    return { success: true, dryRun: true, color: targetColor };
  }

  const liveResult = await probeEndpoint(`${baseUrl}/health/live`);
  const readyResult = await probeEndpoint(`${baseUrl}/health/ready`);

  if (!liveResult.ok || !readyResult.ok) {
    logStep('HEALTH-CHECK', 'FAIL', `Health check failed on ${targetColor}: Live=${liveResult.status}, Ready=${readyResult.status}`);
    return { success: false, liveResult, readyResult };
  }

  logStep('HEALTH-CHECK', 'PASS', `Slot '${targetColor}' is Healthy and Ready.`);
  return { success: true, liveResult, readyResult };
}

if (process.argv[1] && process.argv[1].endsWith('health-check.mjs')) {
  const result = await runHealthCheck();
  if (!result.success) {
    process.exit(1);
  }
}
