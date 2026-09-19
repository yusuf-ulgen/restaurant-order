import fs from 'node:fs';
import path from 'node:path';
import { parseArgs, logStep, getInactiveColor } from './lib/common.mjs';
import { resolveDigests } from './lib/manifest.mjs';

/**
 * Preflight Check: Validates prerequisites before deploying to the inactive slot.
 * Ensures active and inactive colors are well-defined and distinct,
 * validates immutable image digests for API and Worker, and prohibits 'latest'.
 */
export function runPreflight(options = {}) {
  const flags = parseArgs(process.argv.slice(2), options);
  const rootDir = process.cwd();
  const errors = [];

  logStep('PREFLIGHT', 'RUNNING', 'Starting pre-deployment validation...');

  // 1. Resolve Active and Inactive Colors
  const activeColor = (process.env.ACTIVE_DEPLOYMENT_SLOT || 'blue').toLowerCase();
  const targetColor = flags.color || getInactiveColor(activeColor);

  if (activeColor !== 'blue' && activeColor !== 'green') {
    errors.push(`Invalid ACTIVE_DEPLOYMENT_SLOT: '${activeColor}'. Must be 'blue' or 'green'.`);
  }

  if (targetColor !== 'blue' && targetColor !== 'green') {
    errors.push(`Invalid target color: '${targetColor}'. Must be 'blue' or 'green'.`);
  }

  if (targetColor === activeColor) {
    errors.push(`Target deployment color '${targetColor}' cannot match the currently active slot '${activeColor}'! Deployment must target the idle/inactive slot.`);
  }

  // 2. Validate Compose Files Existence
  const requiredFiles = [
    'compose.yml',
    'compose.dev.yml',
    'compose.staging.yml',
    'compose.prod.blue.yml',
    'compose.prod.green.yml',
  ];

  for (const file of requiredFiles) {
    const fullPath = path.join(rootDir, file);
    if (!fs.existsSync(fullPath)) {
      errors.push(`Missing required compose file: ${file}`);
    }
  }

  // 3. Image Digest & Manifest Parity Verification
  const digestResult = resolveDigests({
    apiImageDigest: flags.apiImageDigest,
    workerImageDigest: flags.workerImageDigest,
    imageDigest: flags.imageDigest,
    manifestPath: flags.manifestPath,
    execute: flags.execute,
  });

  if (flags.execute) {
    if (!digestResult.success) {
      errors.push(...digestResult.errors);
    }
  }

  if (errors.length > 0) {
    for (const err of errors) {
      logStep('PREFLIGHT', 'FAIL', err);
    }
    return {
      success: false,
      errors,
      activeColor,
      targetColor,
      digests: digestResult,
    };
  }

  logStep(
    'PREFLIGHT',
    'PASS',
    `Active slot is '${activeColor}'. Target slot is '${targetColor}'. Digests validated.`
  );

  return {
    success: true,
    activeColor,
    targetColor,
    digests: digestResult,
  };
}

if (process.argv[1] && process.argv[1].endsWith('preflight.mjs')) {
  const result = runPreflight();
  if (!result.success) {
    process.exit(1);
  }
}
