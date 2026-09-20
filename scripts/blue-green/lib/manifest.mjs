import fs from 'node:fs';
import path from 'node:path';

export const SHA256_DIGEST_REGEX = /^sha256:[a-f0-9]{64}$/i;
export const PLACEHOLDER_DIGEST = 'sha256:0000000000000000000000000000000000000000000000000000000000000000';

/**
 * Known empty-content SHA-256 digest.
 * This is the hash of zero bytes and must NOT be accepted as a real image digest.
 * Any image using this value was not built or pushed to a real registry.
 */
export const EMPTY_CONTENT_DIGEST = 'sha256:e3b0c44298fc1c149afbf4c8996fb92427ae41e4649b934ca495991b7852b855';

/**
 * All image keys required in a production release manifest.
 * Preflight fails if any of these is missing or invalid.
 */
export const REQUIRED_IMAGE_KEYS = ['api', 'worker', 'customer-web', 'operations-web', 'admin-web'];

/**
 * Validates whether a given string is a valid sha256 digest.
 */
export function isValidDigest(digest) {
  if (!digest || typeof digest !== 'string') return false;
  return SHA256_DIGEST_REGEX.test(digest.trim());
}

/**
 * Returns true if the digest is a known placeholder (all-zeros or empty-content hash).
 * These values indicate no real image was built.
 */
export function isPlaceholderDigest(digest) {
  if (!digest || typeof digest !== 'string') return false;
  const d = digest.trim().toLowerCase();
  return d === PLACEHOLDER_DIGEST || d === EMPTY_CONTENT_DIGEST;
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
 * Validates all 5 required image entries are present.
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

    if (!parsed.version || !parsed.images) {
      return { success: false, error: 'Invalid manifest schema: missing version or images' };
    }

    // Validate all required image keys are present
    const missingImages = REQUIRED_IMAGE_KEYS.filter((key) => !parsed.images[key]);
    if (missingImages.length > 0) {
      return {
        success: false,
        error: `Manifest is missing required image entries: ${missingImages.join(', ')}. All 5 images (api, worker, customer-web, operations-web, admin-web) are required.`,
      };
    }

    return { success: true, manifest: parsed };
  } catch (err) {
    return { success: false, error: `Failed to parse release manifest: ${err.message}` };
  }
}

/**
 * Resolves and validates image digests for all 5 production images.
 * Priority: explicit options > environment variables > release manifest.
 */
export function resolveDigests(options = {}) {
  const manifestResult = loadReleaseManifest(options.manifestPath);
  const manifest = manifestResult.success ? manifestResult.manifest : null;

  // Resolve all 5 image digests
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

  const customerWebDigest = options.customerWebImageDigest ||
    process.env.CUSTOMER_WEB_IMAGE_DIGEST ||
    manifest?.images?.['customer-web']?.digest ||
    null;

  const operationsWebDigest = options.operationsWebImageDigest ||
    process.env.OPERATIONS_WEB_IMAGE_DIGEST ||
    manifest?.images?.['operations-web']?.digest ||
    null;

  const adminWebDigest = options.adminWebImageDigest ||
    process.env.ADMIN_WEB_IMAGE_DIGEST ||
    manifest?.images?.['admin-web']?.digest ||
    null;

  const version = options.version || manifest?.version || 'unknown';

  const errors = [];

  // Helper to validate a single digest
  const validateDigest = (digest, label) => {
    if (!digest) {
      errors.push(`${label} is required`);
    } else if (!isValidDigest(digest)) {
      errors.push(`Invalid ${label} format: '${digest}'. Must be sha256:<64 hex chars>`);
    } else if (isPlaceholderDigest(digest) && options.execute) {
      if (digest.toLowerCase() === EMPTY_CONTENT_DIGEST) {
        errors.push(`${label} is the empty-content hash (sha256:e3b0c...) — this is NOT a real image digest. Build and push the image first.`);
      } else {
        errors.push(`${label} cannot be placeholder all-zeros in execute mode`);
      }
    }
  };

  validateDigest(apiDigest, 'API_IMAGE_DIGEST');
  validateDigest(workerDigest, 'WORKER_IMAGE_DIGEST');
  validateDigest(customerWebDigest, 'CUSTOMER_WEB_IMAGE_DIGEST');
  validateDigest(operationsWebDigest, 'OPERATIONS_WEB_IMAGE_DIGEST');
  validateDigest(adminWebDigest, 'ADMIN_WEB_IMAGE_DIGEST');

  // Check forbidden 'latest' tag in options/env
  const imageTag = options.imageTag || process.env.IMAGE_TAG;
  if (isLatestTagForbidden(imageTag)) {
    errors.push(`Tag '${imageTag}' is forbidden in production. Use immutable sha256 digests.`);
  }

  // Check forbidden tags in manifest
  for (const key of REQUIRED_IMAGE_KEYS) {
    const imgEntry = manifest?.images?.[key];
    if (imgEntry?.tag && isLatestTagForbidden(imgEntry.tag)) {
      errors.push(`Manifest ${key} tag '${imgEntry.tag}' is forbidden in production.`);
    }
  }

  return {
    success: errors.length === 0,
    errors,
    apiDigest,
    workerDigest,
    customerWebDigest,
    operationsWebDigest,
    adminWebDigest,
    version,
    manifest,
  };
}
