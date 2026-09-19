import fs from 'node:fs';
import path from 'node:path';
import { parseArgs, logStep } from './lib/common.mjs';
import { defaultCommandRunner } from './lib/command-runner.mjs';
import { setRedisKey, REDIS_ACTIVE_SLOT_KEY } from './lib/redis-state.mjs';
import { resolveActiveSlot } from './lib/active-slot-resolver.mjs';

const DEFAULT_STATE_FILE = path.join(process.cwd(), '.blue-green-state.json');
const INGRESS_CONTAINER = 'restaurant-order-ingress';

/**
 * Builds rollback upstream config using container DNS names on the shared
 * restaurant_order_ingress Docker network. Internal ports: API=5000, Web=8080.
 */
export function buildRollbackUpstreamConfig(safeSlot, timestamp) {
  const apiContainer = `restaurant-order-api-${safeSlot}`;
  const custContainer = `restaurant-order-customer-web-${safeSlot}`;
  const opsContainer = `restaurant-order-operations-web-${safeSlot}`;
  const adminContainer = `restaurant-order-admin-web-${safeSlot}`;

  return [
    '# ==============================================================================',
    '# ACTIVE UPSTREAM CONFIGURATION (RESTORED BY ROLLBACK)',
    `# Reverted at ${timestamp}`,
    `# Slot: ${safeSlot} | Routing via Docker container DNS on internal port`,
    '# ==============================================================================',
    'upstream api_backend {',
    `    server ${apiContainer}:5000 max_fails=3 fail_timeout=10s;`,
    '    keepalive 32;',
    '}',
    '',
    'upstream customer_web_backend {',
    `    server ${custContainer}:8080 max_fails=3 fail_timeout=10s;`,
    '    keepalive 32;',
    '}',
    '',
    'upstream operations_web_backend {',
    `    server ${opsContainer}:8080 max_fails=3 fail_timeout=10s;`,
    '    keepalive 32;',
    '}',
    '',
    'upstream admin_web_backend {',
    `    server ${adminContainer}:8080 max_fails=3 fail_timeout=10s;`,
    '    keepalive 32;',
    '}',
    '',
  ].join('\n');
}

/**
 * Emergency Rollback: Reverts Nginx ingress to the previous safe slot (<60s),
 * updates the central Redis active slot, and verifies consistency.
 * Retains the failing slot online for forensic analysis.
 *
 * Uses docker exec to validate and reload inside the ingress container.
 * In execute mode, Redis is authoritative unless --emergency-override is passed.
 */
