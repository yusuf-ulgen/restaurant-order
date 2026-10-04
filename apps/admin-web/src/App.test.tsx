import { describe, it, expect, vi, beforeEach } from 'vitest';
import { render, screen, fireEvent, waitFor } from '@testing-library/react';
import { App, AdminContent, type AdminAppProps } from './App';
import { AuthProvider } from './auth/AuthContext';
import { AdminConfigProvider, createDefaultFallbackTheme } from './config/AdminConfigContext';
import { UserPrincipalDto } from '@restaurant-order/contracts';

const mockAdminUser: UserPrincipalDto = {
  userId: 'user-admin-1',
  email: 'admin@restoran.com',
  role: 'RestaurantAdmin',
  tenantId: 'tenant-1',
  securityVersion: 1,
};

const mockUnauthorizedUser: UserPrincipalDto = {
  userId: 'user-waiter-1',
  email: 'waiter@restoran.com',
  role: 'Waiter',
  tenantId: 'tenant-1',
  securityVersion: 1,
};

const renderAdminContent = (user: UserPrincipalDto | null, props: AdminAppProps = {}) =>
  render(
    <AuthProvider initialUser={user}>
      <AdminConfigProvider initialTheme={createDefaultFallbackTheme()}>
        <AdminContent {...props} />
      </AdminConfigProvider>
    </AuthProvider>
  );

