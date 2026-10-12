import { describe, it, expect, vi, beforeEach } from 'vitest';
import {
  customerQrApi,
  setCustomerSession,
  getCustomerSession,
  clearCustomerSession,
} from '../customerQrApi';

describe('customerQrApi & In-Memory Session Store', () => {
  beforeEach(() => {
    vi.restoreAllMocks();
    clearCustomerSession();
    localStorage.clear();
    sessionStorage.clear();
  });

  describe('In-Memory Session Store Security', () => {
    it('stores session strictly in-memory and NEVER writes to localStorage or sessionStorage', () => {
      const mockSession = {
        sessionId: 'sess-123',
        sessionStatus: 'Active' as const,
        accessTokenExpiresAt: '2026-10-10T14:00:00Z',
        tenantId: 'tenant-1',
        branchId: 'branch-1',
        tableNumber: 'T-01',
        tableName: 'Salon Masa 1',
      };

      setCustomerSession(mockSession);

      // Verify in-memory retrieval works
      expect(getCustomerSession()).toEqual(mockSession);

      // Verify localStorage is completely empty
      expect(localStorage.length).toBe(0);
      expect(localStorage.getItem('token')).toBeNull();
      expect(localStorage.getItem('accessToken')).toBeNull();
      expect(localStorage.getItem('session')).toBeNull();

      // Verify sessionStorage is completely empty
      expect(sessionStorage.length).toBe(0);
      expect(sessionStorage.getItem('token')).toBeNull();
      expect(sessionStorage.getItem('accessToken')).toBeNull();

      // Verify clearing session works
      clearCustomerSession();
      expect(getCustomerSession()).toBeNull();
    });
  });

  describe('resolveQr endpoint', () => {
    it('resolves valid QR token successfully', async () => {
      const mockResponse = {
        tenantId: 'tenant-1',
        branchId: 'branch-1',
        brandName: 'Gurme Restoran',
        branchName: 'Kadıköy Şubesi',
        tableNumber: 'T-05',
        tableName: 'Teras Masa 5',
        mode: 'static' as const,
        hasActiveSession: false,
        activeSessionStatus: null,
      };

      vi.spyOn(globalThis, 'fetch').mockResolvedValueOnce({
        ok: true,
        status: 200,
        json: async () => mockResponse,
      } as Response);

      const result = await customerQrApi.resolveQr('valid-token-abc');
      expect(result).toEqual(mockResponse);
      expect(globalThis.fetch).toHaveBeenCalledWith(
        expect.stringContaining('/api/v1/qr/resolve?token=valid-token-abc'),
        expect.any(Object)
      );
    });

    it('handles 410 Revoked / Expired token with specific error code', async () => {
      vi.spyOn(globalThis, 'fetch').mockResolvedValueOnce({
        ok: false,
        status: 410,
        json: async () => ({
          detail: 'Bu QR kodun süresi dolmuş veya iptal edilmiş.',
          title: 'QR Code Revoked',
        }),
      } as Response);

      await expect(customerQrApi.resolveQr('revoked-token')).rejects.toThrow(
        /süresi dolmuş veya iptal edilmiş/i
      );
    });

    it('handles 429 Rate Limit error', async () => {
      vi.spyOn(globalThis, 'fetch').mockResolvedValueOnce({
        ok: false,
        status: 429,
        json: async () => ({ detail: 'Çok fazla istek gönderildi.' }),
      } as Response);

      await expect(customerQrApi.resolveQr('token-xyz')).rejects.toThrow(
        /Çok fazla istek gönderildi/i
      );
    });

    it('handles 503 Service Unavailable error', async () => {
      vi.spyOn(globalThis, 'fetch').mockResolvedValueOnce({
        ok: false,
        status: 503,
        json: async () => ({ detail: 'Hizmet şu anda kullanılamıyor.' }),
      } as Response);

      await expect(customerQrApi.resolveQr('token-503')).rejects.toThrow(
        /Hizmet şu anda kullanılamıyor/i
      );
    });

    it('sanitizes 500 error to prevent leaking backend database details', async () => {
      vi.spyOn(globalThis, 'fetch').mockResolvedValueOnce({
        ok: false,
        status: 500,
        json: async () => ({ detail: 'Npgsql.PostgresException: 42P01 table does not exist' }),
      } as Response);

      await expect(customerQrApi.resolveQr('token-500')).rejects.toThrow(
        'Sunucuda beklenmeyen bir hata oluştu. Lütfen tekrar deneyin.'
      );
    });
  });

  describe('exchangeQr endpoint', () => {
    it('exchanges raw QR token for active customer session', async () => {
      const mockExchange = {
        sessionId: 'sess-888',
        sessionStatus: 'Active' as const,
        accessTokenExpiresAt: '2026-10-10T16:00:00Z',
        tenantId: 'tenant-1',
        branchId: 'branch-1',
        tableNumber: 'T-01',
        tableName: 'Masa 1',
      };

      vi.spyOn(globalThis, 'fetch').mockResolvedValueOnce({
        ok: true,
        status: 200,
        json: async () => mockExchange,
      } as Response);

      const result = await customerQrApi.exchangeQr('exchange-token-xyz');
      expect(result).toEqual(mockExchange);
      expect(globalThis.fetch).toHaveBeenCalledWith(
        expect.stringContaining('/api/v1/qr/exchange'),
        expect.objectContaining({
          method: 'POST',
          body: JSON.stringify({ token: 'exchange-token-xyz' }),
        })
      );
    });

    it('handles 400 Inactive Table response code', async () => {
      vi.spyOn(globalThis, 'fetch').mockResolvedValueOnce({
        ok: false,
        status: 400,
        json: async () => ({
          title: 'Inactive Table',
          detail: 'Table is currently inactive.',
        }),
      } as Response);

      await expect(customerQrApi.exchangeQr('inactive-table-token')).rejects.toThrow(
        /Table is currently inactive/i
      );
    });

    it('handles 400 Invalid QR response code', async () => {
      vi.spyOn(globalThis, 'fetch').mockResolvedValueOnce({
        ok: false,
        status: 400,
        json: async () => ({
          title: 'Bad Request',
          detail: 'Invalid QR signature or payload.',
        }),
      } as Response);

      await expect(customerQrApi.exchangeQr('invalid-token')).rejects.toThrow(
        /Invalid QR signature or payload/i
      );
    });

    it('handles 410 session closed response code without revoked keyword', async () => {
      vi.spyOn(globalThis, 'fetch').mockResolvedValueOnce({
        ok: false,
        status: 410,
        json: async () => ({
          title: 'Dining Session Ended',
          detail: 'The dining session has already ended.',
        }),
      } as Response);

      await expect(customerQrApi.exchangeQr('closed-session-token')).rejects.toThrow(
        /The dining session has already ended/i
      );
    });
  });
});
