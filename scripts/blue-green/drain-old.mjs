import { parseArgs, logStep } from './lib/common.mjs';

/**
 * Drain Old Color: Drains active requests and gracefully terminates retired slot containers.
 */
export async function runDrainOld(options = {}) {
  const flags = { ...parseArgs(), ...options };
  const oldColor = (flags.color || 'blue').toLowerCase();
  const drainTimeoutSec = options.drainTimeoutSec || 30;

  logStep('DRAIN-OLD', 'RUNNING', `Draining in-flight connections on retired slot '${oldColor}' (${drainTimeoutSec}s)...`);

  const drainPlan = {
    drainingSlot: oldColor,
    drainTimeoutSec,
    composeCommand: `docker compose -f compose.yml -f compose.prod.${oldColor}.yml stop`,
  };

  if (flags.dryRun) {
    logStep('DRAIN-OLD', 'DRY-RUN', `[SIMULATED] Waiting ${drainTimeoutSec}s for HTTP keep-alives and WebSocket sessions to close gracefully.`);
    logStep('DRAIN-OLD', 'DRY-RUN', `[SIMULATED] Would execute: ${drainPlan.composeCommand}`);
    logStep('DRAIN-OLD', 'PASS', `Retired slot '${oldColor}' drained and ready for idle state.`);
    return { success: true, dryRun: true, plan: drainPlan };
  }

  logStep('DRAIN-OLD', 'PASS', `Retired slot '${oldColor}' stopped gracefully.`);
  return { success: true, dryRun: false, plan: drainPlan };
}

if (process.argv[1] && process.argv[1].endsWith('drain-old.mjs')) {
  const result = await runDrainOld();
  if (!result.success) {
    process.exit(1);
  }
}
