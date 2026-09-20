/**
 * Common utilities for Blue-Green deployment scripts.
 * Enforces dry-run safety and provides consistent logging.
 */

export { CommandRunner, FakeCommandRunner, defaultCommandRunner } from './command-runner.mjs';
export { resolveDigests, loadReleaseManifest, isValidDigest, isLatestTagForbidden } from './manifest.mjs';

export function parseArgs(args = process.argv.slice(2), options = {}) {
  const flags = {
    dryRun: true, // Safe default: never mutate live state without explicit --execute
    execute: false,
    color: null,
    targetUrl: null,
    imageDigest: null,
    apiImageDigest: null,
    workerImageDigest: null,
    confirmCutover: false,
    confirmRollback: false,
    emergencyOverride: false,
    verbose: false,
  };

  for (let i = 0; i < args.length; i++) {
    const arg = args[i];
    if (arg === '--execute') {
      flags.execute = true;
      flags.dryRun = false;
    } else if (arg === '--dry-run') {
      flags.dryRun = true;
      flags.execute = false;
    } else if (arg === '--confirm-cutover') {
      flags.confirmCutover = true;
    } else if (arg === '--confirm-rollback') {
      flags.confirmRollback = true;
    } else if (arg === '--emergency-override') {
      flags.emergencyOverride = true;
    } else if (arg === '--verbose') {
      flags.verbose = true;
    } else if (arg.startsWith('--color=')) {
      flags.color = arg.split('=')[1].toLowerCase();
    } else if (arg === '--color' && args[i + 1]) {
      flags.color = args[++i].toLowerCase();
    } else if (arg.startsWith('--target-url=')) {
      flags.targetUrl = arg.split('=')[1];
    } else if (arg === '--target-url' && args[i + 1]) {
      flags.targetUrl = args[++i];
    } else if (arg.startsWith('--image-digest=')) {
      flags.imageDigest = arg.split('=')[1];
    } else if (arg === '--image-digest' && args[i + 1]) {
      flags.imageDigest = args[++i];
    } else if (arg.startsWith('--api-digest=')) {
      flags.apiImageDigest = arg.split('=')[1];
    } else if (arg === '--api-digest' && args[i + 1]) {
      flags.apiImageDigest = args[++i];
    } else if (arg.startsWith('--worker-digest=')) {
      flags.workerImageDigest = arg.split('=')[1];
    } else if (arg === '--worker-digest' && args[i + 1]) {
      flags.workerImageDigest = args[++i];
    }
  }

  // Merge programmatic options
  for (const [key, val] of Object.entries(options)) {
    if (val !== undefined) {
      flags[key] = val;
    }
  }

  if (flags.execute && options.dryRun === undefined) {
    flags.dryRun = false;
  }

  return flags;
}

export function resolveFlags(options = {}, args = process.argv.slice(2)) {
  return parseArgs(args, options);
}

export function logStep(stepName, status = 'RUNNING', details = '') {
  const timestamp = new Date().toISOString();
  const badge = status === 'PASS' ? '✓' : status === 'FAIL' ? '✗' : status === 'DRY-RUN' ? 'ℹ' : '►';
  const message = `[${timestamp}] [${badge} ${status}] [${stepName}] ${details}`.trim();
  console.log(message);
}

export function getInactiveColor(activeColor) {
  const normalized = (activeColor || '').trim().toLowerCase();
  if (normalized === 'blue') return 'green';
  if (normalized === 'green') return 'blue';
  throw new Error(`Invalid active color '${activeColor}'. Must be 'blue' or 'green'.`);
}
