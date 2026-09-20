import { createClient } from 'redis';

export const REDIS_ACTIVE_SLOT_KEY = 'restaurant-order:active-slot';

/**
 * Sanitizes a Redis URL by masking credentials for safe logging.
 */
export function maskRedisUrl(rawUrl) {
  if (!rawUrl) return '[EMPTY]';
  try {
    const formatted = rawUrl.startsWith('redis://') || rawUrl.startsWith('rediss://')
      ? rawUrl
      : `redis://${rawUrl}`;
    const url = new URL(formatted);
    if (url.password) url.password = '****';
    if (url.username && url.username !== 'default') url.username = '****';
    return url.toString();
  } catch {
    return '[INVALID_REDIS_URL]';
  }
}

/**
 * Sanitizes an error message by removing connection strings or credentials.
 */
function sanitizeErrorMessage(msg) {
  if (!msg) return 'Unknown Redis error';
  return String(msg).replace(/(?:redis|rediss):\/\/[^@\s]+@/gi, 'redis://[CREDENTIALS_MASKED]@');
}

/**
 * Connects, executes operation, and disconnects safely.
 */
async function withRedisClient(options, operation) {
  const rawUrl = options.redisUrl || process.env.REDIS_URL;
  if (!rawUrl) {
    if (options.required) {
      return { success: false, error: 'REDIS_URL is not set. Cannot access central state in execute mode.' };
    }
    return { success: true, bypassed: true };
  }

  const timeoutMs = options.timeoutMs || 4000;
  const formattedUrl = rawUrl.startsWith('redis://') || rawUrl.startsWith('rediss://')
    ? rawUrl
    : `redis://${rawUrl}`;

  let client = null;
  try {
    client = createClient({
      url: formattedUrl,
      socket: {
        connectTimeout: timeoutMs,
        reconnectStrategy: false, // Fail fast in CLI deployment scripts
      },
    });

    client.on('error', () => {
      // Handled in try/catch to avoid unhandled event emitter errors
    });

    // Connect with strict timeout
    const connectPromise = client.connect();
    const timeoutPromise = new Promise((_, reject) =>
      setTimeout(() => reject(new Error(`Redis connection timed out after ${timeoutMs}ms`)), timeoutMs)
    );
    await Promise.race([connectPromise, timeoutPromise]);

    return await operation(client);
  } catch (err) {
    return {
      success: false,
      error: sanitizeErrorMessage(err.message),
    };
  } finally {
    if (client) {
      try {
        if (client.isOpen) {
          await client.quit();
        }
      } catch {
        client.destroy();
      }
    }
  }
}

/**
 * Sets a key-value pair in Redis with mandatory read-back verification.
 * Fails if result is not 'OK' or if read-back value does not match.
 */
export async function setRedisKey(key, value, options = {}) {
  if (options.fakeClient) {
    return options.fakeClient.set(key, value);
  }

  return withRedisClient(options, async (client) => {
    const setResult = await client.set(key, value);
    if (setResult !== 'OK') {
      return {
        success: false,
        error: `Redis SET command returned unexpected response: ${setResult}`,
      };
    }

    // Read-back verification
    const readBack = await client.get(key);
    if (readBack !== value) {
      return {
        success: false,
        error: `Redis SET verification failed: wrote '${value}', read back '${readBack}'`,
      };
    }

    return { success: true, key, value };
  });
}

/**
 * Gets a key value from Redis using official client.
 */
export async function getRedisKey(key, options = {}) {
  if (options.fakeClient) {
    return options.fakeClient.get(key);
  }

  return withRedisClient(options, async (client) => {
    const value = await client.get(key);
    return { success: true, value };
  });
}

/**
 * FakeRedisClient for reliable unit and integration testing without running Redis.
 * Supports configurable error simulation.
 */
export class FakeRedisClient {
  constructor(initial = {}, failureConfig = {}) {
    this.store = new Map(Object.entries(initial));
    this.calls = [];
    this.failureConfig = { ...failureConfig };
  }

  setFailure(type) {
    this.failureConfig[type] = true;
  }

  clearFailures() {
    this.failureConfig = {};
  }

  async set(key, value) {
    this.calls.push({ action: 'set', key, value, timestamp: new Date().toISOString() });

    if (this.failureConfig.simulateAuthFailure) {
      return { success: false, error: 'NOAUTH Authentication required.' };
    }
    if (this.failureConfig.simulateReadOnly) {
      return { success: false, error: 'READONLY You can\'t write against a read only replica.' };
    }
    if (this.failureConfig.simulateSetFailure) {
      return { success: false, error: 'Redis SET command failed: connection reset by peer' };
    }

    this.store.set(key, value);

    if (this.failureConfig.simulateReadbackMismatch) {
      return {
        success: false,
        error: `Redis SET verification failed: wrote '${value}', read back 'corrupted-mismatch'`,
      };
    }

    return { success: true, key, value };
  }

  async get(key) {
    this.calls.push({ action: 'get', key, timestamp: new Date().toISOString() });

    if (this.failureConfig.simulateAuthFailure) {
      return { success: false, error: 'NOAUTH Authentication required.' };
    }
    if (this.failureConfig.simulateGetFailure) {
      return { success: false, error: 'Redis GET command failed: connection timeout' };
    }

    const val = this.store.get(key) ?? null;
    return { success: true, value: val };
  }
}
