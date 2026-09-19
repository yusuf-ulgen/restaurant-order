import fs from 'node:fs';
import path from 'node:path';

export const SHA256_DIGEST_REGEX = /^sha256:[a-f0-9]{64}$/i;
export const PLACEHOLDER_DIGEST = 'sha256:0000000000000000000000000000000000000000000000000000000000000000';

/**
 * Validates whether a given string is a valid sha256 digest.
 */
export function isValidDigest(digest) {
  if (!digest || typeof digest !== 'string') return false;
  return SHA256_DIGEST_REGEX.test(digest.trim());
}

/**
 * Validates that an image tag does not use forbidden 'latest' in production.
 */
export function isLatestTagForbidden(tag) {
  if (!tag || typeof tag !== 'string') return false;
  const normalized = tag.trim().toLowerCase();
  return normalized === 'latest' || normalized.endsWith(':latest');
}

/**
 * Loads and parses release-manifest.json.
 */
export function loadReleaseManifest(manifestPath = 'release-manifest.json') {
  const fullPath = path.isAbsolute(manifestPath)
    ? manifestPath
    : path.join(process.cwd(), manifestPath);

  if (!fs.existsSync(fullPath)) {
    return { success: false, error: `Release manifest not found at ${fullPath}` };
  }

  try {
    const raw = fs.readFileSync(fullPath, 'utf8');
    const parsed = JSON.parse(raw);

    if (!parsed.version || !parsed.images || !parsed.images.api || !parsed.images.worker) {
      return { success: false, error: 'Invalid manifest schema: missing version or images (api/worker)' };
    }

    return { success: true, manifest: parsed };
  } catch (err) {
    return { success: false, error: `Failed to parse release manifest: ${err.message}` };
  }
}

/**
 * Resolves and validates API and Worker image digests from environment, flags, or manifest.
 */
export function resolveDigests(options = {}) {
  const manifestResult = loadReleaseManifest(options.manifestPath);
  const manifest = manifestResult.success ? manifestResult.manifest : null;

  const apiDigest = options.apiImageDigest ||
    process.env.API_IMAGE_DIGEST ||
    options.imageDigest ||
    process.env.IMAGE_DIGEST ||
    manifest?.images?.api?.digest ||
    null;

  const workerDigest = options.workerImageDigest ||
    process.env.WORKER_IMAGE_DIGEST ||
    options.imageDigest ||
    process.env.IMAGE_DIGEST ||
    manifest?.images?.worker?.digest ||
    null;

  const version = options.version || manifest?.version || 'unknown';

  const errors = [];

  if (!apiDigest) {
    errors.push('API_IMAGE_DIGEST is required');
  } else if (!isValidDigest(apiDigest)) {
    errors.push(`Invalid API_IMAGE_DIGEST format: '${apiDigest}'. Must be sha256:<64 hex chars>`);
  } else if (apiDigest === PLACEHOLDER_DIGEST && options.execute) {
    errors.push('API_IMAGE_DIGEST cannot be placeholder all-zeros in execute mode');
  }

  if (!workerDigest) {
    errors.push('WORKER_IMAGE_DIGEST is required');
  } else if (!isValidDigest(workerDigest)) {
    errors.push(`Invalid WORKER_IMAGE_DIGEST format: '${workerDigest}'. Must be sha256:<64 hex chars>`);
  } else if (workerDigest === PLACEHOLDER_DIGEST && options.execute) {
    errors.push('WORKER_IMAGE_DIGEST cannot be placeholder all-zeros in execute mode');
  }

  // Check forbidden 'latest'
  const imageTag = options.imageTag || process.env.IMAGE_TAG;
  if (isLatestTagForbidden(imageTag)) {
    errors.push(`Tag '${imageTag}' is forbidden in production. Use immutable sha256 digests.`);
  }

  if (manifest?.images?.api?.tag && isLatestTagForbidden(manifest.images.api.tag)) {
    errors.push(`Manifest API tag '${manifest.images.api.tag}' is forbidden in production.`);
  }

  if (manifest?.images?.worker?.tag && isLatestTagForbidden(manifest.images.worker.tag)) {
    errors.push(`Manifest Worker tag '${manifest.images.worker.tag}' is forbidden in production.`);
  }

  return {
    success: errors.length === 0,
    errors,
    apiDigest,
    workerDigest,
    version,
    manifest,
  };
}
