import fs from 'node:fs';
import path from 'node:path';
import { parseArgs, logStep } from './lib/common.mjs';
import { resolveDigests } from './lib/manifest.mjs';
import { resolveActiveSlot } from './lib/active-slot-resolver.mjs';

/**
 * Preflight Check: Validates prerequisites before deploying to the inactive slot.
 * Resolves active slot via central Redis state (single source of truth),
 * ensures target slot is distinct, validates compose files, and checks image digests.
 */
export async function runPreflight(options = {}) {
  const flags = parseArgs(process.argv.slice(2), options);
  const rootDir = process.cwd();
  const errors = [];

  logStep('PREFLIGHT', 'RUNNING', 'Starting pre-deployment validation...');

  // 1. Resolve Active and Target Colors via single source of truth
  const slotResolution = await resolveActiveSlot({
    execute: flags.execute,
    dryRun: flags.dryRun,
    redisUrl: options.redisUrl,
    redisClient: options.redisClient,
    targetColor: flags.color,
  });

  if (!slotResolution.success) {
    errors.push(slotResolution.error);
    logStep('PREFLIGHT', 'FAIL', slotResolution.error);
    return {
      success: false,
      errors,
      activeColor: slotResolution.activeSlot || 'unknown',
      targetColor: slotResolution.targetSlot || 'unknown',
      digests: null,
    };
  }

  const activeColor = slotResolution.activeSlot;
  const targetColor = slotResolution.targetSlot;

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
  const result = await runPreflight();
  if (!result.success) {
    process.exit(1);
  }
}
