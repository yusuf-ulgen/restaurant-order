import { describe, it, expect, vi, beforeEach, afterEach } from 'vitest';
import {
  customerSessionApi,
  CustomerSessionApiError,
  getInMemorySession,
  setInMemorySession,
  clearInMemorySession,
} from '../api/customerSessionApi';
import type { CustomerSessionDto } from '@restaurant-order/contracts';

const sampleSession: CustomerSessionDto = {
  sessionId: 'sess-123',
  tenantId: 'ten-123',
  branchId: 'br-123',
  tableId: 'tbl-123',
  tableNumber: '4',
  tableName: 'Salon 4',
  sessionStatus: 'Active',
  concurrencyToken: 'token-1',
  guestCount: 2,
  openedAtUtc: '2026-10-18T12:00:00Z',
};

describe('customerSessionApi and In-Memory Cache', () => {
  const originalFetch = globalThis.fetch;

  beforeEach(() => {
    vi.restoreAllMocks();
    clearInMemorySession();
  });

  afterEach(() => {
    globalThis.fetch = originalFetch;
    clearInMemorySession();
  });

  describe('in-memory session cache helpers', () => {
    it('stores, retrieves and clears in-memory session correctly', () => {
      expect(getInMemorySession()).toBeNull();
      setInMemorySession(sampleSession);
      expect(getInMemorySession()).toEqual(sampleSession);
      clearInMemorySession();
      expect(getInMemorySession()).toBeNull();
    });
  });

  describe('getCurrentSession API calls', () => {
    it('returns session and updates cache on 200 OK', async () => {
      globalThis.fetch = vi.fn().mockResolvedValue({
        ok: true,
        status: 200,
        json: async () => sampleSession,
        headers: new Headers(),
      });

      const result = await customerSessionApi.getCurrentSession();
      expect(result).toEqual(sampleSession);
      expect(getInMemorySession()).toEqual(sampleSession);
    });

    it('returns null on normal unauthenticated 401', async () => {
      globalThis.fetch = vi.fn().mockResolvedValue({
        ok: false,
        status: 401,
        json: async () => ({ title: 'Unauthorized', detail: 'Token missing' }),
        headers: new Headers(),
      });

      setInMemorySession(sampleSession);
      const result = await customerSessionApi.getCurrentSession();
      expect(result).toBeNull();
      expect(getInMemorySession()).toBeNull();
    });

    it('throws session_expired error when 401 has Session Expired title or closed detail', async () => {
      globalThis.fetch = vi.fn().mockResolvedValue({
        ok: false,
        status: 401,
        json: async () => ({ title: 'Session Expired', detail: 'Dining session was closed' }),
        headers: new Headers(),
      });

      await expect(customerSessionApi.getCurrentSession()).rejects.toThrow(CustomerSessionApiError);

      try {
        await customerSessionApi.getCurrentSession();
      } catch (err) {
        const error = err as CustomerSessionApiError;
        expect(error.status).toBe(401);
        expect(error.errorType).toBe('session_expired');
        expect(error.message).toContain('closed');
      }
    });

    it('handles 429 rate limit with retryAfterSeconds', async () => {
      globalThis.fetch = vi.fn().mockResolvedValue({
        ok: false,
        status: 429,
        json: async () => ({ detail: 'Rate limit exceeded', retryAfterSeconds: 45 }),
        headers: new Headers(),
      });

      try {
        await customerSessionApi.getCurrentSession();
        expect.fail('Should have thrown');
      } catch (err) {
        const error = err as CustomerSessionApiError;
        expect(error.status).toBe(429);
        expect(error.errorType).toBe('rate_limited');
        expect(error.retryAfterSeconds).toBe(45);
      }
    });

    it('handles 503 service unavailable', async () => {
      globalThis.fetch = vi.fn().mockResolvedValue({
        ok: false,
        status: 503,
        json: async () => ({ detail: 'Bakım çalışması devam ediyor' }),
        headers: new Headers(),
      });

      try {
        await customerSessionApi.getCurrentSession();
        expect.fail('Should have thrown');
      } catch (err) {
        const error = err as CustomerSessionApiError;
        expect(error.status).toBe(503);
        expect(error.errorType).toBe('unavailable');
        expect(error.message).toBe('Bakım çalışması devam ediyor');
      }
    });

    it('handles 500 internal server error as generic', async () => {
      globalThis.fetch = vi.fn().mockResolvedValue({
        ok: false,
        status: 500,
        json: async () => ({ detail: 'Internal failure' }),
        headers: new Headers(),
      });

      try {
        await customerSessionApi.getCurrentSession();
        expect.fail('Should have thrown');
      } catch (err) {
        const error = err as CustomerSessionApiError;
        expect(error.status).toBe(500);
        expect(error.errorType).toBe('generic');
      }
    });

    it('handles network failure by converting to status 0 unavailable error', async () => {
      globalThis.fetch = vi.fn().mockRejectedValue(new Error('Network offline'));

      try {
        await customerSessionApi.getCurrentSession();
        expect.fail('Should have thrown');
      } catch (err) {
        const error = err as CustomerSessionApiError;
        expect(error.status).toBe(0);
        expect(error.errorType).toBe('unavailable');
        expect(error.message).toContain('Sunucuya ulaşılamıyor');
      }
    });
  });
});
