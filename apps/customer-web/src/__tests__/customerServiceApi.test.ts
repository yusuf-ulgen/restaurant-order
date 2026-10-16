import { describe, it, expect, vi, beforeEach, afterEach } from 'vitest';
import { customerServiceApi } from '../api/customerServiceApi';
import type { ServiceRequestDto, DiningSessionDto } from '@restaurant-order/contracts';

describe('customerServiceApi client tests', () => {
  const originalFetch = global.fetch;

  beforeEach(() => {
    vi.restoreAllMocks();
  });

  afterEach(() => {
    global.fetch = originalFetch;
  });

  it('createServiceRequest sends POST with Idempotency-Key and payload', async () => {
    const mockCreated: ServiceRequestDto = {
      id: 'sr-100',
      tenantId: 't-1',
      branchId: 'b-1',
      diningSessionId: 's-1',
      tableId: 'tbl-1',
      requestType: 'Water',
      status: 'Open',
      note: '2 bardak lütfen',
      idempotencyKey: 'custom-key-1',
      createdAtUtc: '2026-10-16T20:00:00Z',
      concurrencyToken: 'ct-100',
    };

    const fetchMock = vi.fn().mockResolvedValue({
      ok: true,
      status: 200,
      json: async () => mockCreated,
      headers: new Headers(),
    });
    global.fetch = fetchMock;

    const result = await customerServiceApi.createServiceRequest('Water', '2 bardak lütfen', 'custom-key-1');
    expect(result).toEqual(mockCreated);
    expect(fetchMock).toHaveBeenCalledWith(
      '/api/v1/customer/service-requests',
      expect.objectContaining({
        method: 'POST',
        credentials: 'include',
        body: JSON.stringify({ requestType: 'Water', note: '2 bardak lütfen' }),
      })
    );
    const calledOptions = fetchMock.mock.calls[0]![1] as { headers: Headers };
    expect(calledOptions.headers.get('Idempotency-Key')).toBe('custom-key-1');
  });

  it('createServiceRequest handles 429 rate limited response with retryAfterSeconds', async () => {
    global.fetch = vi.fn().mockResolvedValue({
      ok: false,
      status: 429,
      headers: new Headers({ 'Retry-After': '20' }),
      json: async () => ({
        title: 'Too Many Requests',
        detail: 'Lütfen bekleyin.',
        retryAfterSeconds: 20,
      }),
    });

    await expect(customerServiceApi.createServiceRequest('Waiter')).rejects.toMatchObject({
      name: 'ServiceApiError',
      status: 429,
      code: 'rate_limited',
      retryAfterSeconds: 20,
    });
  });

  it('createServiceRequest handles 409 conflict', async () => {
    global.fetch = vi.fn().mockResolvedValue({
      ok: false,
      status: 409,
      headers: new Headers(),
      json: async () => ({
        title: 'Conflict',
        detail: 'Aynı idempotency key farklı bir istekle kullanılmış.',
      }),
    });

    await expect(customerServiceApi.createServiceRequest('Napkin')).rejects.toMatchObject({
      name: 'ServiceApiError',
      status: 409,
      code: 'conflict',
    });
  });

  it('getOpenRequests returns array of open service requests', async () => {
    const mockList: ServiceRequestDto[] = [
      {
        id: 'sr-1',
        tenantId: 't-1',
        branchId: 'b-1',
        diningSessionId: 's-1',
        tableId: 'tbl-1',
        requestType: 'Waiter',
        status: 'Open',
        idempotencyKey: 'k-1',
        createdAtUtc: '2026-10-16T20:00:00Z',
        concurrencyToken: 'ct-1',
      },
    ];

    global.fetch = vi.fn().mockResolvedValue({
      ok: true,
      status: 200,
      json: async () => mockList,
      headers: new Headers(),
    });

    const result = await customerServiceApi.getOpenRequests();
    expect(result).toHaveLength(1);
    expect(result[0]!.requestType).toBe('Waiter');
  });

  it('cancelRequest sends DELETE and returns cancelled record', async () => {
    const mockCancelled: ServiceRequestDto = {
      id: 'sr-1',
      tenantId: 't-1',
      branchId: 'b-1',
      diningSessionId: 's-1',
      tableId: 'tbl-1',
      requestType: 'Waiter',
      status: 'Cancelled',
      idempotencyKey: 'k-1',
      createdAtUtc: '2026-10-16T20:00:00Z',
      concurrencyToken: 'ct-2',
    };

    global.fetch = vi.fn().mockResolvedValue({
      ok: true,
      status: 200,
      json: async () => mockCancelled,
      headers: new Headers(),
    });

    const result = await customerServiceApi.cancelRequest('sr-1');
    expect(result.status).toBe('Cancelled');
  });

  it('requestBill sends POST to branch session endpoint with concurrency token', async () => {
    const mockSession: DiningSessionDto = {
      id: 's-1',
      tenantId: 't-1',
      branchId: 'b-1',
      tableId: 'tbl-1',
      status: 'BillRequested',
      guestCount: 2,
      createdAtUtc: '2026-10-16T20:00:00Z',
      openedAtUtc: '2026-10-16T20:00:00Z',
      concurrencyToken: 'ct-new',
    };

    const fetchMock = vi.fn().mockResolvedValue({
      ok: true,
      status: 200,
      json: async () => mockSession,
      headers: new Headers(),
    });
    global.fetch = fetchMock;

    const result = await customerServiceApi.requestBill('b-1', 's-1', 'ct-old');
    expect(result.status).toBe('BillRequested');
    expect(fetchMock).toHaveBeenCalledWith(
      '/api/v1/floor/branches/b-1/sessions/s-1/request-bill',
      expect.objectContaining({
        method: 'POST',
        credentials: 'include',
        body: JSON.stringify({ concurrencyToken: 'ct-old' }),
      })
    );
    const calledOptions = fetchMock.mock.calls[0]![1] as { headers: Headers };
    expect(calledOptions.headers.get('If-Match')).toBe('"ct-old"');
  });
});
