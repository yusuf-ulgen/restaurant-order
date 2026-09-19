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

  const projectName = `restaurant-order-${oldColor}`;
  const composeArgs = [
    'compose',
    '-p', projectName,
    '-f', 'compose.yml',
    '-f', `compose.prod.${oldColor}.yml`,
    'stop',
  ];

  const containersToCheck = [
    `restaurant-order-api-${oldColor}`,
    `restaurant-order-worker-${oldColor}`,
    `restaurant-order-customer-web-${oldColor}`,
    `restaurant-order-operations-web-${oldColor}`,
    `restaurant-order-admin-web-${oldColor}`,
  ];

  const drainPlan = {
    drainingSlot: oldColor,
    composeProject: projectName,
    drainTimeoutSec,
    keepFailedSlot,
    composeCommand: `docker ${composeArgs.join(' ')}`,
    verifiedContainers: containersToCheck,
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
    for (const c of containersToCheck) {
      logStep('DRAIN-OLD', 'DRY-RUN', `[SIMULATED] Would inspect status of container '${c}'`);
    }
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

  // 5. Inspect all 5 container statuses to confirm stopped/exited
  for (const containerName of containersToCheck) {
    const inspectResult = await runner.run('docker', [
      'inspect',
      '--format',
      '{{.State.Status}}',
      containerName,
    ]);

    if (!inspectResult.success) {
      const errorMsg = `Failed to inspect container '${containerName}': ${inspectResult.stderr || inspectResult.stdout}. Drain verification failed.`;
      logStep('DRAIN-OLD', 'FAIL', errorMsg);
      return { success: false, error: errorMsg, containerName, inspectResult };
    }

    const status = (inspectResult.stdout || '').trim().toLowerCase();
    const stoppedStatuses = new Set(['exited', 'dead']);
    if (!stoppedStatuses.has(status)) {
      const errorMsg = `Container '${containerName}' is still active (status: '${status}') after stop command! Drain verification failed.`;
      logStep('DRAIN-OLD', 'FAIL', errorMsg);
      return { success: false, error: errorMsg, containerName, containerStatus: status };
    }
  }

  logStep('DRAIN-OLD', 'PASS', `Retired slot '${oldColor}' stopped gracefully and all 5 containers verified non-running.`);
  return { success: true, dryRun: false, plan: drainPlan };
}

if (process.argv[1] && process.argv[1].endsWith('drain-old.mjs')) {
  const result = await runDrainOld();
  if (!result.success) {
    process.exit(1);
  }
}
