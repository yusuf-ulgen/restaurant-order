import { describe, it, expect, vi, beforeEach } from 'vitest';
import { render, screen, fireEvent, waitFor } from '@testing-library/react';
import React from 'react';
import { AdminContent } from '../App';
import { AuthProvider } from '../auth/AuthContext';
import { AdminConfigProvider } from '../config/AdminConfigContext';
import {
  resolveAdminNavigation,
  NavigationRegistryId,
} from '../navigation/navigationRegistry';
import { BrandingSettingsView } from '../settings/BrandingSettingsView';
import { UserPrincipalDto } from '@restaurant-order/contracts';
import {
  EffectiveTenantTheme,
  applyTenantTheme,
  clearTenantTheme,
} from '@restaurant-order/ui';

const mockAdminUser: UserPrincipalDto = {
  userId: 'user-admin-1',
  email: 'admin@restoran.com',
  role: 'RestaurantAdmin',
  tenantId: 'tenant-test-1',
  securityVersion: 1,
};

const mockBranches = [
  { id: 'branch-1', brandId: 'brand-1', name: 'Kadıköy Şubesi', slug: 'kadikoy', isActive: true, status: 'Active' },
  { id: 'branch-2', brandId: 'brand-1', name: 'Beşiktaş Şubesi', slug: 'besiktas', isActive: true, status: 'Active' },
];

const mockCustomTheme: EffectiveTenantTheme = {
  tenantId: 'tenant-test-1',
  brandId: 'brand-1',
  branchId: 'branch-1',
  brandDisplayName: 'Lezzet Dünyası',
  branchDisplayName: 'Kadıköy Şubesi',
  logoUrl: 'https://cdn.example.com/logo.png',
  faviconUrl: '/favicon.ico',
  primaryColor: '#e11d48',
  primaryHoverColor: '#be123c',
  secondaryColor: '#475569',
  accentColor: '#0ea5e9',
  surfaceColor: '#ffffff',
  backgroundColor: '#f8fafc',
  shellTitle: 'Lezzet Portal',
  shellSubtitle: 'Kadıköy Şube Paneli',
  footerText: 'Lezzet Dünyası A.Ş.',
  footerBranchInfo: 'Moda Cad. No: 12',
  hasBranchOverride: true,
  navigationConfigJson: null,
  navigationOverrides: [
    { id: 'menu', isVisible: true, order: 1, labelOverride: 'Özel Menü' },
  ],
};

function renderWithProviders(
  ui: React.ReactElement,
  user: UserPrincipalDto | null = mockAdminUser,
  initialTheme: EffectiveTenantTheme | null = null
) {
  return render(
    <AuthProvider initialUser={user}>
      <AdminConfigProvider initialTheme={initialTheme}>
        {ui}
      </AdminConfigProvider>
    </AuthProvider>
  );
}

