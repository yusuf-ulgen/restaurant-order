import { describe, it, expect, vi } from 'vitest';
import { render, screen, fireEvent } from '@testing-library/react';
import { App } from './App';

describe('Operations Web App', () => {
  describe('Initial Render', () => {
    it('renders header, branch shift info, and waiter mode badge', () => {
      render(<App />);

      expect(screen.getByText('Garson & Operasyon')).toBeDefined();
      expect(screen.getByText('Şube: Kadıköy • Aktif Vardiya')).toBeDefined();
      expect(screen.getByText('Garson Modu')).toBeDefined();
    });

    it('renders table management card with action buttons', () => {
      render(<App />);

      expect(screen.getByText('Masa Yönetimi')).toBeDefined();
      expect(screen.getByText('Masa Planı')).toBeDefined();
      expect(screen.getByText('Çağrılar (0)')).toBeDefined();
    });
  });

  describe('Empty State & Error Boundary', () => {
    it('renders empty state when there are no active tables', () => {
      render(<App hasActiveTables={false} />);

      expect(screen.getByRole('status')).toBeDefined();
      expect(screen.getByText('Aktif Masa Bulunmuyor')).toBeDefined();
      const actionButton = screen.getByText('Masaları Yenile');
      expect(actionButton).toBeDefined();
      fireEvent.click(actionButton);
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
      expect(h1.textContent).toBe('Garson & Operasyon');

      const h2 = screen.getByRole('heading', { level: 2 });
      expect(h2).toBeDefined();
      expect(h2.textContent).toBe('Masa Yönetimi');
    });

    it('has accessible buttons', () => {
      render(<App />);

      const buttons = screen.getAllByRole('button');
      expect(buttons.length).toBeGreaterThanOrEqual(2);
      expect(buttons.some((btn) => btn.textContent?.includes('Masa Planı'))).toBe(true);
      expect(buttons.some((btn) => btn.textContent?.includes('Çağrılar'))).toBe(true);
    });

    it('opens and closes active calls Modal when Çağrılar button is clicked', () => {
      render(<App />);

      const callsBtn = screen.getByRole('button', { name: 'Çağrılar (0)' });
      fireEvent.click(callsBtn);

      const dialog = screen.getByRole('dialog');
      expect(dialog).toBeDefined();
      expect(screen.getByText('Aktif Çağrılar')).toBeDefined();
      expect(screen.getByText('Bekleyen Çağrı Yok')).toBeDefined();

      const closeBtn = screen.getByRole('button', { name: 'Kapat' });
      fireEvent.click(closeBtn);

      expect(screen.queryByRole('dialog')).toBeNull();
    });
  });
});
