import { describe, it, expect, vi } from 'vitest';
import { render, screen } from '@testing-library/react';
import { App } from './App';

describe('Admin Web App', () => {
  describe('Initial Render', () => {
    it('renders sidebar with navigation buttons', () => {
      render(<App />);

      expect(screen.getByText('Restoran Yönetim')).toBeDefined();
      expect(screen.getByText('Kontrol Paneli')).toBeDefined();
      expect(screen.getByText('Menü Yönetimi')).toBeDefined();
      expect(screen.getByText('Şube & Masalar')).toBeDefined();
    });

    it('renders header, title, and admin badge', () => {
      render(<App />);

      expect(screen.getByText('Yönetim Paneli')).toBeDefined();
      expect(screen.getByText('Organizasyon Genel Bakışı')).toBeDefined();
      expect(screen.getByText('Restoran Admini')).toBeDefined();
    });

    it('renders metric cards for orders and tables', () => {
      render(<App />);

      expect(screen.getByText('Günlük Sipariş')).toBeDefined();
      expect(screen.getByText('Aktif Masalar')).toBeDefined();
    });
  });

  describe('Empty State & Error Boundary', () => {
    it('renders empty state when there are no metrics', () => {
      render(<App hasMetrics={false} />);

      expect(screen.getByRole('status')).toBeDefined();
      expect(screen.getByText('Henüz Raporlanmış Veri Yok')).toBeDefined();
      expect(screen.getByText('Raporları Yenile')).toBeDefined();
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
      expect(h1.textContent).toBe('Yönetim Paneli');

      const h2 = screen.getByRole('heading', { level: 2 });
      expect(h2).toBeDefined();
      expect(h2.textContent).toBe('Restoran Yönetim');
    });

    it('has accessible navigation buttons', () => {
      render(<App />);

      const nav = screen.getByRole('navigation', { name: 'Ana Gezinti' });
      expect(nav).toBeDefined();

      const buttons = screen.getAllByRole('button');
      expect(buttons.length).toBeGreaterThanOrEqual(3);
    });
  });
});
