import fs from 'node:fs';
import path from 'node:path';
import { parseArgs, logStep } from './lib/common.mjs';
import { defaultCommandRunner } from './lib/command-runner.mjs';
import { setRedisKey, REDIS_ACTIVE_SLOT_KEY } from './lib/redis-state.mjs';
import { resolveActiveSlot } from './lib/active-slot-resolver.mjs';

const DEFAULT_STATE_FILE = path.join(process.cwd(), '.blue-green-state.json');
const DEFAULT_JOURNAL_FILE = path.join(process.cwd(), '.deployment-journal.jsonl');
const INGRESS_CONTAINER = 'restaurant-order-ingress';

/**
 * Sanitizes Redis error message to ensure no connection URLs, secrets, or passwords
 * are leaked into deployment journal entries.
 */
function sanitizeJournalError(msg) {
  if (!msg) return 'Unknown Redis error';
  let sanitized = String(msg);
  sanitized = sanitized.replace(/(?:redis|rediss):\/\/\S+/gi, '[MASKED_REDIS_URL]');
  sanitized = sanitized.replace(/(?:password|passwd|token|secret|auth)\s*[:=]\s*\S+/gi, '[MASKED_CREDENTIAL]');
  return sanitized;
}

/**
 * Appends an entry to the deployment journal file in JSONL format.
 * Creates the directory safely if missing. Throws if writing fails.
 */
function appendRollbackJournal(entry, customJournalFile = null) {
  const journalPath = customJournalFile || process.env.DEPLOYMENT_JOURNAL_FILE || DEFAULT_JOURNAL_FILE;
  try {
    const journalDir = path.dirname(journalPath);
    if (journalDir && !fs.existsSync(journalDir)) {
      fs.mkdirSync(journalDir, { recursive: true });
    }
    const line = JSON.stringify(entry) + '\n';
    fs.appendFileSync(journalPath, line, 'utf8');
  } catch (err) {
    const journalErr = new Error(`CRITICAL: Failed to write to deployment journal '${journalPath}': ${err.message}`);
    logStep('ROLLBACK', 'FAIL', journalErr.message);
    throw journalErr;
  }
}

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
 * Uses candidate config validation, atomic rename, and verified reload.
 * In execute mode, Redis is authoritative. If --emergency-override is passed
 * and Redis is unverified, traffic is restored but the operation is flagged as
 * CRITICAL_INCONSISTENT_STATE (not a normal success), without writing verified
 * activeSlot to the standard state file.
 */