export async function runRollback(options = {}) {
  const flags = { ...parseArgs(), ...options };
  const runner = options.runner || defaultCommandRunner;
  const stateFile = options.stateFile || DEFAULT_STATE_FILE;
  const isExecute = flags.execute === true && flags.dryRun !== true;

  let state = {};
  if (fs.existsSync(stateFile)) {
    try {
      state = JSON.parse(fs.readFileSync(stateFile, 'utf8'));
    } catch {
      // Ignore corrupt state file
    }
  }

  // Resolve current active slot via authoritative Redis
  const slotResolution = await resolveActiveSlot({
    execute: isExecute,
    dryRun: flags.dryRun,
    redisUrl: options.redisUrl,
    redisClient: options.redisClient,
    targetColor: options.targetColor,
  });

  if (isExecute && !slotResolution.success && !flags.emergencyOverride) {
    const errorMsg = `ROLLBACK ABORTED: Cannot resolve active slot from authoritative Redis (${slotResolution.error}). Refusing silent fallback in execute mode. Operator must supply --emergency-override to bypass during a total Redis outage.`;
    logStep('ROLLBACK', 'FAIL', errorMsg);
    return { success: false, status: 'BLOCKED_NO_CENTRAL_STATE', error: errorMsg };
  }

  if (flags.emergencyOverride) {
    logStep('ROLLBACK', 'WARNING', 'EMERGENCY OVERRIDE ACTIVE: Bypassing central Redis state resolution. Manual operator reconciliation required after recovery.');
  }

  // Determine current and safe rollback target using matched state fields (activeSlot, previousActiveSlot)
  const currentSlot = (flags.color || (slotResolution.success ? slotResolution.activeSlot : (state.activeSlot || state.newActiveSlot)) || 'green').toLowerCase();
  const safeSlot = (options.targetColor || state.previousActiveSlot || (currentSlot === 'blue' ? 'green' : 'blue')).toLowerCase();

  logStep('ROLLBACK', 'RUNNING', `Initiating emergency rollback from '${currentSlot}' to '${safeSlot}' (container DNS routing)...`);

  if (!flags.confirmRollback && !flags.dryRun) {
    const errorMsg = 'ROLLBACK ABORTED: Missing required --confirm-rollback operator flag. Refusing to alter traffic.';
    logStep('ROLLBACK', 'FAIL', errorMsg);
    return { success: false, error: errorMsg };
  }

  const rollbackPlan = {
    revertedFromSlot: currentSlot,
    restoredActiveSlot: safeSlot,
    ingressContainer: INGRESS_CONTAINER,
    keepFailedSlotRunning: true, // Forensic preservation
    timestamp: new Date().toISOString(),
    emergencyOverride: flags.emergencyOverride === true,
  };

  const nginxDir = options.nginxDir || process.env.NGINX_CONF_DIR || path.join(process.cwd(), 'deploy/nginx');
  const upstreamConfPath = path.join(nginxDir, 'conf.d/upstream.conf');
  const backupConfPath = path.join(nginxDir, 'conf.d/upstream.conf.rollback-backup');

  if (flags.dryRun) {
    const safeApiContainer = `restaurant-order-api-${safeSlot}`;
    logStep('ROLLBACK', 'DRY-RUN', `[SIMULATED] Reverting Nginx upstream to container '${safeApiContainer}:5000'`);
    logStep('ROLLBACK', 'DRY-RUN', `[SIMULATED] docker exec ${INGRESS_CONTAINER} nginx -t`);
    logStep('ROLLBACK', 'DRY-RUN', `[SIMULATED] docker exec ${INGRESS_CONTAINER} nginx -s reload`);
    logStep('ROLLBACK', 'DRY-RUN', `[SIMULATED] Retaining slot '${currentSlot}' online for crash dump analysis.`);
    logStep('ROLLBACK', 'PASS', `Dry-run rollback plan verified. Restored active slot: '${safeSlot}'.`);
    return { success: true, dryRun: true, plan: rollbackPlan };
  }

  // Check ingress container availability
  const ingressContainer = options.ingressContainer || INGRESS_CONTAINER;
  if (!options.runner) {
    const inspectResult = await runner.run('docker', ['inspect', '--format', '{{.State.Status}}', ingressContainer]);
    if (!inspectResult.success || inspectResult.stdout.trim() !== 'running') {
      const errorMsg = `BLOCKED: Ingress container '${ingressContainer}' is not running. Cannot perform rollback.`;
      logStep('ROLLBACK', 'FAIL', errorMsg);
      return { success: false, status: 'BLOCKED', error: errorMsg };
    }
  }

  const candidateContent = buildRollbackUpstreamConfig(safeSlot, rollbackPlan.timestamp);

  let originalContent = '';
  try {
    if (fs.existsSync(upstreamConfPath)) {
      originalContent = fs.readFileSync(upstreamConfPath, 'utf8');
      fs.writeFileSync(backupConfPath, originalContent, 'utf8');
    }

    fs.mkdirSync(path.dirname(upstreamConfPath), { recursive: true });
    fs.writeFileSync(upstreamConfPath, candidateContent, 'utf8');

    // Validate with nginx -t inside the ingress container
    logStep('ROLLBACK', 'RUNNING', `Validating rollback config: docker exec ${ingressContainer} nginx -t`);
    const testResult = await runner.run('docker', ['exec', ingressContainer, 'nginx', '-t']);

    if (!testResult.success) {
      if (originalContent) {
        fs.writeFileSync(upstreamConfPath, originalContent, 'utf8');
      }
      if (fs.existsSync(backupConfPath)) fs.unlinkSync(backupConfPath);

      const errorMsg = `Rollback Nginx validation failed: ${testResult.stderr || testResult.stdout}`;
      logStep('ROLLBACK', 'FAIL', errorMsg);
      return { success: false, error: errorMsg };
    }

    // Reload Nginx inside the ingress container
    logStep('ROLLBACK', 'RUNNING', `Reloading Nginx: docker exec ${ingressContainer} nginx -s reload`);
    const reloadResult = await runner.run('docker', ['exec', ingressContainer, 'nginx', '-s', 'reload']);

    if (!reloadResult.success) {
      if (originalContent) {
        fs.writeFileSync(upstreamConfPath, originalContent, 'utf8');
        await runner.run('docker', ['exec', ingressContainer, 'nginx', '-s', 'reload']);
      }
      if (fs.existsSync(backupConfPath)) fs.unlinkSync(backupConfPath);

      const errorMsg = `Rollback Nginx reload failed: ${reloadResult.stderr || reloadResult.stdout}`;
      logStep('ROLLBACK', 'FAIL', errorMsg);
      return { success: false, error: errorMsg };
    }

    if (fs.existsSync(backupConfPath)) fs.unlinkSync(backupConfPath);

    // Update centralized active slot state in Redis
    logStep('ROLLBACK', 'RUNNING', `Restoring centralized active slot in Redis to '${safeSlot}'...`);
    const redisResult = await setRedisKey(REDIS_ACTIVE_SLOT_KEY, safeSlot, {
      redisUrl: options.redisUrl || process.env.REDIS_URL,
      fakeClient: options.redisClient,
      required: flags.execute && !flags.emergencyOverride,
    });

    if (!redisResult.success && !flags.emergencyOverride) {
      const errorMsg = `CRITICAL_INCONSISTENT_STATE: Ingress was reverted to '${safeSlot}', but Redis active-slot state failed to update: ${redisResult.error}!`;
      logStep('ROLLBACK', 'FAIL', errorMsg);
      if (options.appendJournalFn) {
        options.appendJournalFn({
          event: 'ROLLBACK_INCONSISTENT_STATE',
          error: errorMsg,
          ingressSlot: safeSlot,
          redisError: redisResult.error,
        });
      }
      return {
        success: false,
        status: 'CRITICAL_INCONSISTENT_STATE',
        error: errorMsg,
        plan: rollbackPlan,
      };
    } else if (!redisResult.success && flags.emergencyOverride) {
      logStep('ROLLBACK', 'WARNING', `Emergency override: Redis state could not be updated (${redisResult.error}), but Nginx rollback succeeded.`);
    }

    // Persist rollback state ONLY after Redis update succeeds
    fs.writeFileSync(stateFile, JSON.stringify({
      activeSlot: safeSlot,
      previousActiveSlot: currentSlot,
      rollbackTimestamp: rollbackPlan.timestamp,
      ingressContainer,
      emergencyOverride: flags.emergencyOverride === true,
    }, null, 2), 'utf8');

    logStep('ROLLBACK', 'PASS', `Emergency rollback completed. Traffic routed back to safe slot '${safeSlot}' via container DNS.`);
    logStep('ROLLBACK', 'PASS', `Failing slot '${currentSlot}' retained online for forensics and memory dump.`);
    return { success: true, status: flags.emergencyOverride ? 'EMERGENCY_OVERRIDE_SUCCESS' : 'ROLLBACK_SUCCESS', plan: rollbackPlan };
  } catch (err) {
    if (originalContent && fs.existsSync(upstreamConfPath)) {
      fs.writeFileSync(upstreamConfPath, originalContent, 'utf8');
    }
    if (fs.existsSync(backupConfPath)) fs.unlinkSync(backupConfPath);

    const errorMsg = `Rollback encountered an unexpected error: ${err.message}`;
    logStep('ROLLBACK', 'FAIL', errorMsg);
    return { success: false, error: errorMsg };
  }
}

if (process.argv[1] && process.argv[1].endsWith('rollback.mjs')) {
  const result = await runRollback();
  if (!result.success) {
    process.exit(1);
  }
}