describe('Admin Web App - Authentication & Access Control', () => {
  beforeEach(() => {
    vi.restoreAllMocks();
  });

  describe('Unconditional Security Enforcement', () => {
    it('anonymous user cannot view admin content and sees login form', async () => {
      globalThis.fetch = vi.fn().mockImplementation((url: string) => {
        if (url === '/api/v1/auth/me') {
          return Promise.resolve({ ok: false, status: 401, json: async () => ({}) });
        }
        if (url === '/api/v1/auth/refresh') {
          return Promise.resolve({ ok: false, status: 401 });
        }
        return Promise.resolve({ ok: false });
      }) as unknown as typeof fetch;

      render(<App />);

      await waitFor(() => {
        expect(screen.getByText('Restoran Yönetim Girişi')).toBeDefined();
        expect(screen.getByRole('button', { name: 'Giriş Yap' })).toBeDefined();
      });

      // Admin content MUST NOT be present in DOM
      expect(screen.queryByText('Yönetim Paneli')).toBeNull();
      expect(screen.queryByText('Organizasyon Genel Bakışı')).toBeNull();
      expect(screen.queryByText('Restoran Yönetim')).toBeNull();
    });

    it('admin content is not rendered while authentication is loading', () => {
      // Unresolved promise simulates in-flight session verification
      globalThis.fetch = vi.fn().mockReturnValue(new Promise(() => {})) as unknown as typeof fetch;

      render(<App />);

      // Spinner is displayed, admin content MUST NOT be present
      expect(screen.getByLabelText('Yükleniyor...')).toBeDefined();
      expect(screen.queryByText('Yönetim Paneli')).toBeNull();
      expect(screen.queryByText('Organizasyon Genel Bakışı')).toBeNull();
      expect(screen.queryByText('Restoran Yönetim')).toBeNull();
    });

    it('shows admin content after successful login', async () => {
      globalThis.fetch = vi.fn().mockImplementation((url: string) => {
        if (url === '/api/v1/auth/me') {
          return Promise.resolve({ ok: false, status: 401, json: async () => ({}) });
        }
        if (url === '/api/v1/auth/refresh') {
          return Promise.resolve({ ok: false, status: 401 });
        }
        if (url === '/api/v1/auth/login') {
          return Promise.resolve({
            ok: true,
            json: async () => ({ user: mockAdminUser }),
          });
        }
        return Promise.resolve({ ok: false });
      }) as unknown as typeof fetch;

      render(<App />);

      const submitBtn = await screen.findByRole('button', { name: 'Giriş Yap' });
      fireEvent.change(screen.getByLabelText(/E-Posta Adresi/i), { target: { value: 'admin@restoran.com' } });
      fireEvent.change(screen.getByLabelText(/Şifre/i), { target: { value: 'secret123' } });

      fireEvent.click(submitBtn);

      await waitFor(() => {
        expect(screen.getByText('Yönetim Paneli')).toBeDefined();
        expect(screen.getByText('Organizasyon Genel Bakışı')).toBeDefined();
        expect(screen.getByText('Restoran Admini')).toBeDefined();
      });
    });

    it('unauthorized role cannot view content and sees access denied screen', async () => {
      globalThis.fetch = vi.fn().mockImplementation((url: string) => {
        if (url === '/api/v1/auth/me') {
          return Promise.resolve({
            ok: true,
            json: async () => mockUnauthorizedUser,
          });
        }
        return Promise.resolve({ ok: false });
      }) as unknown as typeof fetch;

      render(<App />);

      await waitFor(() => {
        expect(screen.getByRole('alert')).toBeDefined();
        expect(screen.getByText('Yetkisiz Erişim')).toBeDefined();
        expect(screen.getByText('Bu sayfayı görüntülemek için gerekli yetkilere sahip değilsiniz.')).toBeDefined();
      });

      expect(screen.queryByText('Yönetim Paneli')).toBeNull();
      expect(screen.queryByText('Organizasyon Genel Bakışı')).toBeNull();
    });

    it('disabling authentication protection via props is impossible', async () => {
      globalThis.fetch = vi.fn().mockImplementation((url: string) => {
        if (url === '/api/v1/auth/me') {
          return Promise.resolve({ ok: false, status: 401, json: async () => ({}) });
        }
        if (url === '/api/v1/auth/refresh') {
          return Promise.resolve({ ok: false, status: 401 });
        }
        return Promise.resolve({ ok: false });
      }) as unknown as typeof fetch;

      // Attempt to pass arbitrary bypass prop
      const illegalProps = { requireAuth: false, bypassAuth: true } as unknown as AdminAppProps;
      render(<App {...illegalProps} />);

      await waitFor(() => {
        // App MUST still require login
        expect(screen.getByText('Restoran Yönetim Girişi')).toBeDefined();
      });

      expect(screen.queryByText('Yönetim Paneli')).toBeNull();
    });
  });

  describe('AdminContent UI & Layout', () => {
    it('renders sidebar with navigation buttons', () => {
      render(<AdminContent />);

      expect(screen.getByText('Restoran Yönetim')).toBeDefined();
      expect(screen.getByText('Kontrol Paneli')).toBeDefined();
      expect(screen.getByText(/Menü Yönetimi/i)).toBeDefined();
      expect(screen.getByText(/Şube & Masalar/i)).toBeDefined();
    });

    it('renders header, title, and admin badge', () => {
      render(<AdminContent />);

      expect(screen.getByText('Yönetim Paneli')).toBeDefined();
      expect(screen.getByText('Organizasyon Genel Bakışı')).toBeDefined();
      expect(screen.getByText('Restoran Admini')).toBeDefined();
    });

    it('renders metric cards for orders and tables', () => {
      render(<AdminContent />);

      expect(screen.getByText('Günlük Sipariş')).toBeDefined();
      expect(screen.getByText('Aktif Masalar')).toBeDefined();
    });

    it('renders empty state when there are no metrics', () => {
      render(<AdminContent hasMetrics={false} />);

      expect(screen.getByRole('status')).toBeDefined();
      expect(screen.getByText('Henüz Raporlanmış Veri Yok')).toBeDefined();
      expect(screen.queryByText('Raporları Yenile')).toBeNull();
    });

    it('catches render errors and displays error boundary fallback', () => {
      const spy = vi.spyOn(console, 'error').mockImplementation(() => {});

      expect(() => render(<AdminContent initialError={true} />)).toThrow('Yönetim verileri yüklenemedi.');

      spy.mockRestore();
    });

    it('has accessible landmark roles and proper heading hierarchy', () => {
      render(<AdminContent />);

      expect(screen.getByRole('banner')).toBeDefined();
      expect(screen.getByRole('main')).toBeDefined();

      const h1 = screen.getByRole('heading', { level: 1 });
      expect(h1).toBeDefined();
      expect(h1.textContent).toBe('Yönetim Paneli');

      const h2 = screen.getByRole('heading', { level: 2 });
      expect(h2).toBeDefined();
      expect(h2.textContent).toBe('Restoran Yönetim');
    });

    it('has accessible navigation items with correct link and disabled semantics', () => {
      render(<AdminContent />);

      const nav = screen.getByRole('navigation', { name: 'Ana Gezinti' });
      expect(nav).toBeDefined();

      const activeLink = screen.getByRole('link', { name: /Kontrol Paneli/i });
      expect(activeLink.getAttribute('aria-current')).toBe('page');
      expect(activeLink.getAttribute('href')).toBe('/');

      const disabledMenu = screen.getByTestId('sidebar-item-menu');
      expect(disabledMenu.getAttribute('aria-disabled')).toBe('true');
      expect(disabledMenu.textContent).toContain('Menü Yönetimi (Yakında)');
    });

    it('toggles sidebar collapsed state via collapse button', () => {
      render(<AdminContent />);

      const collapseBtn = screen.getByTestId('sidebar-collapse-btn');
      expect(collapseBtn).toBeDefined();

      fireEvent.click(collapseBtn);
      const sidebar = screen.getByTestId('desktop-sidebar');
      expect(sidebar.getAttribute('data-collapsed')).toBe('true');

      fireEvent.click(collapseBtn);
      expect(sidebar.getAttribute('data-collapsed')).toBeNull();
    });

    it('opens and closes mobile drawer via header toggle', () => {
      render(<AdminContent />);

      const mobileToggleBtn = screen.getByTestId('mobile-menu-toggle');
      fireEvent.click(mobileToggleBtn);

      const drawer = screen.getByRole('dialog');
      expect(drawer).toBeDefined();

      const closeBtn = screen.getByRole('button', { name: 'Kapat' });
      fireEvent.click(closeBtn);

      expect(screen.queryByRole('dialog')).toBeNull();
    });

    it('renders supported initial views and the dashboard empty state', () => {
      const cases: Array<[NonNullable<AdminAppProps['initialView']>, RegExp]> = [
        ['brand-settings', /Tema Ayarlar/],
        ['dining-areas', /Masa Alan/],
        ['preparation-stations', /Haz.rl.k/],
        ['feature-settings', /Özellik Yönetimi/],
        ['menu', /Bu mod/],
      ];

      for (const [initialView, expectedText] of cases) {
        const rendered = renderAdminContent(null, { initialView });
        expect(screen.getByText(expectedText)).toBeDefined();
        rendered.unmount();
      }

      renderAdminContent(null, { initialView: 'dashboard', hasMetrics: false });
      expect(screen.getByText(/Raporlan/)).toBeDefined();
    });

    it('formats roles in the header badge', () => {
      const renderWithRole = (role: string) => {
        const user: UserPrincipalDto = {
          userId: '1', email: 'test@example.test', role, tenantId: 'tenant-test', securityVersion: 1,
        };
        return renderAdminContent(user);
      };

      const roles: Array<[string, RegExp]> = [
        ['SuperAdmin', /^Super Admin$/],
        ['BranchManager', /M.d.r./],
        ['Cashier', /Kasa \/ Operasyon/],
        ['Waiter', /^Garson$/],
        ['UnknownRole', /^UnknownRole$/],
      ];

      for (const [role, expectedText] of roles) {
        const rendered = renderWithRole(role);
        expect(screen.getByText(expectedText)).toBeDefined();
        rendered.unmount();
      }
    });
  });
});
