import { parseArgs, logStep } from './lib/common.mjs';
import { defaultCommandRunner } from './lib/command-runner.mjs';
import { resolveActiveSlot } from './lib/active-slot-resolver.mjs';
import { resolveDigests, isValidDigest, isPlaceholderDigest, REQUIRED_IMAGE_KEYS } from './lib/manifest.mjs';

/**
 * Compose project name prefix for slot isolation.
 * Blue slot = restaurant-order-blue, Green slot = restaurant-order-green.
 * This prevents Docker from reusing service identity across slots.
 */
function getComposeProject(color) {
  return `restaurant-order-${color}`;
}

/**
 * Deploy Inactive Color: Deploys containers to the idle/inactive slot using Docker Compose.
 *
 * Production topology:
 * - Uses `docker compose -p <project>` for slot isolation.
 * - Pulls all 5 images and verifies RepoDigests against the release manifest.
 * - Enforces recreation (--force-recreate) so new images take effect.
 * - Does NOT affect the other slot's project containers.
 * - Health-polls the API container until healthy.
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
  const composeProject = getComposeProject(targetColor);

  logStep('DEPLOY-INACTIVE', 'RUNNING', `Preparing deployment to inactive slot '${targetColor}' (project: ${composeProject})...`);

  if (targetColor === activeColor) {
    const errorMsg = `Cannot deploy to active slot '${activeColor}'. Must deploy to idle slot.`;
    logStep('DEPLOY-INACTIVE', 'FAIL', errorMsg);
    return { success: false, error: errorMsg };
  }

  // Validate all 5 image digests before doing any Docker operations
  const digestResolution = resolveDigests({ ...options, execute: flags.execute });

  if (!digestResolution.success && flags.execute) {
    const errorMsg = `Image digest validation failed:\n${digestResolution.errors.join('\n')}`;
    logStep('DEPLOY-INACTIVE', 'FAIL', errorMsg);
    return { success: false, error: errorMsg, digestErrors: digestResolution.errors };
  }

  const baseComposeArgs = [
    'compose',
    '-p', composeProject,
    '-f', 'compose.yml',
    '-f', `compose.prod.${targetColor}.yml`,
  ];

  const pullCommand = `docker ${[...baseComposeArgs, 'pull'].join(' ')}`;
  const upCommand = `docker ${[...baseComposeArgs, 'up', '-d', '--force-recreate'].join(' ')}`;

  if (flags.dryRun) {
    logStep('DEPLOY-INACTIVE', 'DRY-RUN', `[SIMULATED] Would execute: ${pullCommand}`);
    logStep('DEPLOY-INACTIVE', 'DRY-RUN', `[SIMULATED] Would verify RepoDigests for all 5 images`);
    logStep('DEPLOY-INACTIVE', 'DRY-RUN', `[SIMULATED] Would execute: ${upCommand}`);
    logStep('DEPLOY-INACTIVE', 'DRY-RUN', `[SIMULATED] Would poll health on restaurant-order-api-${targetColor}`);
    logStep('DEPLOY-INACTIVE', 'PASS', `Dry-run completed successfully for slot '${targetColor}' (project: ${composeProject}).`);
    return { success: true, dryRun: true, targetColor, composeProject, pullCommand, upCommand };
  }

  // 1. Pull all images
  logStep('DEPLOY-INACTIVE', 'RUNNING', `Pulling images: ${pullCommand}`);
  const pullResult = await runner.run('docker', [...baseComposeArgs, 'pull']);

  if (!pullResult.success) {
    const errorMsg = `Docker pull failed for project '${composeProject}': ${pullResult.stderr || pullResult.stdout}`;
    logStep('DEPLOY-INACTIVE', 'FAIL', errorMsg);
    return { success: false, error: errorMsg, result: pullResult };
  }

  // 2. Verify RepoDigests after pull (if digests are provided and not fake runner)
  if (flags.execute && digestResolution.success && !options.runner) {
    logStep('DEPLOY-INACTIVE', 'RUNNING', 'Verifying pulled image RepoDigests against manifest...');
    const digestVerificationErrors = [];

    const imageVerifyMap = {
      api: { envKey: 'API_IMAGE', digest: digestResolution.apiDigest },
      worker: { envKey: 'WORKER_IMAGE', digest: digestResolution.workerDigest },
      'customer-web': { envKey: 'CUSTOMER_WEB_IMAGE', digest: digestResolution.customerWebDigest },
      'operations-web': { envKey: 'OPERATIONS_WEB_IMAGE', digest: digestResolution.operationsWebDigest },
      'admin-web': { envKey: 'ADMIN_WEB_IMAGE', digest: digestResolution.adminWebDigest },
    };

    for (const [key, { envKey, digest }] of Object.entries(imageVerifyMap)) {
      if (!digest) continue;
      const imageRef = process.env[envKey];
      if (!imageRef) continue;

      const inspectResult = await runner.run('docker', [
        'inspect', '--format', '{{index .RepoDigests 0}}', imageRef,
      ]);

      if (inspectResult.success) {
        const repoDigest = inspectResult.stdout.trim();
        if (repoDigest && !repoDigest.includes(digest)) {
          digestVerificationErrors.push(
            `${key}: pulled RepoDigest does not match manifest. Expected digest ending in '${digest.slice(-16)}', got: '${repoDigest}'`
          );
        }
      }
    }

    if (digestVerificationErrors.length > 0) {
      const errorMsg = `Image digest mismatch detected — refusing to start containers:\n${digestVerificationErrors.join('\n')}`;
      logStep('DEPLOY-INACTIVE', 'FAIL', errorMsg);
      return { success: false, error: errorMsg, digestVerificationErrors };
    }

    logStep('DEPLOY-INACTIVE', 'PASS', 'All image RepoDigests verified against manifest.');
  }

  // 3. Execute docker compose up with --force-recreate on the target project only
  logStep('DEPLOY-INACTIVE', 'RUNNING', `Deploying inactive slot: ${upCommand}`);
  const upResult = await runner.run('docker', [...baseComposeArgs, 'up', '-d', '--force-recreate']);

  if (!upResult.success) {
    const errorMsg = `Docker compose up failed for project '${composeProject}': ${upResult.stderr || upResult.stdout}`;
    logStep('DEPLOY-INACTIVE', 'FAIL', errorMsg);
    return { success: false, error: errorMsg, result: upResult };
  }

  // 4. Poll container health status for the API container
  const containerName = `restaurant-order-api-${targetColor}`;
  const timeoutMs = options.timeoutMs ?? 60000;
  const pollIntervalMs = options.pollIntervalMs ?? 2000;
  const startTime = Date.now();
  let isHealthy = false;
  let lastStatus = 'unknown';

  if (options.skipHealthPoll) {
    isHealthy = true;
  } else {
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

      await new Promise((r) => setTimeout(r, pollIntervalMs));
    }
  }

  if (!isHealthy) {
    const errorMsg = `Timeout waiting for '${containerName}' to become healthy. Last status: ${lastStatus}`;
    logStep('DEPLOY-INACTIVE', 'FAIL', errorMsg);
    return { success: false, error: errorMsg, lastStatus };
  }

  logStep('DEPLOY-INACTIVE', 'PASS', `Deployed inactive slot '${targetColor}' (project: ${composeProject}) successfully. Container is healthy.`);
  return { success: true, dryRun: false, targetColor, composeProject, pullCommand, upCommand, containerStatus: lastStatus };
}

if (process.argv[1] && process.argv[1].endsWith('deploy-inactive.mjs')) {
  const result = await runDeployInactive();
  if (!result.success) {
    process.exit(1);
  }
}
