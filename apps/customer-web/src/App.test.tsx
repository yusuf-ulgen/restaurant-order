import { describe, it, expect, vi } from 'vitest';
import { render, screen, fireEvent } from '@testing-library/react';
import { App } from './App';
import { customerQrApi } from './customerQrApi';

describe('Customer Web App', () => {
  describe('Initial Render', () => {
    it('renders header, table info, and active session badge', () => {
      render(<App />);

      expect(screen.getByText('Restoran Sipariş')).toBeDefined();
      expect(screen.getByText('Masa 04 • Giriş Salonu')).toBeDefined();
      expect(screen.getByText('Açık Oturum')).toBeDefined();
    });

    it('renders welcome card with action buttons, where menu is disabled as upcoming', () => {
      render(<App />);

      expect(screen.getByText('Hoş Geldiniz')).toBeDefined();
      const menuBtn = screen.getByRole('button', { name: /Menüyü İncele/ });
      expect(menuBtn).toBeDefined();
      expect(menuBtn.hasAttribute('disabled')).toBe(true);

      const callWaiterBtn = screen.getByRole('button', { name: 'Garson Çağır' });
      expect(callWaiterBtn).toBeDefined();
      expect(callWaiterBtn.hasAttribute('disabled')).toBe(false);
    });
  });

  describe('Empty State & Error Boundary', () => {
    it('renders empty state without fake action button when there is no active session', () => {
      render(<App hasActiveSession={false} />);

      expect(screen.getByRole('status')).toBeDefined();
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
    it('has accessible landmark roles', () => {
      render(<App />);

      expect(screen.getByRole('banner')).toBeDefined();
      expect(screen.getByRole('main')).toBeDefined();
    });

    it('has proper heading hierarchy', () => {
      render(<App />);

      const h1 = screen.getByRole('heading', { level: 1 });
      expect(h1).toBeDefined();
      expect(h1.textContent).toBe('Restoran Sipariş');

      const h2 = screen.getByRole('heading', { level: 2 });
      expect(h2).toBeDefined();
      expect(h2.textContent).toBe('Hoş Geldiniz');
    });

    it('has accessible buttons', () => {
      render(<App />);

      const buttons = screen.getAllByRole('button');
      expect(buttons.length).toBeGreaterThanOrEqual(2);
      expect(buttons.some((btn) => btn.textContent?.includes('Menüyü İncele'))).toBe(true);
      expect(buttons.some((btn) => btn.textContent?.includes('Garson Çağır'))).toBe(true);
    });

    it('opens BottomSheet, keeps options disabled as upcoming, and closes via close button', () => {
      render(<App />);

      const callWaiterBtn = screen.getByRole('button', { name: 'Garson Çağır' });
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

    it('renders TableWelcomeView when isQrLanding is true or token prop is passed', () => {
      render(<App isQrLanding={true} token="test-qr-landing-token" />);
      expect(screen.getByTestId('table-welcome-container')).toBeDefined();
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
});
