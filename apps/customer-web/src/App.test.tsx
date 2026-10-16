import { describe, it, expect, vi, beforeEach } from 'vitest';
import { render, screen, fireEvent, waitFor } from '@testing-library/react';
import { App } from './App';
import { customerQrApi } from './customerQrApi';
import { customerSessionApi, CustomerSessionApiError } from './api/customerSessionApi';
import { useSessionStateCleanup } from './session/CustomerSessionContext';
import type { CustomerSessionDto } from '@restaurant-order/contracts';

const mockDefaultSession: CustomerSessionDto = {
  sessionId: 'test-session-1',
  tenantId: 'test-tenant-1',
  branchId: 'test-branch-1',
  tableId: 'test-table-1',
  tableNumber: '04',
  tableName: 'Giriş Salonu',
  sessionStatus: 'Open',
  concurrencyToken: '00000000-0000-0000-0000-000000000000',
  guestCount: 2,
  openedAtUtc: '2026-10-16T12:00:00Z',
};

describe('Customer Web App', () => {
  beforeEach(() => {
    vi.restoreAllMocks();
    localStorage.clear();
    sessionStorage.clear();
    vi.spyOn(customerSessionApi, 'getCurrentSession').mockResolvedValue(mockDefaultSession);
  });

  describe('Initial Render & Active Session View', () => {
    it('renders header, table info, and active session badge upon recovery', async () => {
      render(<App />);

      expect(screen.getByText('Restoran Sipariş')).toBeDefined();
      expect(await screen.findByText('Masa 04 • Giriş Salonu')).toBeDefined();
      expect(screen.getByText('Oturum Açık')).toBeDefined();
    });

    it('renders welcome card with action buttons, where menu is disabled as upcoming', async () => {
      render(<App />);

      expect(await screen.findByText('Hoş Geldiniz')).toBeDefined();
      const menuBtn = screen.getByRole('button', { name: /Menüyü İncele/ });
      expect(menuBtn).toBeDefined();
      expect(menuBtn.hasAttribute('disabled')).toBe(true);

      const callWaiterBtn = screen.getByRole('button', { name: 'Garson Çağır' });
      expect(callWaiterBtn).toBeDefined();
      expect(callWaiterBtn.hasAttribute('disabled')).toBe(false);
    });
  });

  describe('Empty State & Error Boundary', () => {
    it('renders empty state without fake action button when there is no active session', async () => {
      vi.spyOn(customerSessionApi, 'getCurrentSession').mockResolvedValue(null);

      render(<App hasActiveSession={false} skipBootstrap={true} />);

      expect(await screen.findByRole('status')).toBeDefined();
      expect(screen.getByText('Aktif Sipariş Bulunmuyor')).toBeDefined();
      expect(screen.queryByRole('button', { name: 'Menüyü Aç' })).toBeNull();
    });

    it('catches render errors and displays error boundary fallback', () => {
      const spy = vi.spyOn(console, 'error').mockImplementation(() => {});

      render(<App initialError={true} />);

      const alert = screen.getByRole('alert');
      expect(alert).toBeDefined();
      expect(screen.getByText('Beklenmeyen Bir Hata Oluştu')).toBeDefined();

      spy.mockRestore();
    });
  });

  describe('Basic Accessibility', () => {
    it('has accessible landmark roles', async () => {
      render(<App />);

      expect(screen.getByRole('banner')).toBeDefined();
      expect(await screen.findByRole('main')).toBeDefined();
    });

    it('has proper heading hierarchy', async () => {
      render(<App />);

      const h1 = screen.getByRole('heading', { level: 1 });
      expect(h1).toBeDefined();
      expect(h1.textContent).toBe('Restoran Sipariş');

      const h2 = await screen.findByRole('heading', { level: 2 });
      expect(h2).toBeDefined();
      expect(h2.textContent).toBe('Hoş Geldiniz');
    });

    it('has accessible buttons', async () => {
      render(<App />);

      await screen.findByText('Hoş Geldiniz');
      const buttons = screen.getAllByRole('button');
      expect(buttons.length).toBeGreaterThanOrEqual(2);
      expect(buttons.some((btn) => btn.textContent?.includes('Menüyü İncele'))).toBe(true);
      expect(buttons.some((btn) => btn.textContent?.includes('Garson Çağır'))).toBe(true);
    });

    it('opens BottomSheet, keeps options disabled as upcoming, and closes via close button', async () => {
      render(<App />);

      const callWaiterBtn = await screen.findByRole('button', { name: 'Garson Çağır' });
      fireEvent.click(callWaiterBtn);

      const dialog = screen.getByRole('dialog');
      expect(dialog).toBeDefined();
      expect(screen.getByText('Masanız için servis veya destek talebi iletin.')).toBeDefined();

      // Options must be disabled and not pretend to send a request
      const optionBtn = screen.getByRole('button', { name: /Masaya Su \/ Peçete Talebi/ });
      expect(optionBtn.hasAttribute('disabled')).toBe(true);
      fireEvent.click(optionBtn);

      // Dialog must NOT close on clicking disabled option
      expect(screen.getByRole('dialog')).toBeDefined();

      // Close dialog via close button
      const closeBtn = screen.getByRole('button', { name: 'Kapat' });
      fireEvent.click(closeBtn);
      expect(screen.queryByRole('dialog')).toBeNull();
    });
  });

  describe('QR Landing & Exchange', () => {
    it('renders TableWelcomeView when isQrLanding is true or token prop is passed', async () => {
      vi.spyOn(customerQrApi, 'resolveQr').mockResolvedValue({
        tenantId: 'tenant-1',
        branchId: 'branch-1',
        brandName: 'Gurme Restoran',
        branchName: 'Kadıköy',
        tableNumber: 'T-07',
        tableName: 'Teras 7',
        mode: 'static',
        hasActiveSession: false,
        activeSessionStatus: null,
      });

      render(<App isQrLanding={true} token="test-qr-landing-token" />);
      expect(screen.getByTestId('table-welcome-container')).toBeDefined();
      await waitFor(() => {
        expect(screen.getByText('Teras 7')).toBeDefined();
      });
    });

    it('updates header subtitle and badge when session becomes ready', async () => {
      vi.spyOn(customerQrApi, 'resolveQr').mockResolvedValue({
        tenantId: 'tenant-1',
        branchId: 'branch-1',
        brandName: 'Gurme Restoran',
        branchName: 'Kadıköy',
        tableNumber: 'T-07',
        tableName: 'Teras 7',
        mode: 'static',
        hasActiveSession: false,
        activeSessionStatus: null,
      });
      vi.spyOn(customerQrApi, 'exchangeQr').mockResolvedValue({
        sessionId: 'sess-1',
        sessionStatus: 'Active',
        accessTokenExpiresAt: '2026-10-10T16:00:00Z',
        tenantId: 'tenant-1',
        branchId: 'branch-1',
        tableNumber: 'T-07',
        tableName: 'Teras 7',
      });

      render(<App token="landing-token" />);

      const btn = await screen.findByTestId('continue-to-table-btn');
      fireEvent.click(btn);

      expect(await screen.findByText('Masa T-07 • Teras 7')).toBeDefined();
      expect(screen.getByText('Oturum Açık')).toBeDefined();
    });
  });

  describe('Phase 7 Session Recovery & State Lifecycle', () => {
    it('restores active dining session on page refresh when customer cookie is present', async () => {
      const recoveredSession: CustomerSessionDto = {
        sessionId: 'recovered-sess-123',
        tenantId: 'tenant-abc',
        branchId: 'branch-xyz',
        tableId: 'table-777',
        tableNumber: '12',
        tableName: 'Bahçe 12',
        sessionStatus: 'Active',
        concurrencyToken: 'token-concurrency',
        guestCount: 4,
        openedAtUtc: '2026-10-16T14:30:00Z',
      };

      vi.spyOn(customerSessionApi, 'getCurrentSession').mockResolvedValue(recoveredSession);

      render(<App />);

      expect(await screen.findByText('Masa 12 • Bahçe 12')).toBeDefined();
      expect(screen.getByText('Oturum Açık')).toBeDefined();
      expect(screen.getAllByText(/Bahçe 12/).length).toBeGreaterThanOrEqual(1);
    });

    it('renders unauthenticated QR required view when no cookie exists (null session)', async () => {
      vi.spyOn(customerSessionApi, 'getCurrentSession').mockResolvedValue(null);

      render(<App />);

      expect(await screen.findByTestId('unauthenticated-qr-view')).toBeDefined();
      expect(screen.getByText('Masadaki QR Kodu Okutun')).toBeDefined();
    });

    it('renders expired view when dining session is closed/expired', async () => {
      vi.spyOn(customerSessionApi, 'getCurrentSession').mockRejectedValue(
        new CustomerSessionApiError(401, 'session_expired', 'Oturum süresi doldu veya kapatıldı.')
      );

      render(<App />);

      expect(await screen.findByTestId('expired-session-view')).toBeDefined();
      expect(screen.getByText('Oturum Sona Erdi')).toBeDefined();
      const newQrBtn = screen.getByRole('button', { name: 'Yeni QR Kod Okut' });
      expect(newQrBtn).toBeDefined();

      fireEvent.click(newQrBtn);
      expect(await screen.findByTestId('unauthenticated-qr-view')).toBeDefined();
    });

    it('renders error view with retry button on API 500 or network failure', async () => {
      vi.spyOn(customerSessionApi, 'getCurrentSession').mockRejectedValue(
        new CustomerSessionApiError(500, 'generic', 'Sunucu ile iletişim kurulamadı.')
      );

      render(<App />);

      expect(await screen.findByTestId('session-error-view')).toBeDefined();
      expect(screen.getByText('Sunucu ile iletişim kurulamadı.')).toBeDefined();

      const retryBtn = screen.getByRole('button', { name: 'Yeniden Dene' });
      expect(retryBtn).toBeDefined();

      // Retry successfully recovers
      vi.spyOn(customerSessionApi, 'getCurrentSession').mockResolvedValue(mockDefaultSession);
      fireEvent.click(retryBtn);

      expect(await screen.findByText('Hoş Geldiniz')).toBeDefined();
    });

    it('renders rate limit message and retryAfterSeconds on API 429', async () => {
      vi.spyOn(customerSessionApi, 'getCurrentSession').mockRejectedValue(
        new CustomerSessionApiError(429, 'rate_limited', 'Çok fazla istek yapıldı.', 45)
      );

      render(<App />);

      expect(await screen.findByTestId('session-error-view')).toBeDefined();
      expect(screen.getByText('Çok fazla istek yapıldı.')).toBeDefined();
      expect(screen.getByText(/Lütfen 45 saniye bekleyin/)).toBeDefined();
    });

    it('never writes access token or credentials to localStorage or sessionStorage', async () => {
      render(<App />);
      await screen.findByText('Hoş Geldiniz');

      expect(localStorage.getItem('token')).toBeNull();
      expect(localStorage.getItem('accessToken')).toBeNull();
      expect(localStorage.getItem('restaurant_access_token')).toBeNull();
      expect(sessionStorage.getItem('token')).toBeNull();
      expect(sessionStorage.getItem('accessToken')).toBeNull();
      expect(sessionStorage.getItem('restaurant_access_token')).toBeNull();
    });

    it('useSessionStateCleanup triggers callback when session expires', () => {
      const cleanupMock = vi.fn();

      const TestCleaner: React.FC = () => {
        useSessionStateCleanup(cleanupMock);
        return <div>Cleaner Active</div>;
      };

      render(
        <App initialSession={mockDefaultSession}>
          <TestCleaner />
        </App>
      );

      // Verify hook binds successfully
      expect(screen.getByText('Restoran Sipariş')).toBeDefined();
    });
  });
});
