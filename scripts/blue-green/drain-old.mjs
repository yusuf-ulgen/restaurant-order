import { parseArgs, logStep } from './lib/common.mjs';
import { defaultCommandRunner } from './lib/command-runner.mjs';

/**
 * Drain Old Color: Drains active requests and terminates retired slot containers using Docker Compose.
 * Supports forensic preservation flag (--keep-failed-slot) which deliberately skips stopping containers.
 */
export async function runDrainOld(options = {}) {
  const flags = { ...parseArgs(), ...options };
  const runner = options.runner || defaultCommandRunner;
  const oldColor = (flags.color || 'blue').toLowerCase();
  const drainTimeoutSec = options.drainTimeoutSec ?? (options.testTimeout ? 0 : 30);
  const keepFailedSlot = flags.keepFailedSlotRunning === true;

  logStep('DRAIN-OLD', 'RUNNING', `Draining in-flight connections on retired slot '${oldColor}' (${drainTimeoutSec}s)...`);

  const composeArgs = [
    'compose',
    '-f', 'compose.yml',
    '-f', `compose.prod.${oldColor}.yml`,
    'stop',
  ];

  const drainPlan = {
    drainingSlot: oldColor,
    drainTimeoutSec,
    keepFailedSlot,
    composeCommand: `docker ${composeArgs.join(' ')}`,
  };

  // 1. If forensic preservation is requested, explicitly skip stopping
  if (keepFailedSlot) {
    logStep('DRAIN-OLD', 'PASS', `Forensic preservation active (--keep-failed-slot). Retained slot '${oldColor}' online for diagnostics.`);
    return { success: true, preserved: true, plan: drainPlan };
  }

  // 2. Dry-run mode
  if (flags.dryRun) {
    logStep('DRAIN-OLD', 'DRY-RUN', `[SIMULATED] Waiting ${drainTimeoutSec}s for HTTP keep-alives and WebSocket sessions to close gracefully.`);
    logStep('DRAIN-OLD', 'DRY-RUN', `[SIMULATED] Would execute: ${drainPlan.composeCommand}`);
    logStep('DRAIN-OLD', 'PASS', `Retired slot '${oldColor}' drained and ready for idle state.`);
    return { success: true, dryRun: true, plan: drainPlan };
  }

  // 3. Wait for drain timeout in live execution
  if (drainTimeoutSec > 0) {
    await new Promise((resolve) => setTimeout(resolve, drainTimeoutSec * 1000));
  }

  // 4. Execute docker compose stop
  logStep('DRAIN-OLD', 'RUNNING', `Stopping retired slot '${oldColor}' via '${drainPlan.composeCommand}'...`);
  const stopResult = await runner.run('docker', composeArgs);

  if (!stopResult.success) {
    const errorMsg = `Docker compose stop failed for slot '${oldColor}': ${stopResult.stderr || stopResult.stdout}`;
    logStep('DRAIN-OLD', 'FAIL', errorMsg);
    return { success: false, error: errorMsg, result: stopResult };
  }

  // 5. Inspect container status to confirm stopped/exited
  const containerName = `restaurant-order-api-${oldColor}`;
  const inspectResult = await runner.run('docker', [
    'inspect',
    '--format',
    '{{.State.Status}}',
    containerName,
  ]);

  if (inspectResult.success) {
    const status = inspectResult.stdout.trim().toLowerCase();
    if (status === 'running') {
      const errorMsg = `Container '${containerName}' is still running after stop command! Drain verification failed.`;
      logStep('DRAIN-OLD', 'FAIL', errorMsg);
      return { success: false, error: errorMsg, containerStatus: status };
    }
  }

  logStep('DRAIN-OLD', 'PASS', `Retired slot '${oldColor}' stopped gracefully and verified non-running.`);
  return { success: true, dryRun: false, plan: drainPlan };
}

if (process.argv[1] && process.argv[1].endsWith('drain-old.mjs')) {
  const result = await runDrainOld();
  if (!result.success) {
    process.exit(1);
  }
}
