import { describe, it, expect, vi, beforeEach } from 'vitest';
import {
  fetchActiveServiceRequests,
  fetchBranchReadyItems,
  fetchTableServiceRequests,
} from '../api';
import * as contracts from '@restaurant-order/contracts';

vi.mock('@restaurant-order/contracts', async () => {
  const actual = await vi.importActual<typeof import('@restaurant-order/contracts')>('@restaurant-order/contracts');
  return {
    ...actual,
    fetchWithCsrf: vi.fn(),
  };
});

describe('Paginated Active Work & Service Requests', () => {
  const branchId = '11111111-2222-3333-4444-555555555555';

  beforeEach(() => {
    vi.clearAllMocks();
  });

  it('fetchActiveServiceRequests exhausts all pages when active calls exceed 100', async () => {
    // Generate 125 active requests across 2 pages (page 1: 100, page 2: 25)
    const page1 = Array.from({ length: 100 }, (_, i) => ({
      id: `req-${i + 1}`,
      status: 'Open',
      requestType: 'WaiterCall',
    }));
    const page2 = Array.from({ length: 25 }, (_, i) => ({
      id: `req-${i + 101}`,
      status: 'Acknowledged',
      requestType: 'Water',
    }));

    vi.mocked(contracts.fetchWithCsrf)
      .mockResolvedValueOnce({
        ok: true,
        json: async () => page1,
      } as any)
      .mockResolvedValueOnce({
        ok: true,
        json: async () => page2,
      } as any);

    const result = await fetchActiveServiceRequests(branchId);

    // Verify all 125 items were fetched and stitched together
    expect(result).toHaveLength(125);
    expect(result[0]!.id).toBe('req-1');
    expect(result[124]!.id).toBe('req-125');

    // Verify correct paginated calls
    expect(contracts.fetchWithCsrf).toHaveBeenCalledTimes(2);
    expect(contracts.fetchWithCsrf).toHaveBeenNthCalledWith(
      1,
      `/api/v1/floor/branches/${branchId}/service-requests?status=active&limit=100&offset=0`,
      { signal: undefined }
    );
    expect(contracts.fetchWithCsrf).toHaveBeenNthCalledWith(
      2,
      `/api/v1/floor/branches/${branchId}/service-requests?status=active&limit=100&offset=100`,
      { signal: undefined }
    );
  });

  it('fetchBranchReadyItems exhausts all pages when ready items exceed 50', async () => {
    // Generate 75 ready items across 2 pages (page 1: 50, page 2: 25)
    const page1 = Array.from({ length: 50 }, (_, i) => ({
      orderId: `ord-${i + 1}`,
      itemId: `item-${i + 1}`,
      itemName: `Burger ${i + 1}`,
      status: 'Ready',
    }));
    const page2 = Array.from({ length: 25 }, (_, i) => ({
      orderId: `ord-${i + 51}`,
      itemId: `item-${i + 51}`,
      itemName: `Salad ${i + 51}`,
      status: 'Ready',
    }));

    vi.mocked(contracts.fetchWithCsrf)
      .mockResolvedValueOnce({
        ok: true,
        json: async () => page1,
      } as any)
      .mockResolvedValueOnce({
        ok: true,
        json: async () => page2,
      } as any);

    const result = await fetchBranchReadyItems(branchId);

    // Verify all 75 items were retrieved
    expect(result).toHaveLength(75);
    expect(result[0]!.itemId).toBe('item-1');
    expect(result[74]!.itemId).toBe('item-75');

    expect(contracts.fetchWithCsrf).toHaveBeenCalledTimes(2);
    expect(contracts.fetchWithCsrf).toHaveBeenNthCalledWith(
      1,
      `/api/v1/floor/branches/${branchId}/orders/ready-items?limit=50&offset=0`,
      { signal: undefined }
    );
    expect(contracts.fetchWithCsrf).toHaveBeenNthCalledWith(
      2,
      `/api/v1/floor/branches/${branchId}/orders/ready-items?limit=50&offset=50`,
      { signal: undefined }
    );
  });

  it('fetchTableServiceRequests defaults to status=active and limit=100 to prevent completed items pushing out active calls', async () => {
    vi.mocked(contracts.fetchWithCsrf).mockResolvedValueOnce({
      ok: true,
      json: async () => [{ id: 'req-active-1', status: 'Open' }],
    } as any);

    const tableId = 'tbl-123';
    const result = await fetchTableServiceRequests(branchId, tableId);

    expect(result).toHaveLength(1);
    expect(result[0]!.id).toBe('req-active-1');
    expect(contracts.fetchWithCsrf).toHaveBeenCalledWith(
      `/api/v1/floor/branches/${branchId}/service-requests?tableId=${tableId}&status=active&limit=100`,
      { signal: undefined }
    );
  });
});
