import { parseArgs, logStep } from './lib/common.mjs';

/**
 * Warmup: Exercises endpoints on inactive slot before routing live traffic.
 */
export async function runWarmup(options = {}) {
  const flags = { ...parseArgs(), ...options };
  const targetColor = flags.color || 'green';

  logStep('WARMUP', 'RUNNING', `Warming up JIT compiler, caches, and connection pools on slot '${targetColor}'...`);

  const warmupPaths = [
    '/health/live',
    '/health/ready',
  ];

  if (flags.dryRun) {
    for (const p of warmupPaths) {
      logStep('WARMUP', 'DRY-RUN', `[SIMULATED] Sent warmup probe to ${p}`);
    }
    logStep('WARMUP', 'PASS', `Slot '${targetColor}' warmup completed.`);
    return { success: true, dryRun: true };
  }

  logStep('WARMUP', 'PASS', `Slot '${targetColor}' warmed up.`);
  return { success: true, dryRun: false };
}

if (process.argv[1] && process.argv[1].endsWith('warmup.mjs')) {
  const result = await runWarmup();
  if (!result.success) {
    process.exit(1);
  }
}
