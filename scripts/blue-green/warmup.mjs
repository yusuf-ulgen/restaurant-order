import { parseArgs, logStep } from './lib/common.mjs';
import { probeEndpoint } from './health-check.mjs';

/**
 * Warmup: Exercises endpoints on inactive slot before routing live traffic.
 * In execute mode, makes real HTTP calls to warm JIT and connection pools.
 */
export async function runWarmup(options = {}) {
  const flags = parseArgs(process.argv.slice(2), options);
  const targetColor = (flags.color || 'green').toLowerCase();
  const port = targetColor === 'blue'
    ? (process.env.API_PORT_BLUE || '5001')
    : (process.env.API_PORT_GREEN || '5002');
  const baseUrl = flags.targetUrl || `http://localhost:${port}`;
  const probe = options.probeFn || probeEndpoint;

  logStep('WARMUP', 'RUNNING', `Warming up JIT compiler, caches, and connection pools on slot '${targetColor}' (${baseUrl})...`);

  const warmupPaths = [
    '/health/live',
    '/health/ready',
  ];

  if (flags.dryRun) {
    for (const p of warmupPaths) {
      logStep('WARMUP', 'DRY-RUN', `[SIMULATED] Sent warmup probe to ${baseUrl}${p}`);
    }
    logStep('WARMUP', 'PASS', `Slot '${targetColor}' warmup completed.`);
    return { success: true, dryRun: true };
  }

  for (const p of warmupPaths) {
    const url = `${baseUrl}${p}`;
    logStep('WARMUP', 'RUNNING', `Sending warmup probe to ${url}...`);
    const res = await probe(url);
    if (!res.ok) {
      const errorMsg = `Warmup probe failed for ${url}: status ${res.status}`;
      logStep('WARMUP', 'FAIL', errorMsg);
      return { success: false, error: errorMsg, path: p, res };
    }
  }

  logStep('WARMUP', 'PASS', `Slot '${targetColor}' warmed up successfully.`);
  return { success: true, dryRun: false };
}

if (process.argv[1] && process.argv[1].endsWith('warmup.mjs')) {
  const result = await runWarmup();
  if (!result.success) {
    process.exit(1);
  }
}
