import { describe, it, expect, vi, beforeEach } from 'vitest';
import { render, screen, fireEvent, waitFor } from '@testing-library/react';
import { TableWelcomeView } from '../TableWelcomeView';
import {
  customerQrApi,
  CustomerQrApiError,
  clearCustomerSession,
  getCustomerSession,
} from '../customerQrApi';

describe('TableWelcomeView Component', () => {
  const mockResolveData = {
    tenantId: 'tenant-1',
    branchId: 'branch-1',
    brandName: 'Gurme Restoran',
    branchName: 'Kadıköy Şubesi',
    tableNumber: 'T-01',
    tableName: 'Salon Masası',
    mode: 'static' as const,
    hasActiveSession: false,
    activeSessionStatus: null,
  };

  const mockExchangeData = {
    sessionId: 'sess-active-1',
    sessionStatus: 'Active' as const,
    accessTokenExpiresAt: '2026-10-10T16:00:00Z',
    tenantId: 'tenant-1',
    branchId: 'branch-1',
    tableNumber: 'T-01',
    tableName: 'Salon Masası',
  };

  beforeEach(() => {
    vi.restoreAllMocks();
    clearCustomerSession();
    localStorage.clear();
    sessionStorage.clear();
  });

  it('resolves QR token on mount and displays branch and table information', async () => {
    vi.spyOn(customerQrApi, 'resolveQr').mockResolvedValue(mockResolveData);

    render(<TableWelcomeView qrToken="test-qr-token-123" />);

    // Shows loading state initially
    expect(screen.getByRole('status')).toBeDefined();

    // Resolves and displays details
    await waitFor(() => {
      expect(screen.getByTestId('table-resolve-view')).toBeDefined();
    });

    expect(screen.getByText('Kadıköy Şubesi')).toBeDefined();
    expect(screen.getByText(/Masa T-01/i)).toBeDefined();
    expect(screen.getByText('Salon Masası')).toBeDefined();
    expect(screen.getByTestId('continue-to-table-btn')).toBeDefined();
  });

  it('allows switching interface language between Turkish and English', async () => {
    vi.spyOn(customerQrApi, 'resolveQr').mockResolvedValue(mockResolveData);

    render(<TableWelcomeView qrToken="test-qr-token-123" />);

    await waitFor(() => {
      expect(screen.getByTestId('table-resolve-view')).toBeDefined();
    });

    expect(screen.getByText('Masaya Devam Et')).toBeDefined();

    // Switch to English
    const enBtn = screen.getByTestId('lang-en-btn');
    fireEvent.click(enBtn);

    expect(screen.getByText('Continue to Table')).toBeDefined();
    expect(screen.getByText('Table T-01')).toBeDefined();
  });

  it('exchanges QR token, strips token from URL via replaceState, and keeps storage empty', async () => {
    vi.spyOn(customerQrApi, 'resolveQr').mockResolvedValue(mockResolveData);
    vi.spyOn(customerQrApi, 'exchangeQr').mockResolvedValue(mockExchangeData);
    const replaceStateSpy = vi.spyOn(window.history, 'replaceState');

    render(<TableWelcomeView qrToken="secret-qr-token-abc" />);

    await waitFor(() => {
      expect(screen.getByTestId('continue-to-table-btn')).toBeDefined();
    });

    // Click confirm button to exchange token
    const confirmBtn = screen.getByTestId('continue-to-table-btn');
    fireEvent.click(confirmBtn);

    // Verify session welcome card is shown
    await waitFor(() => {
      expect(screen.getByTestId('session-welcome-view')).toBeDefined();
    });

    // 1. Verify history.replaceState was called to remove token from address bar
    expect(replaceStateSpy).toHaveBeenCalledWith(
      null,
      '',
      expect.not.stringContaining('secret-qr-token-abc')
    );

    // 2. Verify token is in in-memory session store
    expect(getCustomerSession()).toEqual(mockExchangeData);

    // 3. Verify zero tokens or data in localStorage / sessionStorage
    expect(localStorage.length).toBe(0);
    expect(sessionStorage.length).toBe(0);

    // 4. Verify Phase 6.5 strict boundary: Menu, Cart, Order, Waiter call, Payments are NOT rendered as actions
    expect(screen.queryByRole('button', { name: /Menü/i })).toBeNull();
    expect(screen.queryByRole('button', { name: /Sepet/i })).toBeNull();
    expect(screen.queryByRole('button', { name: /Sipariş/i })).toBeNull();
    expect(screen.queryByRole('button', { name: /Garson/i })).toBeNull();
    expect(screen.queryByRole('button', { name: /Ödeme/i })).toBeNull();
    expect(screen.queryByRole('button', { name: /Bahşiş/i })).toBeNull();
  });

  it('displays revoked / expired QR error screen for 410 response', async () => {
    vi.spyOn(customerQrApi, 'resolveQr').mockRejectedValue(
      new CustomerQrApiError(
        410,
        'revoked',
        'Bu QR kodunun sürümü yenilenmiş ve geçerliliğini yitirmiştir. Lütfen güncel masa QR kodunu tarayın.'
      )
    );

    render(<TableWelcomeView qrToken="expired-token" />);

    await waitFor(() => {
      expect(screen.getByTestId('qr-error-view')).toBeDefined();
    });

    expect(screen.getByText(/geçerliliğini yitirmiştir/i)).toBeDefined();
  });

  it('displays rate limit error for 429 response', async () => {
    vi.spyOn(customerQrApi, 'resolveQr').mockRejectedValue(
      new CustomerQrApiError(
        429,
        'rate_limited',
        'Çok fazla istek gönderildi. Lütfen biraz bekleyin.'
      )
    );

    render(<TableWelcomeView qrToken="rate-limit-token" />);

    await waitFor(() => {
      expect(screen.getByTestId('qr-error-view')).toBeDefined();
    });

    expect(screen.getByText(/Çok fazla istek gönderildi/i)).toBeDefined();
  });

  it('safely renders user-generated content without XSS vulnerabilities', async () => {
    const xssPayload = {
      ...mockResolveData,
      branchName: '<b id="injected-html">Kadıköy</b>',
      tableName: '<img src="x" onerror="alert(1)" />',
    };

    vi.spyOn(customerQrApi, 'resolveQr').mockResolvedValue(xssPayload);

    render(<TableWelcomeView qrToken="test-token" />);

    await waitFor(() => {
      expect(screen.getByTestId('table-resolve-view')).toBeDefined();
    });

    // Verify injected HTML tags are rendered as plain text strings and not executed as DOM elements
    expect(document.getElementById('injected-html')).toBeNull();
    expect(document.querySelector('img[src="x"]')).toBeNull();
    expect(screen.getByText('<b id="injected-html">Kadıköy</b>')).toBeDefined();
    expect(screen.getByText('<img src="x" onerror="alert(1)" />')).toBeDefined();
  });
});
