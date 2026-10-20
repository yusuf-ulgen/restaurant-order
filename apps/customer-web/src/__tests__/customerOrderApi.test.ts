import { describe, it, expect, vi, beforeEach } from 'vitest';
import { customerOrderApi, CustomerOrderApiError } from '../api/customerOrderApi';
import type { CreateCustomerOrderRequest, CustomerOrderResponseDto } from '@restaurant-order/contracts';

describe('Customer Order API Client & Error Mapping', () => {
  beforeEach(() => {
    vi.restoreAllMocks();
  });

  const sampleRequest: CreateCustomerOrderRequest = {
    clientPricingPolicyFingerprint: '0268781158e5455cd5066342bc2e2a04ede6e69974fef181a1dbd5d26c4728b9',
    clientExpectedTotalMinorUnits: 5000,
    items: [
      {
        menuItemId: 'item-1',
        itemVariantId: null,
        quantity: 2,
        customerNote: 'Acısız',
        selectedModifiers: null,
        clientExpectedUnitPriceMinorUnits: 2500,
      },
    ],
  };

  const sampleResponse: CustomerOrderResponseDto = {
    id: 'ord-123',
    tenantId: 'tenant-1',
    branchId: 'branch-1',
    diningSessionId: 'session-1',
    tableId: 'table-1',
    roundNumber: 1,
    orderSource: 'Customer',
    status: 'Submitted',
    currency: 'TRY',
    subtotalMinorUnits: 5000,
    taxMinorUnits: 500,
    serviceChargeMinorUnits: 250,
    totalMinorUnits: 5750,
    idempotencyKey: 'key-123',
    concurrencyToken: 'token-123',
    createdAtUtc: '2026-10-17T12:00:00Z',
    submittedAtUtc: '2026-10-17T12:00:00Z',
    items: [],
  };

  it('submits order successfully and returns created dto', async () => {
    globalThis.fetch = vi.fn().mockResolvedValue({
      ok: true,
      status: 201,
      json: async () => sampleResponse,
      headers: new Headers(),
    });

    const result = await customerOrderApi.submitOrder(sampleRequest, 'key-123');
    expect(result.id).toBe('ord-123');
    expect(result.roundNumber).toBe(1);
    expect(result.status).toBe('Submitted');

    const [url, init] = vi.mocked(globalThis.fetch).mock.calls[0] as [string, RequestInit];
    expect(url).toBe('/api/v1/customer/orders');
    expect(init.method).toBe('POST');
    const headers = init.headers as Headers;
    expect(headers.get('Idempotency-Key')).toBe('key-123');
  });

  it('handles ORDER_PRICE_CHANGED 409 conflict with friendly user message and price details', async () => {
    globalThis.fetch = vi.fn().mockResolvedValue({
      ok: false,
      status: 409,
      json: async () => ({
        type: 'https://httpstatuses.com/409',
        title: 'Order Price Changed',
        status: 409,
        detail: 'Price has changed',
        extensions: {
          errorCode: 'ORDER_PRICE_CHANGED',
          previousPriceMinorUnits: 2500,
          currentPriceMinorUnits: 2900,
          correlationId: 'corr-123',
        },
      }),
      headers: new Headers(),
    });

    await expect(customerOrderApi.submitOrder(sampleRequest, 'key-123')).rejects.toThrow(CustomerOrderApiError);

    try {
      await customerOrderApi.submitOrder(sampleRequest, 'key-123');
    } catch (err) {
      const error = err as CustomerOrderApiError;
      expect(error.status).toBe(409);
      expect(error.errorCode).toBe('ORDER_PRICE_CHANGED');
      expect(error.userMessage).toContain('fiyatları güncellendi');
      expect(error.previousPriceMinorUnits).toBe(2500);
      expect(error.currentPriceMinorUnits).toBe(2900);
      expect(error.correlationId).toBe('corr-123');
    }
  });

  it('handles ITEM_OUT_OF_STOCK 409 conflict with friendly message', async () => {
    globalThis.fetch = vi.fn().mockResolvedValue({
      ok: false,
      status: 409,
      json: async () => ({
        status: 409,
        detail: 'Product 86',
        extensions: {
          errorCode: 'ITEM_OUT_OF_STOCK',
          menuItemId: 'item-1',
        },
      }),
      headers: new Headers(),
    });

    try {
      await customerOrderApi.submitOrder(sampleRequest, 'key-123');
      expect.fail('Should have thrown');
    } catch (err) {
      const error = err as CustomerOrderApiError;
      expect(error.errorCode).toBe('ITEM_OUT_OF_STOCK');
      expect(error.userMessage).toContain('maalesef tükendi');
    }
  });

  it('handles ORDER_TAKING_DISABLED 403 forbidden', async () => {
    globalThis.fetch = vi.fn().mockResolvedValue({
      ok: false,
      status: 403,
      json: async () => ({
        status: 403,
        detail: 'Disabled',
        extensions: { errorCode: 'ORDER_TAKING_DISABLED' },
      }),
      headers: new Headers(),
    });

    try {
      await customerOrderApi.submitOrder(sampleRequest, 'key-123');
      expect.fail('Should have thrown');
    } catch (err) {
      const error = err as CustomerOrderApiError;
      expect(error.errorCode).toBe('ORDER_TAKING_DISABLED');
      expect(error.userMessage).toContain('yeni sipariş kabul etmemektedir');
    }
  });

  it('handles SESSION_NOT_ACTIVE 409 conflict', async () => {
    globalThis.fetch = vi.fn().mockResolvedValue({
      ok: false,
      status: 409,
      json: async () => ({
        status: 409,
        detail: 'Session closed',
        extensions: { errorCode: 'SESSION_NOT_ACTIVE' },
      }),
      headers: new Headers(),
    });

    try {
      await customerOrderApi.submitOrder(sampleRequest, 'key-123');
      expect.fail('Should have thrown');
    } catch (err) {
      const error = err as CustomerOrderApiError;
      expect(error.errorCode).toBe('SESSION_NOT_ACTIVE');
      expect(error.userMessage).toContain('oturumunuz aktif değil');
    }
  });

  it('handles network failure without losing cart context', async () => {
    globalThis.fetch = vi.fn().mockRejectedValue(new Error('Failed to fetch'));

    try {
      await customerOrderApi.submitOrder(sampleRequest, 'key-123');
      expect.fail('Should have thrown');
    } catch (err) {
      const error = err as CustomerOrderApiError;
      expect(error.errorCode).toBe('NETWORK_ERROR');
      expect(error.userMessage).toContain('Sepetiniz korundu');
    }
  });

  it('fetches order history list via getOrders', async () => {
    globalThis.fetch = vi.fn().mockResolvedValue({
      ok: true,
      status: 200,
      json: async () => [sampleResponse],
      headers: new Headers(),
    });

    const orders = await customerOrderApi.getOrders();
    expect(orders).toHaveLength(1);
    expect(orders[0]!.id).toBe('ord-123');
  });

  it('handles getOrders HTTP failure', async () => {
    globalThis.fetch = vi.fn().mockResolvedValue({
      ok: false,
      status: 500,
      json: async () => ({
        status: 500,
        detail: 'Database error',
        extensions: { errorCode: 'INTERNAL_ERROR' },
      }),
      headers: new Headers(),
    });

    await expect(customerOrderApi.getOrders()).rejects.toThrow(CustomerOrderApiError);
  });

  it('handles getOrders network failure', async () => {
    globalThis.fetch = vi.fn().mockRejectedValue(new Error('Network drop'));

    try {
      await customerOrderApi.getOrders();
      expect.fail('Should have thrown');
    } catch (err) {
      const error = err as CustomerOrderApiError;
      expect(error.errorCode).toBe('NETWORK_ERROR');
      expect(error.userMessage).toContain('Sipariş geçmişi yüklenemedi');
    }
  });

  it('fetches single order by id', async () => {
    globalThis.fetch = vi.fn().mockResolvedValue({
      ok: true,
      status: 200,
      json: async () => sampleResponse,
      headers: new Headers(),
    });

    const order = await customerOrderApi.getOrderById('ord-123');
    expect(order.id).toBe('ord-123');
  });

  it('handles getOrderById HTTP error', async () => {
    globalThis.fetch = vi.fn().mockResolvedValue({
      ok: false,
      status: 404,
      json: async () => ({
        status: 404,
        detail: 'Order not found',
        extensions: { errorCode: 'ORDER_NOT_FOUND' },
      }),
      headers: new Headers(),
    });

    await expect(customerOrderApi.getOrderById('ord-nonexistent')).rejects.toThrow(CustomerOrderApiError);
  });

  it('handles getOrderById network failure', async () => {
    globalThis.fetch = vi.fn().mockRejectedValue(new Error('Connection lost'));

    try {
      await customerOrderApi.getOrderById('ord-123');
      expect.fail('Should have thrown');
    } catch (err) {
      const error = err as CustomerOrderApiError;
      expect(error.errorCode).toBe('NETWORK_ERROR');
      expect(error.userMessage).toContain('Sipariş detayları yüklenemedi');
    }
  });
});
