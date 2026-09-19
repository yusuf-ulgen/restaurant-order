import { parseArgs, logStep, getInactiveColor } from './lib/common.mjs';
import { runPreflight } from './preflight.mjs';
import { runConfigValidate } from './config-validate.mjs';
import { runMigrationCheck } from './migration-check.mjs';
import { runDeployInactive } from './deploy-inactive.mjs';
import { runHealthCheck } from './health-check.mjs';
import { runWarmup } from './warmup.mjs';
import { runSmoke } from './smoke.mjs';
import { runCutover } from './cutover.mjs';
import { runObserve } from './observe.mjs';
import { runDrainOld } from './drain-old.mjs';

/**
 * Blue-Green Orchestrator: Executes the end-to-end blue-green deployment pipeline.
 * Default execution mode is DRY-RUN.
 */
export async function runOrchestrator(options = {}) {
  const flags = { ...parseArgs(), ...options };
  const activeColor = (process.env.ACTIVE_DEPLOYMENT_SLOT || 'blue').toLowerCase();
  const targetColor = flags.color || getInactiveColor(activeColor);

  console.log('================================================================================');
  console.log(`RESTAURANT-ORDER BLUE-GREEN DEPLOYMENT ORCHESTRATOR`);
  console.log(`Active Slot: '${activeColor}' | Target Slot: '${targetColor}' | Mode: ${flags.dryRun ? 'DRY-RUN (Safe)' : 'EXECUTE (Live)'}`);
  console.log('================================================================================\n');

  const steps = [
    { name: 'Preflight Validation', fn: () => runPreflight({ ...flags, color: targetColor }) },
    { name: 'Configuration Validation', fn: () => runConfigValidate(flags) },
    { name: 'Database Migration Safety', fn: () => runMigrationCheck(flags) },
    { name: 'Deploy Inactive Slot', fn: () => runDeployInactive({ ...flags, color: targetColor }) },
    { name: 'Health Probe Verification', fn: () => runHealthCheck({ ...flags, color: targetColor }) },
    { name: 'Endpoint Warmup', fn: () => runWarmup({ ...flags, color: targetColor }) },
    { name: 'Automated Smoke Testing', fn: () => runSmoke({ ...flags, color: targetColor }) },
    { name: 'Traffic Cutover', fn: () => runCutover({ ...flags, color: targetColor, confirmCutover: flags.confirmCutover }) },
    { name: 'Post-Cutover Observation', fn: () => runObserve({ ...flags, color: targetColor }) },
    { name: 'Drain Retired Slot', fn: () => runDrainOld({ ...flags, color: activeColor }) },
  ];

  for (let i = 0; i < steps.length; i++) {
    const step = steps[i];
    console.log(`\n--- Step ${i + 1}/${steps.length}: ${step.name} ---`);
    const result = await step.fn();

    if (!result.success) {
      logStep('ORCHESTRATOR', 'FAIL', `Pipeline halted at step: ${step.name}. Triggering safety abort.`);
      return { success: false, failedStep: step.name, result };
    }
  }

  console.log('\n================================================================================');
  logStep('ORCHESTRATOR', 'PASS', `Blue-Green deployment pipeline completed successfully! Active slot is now '${targetColor}'.`);
  console.log('================================================================================\n');

  return { success: true, activeSlot: targetColor };
}

if (process.argv[1] && process.argv[1].endsWith('orchestrator.mjs')) {
  const result = await runOrchestrator();
  if (!result.success) {
    process.exit(1);
  }
}