export async function runRollback(options = {}) {
  const flags = { ...parseArgs(), ...options };
  const runner = options.runner || defaultCommandRunner;
  const renameFile = options.renameFn || fs.renameSync;
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
    logStep('ROLLBACK', 'WARNING', 'EMERGENCY OVERRIDE ACTIVE: Central Redis state resolution bypassed or unverified. Manual operator reconciliation required after recovery.');
  }

  // Determine current and safe rollback target using authoritative fields (no legacy newActiveSlot fallback)
  const currentSlot = (flags.color || (slotResolution.success ? slotResolution.activeSlot : state.activeSlot) || 'green').toLowerCase();
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
  const confD = path.join(nginxDir, 'conf.d');
  const upstreamConfPath = path.join(confD, 'upstream.conf');
  const candidateConfPath = path.join(confD, 'upstream.conf.rollback-candidate');
  const backupConfPath = path.join(confD, 'upstream.conf.rollback-backup');
  const validateHarnessPath = path.join(confD, '.nginx-rollback-validate.conf.tmp');

  const cleanupTempFiles = () => {
    try { if (fs.existsSync(candidateConfPath)) fs.unlinkSync(candidateConfPath); } catch {}
    try { if (fs.existsSync(backupConfPath)) fs.unlinkSync(backupConfPath); } catch {}
    try { if (fs.existsSync(validateHarnessPath)) fs.unlinkSync(validateHarnessPath); } catch {}
  };

  // Fail-closed if no prior config exists
  const hasOriginal = fs.existsSync(upstreamConfPath);
  if (!hasOriginal) {
    const errorMsg = 'ROLLBACK ABORTED: Missing existing upstream.conf. Cannot perform safe rollback without previous configuration.';
    logStep('ROLLBACK', 'FAIL', errorMsg);
    return { success: false, status: 'BLOCKED_NO_UPSTREAM', error: errorMsg };
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

  const originalContent = fs.readFileSync(upstreamConfPath, 'utf8');
  const candidateContent = buildRollbackUpstreamConfig(safeSlot, rollbackPlan.timestamp);

  try {
    fs.mkdirSync(confD, { recursive: true });
    fs.writeFileSync(backupConfPath, originalContent, 'utf8');

    // Write candidate config to isolated temp file
    fs.writeFileSync(candidateConfPath, candidateContent, 'utf8');

    // Validate candidate config inside ingress container WITHOUT modifying live upstream.conf
    fs.writeFileSync(validateHarnessPath, [
      'events { worker_connections 1024; }',
      'http {',
      '    include /etc/nginx/conf.d/upstream.conf.rollback-candidate;',
      '    server {',
      '        listen 80;',
      '        location / { proxy_pass http://api_backend; }',
      '    }',
      '}',
      '',
    ].join('\n'), 'utf8');

    logStep('ROLLBACK', 'RUNNING', `Validating rollback candidate config: docker exec ${ingressContainer} nginx -t`);
    const testResult = await runner.run('docker', [
      'exec', ingressContainer, 'nginx', '-t', '-c', '/etc/nginx/conf.d/.nginx-rollback-validate.conf.tmp',
    ]);

    if (!testResult.success) {
      cleanupTempFiles();
      const errorMsg = `Rollback Nginx validation failed: ${testResult.stderr || testResult.stdout}. Live upstream untouched.`;
      logStep('ROLLBACK', 'FAIL', errorMsg);
      return { success: false, error: errorMsg };
    }

    // Candidate verified — perform atomic rename on local filesystem
    try {
      renameFile(candidateConfPath, upstreamConfPath);
    } catch (renameErr) {
      cleanupTempFiles();
      const errorMsg = `Atomic rename failed during rollback: ${renameErr.message}. Live upstream untouched.`;
      logStep('ROLLBACK', 'FAIL', errorMsg);
      return { success: false, error: errorMsg };
    }

    // Reload Nginx inside the ingress container
    logStep('ROLLBACK', 'RUNNING', `Reloading Nginx: docker exec ${ingressContainer} nginx -s reload`);
    const reloadResult = await runner.run('docker', ['exec', ingressContainer, 'nginx', '-s', 'reload']);

    if (!reloadResult.success) {
      // Revert to backup atomically
      try {
        renameFile(backupConfPath, upstreamConfPath);
      } catch {
        fs.writeFileSync(upstreamConfPath, originalContent, 'utf8');
      }

      await runner.run('docker', ['exec', ingressContainer, 'nginx', '-s', 'reload']);
      cleanupTempFiles();

      const errorMsg = `Rollback Nginx reload failed: ${reloadResult.stderr || reloadResult.stdout}. Reverted to previous upstream.`;
      logStep('ROLLBACK', 'FAIL', errorMsg);
      return { success: false, error: errorMsg };
    }

    // Retain backup until Redis update is processed
    logStep('ROLLBACK', 'RUNNING', `Restoring centralized active slot in Redis to '${safeSlot}'...`);
    const redisResult = await setRedisKey(REDIS_ACTIVE_SLOT_KEY, safeSlot, {
      redisUrl: options.redisUrl || process.env.REDIS_URL,
      fakeClient: options.redisClient,
      required: isExecute && !flags.emergencyOverride,
    });

    const reconciliationCommand = `node scripts/blue-green/cutover.mjs --execute --color ${safeSlot} --confirm-cutover`;

    if (!redisResult.success && flags.emergencyOverride) {
      // Ingress traffic is restored, but Redis state is NOT reconciled
      cleanupTempFiles();
      const warningMsg = `EMERGENCY_TRAFFIC_RESTORED_REDIS_UNVERIFIED: Traffic reverted to '${safeSlot}', but Redis active slot failed to update (${redisResult.error}). System is in inconsistent state.`;
      logStep('ROLLBACK', 'WARNING', warningMsg);
      logStep('ROLLBACK', 'WARNING', `Operator must run once Redis recovers: ${reconciliationCommand}`);

      const journalEntry = {
        timestamp: rollbackPlan.timestamp,
        event: 'EMERGENCY_TRAFFIC_RESTORED_REDIS_UNVERIFIED',
        revertedToSlot: safeSlot,
        revertedFromSlot: currentSlot,
        redisError: sanitizeJournalError(redisResult.error),
        reconciliationCommand,
      };

      if (options.appendJournalFn) {
        options.appendJournalFn(journalEntry);
      } else {
        appendRollbackJournal(journalEntry, options.journalFile);
      }

      // Do NOT write verified activeSlot to standard state file
      return {
        success: false,
        status: 'CRITICAL_INCONSISTENT_STATE',
        trafficRestored: true,
        redisReconciled: false,
        requiresManualReconciliation: true,
        error: warningMsg,
        reconciliationCommand,
        plan: rollbackPlan,
      };
    }

    if (!redisResult.success && !flags.emergencyOverride) {
      cleanupTempFiles();
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
        trafficRestored: true,
        redisReconciled: false,
        requiresManualReconciliation: true,
        error: errorMsg,
        plan: rollbackPlan,
      };
    }

    // Redis update succeeded: persist state via temp file + atomic rename
    const tempStateFile = `${stateFile}.tmp.${Date.now()}.${Math.random().toString(36).slice(2, 8)}`;
    try {
      fs.writeFileSync(tempStateFile, JSON.stringify({
        activeSlot: safeSlot,
        previousActiveSlot: currentSlot,
        rollbackTimestamp: rollbackPlan.timestamp,
        ingressContainer,
      }, null, 2), 'utf8');
      renameFile(tempStateFile, stateFile);
    } catch {
      try { if (fs.existsSync(tempStateFile)) fs.unlinkSync(tempStateFile); } catch {}
    }

    cleanupTempFiles();

    logStep('ROLLBACK', 'PASS', `Emergency rollback completed. Traffic routed back to safe slot '${safeSlot}' via container DNS.`);
    logStep('ROLLBACK', 'PASS', `Failing slot '${currentSlot}' retained online for forensics and memory dump.`);
    return { success: true, status: 'ROLLBACK_SUCCESS', plan: rollbackPlan };
  } catch (err) {
    cleanupTempFiles();
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
