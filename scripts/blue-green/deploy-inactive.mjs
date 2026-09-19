import { parseArgs, logStep } from './lib/common.mjs';
import { defaultCommandRunner } from './lib/command-runner.mjs';
import { resolveActiveSlot } from './lib/active-slot-resolver.mjs';

/**
 * Deploy Inactive Color: Deploys containers to the idle/inactive slot using Docker Compose.
 * Enforces recreation (--force-recreate) so new images take effect,
 * verifies container state, and waits for container health.
 */
export async function runDeployInactive(options = {}) {
  const flags = parseArgs(process.argv.slice(2), options);
  const runner = options.runner || defaultCommandRunner;

  // Resolve Active and Target Colors via single source of truth
  const slotResolution = await resolveActiveSlot({
    execute: flags.execute,
    dryRun: flags.dryRun,
    redisUrl: options.redisUrl,
    redisClient: options.redisClient,
    targetColor: flags.color,
  });

  if (!slotResolution.success) {
    logStep('DEPLOY-INACTIVE', 'FAIL', slotResolution.error);
    return { success: false, error: slotResolution.error };
  }

  const activeColor = slotResolution.activeSlot;
  const targetColor = slotResolution.targetSlot;

  logStep('DEPLOY-INACTIVE', 'RUNNING', `Preparing deployment to inactive slot '${targetColor}'...`);

  if (targetColor === activeColor) {
    const errorMsg = `Cannot deploy to active slot '${activeColor}'. Must deploy to idle slot.`;
    logStep('DEPLOY-INACTIVE', 'FAIL', errorMsg);
    return { success: false, error: errorMsg };
  }

  const composeArgs = [
    'compose',
    '-f', 'compose.yml',
    '-f', `compose.prod.${targetColor}.yml`,
    'up', '-d',
    '--force-recreate',
  ];

  const composeCommand = `docker ${composeArgs.join(' ')}`;

  if (flags.dryRun) {
    logStep('DEPLOY-INACTIVE', 'DRY-RUN', `[SIMULATED] Would execute: ${composeCommand}`);
    logStep('DEPLOY-INACTIVE', 'DRY-RUN', `[SIMULATED] Would poll health on restaurant-order-api-${targetColor}`);
    logStep('DEPLOY-INACTIVE', 'PASS', `Dry-run completed successfully for slot '${targetColor}'.`);
    return { success: true, dryRun: true, targetColor, composeCommand };
  }

  // 1. Execute docker compose up
  logStep('DEPLOY-INACTIVE', 'RUNNING', `Executing: ${composeCommand}`);
  const upResult = await runner.run('docker', composeArgs);

  if (!upResult.success) {
    const errorMsg = `Docker compose up failed for slot '${targetColor}': ${upResult.stderr || upResult.stdout}`;
    logStep('DEPLOY-INACTIVE', 'FAIL', errorMsg);
    return { success: false, error: errorMsg, result: upResult };
  }

  // 2. Poll container health status
  const containerName = `restaurant-order-api-${targetColor}`;
  const timeoutMs = options.timeoutMs ?? 60000;
  const pollIntervalMs = options.pollIntervalMs ?? 2000;
  const startTime = Date.now();
  let isHealthy = false;
  let lastStatus = 'unknown';

  logStep('DEPLOY-INACTIVE', 'RUNNING', `Waiting for container '${containerName}' to become healthy (timeout: ${timeoutMs}ms)...`);

  while (Date.now() - startTime < timeoutMs) {
    const inspectResult = await runner.run('docker', [
      'inspect',
      '--format',
      '{{.State.Status}}|{{if .State.Health}}{{.State.Health.Status}}{{else}}no-healthcheck{{end}}',
      containerName,
    ]);

    if (inspectResult.success) {
      const output = inspectResult.stdout.trim();
      const [stateStatus, healthStatus] = output.split('|');
      lastStatus = `${stateStatus} (${healthStatus})`;

      if (stateStatus === 'running' && (healthStatus === 'healthy' || healthStatus === 'no-healthcheck')) {
        isHealthy = true;
        break;
      }

      if (stateStatus === 'exited' || stateStatus === 'dead') {
        const errorMsg = `Container '${containerName}' terminated unexpectedly with state: ${stateStatus}`;
        logStep('DEPLOY-INACTIVE', 'FAIL', errorMsg);
        return { success: false, error: errorMsg, lastStatus };
      }
    }

    if (options.skipHealthPoll) {
      isHealthy = true;
      break;
    }

    await new Promise((r) => setTimeout(r, pollIntervalMs));
  }

  if (!isHealthy) {
    const errorMsg = `Timeout waiting for '${containerName}' to become healthy. Last status: ${lastStatus}`;
    logStep('DEPLOY-INACTIVE', 'FAIL', errorMsg);
    return { success: false, error: errorMsg, lastStatus };
  }

  logStep('DEPLOY-INACTIVE', 'PASS', `Deployed inactive slot '${targetColor}' successfully. Container is healthy.`);
  return { success: true, dryRun: false, targetColor, composeCommand, containerStatus: lastStatus };
}

if (process.argv[1] && process.argv[1].endsWith('deploy-inactive.mjs')) {
  const result = await runDeployInactive();
  if (!result.success) {
    process.exit(1);
  }
}
