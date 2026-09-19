import fs from 'node:fs';
import path from 'node:path';
import { parseArgs, logStep } from './lib/common.mjs';
import { defaultCommandRunner } from './lib/command-runner.mjs';
import { setRedisKey, REDIS_ACTIVE_SLOT_KEY } from './lib/redis-state.mjs';
import { resolveActiveSlot } from './lib/active-slot-resolver.mjs';

const STATE_FILE = path.join(process.cwd(), '.blue-green-state.json');

/**
 * Emergency Rollback: Reverts Nginx ingress to the previous safe slot (< 60s),
 * updates the central Redis active slot, and verifies consistency.
 * Retains the failing slot online for forensic analysis.
 */
export async function runRollback(options = {}) {
  const flags = { ...parseArgs(), ...options };
  const runner = options.runner || defaultCommandRunner;

  let state = {};
  if (fs.existsSync(STATE_FILE)) {
    try {
      state = JSON.parse(fs.readFileSync(STATE_FILE, 'utf8'));
    } catch {
      // Ignore corrupt state file
    }
  }

  // Resolve current active slot
  const slotResolution = await resolveActiveSlot({
    execute: flags.execute,
    dryRun: flags.dryRun,
    redisUrl: options.redisUrl,
    redisClient: options.redisClient,
    targetColor: options.targetColor,
  });

  const currentSlot = (flags.color || (slotResolution.success ? slotResolution.activeSlot : state.newActiveSlot) || 'green').toLowerCase();
  const safeSlot = (options.targetColor || state.previousActiveSlot || (currentSlot === 'blue' ? 'green' : 'blue')).toLowerCase();

  const safePort = safeSlot === 'blue'
    ? (process.env.API_PORT_BLUE || 5001)
    : (process.env.API_PORT_GREEN || 5002);
  const safeCustPort = safeSlot === 'blue'
    ? (process.env.CUSTOMER_WEB_PORT_BLUE || 3001)
    : (process.env.CUSTOMER_WEB_PORT_GREEN || 3011);
  const safeOpsPort = safeSlot === 'blue'
    ? (process.env.OPERATIONS_WEB_PORT_BLUE || 3002)
    : (process.env.OPERATIONS_WEB_PORT_GREEN || 3012);
  const safeAdminPort = safeSlot === 'blue'
    ? (process.env.ADMIN_WEB_PORT_BLUE || 3003)
    : (process.env.ADMIN_WEB_PORT_GREEN || 3013);

  logStep('ROLLBACK', 'RUNNING', `Initiating emergency rollback from '${currentSlot}' to '${safeSlot}' (port ${safePort})...`);

  if (!flags.confirmRollback && !flags.dryRun) {
    const errorMsg = 'ROLLBACK ABORTED: Missing required --confirm-rollback operator flag. Refusing to alter traffic.';
    logStep('ROLLBACK', 'FAIL', errorMsg);
    return { success: false, error: errorMsg };
  }

  const rollbackPlan = {
    revertedFromSlot: currentSlot,
    restoredActiveSlot: safeSlot,
    targetPort: Number(safePort),
    targetCustPort: Number(safeCustPort),
    targetOpsPort: Number(safeOpsPort),
    targetAdminPort: Number(safeAdminPort),
    keepFailedSlotRunning: true, // Forensic preservation
    timestamp: new Date().toISOString(),
  };

  const nginxDir = options.nginxDir || process.env.NGINX_CONF_DIR || path.join(process.cwd(), 'deploy/nginx');
  const mainConfPath = path.join(nginxDir, 'nginx.conf');
  const upstreamConfPath = path.join(nginxDir, 'conf.d/upstream.conf');
  const backupConfPath = path.join(nginxDir, 'conf.d/upstream.conf.rollback-backup');

  if (flags.dryRun) {
    logStep('ROLLBACK', 'DRY-RUN', `[SIMULATED] Reverting Nginx upstream to server 127.0.0.1:${safePort} (${safeSlot})`);
    logStep('ROLLBACK', 'DRY-RUN', `[SIMULATED] Validating config via 'nginx -t -c ${mainConfPath}'`);
    logStep('ROLLBACK', 'DRY-RUN', `[SIMULATED] Reloading Nginx via 'nginx -s reload'`);
    logStep('ROLLBACK', 'DRY-RUN', `[SIMULATED] Retaining slot '${currentSlot}' online for crash dump analysis.`);
    logStep('ROLLBACK', 'PASS', `Dry-run rollback plan verified. Restored active slot: '${safeSlot}'.`);
    return { success: true, dryRun: true, plan: rollbackPlan };
  }

  // Check ingress provider availability
  const ingressProvider = options.ingressProvider || process.env.INGRESS_PROVIDER || 'nginx';
  if (ingressProvider === 'none' || (!options.runner && !fs.existsSync(mainConfPath))) {
    const errorMsg = 'BLOCKED: Ingress provider is not configured or nginx config is missing. Cannot perform live rollback without verified ingress.';
    logStep('ROLLBACK', 'FAIL', errorMsg);
    return { success: false, status: 'BLOCKED', error: errorMsg };
  }

  const candidateContent = [
    '# ==============================================================================',
    '# ACTIVE UPSTREAM CONFIGURATION (RESTORED BY ROLLBACK)',
    `# Reverted at ${rollbackPlan.timestamp} from ${currentSlot} to ${safeSlot}`,
    `# Slot: ${safeSlot} | API: ${safePort} | Customer: ${safeCustPort} | Ops: ${safeOpsPort} | Admin: ${safeAdminPort}`,
    '# ==============================================================================',
    'upstream api_backend {',
    `    server 127.0.0.1:${safePort} max_fails=3 fail_timeout=10s;`,
    '    keepalive 32;',
    '}',
    '',
    'upstream customer_web_backend {',
    `    server 127.0.0.1:${safeCustPort} max_fails=3 fail_timeout=10s;`,
    '    keepalive 32;',
    '}',
    '',
    'upstream operations_web_backend {',
    `    server 127.0.0.1:${safeOpsPort} max_fails=3 fail_timeout=10s;`,
    '    keepalive 32;',
    '}',
    '',
    'upstream admin_web_backend {',
    `    server 127.0.0.1:${safeAdminPort} max_fails=3 fail_timeout=10s;`,
    '    keepalive 32;',
    '}',
    '',
  ].join('\n');

  let originalContent = '';
  try {
    if (fs.existsSync(upstreamConfPath)) {
      originalContent = fs.readFileSync(upstreamConfPath, 'utf8');
      fs.writeFileSync(backupConfPath, originalContent, 'utf8');
    }

    fs.mkdirSync(path.dirname(upstreamConfPath), { recursive: true });
    fs.writeFileSync(upstreamConfPath, candidateContent, 'utf8');

    // Validate with nginx -t
    logStep('ROLLBACK', 'RUNNING', `Validating restored configuration with 'nginx -t'...`);
    const testResult = await runner.run('nginx', ['-t', '-c', mainConfPath]);

    if (!testResult.success) {
      if (originalContent) {
        fs.writeFileSync(upstreamConfPath, originalContent, 'utf8');
      }
      if (fs.existsSync(backupConfPath)) fs.unlinkSync(backupConfPath);

      const errorMsg = `Rollback Nginx validation failed: ${testResult.stderr || testResult.stdout}`;
      logStep('ROLLBACK', 'FAIL', errorMsg);
      return { success: false, error: errorMsg };
    }

    // Reload Nginx
    logStep('ROLLBACK', 'RUNNING', `Reloading Nginx with 'nginx -s reload'...`);
    const reloadResult = await runner.run('nginx', ['-s', 'reload']);

    if (!reloadResult.success) {
      if (originalContent) {
        fs.writeFileSync(upstreamConfPath, originalContent, 'utf8');
        await runner.run('nginx', ['-s', 'reload']);
      }
      if (fs.existsSync(backupConfPath)) fs.unlinkSync(backupConfPath);

      const errorMsg = `Rollback Nginx reload failed: ${reloadResult.stderr || reloadResult.stdout}`;
      logStep('ROLLBACK', 'FAIL', errorMsg);
      return { success: false, error: errorMsg };
    }

    if (fs.existsSync(backupConfPath)) fs.unlinkSync(backupConfPath);

    // Update centralized active slot state in Redis
    logStep('ROLLBACK', 'RUNNING', `Restoring centralized active slot state in Redis to '${safeSlot}'...`);
    const redisResult = await setRedisKey(REDIS_ACTIVE_SLOT_KEY, safeSlot, {
      redisUrl: options.redisUrl || process.env.REDIS_URL,
      fakeClient: options.redisClient,
      required: flags.execute,
    });

    if (!redisResult.success) {
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
    }

    // Persist rollback state ONLY after Redis update succeeds
    fs.writeFileSync(STATE_FILE, JSON.stringify({
      activeSlot: safeSlot,
      previousSlot: currentSlot,
      rollbackTimestamp: rollbackPlan.timestamp,
    }, null, 2), 'utf8');

    logStep('ROLLBACK', 'PASS', `Emergency rollback completed. Traffic routed back to safe slot '${safeSlot}' on port ${safePort}.`);
    logStep('ROLLBACK', 'PASS', `Failing slot '${currentSlot}' retained online for forensics and memory dump.`);
    return { success: true, status: 'ROLLBACK_SUCCESS', plan: rollbackPlan };
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
