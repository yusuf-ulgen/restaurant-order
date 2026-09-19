import net from 'node:net';

export const REDIS_ACTIVE_SLOT_KEY = 'restaurant-order:active-slot';

/**
 * Parses a Redis connection URL into host, port, and password.
 * Format: redis://[:password@]host[:port][/db]
 */
export function parseRedisUrl(rawUrl = process.env.REDIS_URL || 'redis://127.0.0.1:6379') {
  try {
    const url = new URL(rawUrl.startsWith('redis://') ? rawUrl : `redis://${rawUrl}`);
    return {
      host: url.hostname || '127.0.0.1',
      port: parseInt(url.port || '6379', 10),
      password: url.password ? decodeURIComponent(url.password) : null,
    };
  } catch {
    return { host: '127.0.0.1', port: 6379, password: null };
  }
}

/**
 * Sets a key-value pair in Redis using native RESP protocol over TCP.
 * Zero external dependencies.
 */
export async function setRedisKey(key, value, options = {}) {
  if (options.fakeClient) {
    return options.fakeClient.set(key, value);
  }

  const rawUrl = options.redisUrl || process.env.REDIS_URL;
  if (!rawUrl) {
    if (options.required) {
      return { success: false, error: 'REDIS_URL is not set. Cannot update central state in execute mode.' };
    }
    return { success: true, bypassed: true };
  }

  const { host, port, password } = parseRedisUrl(rawUrl);
  const timeoutMs = options.timeoutMs || 3000;

  return new Promise((resolve) => {
    let resolved = false;
    const client = net.createConnection({ host, port }, () => {
      let payload = '';
      if (password) {
        const passBuf = Buffer.from(password, 'utf8');
        payload += `*2\r\n$4\r\nAUTH\r\n$${passBuf.length}\r\n${passBuf.toString()}\r\n`;
      }
      const keyBuf = Buffer.from(key, 'utf8');
      const valBuf = Buffer.from(value, 'utf8');
      payload += `*3\r\n$3\r\nSET\r\n$${keyBuf.length}\r\n${keyBuf.toString()}\r\n$${valBuf.length}\r\n${valBuf.toString()}\r\n`;
      client.write(payload);
    });

    client.setTimeout(timeoutMs);

    let buffer = '';
    client.on('data', (chunk) => {
      buffer += chunk.toString();
      if (buffer.includes('+OK') || buffer.includes('-ERR') || buffer.includes('-NOAUTH') || buffer.includes('-WRONGPASS')) {
        client.end();
      }
    });

    client.on('end', () => {
      if (!resolved) {
        resolved = true;
        if (buffer.includes('+OK')) {
          resolve({ success: true, key, value });
        } else {
          resolve({ success: false, error: buffer.trim() || 'Unknown Redis response' });
        }
      }
    });

    client.on('error', (err) => {
      if (!resolved) {
        resolved = true;
        resolve({ success: false, error: err.message });
      }
    });

    client.on('timeout', () => {
      if (!resolved) {
        resolved = true;
        client.destroy();
        resolve({ success: false, error: `Redis connection timed out after ${timeoutMs}ms` });
      }
    });
  });
}

/**
 * Gets a key value from Redis using native RESP protocol over TCP.
 */
export async function getRedisKey(key, options = {}) {
  if (options.fakeClient) {
    return options.fakeClient.get(key);
  }

  const rawUrl = options.redisUrl || process.env.REDIS_URL;
  if (!rawUrl) {
    if (options.required) {
      return { success: false, error: 'REDIS_URL is not set.' };
    }
    return { success: true, value: null, bypassed: true };
  }

  const { host, port, password } = parseRedisUrl(rawUrl);
  const timeoutMs = options.timeoutMs || 3000;

  return new Promise((resolve) => {
    let resolved = false;
    const client = net.createConnection({ host, port }, () => {
      let payload = '';
      if (password) {
        const passBuf = Buffer.from(password, 'utf8');
        payload += `*2\r\n$4\r\nAUTH\r\n$${passBuf.length}\r\n${passBuf.toString()}\r\n`;
      }
      const keyBuf = Buffer.from(key, 'utf8');
      payload += `*2\r\n$3\r\nGET\r\n$${keyBuf.length}\r\n${keyBuf.toString()}\r\n`;
      client.write(payload);
    });

    client.setTimeout(timeoutMs);

    let buffer = '';
    client.on('data', (chunk) => {
      buffer += chunk.toString();
      if (buffer.includes('\r\n')) {
        client.end();
      }
    });

    client.on('end', () => {
      if (!resolved) {
        resolved = true;
        // Parse simple string or bulk string
        if (buffer.startsWith('$-1')) {
          resolve({ success: true, value: null });
        } else if (buffer.startsWith('$')) {
          const lines = buffer.split('\r\n');
          resolve({ success: true, value: lines[1] || '' });
        } else {
          resolve({ success: false, error: buffer.trim() });
        }
      }
    });

    client.on('error', (err) => {
      if (!resolved) {
        resolved = true;
        resolve({ success: false, error: err.message });
      }
    });

    client.on('timeout', () => {
      if (!resolved) {
        resolved = true;
        client.destroy();
        resolve({ success: false, error: `Redis connection timed out after ${timeoutMs}ms` });
      }
    });
  });
}

/**
 * FakeRedisClient for reliable unit and integration testing without running Redis.
 */
export class FakeRedisClient {
  constructor(initial = {}) {
    this.store = new Map(Object.entries(initial));
    this.calls = [];
  }

  async set(key, value) {
    this.calls.push({ action: 'set', key, value, timestamp: new Date().toISOString() });
    this.store.set(key, value);
    return { success: true, key, value };
  }

  async get(key) {
    this.calls.push({ action: 'get', key, timestamp: new Date().toISOString() });
    const val = this.store.get(key) ?? null;
    return { success: true, value: val };
  }
}
