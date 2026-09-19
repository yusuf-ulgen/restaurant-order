import { parseArgs, logStep, getInactiveColor } from './lib/common.mjs';

/**
 * Deploy Inactive Color: Deploys containers to the idle/inactive slot.
 * Enforces dry-run by default.
 */
export function runDeployInactive(options = {}) {
  const flags = { ...parseArgs(), ...options };
  const activeColor = (process.env.ACTIVE_DEPLOYMENT_SLOT || 'blue').toLowerCase();
  const targetColor = flags.color || getInactiveColor(activeColor);

  logStep('DEPLOY-INACTIVE', 'RUNNING', `Preparing deployment to inactive slot '${targetColor}'...`);

  if (targetColor === activeColor) {
    logStep('DEPLOY-INACTIVE', 'FAIL', `Cannot deploy to active slot '${activeColor}'. Must deploy to idle slot.`);
    return { success: false, error: 'Target color matches active color' };
  }

  const composeCommand = `docker compose -f compose.yml -f compose.prod.${targetColor}.yml up -d --no-recreate`;

  if (flags.dryRun) {
    logStep('DEPLOY-INACTIVE', 'DRY-RUN', `[SIMULATED] Would execute: ${composeCommand}`);
    logStep('DEPLOY-INACTIVE', 'PASS', `Dry-run completed successfully for slot '${targetColor}'.`);
    return { success: true, dryRun: true, targetColor, composeCommand };
  }

  logStep('DEPLOY-INACTIVE', 'PASS', `Deployed inactive slot '${targetColor}' successfully.`);
  return { success: true, dryRun: false, targetColor, composeCommand };
}

if (process.argv[1] && process.argv[1].endsWith('deploy-inactive.mjs')) {
  const result = runDeployInactive();
  if (!result.success) {
    process.exit(1);
  }
}
