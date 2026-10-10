import { describe, it, expect, vi, beforeEach } from 'vitest';
import { floorApi } from '../floorApi';

describe('floorApi client', () => {
  beforeEach(() => {
    vi.restoreAllMocks();
  });

  it('fetches tables for a branch', async () => {
    const mockTables = [
      {
        id: 'tbl-1',
        tableNumber: 'T-01',
        name: 'Masa 1',
        diningAreaId: 'area-1',
        branchId: 'br-1',
        capacity: 4,
        shape: 'Square',
        positionX: 100,
        positionY: 100,
        width: 80,
        height: 80,
        rotationDegrees: 0,
        isActive: true,
        concurrencyToken: 'ct-1',
      },
    ];

    vi.spyOn(globalThis, 'fetch').mockResolvedValueOnce({
      ok: true,
      status: 200,
      json: async () => mockTables,
    } as Response);

    const result = await floorApi.getTables('br-1');
    expect(result).toEqual(mockTables);
    expect(globalThis.fetch).toHaveBeenCalledWith(
      expect.stringContaining('/api/v1/floor/branches/br-1/tables'),
      expect.any(Object)
    );
  });

  it('handles batch layout update with headers', async () => {
    vi.spyOn(globalThis, 'fetch').mockResolvedValueOnce({
      ok: true,
      status: 200,
      json: async () => ({ success: true }),
    } as Response);

    const batch = [
      {
        tableId: 'tbl-1',
        positionX: 120,
        positionY: 140,
        width: 80,
        height: 80,
        rotationDegrees: 0,
        shape: 'Square' as const,
        concurrencyToken: 'ct-1',
      },
    ];

    await floorApi.batchUpdateLayout('br-1', batch);

    expect(globalThis.fetch).toHaveBeenCalledWith(
      expect.stringContaining('/api/v1/floor/branches/br-1/tables/batch-layout'),
      expect.objectContaining({
        method: 'PUT',
        body: JSON.stringify({ items: batch }),
      })
    );
  });

  it('handles 412 Precondition Failed with concurrency error masking', async () => {
    vi.spyOn(globalThis, 'fetch').mockResolvedValueOnce({
      ok: false,
      status: 412,
      json: async () => ({ detail: 'Veri güncelliğini yitirmiş (ETag uyuşmazlığı).' }),
    } as Response);

    await expect(
      floorApi.updateTable('br-1', 'tbl-1', { tableNumber: 'T-02', name: 'Masa 2', diningAreaId: 'a-1', capacity: 2 }, 'ct-old')
    ).rejects.toThrow(/ETag uyuşmazlığı/i);
  });

  it('handles 409 Conflict with appropriate message', async () => {
    vi.spyOn(globalThis, 'fetch').mockResolvedValueOnce({
      ok: false,
      status: 409,
      json: async () => ({ detail: 'Masa numarası bu şubede zaten mevcut.' }),
    } as Response);

    await expect(
      floorApi.createTable('br-1', {
        diningAreaId: 'a-1',
        tableNumber: 'T-01',
        name: 'Masa 1',
        capacity: 4,
        shape: 'Square',
        positionX: 0,
        positionY: 0,
        width: 80,
        height: 80,
        rotationDegrees: 0,
      })
    ).rejects.toThrow(/zaten mevcut/i);
  });

  it('handles 403 Forbidden with permission denied message', async () => {
    vi.spyOn(globalThis, 'fetch').mockResolvedValueOnce({
      ok: false,
      status: 403,
      json: async () => ({ detail: 'Bu işlem için yetkiniz bulunmamaktadır.' }),
    } as Response);

    await expect(floorApi.getTables('br-1')).rejects.toThrow(/Bu işlem için yetkiniz bulunmamaktadır/i);
  });

  it('handles 429 Rate Limit error', async () => {
    vi.spyOn(globalThis, 'fetch').mockResolvedValueOnce({
      ok: false,
      status: 429,
      json: async () => ({ detail: 'Çok fazla istek gönderildi.' }),
    } as Response);

    await expect(floorApi.getTables('br-1')).rejects.toThrow(/Çok fazla istek gönderildi/i);
  });

  it('handles 503 Service Unavailable', async () => {
    vi.spyOn(globalThis, 'fetch').mockResolvedValueOnce({
      ok: false,
      status: 503,
      json: async () => ({ detail: 'Servis şu anda geçici olarak kullanılamıyor.' }),
    } as Response);

    await expect(floorApi.getTables('br-1')).rejects.toThrow(/Servis şu anda geçici olarak kullanılamıyor/i);
  });

  it('masks raw SQL error messages in 500 responses', async () => {
    vi.spyOn(globalThis, 'fetch').mockResolvedValueOnce({
      ok: false,
      status: 500,
      json: async () => ({ detail: 'NpgsqlException: SELECT * FROM tables WHERE tenant_id = 123 (syntax error)' }),
    } as Response);

    await expect(floorApi.getTables('br-1')).rejects.toThrow(
      'Sunucuda beklenmeyen bir hata oluştu. Lütfen daha sonra tekrar deneyin.'
    );
  });
});
