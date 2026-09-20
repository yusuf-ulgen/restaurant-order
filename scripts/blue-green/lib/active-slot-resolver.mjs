import { getRedisKey, REDIS_ACTIVE_SLOT_KEY } from './redis-state.mjs';
import { getInactiveColor, logStep } from './common.mjs';

/**
 * Resolves the single source of truth for the active deployment slot.
 * In Staging and Production (execute mode), the central state in Redis
 * (key: 'restaurant-order:active-slot') is mandatory and authoritative.
 * In development / dry-run mode, environment variable fallback is permitted.
 */
export async function resolveActiveSlot(options = {}) {
  const isExecute = options.execute === true;
  const isDryRun = options.dryRun !== false && !isExecute;
  const rawEnvSlot = (options.envSlot || process.env.ACTIVE_DEPLOYMENT_SLOT || 'blue').trim().toLowerCase();

  // 1. Query Redis central state
  const redisResult = await getRedisKey(REDIS_ACTIVE_SLOT_KEY, {
    redisUrl: options.redisUrl || process.env.REDIS_URL,
    fakeClient: options.redisClient,
    required: isExecute,
  });

  if (!redisResult.success) {
    if (isExecute) {
      const errorMsg = `[FAIL-CLOSED] Failed to resolve active slot from Redis: ${redisResult.error}`;
      logStep('ACTIVE-SLOT', 'FAIL', errorMsg);
      return { success: false, error: errorMsg, source: 'none' };
    }

    // In dry-run or local dev without Redis, allow fallback
    const fallbackSlot = (rawEnvSlot === 'blue' || rawEnvSlot === 'green') ? rawEnvSlot : 'blue';
    const targetSlot = options.targetColor || getInactiveColor(fallbackSlot);
    return {
      success: true,
      activeSlot: fallbackSlot,
      targetSlot,
      source: 'fallback',
      bypassed: true,
    };
  }

  // 2. Validate Redis slot value
  const redisSlot = redisResult.value ? String(redisResult.value).trim().toLowerCase() : null;

  if (!redisSlot) {
    if (isExecute) {
      const errorMsg = `[FAIL-CLOSED] Central Redis key '${REDIS_ACTIVE_SLOT_KEY}' is empty or unconfigured. Cannot determine active slot in execute mode.`;
      logStep('ACTIVE-SLOT', 'FAIL', errorMsg);
      return { success: false, error: errorMsg, source: 'redis-empty' };
    }

    // Dry-run fallback
    const fallbackSlot = (rawEnvSlot === 'blue' || rawEnvSlot === 'green') ? rawEnvSlot : 'blue';
    const targetSlot = options.targetColor || getInactiveColor(fallbackSlot);
    return {
      success: true,
      activeSlot: fallbackSlot,
      targetSlot,
      source: 'fallback',
      bypassed: true,
    };
  }

  if (redisSlot !== 'blue' && redisSlot !== 'green') {
    const errorMsg = `[FAIL-CLOSED] Central Redis key '${REDIS_ACTIVE_SLOT_KEY}' contains invalid value: '${redisSlot}'. Must be 'blue' or 'green'.`;
    logStep('ACTIVE-SLOT', 'FAIL', errorMsg);
    return { success: false, error: errorMsg, source: 'redis-invalid' };
  }

  // 3. Compute target slot
  const targetSlot = options.targetColor || getInactiveColor(redisSlot);

  if (targetSlot === redisSlot) {
    const errorMsg = `Target deployment slot '${targetSlot}' matches active slot '${redisSlot}'. Deployment must target the inactive slot.`;
    logStep('ACTIVE-SLOT', 'FAIL', errorMsg);
    return { success: false, error: errorMsg, activeSlot: redisSlot, targetSlot, source: 'redis' };
  }

  return {
    success: true,
    activeSlot: redisSlot,
    targetSlot,
    source: 'redis',
  };
}
