import fs from 'node:fs';
import path from 'node:path';
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
import { runRollback } from './rollback.mjs';

const JOURNAL_FILE = path.join(process.cwd(), '.deployment-journal.jsonl');

function appendJournal(entry) {
  try {
    const line = JSON.stringify({ timestamp: new Date().toISOString(), ...entry }) + '\n';
    fs.appendFileSync(JOURNAL_FILE, line, 'utf8');
  } catch {
    // Non-blocking if disk write fails
  }
}

/**
 * Blue-Green Orchestrator: Executes the end-to-end blue-green deployment pipeline.
 * Default execution mode is DRY-RUN.
 * Records step outputs to deployment journal.
 * Halts on failure and triggers controlled rollback if failure occurs after cutover.
 */
export async function runOrchestrator(options = {}) {
  const flags = { ...parseArgs(), ...options };
  const activeColor = (process.env.ACTIVE_DEPLOYMENT_SLOT || 'blue').toLowerCase();
  const targetColor = flags.color || getInactiveColor(activeColor);

  console.log('================================================================================');
  console.log('RESTAURANT-ORDER BLUE-GREEN DEPLOYMENT ORCHESTRATOR');
  console.log(`Active Slot: '${activeColor}' | Target Slot: '${targetColor}' | Mode: ${flags.dryRun ? 'DRY-RUN (Safe)' : 'EXECUTE (Live)'}`);
  console.log('================================================================================\n');

  appendJournal({
    event: 'PIPELINE_START',
    activeColor,
    targetColor,
    mode: flags.dryRun ? 'DRY-RUN' : 'EXECUTE',
  });

  const steps = [
    { name: 'Preflight Validation', id: 'preflight', fn: () => (options.preflightFn ? options.preflightFn() : runPreflight({ ...flags, color: targetColor })) },
    { name: 'Configuration Validation', id: 'config-validate', fn: () => (options.configValidateFn ? options.configValidateFn() : runConfigValidate(flags)) },
    { name: 'Database Migration Safety', id: 'migration-check', fn: () => (options.migrationCheckFn ? options.migrationCheckFn() : runMigrationCheck(flags)) },
    { name: 'Deploy Inactive Slot', id: 'deploy-inactive', fn: () => (options.deployInactiveFn ? options.deployInactiveFn() : runDeployInactive({ ...flags, color: targetColor })) },
    { name: 'Health Probe Verification', id: 'health-check', fn: () => (options.healthCheckFn ? options.healthCheckFn() : runHealthCheck({ ...flags, color: targetColor })) },
    { name: 'Endpoint Warmup', id: 'warmup', fn: () => (options.warmupFn ? options.warmupFn() : runWarmup({ ...flags, color: targetColor })) },
    { name: 'Automated Smoke Testing', id: 'smoke', fn: () => (options.smokeFn ? options.smokeFn() : runSmoke({ ...flags, color: targetColor })) },
    { name: 'Traffic Cutover', id: 'cutover', fn: () => (options.cutoverFn ? options.cutoverFn() : runCutover({ ...flags, color: targetColor, confirmCutover: flags.confirmCutover })) },
    { name: 'Post-Cutover Observation', id: 'observe', fn: () => (options.observeFn ? options.observeFn() : runObserve({ ...flags, color: targetColor })) },
    { name: 'Drain Retired Slot', id: 'drain-old', fn: () => (options.drainOldFn ? options.drainOldFn() : runDrainOld({ ...flags, color: activeColor })) },
  ];

  let cutoverCompleted = false;

  for (let i = 0; i < steps.length; i++) {
    const step = steps[i];
    console.log(`\n--- Step ${i + 1}/${steps.length}: ${step.name} ---`);
    const result = await step.fn();

    appendJournal({
      event: 'STEP_RESULT',
      step: step.id,
      name: step.name,
      success: result.success,
      status: result.status || (result.success ? 'PASS' : 'FAIL'),
      error: result.error,
    });

    if (!result.success) {
      logStep('ORCHESTRATOR', 'FAIL', `Pipeline halted at step: ${step.name}. Triggering safety abort.`);

      // If cutover already succeeded and a post-cutover step (e.g. observe) fails, execute rollback!
      if (cutoverCompleted) {
        logStep('ORCHESTRATOR', 'RUNNING', `Post-cutover failure detected. Triggering automated rollback to '${activeColor}'...`);
        const rollbackHandler = options.rollbackFn || runRollback;
        const rollbackResult = await rollbackHandler({
          ...flags,
          color: targetColor,
          targetColor: activeColor,
          confirmRollback: true,
          reason: `Post-cutover failure at step: ${step.name}`,
        });

        appendJournal({
          event: 'POST_CUTOVER_ROLLBACK',
          rollbackSuccess: rollbackResult.success,
          rollbackResult,
        });

        if (!rollbackResult.success) {
          logStep('ORCHESTRATOR', 'FAIL', 'CRITICAL: Post-cutover rollback failed!');
        }
      }

      appendJournal({ event: 'PIPELINE_FAILED', failedStep: step.name });
      return { success: false, failedStep: step.name, result, cutoverCompleted };
    }

    if (step.id === 'cutover') {
      cutoverCompleted = true;
    }
  }

  console.log('\n================================================================================');
  logStep('ORCHESTRATOR', 'PASS', `Blue-Green deployment pipeline completed successfully! Active slot is now '${targetColor}'.`);
  console.log('================================================================================\n');

  appendJournal({ event: 'PIPELINE_SUCCESS', activeSlot: targetColor });
  return { success: true, activeSlot: targetColor };
}

if (process.argv[1] && process.argv[1].endsWith('orchestrator.mjs')) {
  const result = await runOrchestrator();
  if (!result.success) {
    process.exit(1);
  }
}
