import fs from 'node:fs';
import path from 'node:path';
import { parseArgs, logStep } from './lib/common.mjs';
import { resolveActiveSlot } from './lib/active-slot-resolver.mjs';
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

function appendJournal(entry, isExecute = false) {
  try {
    const line = JSON.stringify({ timestamp: new Date().toISOString(), ...entry }) + '\n';
    fs.appendFileSync(JOURNAL_FILE, line, 'utf8');
  } catch (err) {
    if (isExecute) {
      throw new Error(`CRITICAL: Failed to write to deployment journal '${JOURNAL_FILE}': ${err.message}`);
    }
  }
}

/**
 * Blue-Green Orchestrator: Executes the end-to-end blue-green deployment pipeline.
 * Resolves active slot from central Redis state (single source of truth).
 * Enforces mid-pipeline consistency and post-cutover verification.
 */
export async function runOrchestrator(options = {}) {
  const flags = { ...parseArgs(), ...options };
  const isExecute = flags.execute === true;

  // 1. Initial Slot Resolution via Single Source of Truth
  const slotResolution = await resolveActiveSlot({
    execute: isExecute,
    dryRun: flags.dryRun,
    redisUrl: options.redisUrl,
    redisClient: options.redisClient,
    targetColor: flags.color,
  });

  if (!slotResolution.success) {
    logStep('ORCHESTRATOR', 'FAIL', `Initial active slot resolution failed: ${slotResolution.error}`);
    return { success: false, error: slotResolution.error, failedStep: 'active-slot-resolution' };
  }

  const activeColor = slotResolution.activeSlot;
  const targetColor = slotResolution.targetSlot;

  console.log('================================================================================');
  console.log('RESTAURANT-ORDER BLUE-GREEN DEPLOYMENT ORCHESTRATOR');
  console.log(`Active Slot: '${activeColor}' | Target Slot: '${targetColor}' | Mode: ${flags.dryRun ? 'DRY-RUN (Safe)' : 'EXECUTE (Live)'}`);
  console.log('================================================================================\n');

  appendJournal({
    event: 'PIPELINE_START',
    activeColor,
    targetColor,
    mode: flags.dryRun ? 'DRY-RUN' : 'EXECUTE',
    source: slotResolution.source,
  }, isExecute);

  const sharedContext = {
    ...flags,
    redisUrl: options.redisUrl,
    redisClient: options.redisClient,
    runner: options.runner,
  };

  const steps = [
    { name: 'Preflight Validation', id: 'preflight', fn: () => (options.preflightFn ? options.preflightFn() : runPreflight({ ...sharedContext, color: targetColor })) },
    { name: 'Configuration Validation', id: 'config-validate', fn: () => (options.configValidateFn ? options.configValidateFn() : runConfigValidate(sharedContext)) },
    { name: 'Database Migration Safety', id: 'migration-check', fn: () => (options.migrationCheckFn ? options.migrationCheckFn() : runMigrationCheck(sharedContext)) },
    { name: 'Deploy Inactive Slot', id: 'deploy-inactive', fn: () => (options.deployInactiveFn ? options.deployInactiveFn() : runDeployInactive({ ...sharedContext, color: targetColor })) },
    { name: 'Health Probe Verification', id: 'health-check', fn: () => (options.healthCheckFn ? options.healthCheckFn() : runHealthCheck({ ...sharedContext, color: targetColor })) },
    { name: 'Endpoint Warmup', id: 'warmup', fn: () => (options.warmupFn ? options.warmupFn() : runWarmup({ ...sharedContext, color: targetColor })) },
    { name: 'Automated Smoke Testing', id: 'smoke', fn: () => (options.smokeFn ? options.smokeFn() : runSmoke({ ...sharedContext, color: targetColor })) },
    { name: 'Traffic Cutover', id: 'cutover', fn: async () => {
      // Mid-pipeline consistency check: verify active slot has not changed
      const midCheck = await resolveActiveSlot({
        execute: isExecute,
        dryRun: flags.dryRun,
        redisUrl: options.redisUrl,
        redisClient: options.redisClient,
      });

      if (midCheck.success && midCheck.activeSlot !== activeColor) {
        const errorMsg = `Active slot changed unexpectedly from '${activeColor}' to '${midCheck.activeSlot}' mid-pipeline! Halting cutover.`;
        logStep('ORCHESTRATOR', 'FAIL', errorMsg);
        return { success: false, error: errorMsg };
      }

      const cutoverFn = options.cutoverFn || runCutover;
      const cutoverRes = await cutoverFn({ ...sharedContext, color: targetColor, confirmCutover: flags.confirmCutover });

      // Post-cutover verification of Redis state
      if (cutoverRes.success && isExecute) {
        const postCutoverCheck = await resolveActiveSlot({
          execute: true,
          redisUrl: options.redisUrl,
          redisClient: options.redisClient,
        });

        if (!postCutoverCheck.success || postCutoverCheck.activeSlot !== targetColor) {
          const verifyError = `Post-cutover Redis verification failed: expected '${targetColor}', got '${postCutoverCheck.activeSlot || 'error'}'.`;
          logStep('ORCHESTRATOR', 'FAIL', verifyError);
          return { success: false, error: verifyError };
        }
      }

      return cutoverRes;
    }},
    { name: 'Post-Cutover Observation', id: 'observe', fn: () => (options.observeFn ? options.observeFn() : runObserve({ ...sharedContext, color: targetColor })) },
    { name: 'Drain Retired Slot', id: 'drain-old', fn: () => (options.drainOldFn ? options.drainOldFn() : runDrainOld({ ...sharedContext, color: activeColor })) },
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
    }, isExecute);

    if (!result.success) {
      logStep('ORCHESTRATOR', 'FAIL', `Pipeline halted at step: ${step.name}. Triggering safety abort.`);

      // If cutover already succeeded and a post-cutover step (e.g. observe) fails, execute rollback!
      if (cutoverCompleted) {
        logStep('ORCHESTRATOR', 'RUNNING', `Post-cutover failure detected. Triggering automated rollback to '${activeColor}'...`);
        const rollbackHandler = options.rollbackFn || runRollback;
        const rollbackResult = await rollbackHandler({
          ...sharedContext,
          color: targetColor,
          targetColor: activeColor,
          confirmRollback: true,
          reason: `Post-cutover failure at step: ${step.name}`,
          appendJournalFn: (entry) => appendJournal(entry, isExecute),
        });

        appendJournal({
          event: 'POST_CUTOVER_ROLLBACK',
          rollbackSuccess: rollbackResult.success,
          rollbackResult,
        }, isExecute);

        if (!rollbackResult.success) {
          logStep('ORCHESTRATOR', 'FAIL', 'CRITICAL: Post-cutover rollback failed!');
        }
      }

      appendJournal({ event: 'PIPELINE_FAILED', failedStep: step.name }, isExecute);
      return { success: false, failedStep: step.name, result, cutoverCompleted };
    }

    if (step.id === 'cutover') {
      cutoverCompleted = true;
    }
  }

  console.log('\n================================================================================');
  logStep('ORCHESTRATOR', 'PASS', `Blue-Green deployment pipeline completed successfully! Active slot is now '${targetColor}'.`);
  console.log('================================================================================\n');

  appendJournal({ event: 'PIPELINE_SUCCESS', activeSlot: targetColor }, isExecute);
  return { success: true, activeSlot: targetColor };
}

if (process.argv[1] && process.argv[1].endsWith('orchestrator.mjs')) {
  const result = await runOrchestrator();
  if (!result.success) {
    process.exit(1);
  }
}