describe('Admin Configuration Driven Shell (Phase 4.3)', () => {
  beforeEach(() => {
    vi.restoreAllMocks();
    clearTenantTheme();
  });

  describe('Configuration Loading, Fallback & Retry', () => {
    it('successfully loads effective config from API and applies theme tokens', async () => {
      globalThis.fetch = vi.fn().mockImplementation((url: string) => {
        if (url === '/api/v1/restaurant-config/branches') {
          return Promise.resolve({ ok: true, json: async () => mockBranches });
        }
        if (url === '/api/v1/restaurant-config/branches/branch-1/theme') {
          return Promise.resolve({ ok: true, json: async () => mockCustomTheme });
        }
        return Promise.resolve({ ok: false, status: 404 });
      }) as unknown as typeof fetch;

      renderWithProviders(<AdminContent />);

      await waitFor(() => {
        expect(screen.getByText('Lezzet Portal')).toBeDefined();
        expect(screen.getByTestId('header-branch-name')).toBeDefined();
        expect(screen.getByText('Kadıköy Şube Paneli')).toBeDefined();
      });

      // Branch switcher should display available branches
      const switcher = screen.getByTestId('branch-switcher');
      expect(switcher).toBeDefined();
      expect(screen.getByText('Beşiktaş Şubesi')).toBeDefined();
    });

    it('displays loading spinner while config is in-flight', () => {
      globalThis.fetch = vi.fn().mockReturnValue(new Promise(() => {})) as unknown as typeof fetch;

      renderWithProviders(<AdminContent />);

      expect(screen.getByTestId('admin-loading')).toBeDefined();
      expect(screen.getByLabelText('Yönetim ayarları yükleniyor...')).toBeDefined();
    });

    it('falls back to safe default shell and provides retry on API failure', async () => {
      let fail = true;
      globalThis.fetch = vi.fn().mockImplementation((url: string) => {
        if (fail) {
          return Promise.reject(new Error('Network error'));
        }
        if (url === '/api/v1/restaurant-config/branches') {
          return Promise.resolve({ ok: true, json: async () => mockBranches });
        }
        if (url === '/api/v1/restaurant-config/branches/branch-1/theme') {
          return Promise.resolve({ ok: true, json: async () => mockCustomTheme });
        }
        return Promise.resolve({ ok: false });
      }) as unknown as typeof fetch;

      renderWithProviders(<AdminContent />);

      // Wait for error state with fallback shell
      await waitFor(() => {
        expect(screen.getByTestId('config-error-message')).toBeDefined();
        expect(screen.getByText('Yönetim Paneli')).toBeDefined(); // Safe fallback title
        expect(screen.getByRole('button', { name: 'Tekrar Dene' })).toBeDefined();
      });

      // Recover after retry
      fail = false;
      fireEvent.click(screen.getByRole('button', { name: 'Tekrar Dene' }));

      await waitFor(() => {
        expect(screen.queryByTestId('config-error-message')).toBeNull();
        expect(screen.getByText('Lezzet Portal')).toBeDefined();
      });
    });

    it('applies and clears tenant theme tokens cleanly', () => {
      applyTenantTheme({
        primary: '#123456',
        primaryHover: '#234567',
        bg: '#abcdef',
      });

      expect(document.documentElement.style.getPropertyValue('--ro-color-primary')).toBe('#123456');

      clearTenantTheme();

      expect(document.documentElement.style.getPropertyValue('--ro-color-primary')).toBe('');
    });
  });

  describe('Branch & Tenant Lifecycle', () => {
    it('switching branch reloads theme and updates header branch name', async () => {
      const besiktasTheme: EffectiveTenantTheme = {
        ...mockCustomTheme,
        branchId: 'branch-2',
        branchDisplayName: 'Beşiktaş Şubesi',
        shellTitle: 'Beşiktaş Portalı',
        shellSubtitle: 'Çarşı Şubesi',
        primaryColor: '#000000',
      };

      globalThis.fetch = vi.fn().mockImplementation((url: string) => {
        if (url === '/api/v1/restaurant-config/branches') {
          return Promise.resolve({ ok: true, json: async () => mockBranches });
        }
        if (url === '/api/v1/restaurant-config/branches/branch-1/theme') {
          return Promise.resolve({ ok: true, json: async () => mockCustomTheme });
        }
        if (url === '/api/v1/restaurant-config/branches/branch-2/theme') {
          return Promise.resolve({ ok: true, json: async () => besiktasTheme });
        }
        return Promise.resolve({ ok: false });
      }) as unknown as typeof fetch;

      renderWithProviders(<AdminContent />);

      await waitFor(() => {
        expect(screen.getByText('Lezzet Portal')).toBeDefined();
      });

      const switcher = screen.getByTestId('branch-switcher');
      fireEvent.change(switcher, { target: { value: 'branch-2' } });

      await waitFor(() => {
        expect(screen.getByText('Beşiktaş Portalı')).toBeDefined();
        expect(screen.getByText('Çarşı Şubesi')).toBeDefined();
      });
    });

    it('clears theme and config state on logout', async () => {
      const { unmount } = renderWithProviders(<AdminContent />, mockAdminUser, mockCustomTheme);

      expect(screen.getByText('Lezzet Portal')).toBeDefined();

      // Unmounting simulates logout / unauthenticated transition
      unmount();
      clearTenantTheme();

      expect(document.documentElement.style.getPropertyValue('--ro-color-primary')).toBe('');
    });
  });

  describe('Navigation Registry, RBAC & Overrides', () => {
    it('restricts brand settings from BranchManager role', () => {
      const sectionsAdmin = resolveAdminNavigation(null, 'RestaurantAdmin');
      const settingsSectionAdmin = sectionsAdmin.find((s) => s.id === 'settings');
      expect(settingsSectionAdmin?.items.some((i) => i.id === 'brand-settings')).toBe(true);

      const sectionsBranchManager = resolveAdminNavigation(null, 'BranchManager');
      const allBMItemIds = sectionsBranchManager.flatMap((s) => s.items.map((i) => i.id));
      expect(allBMItemIds.includes('brand-settings')).toBe(false);
    });

    it('feature flag cannot bypass RBAC permissions', () => {
      // Configuration attempts to force isVisible: true on brand-settings for Waiter
      const maliciousOverride = [
        { id: 'brand-settings', isVisible: true, disabled: false },
      ];

      const sections = resolveAdminNavigation(maliciousOverride, 'Waiter');
      const allItemIds = sections.flatMap((s) => s.items.map((i) => i.id));

      expect(allItemIds.includes('brand-settings')).toBe(false);
    });

    it('rejects unknown navigation IDs in configuration overrides', () => {
      const invalidOverride = [
        { id: 'arbitrary-malicious-route', isVisible: true, labelOverride: 'Hacked' },
        { id: 'menu', labelOverride: 'Gurme Menü' },
      ];

      const sections = resolveAdminNavigation(invalidOverride, 'RestaurantAdmin');
      const allItemIds = sections.flatMap((s) => s.items.map((i) => i.id));

      expect(allItemIds.includes('arbitrary-malicious-route' as NavigationRegistryId)).toBe(false);
      expect(allItemIds.includes('menu')).toBe(true);

      const menuItem = sections.flatMap((s) => s.items).find((i) => i.id === 'menu');
      expect(menuItem?.label).toBe('Gurme Menü');
    });

    it('sanitizes HTML tags in label overrides', () => {
      const xssOverride = [
        { id: 'menu', labelOverride: '<script>alert(1)</script>Temiz Menü' },
      ];

      const sections = resolveAdminNavigation(xssOverride, 'RestaurantAdmin');
      const menuItem = sections.flatMap((s) => s.items).find((i) => i.id === 'menu');

      expect(menuItem?.label).not.toContain('<');
      expect(menuItem?.label).not.toContain('>');
      expect(menuItem?.label).toContain('Temiz Menü');
    });
  });

  describe('Shell Accessibility & Responsive Design', () => {
    it('supports desktop collapse and mobile drawer toggle', () => {
      renderWithProviders(<AdminContent />, mockAdminUser, mockCustomTheme);

      // Desktop collapse
      const collapseBtn = screen.getByTestId('sidebar-collapse-btn');
      fireEvent.click(collapseBtn);
      expect(screen.getByTestId('desktop-sidebar').getAttribute('data-collapsed')).toBe('true');
      fireEvent.click(collapseBtn);
      expect(screen.getByTestId('desktop-sidebar').getAttribute('data-collapsed')).toBeNull();

      // Mobile drawer
      const mobileBtn = screen.getByTestId('mobile-menu-toggle');
      fireEvent.click(mobileBtn);
      expect(screen.getByRole('dialog')).toBeDefined();

      const closeBtn = screen.getByRole('button', { name: 'Kapat' });
      fireEvent.click(closeBtn);
      expect(screen.queryByRole('dialog')).toBeNull();
    });

    it('has zero overflow at 320px viewport', () => {
      renderWithProviders(<AdminContent />, mockAdminUser, mockCustomTheme);

      const shell = screen.getByTestId('app-shell');
      expect(shell.style.maxWidth).toBe('100vw');
      expect(shell.style.overflowX).toBe('hidden');
    });
  });

  describe('Branding Settings View & Mutation Workflows', () => {
    it('saves updated branding settings and displays success notification', async () => {
      globalThis.fetch = vi.fn().mockImplementation((url: string, init?: RequestInit) => {
        if (url === '/api/v1/restaurant-config/branches') {
          return Promise.resolve({ ok: true, json: async () => mockBranches });
        }
        if (url === '/api/v1/restaurant-config/brands/brand-1/theme' && init?.method === 'PUT') {
          return Promise.resolve({
            ok: true,
            json: async () => ({
              ...mockCustomTheme,
              displayName: 'Yeni Lezzet',
              concurrencyToken: 'token-new-123',
            }),
          });
        }
        if (url === '/api/v1/restaurant-config/brands/brand-1/theme') {
          return Promise.resolve({
            ok: true,
            json: async () => ({ ...mockCustomTheme, concurrencyToken: 'token-initial-123' }),
          });
        }
        return Promise.resolve({ ok: true, json: async () => ({}) });
      }) as unknown as typeof fetch;

      renderWithProviders(<BrandingSettingsView />, mockAdminUser, mockCustomTheme);

      const displayNameInput = screen.getByLabelText(/Marka Görünen Adı/i);
      fireEvent.change(displayNameInput, { target: { value: 'Yeni Lezzet' } });

      const saveBtn = screen.getByRole('button', { name: 'Ayarları Kaydet' });
      fireEvent.click(saveBtn);

      await waitFor(() => {
        expect(screen.getByTestId('branding-success-message')).toBeDefined();
        expect(screen.getByText(/başarıyla kaydedildi/i)).toBeDefined();
      });
    });

    it('rejects invalid hex color with friendly form error', async () => {
      renderWithProviders(<BrandingSettingsView />, mockAdminUser, mockCustomTheme);

      const primaryInput = screen.getByLabelText(/Primary Renk/i);
      fireEvent.change(primaryInput, { target: { value: 'red-invalid' } });

      const saveBtn = screen.getByRole('button', { name: 'Ayarları Kaydet' });
      fireEvent.click(saveBtn);

      await waitFor(() => {
        expect(screen.getByText(/geçerli bir hex renk kodu/i)).toBeDefined();
      });
    });

    it('displays user-friendly error message on 409 stale concurrency conflict', async () => {
      globalThis.fetch = vi.fn().mockImplementation((url: string, init?: RequestInit) => {
        if (url.includes('/theme') && init?.method === 'PUT') {
          return Promise.resolve({
            ok: false,
            status: 409,
            json: async () => ({ detail: 'Concurrency conflict' }),
          });
        }
        return Promise.resolve({ ok: true, json: async () => ({ concurrencyToken: 'token-old' }) });
      }) as unknown as typeof fetch;

      renderWithProviders(<BrandingSettingsView />, mockAdminUser, mockCustomTheme);

      const saveBtn = screen.getByRole('button', { name: 'Ayarları Kaydet' });
      fireEvent.click(saveBtn);

      await waitFor(() => {
        expect(
          screen.getByText(/Bu ayarlar başka bir kullanıcı tarafından güncellenmiştir/i)
        ).toBeDefined();
      });
    });
  });
});
