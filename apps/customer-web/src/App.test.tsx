import { describe, it, expect, vi } from 'vitest';
import { render, screen } from '@testing-library/react';
import { App } from './App';

describe('Customer Web App', () => {
  describe('Initial Render', () => {
    it('renders header, table info, and active session badge', () => {
      render(<App />);

      expect(screen.getByText('Restoran Sipariş')).toBeDefined();
      expect(screen.getByText('Masa 04 • Giriş Salonu')).toBeDefined();
      expect(screen.getByText('Açık Oturum')).toBeDefined();
    });

    it('renders welcome card with action buttons', () => {
      render(<App />);

      expect(screen.getByText('Hoş Geldiniz')).toBeDefined();
      expect(screen.getByText('Menüyü İncele')).toBeDefined();
      expect(screen.getByText('Garson Çağır')).toBeDefined();
    });
  });

  describe('Empty State & Error Boundary', () => {
    it('renders empty state when there is no active session', () => {
      render(<App hasActiveSession={false} />);

      expect(screen.getByRole('status')).toBeDefined();
      expect(screen.getByText('Aktif Sipariş Bulunmuyor')).toBeDefined();
      expect(screen.getByText('Menüyü Aç')).toBeDefined();
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
  });
});
