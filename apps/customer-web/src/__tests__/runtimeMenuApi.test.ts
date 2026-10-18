import { describe, it, expect, vi, beforeEach, afterEach } from 'vitest';
import { runtimeMenuApi, RuntimeMenuApiError } from '../api/runtimeMenuApi';
import type { RuntimeMenuReadModel } from '@restaurant-order/contracts';

describe('runtimeMenuApi client', () => {
  const originalFetch = global.fetch;

  beforeEach(() => {
    vi.restoreAllMocks();
  });

  afterEach(() => {
    global.fetch = originalFetch;
  });

  it('fetches runtime menu successfully with credentials include and returns data', async () => {
    const mockData: RuntimeMenuReadModel = {
      branchId: 'b-123',
      currency: 'TRY',
      pricesIncludeTax: true,
      defaultTaxRateBps: 1000,
      isServiceChargeEnabled: false,
      serviceChargeRateBps: 0,
      menus: [],
    };

    const fetchMock = vi.fn().mockResolvedValue({
      ok: true,
      status: 200,
      json: async () => mockData,
      headers: new Headers(),
    });
    global.fetch = fetchMock;

    const result = await runtimeMenuApi.getRuntimeMenu('b-123');
    expect(result).toEqual(mockData);
    expect(fetchMock).toHaveBeenCalledWith(
      '/api/v1/catalog/branches/b-123/runtime-menu',
      expect.objectContaining({
        method: 'GET',
        credentials: 'include',
        headers: { Accept: 'application/json' },
      })
    );
  });

  it('throws RuntimeMenuApiError with session_expired on 401 response', async () => {
    global.fetch = vi.fn().mockResolvedValue({
      ok: false,
      status: 401,
      json: async () => ({ title: 'Unauthorized', detail: 'Oturum süresi doldu.' }),
      headers: new Headers(),
    });

    await expect(runtimeMenuApi.getRuntimeMenu('b-123')).rejects.toMatchObject({
      name: 'RuntimeMenuApiError',
      status: 401,
      errorType: 'session_expired',
      message: 'Oturum süresi doldu.',
    });
  });

  it('throws RuntimeMenuApiError with forbidden_scope on 403 response', async () => {
    global.fetch = vi.fn().mockResolvedValue({
      ok: false,
      status: 403,
      json: async () => ({ title: 'Forbidden', detail: 'Bu şubenin menüsüne erişim yetkiniz bulunmamaktadır.' }),
      headers: new Headers(),
    });

    await expect(runtimeMenuApi.getRuntimeMenu('foreign-branch')).rejects.toMatchObject({
      name: 'RuntimeMenuApiError',
      status: 403,
      errorType: 'forbidden_scope',
    });
  });

  it('throws RuntimeMenuApiError with rate_limited and retryAfterSeconds on 429 response', async () => {
    const headers = new Headers();
    headers.set('Retry-After', '45');

    global.fetch = vi.fn().mockResolvedValue({
      ok: false,
      status: 429,
      json: async () => ({ title: 'Too Many Requests', detail: 'Çok fazla istek yapıldı.' }),
      headers,
    });

    try {
      await runtimeMenuApi.getRuntimeMenu('b-123');
      expect.fail('Should have thrown');
    } catch (err: unknown) {
      expect(err).toBeInstanceOf(RuntimeMenuApiError);
      const apiErr = err as RuntimeMenuApiError;
      expect(apiErr.status).toBe(429);
      expect(apiErr.errorType).toBe('rate_limited');
      expect(apiErr.retryAfterSeconds).toBe(45);
    }
  });

  it('throws RuntimeMenuApiError with unavailable on 503 response', async () => {
    global.fetch = vi.fn().mockResolvedValue({
      ok: false,
      status: 503,
      json: async () => ({ detail: 'Sunucu bakımda.' }),
      headers: new Headers(),
    });

    await expect(runtimeMenuApi.getRuntimeMenu('b-123')).rejects.toMatchObject({
      name: 'RuntimeMenuApiError',
      status: 503,
      errorType: 'unavailable',
    });
  });

  it('throws RuntimeMenuApiError with malformed on unexpected response shape', async () => {
    global.fetch = vi.fn().mockResolvedValue({
      ok: true,
      status: 200,
      json: async () => ({ unexpectedField: 123 }), // missing menus and currency
      headers: new Headers(),
    });

    await expect(runtimeMenuApi.getRuntimeMenu('b-123')).rejects.toMatchObject({
      name: 'RuntimeMenuApiError',
      errorType: 'malformed',
    });
  });

  it('handles abort and timeout cleanly', async () => {
    const controller = new AbortController();
    controller.abort();

    global.fetch = vi.fn().mockImplementation(() => {
      const err = new DOMException('The user aborted a request.', 'AbortError');
      return Promise.reject(err);
    });

    await expect(runtimeMenuApi.getRuntimeMenu('b-123', controller.signal)).rejects.toMatchObject({
      name: 'RuntimeMenuApiError',
      status: 408,
      errorType: 'unavailable',
    });
  });

  it('handles generic network failure', async () => {
    global.fetch = vi.fn().mockRejectedValue(new Error('Failed to fetch'));

    await expect(runtimeMenuApi.getRuntimeMenu('b-123')).rejects.toMatchObject({
      name: 'RuntimeMenuApiError',
      status: 0,
      errorType: 'unavailable',
    });
  });
});
