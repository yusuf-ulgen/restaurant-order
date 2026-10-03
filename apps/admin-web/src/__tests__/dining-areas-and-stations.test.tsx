import { describe, it, expect, vi, beforeEach } from 'vitest';
import { render, screen, fireEvent, waitFor } from '@testing-library/react';
import React from 'react';
import * as contracts from '@restaurant-order/contracts';
import { AuthProvider } from '../auth/AuthContext';
import { AdminConfigProvider } from '../config/AdminConfigContext';
import { DiningAreasView } from '../settings/DiningAreasView';
import { PreparationStationsView } from '../settings/PreparationStationsView';
import { FeatureFlagsView } from '../settings/FeatureFlagsView';
import {
  DiningAreaContract,
  PreparationStationContract,
  EffectiveFeatureFlagsContract,
  BranchFeatureFlagsContract,
  TenantFeatureFlagsContract,
  UserPrincipalDto,
} from '@restaurant-order/contracts';

const mockAdminUser: UserPrincipalDto = {
  userId: 'user-admin-1',
  email: 'admin@restoran.com',
  role: 'RestaurantAdmin',
  tenantId: 'tenant-test-1',
  securityVersion: 1,
};

const mockAreas: DiningAreaContract[] = [
  {
    id: 'area-1',
    tenantId: 'tenant-test-1',
    branchId: 'branch-1',
    name: 'İç Salon',
    code: 'ic-salon',
    areaType: 'Indoor',
    sortOrder: 0,
    isActive: true,
    createdAtUtc: '2026-10-02T10:00:00Z',
    updatedAtUtc: '2026-10-02T10:00:00Z',
    concurrencyToken: 'token-area-1',
  },
  {
    id: 'area-2',
    tenantId: 'tenant-test-1',
    branchId: 'branch-1',
    name: 'Teras',
    code: 'teras',
    areaType: 'Terrace',
    sortOrder: 1,
    isActive: false,
    createdAtUtc: '2026-10-02T10:00:00Z',
    updatedAtUtc: '2026-10-02T10:00:00Z',
    concurrencyToken: 'token-area-2',
  },
];

const mockStations: PreparationStationContract[] = [
  {
    id: 'station-1',
    tenantId: 'tenant-test-1',
    branchId: 'branch-1',
    code: 'kitchen-main',
    displayName: 'Ana Mutfak',
    stationType: 'Kitchen',
    sortOrder: 0,
    isActive: true,
    createdAtUtc: '2026-10-02T10:00:00Z',
    updatedAtUtc: '2026-10-02T10:00:00Z',
    concurrencyToken: 'token-station-1',
  },
];

const mockEffectiveFlags: EffectiveFeatureFlagsContract = {
  tenantId: 'tenant-test-1',
  branchId: 'branch-1',
  flags: [
    { key: 'CustomerQrOrdering', isEnabled: true, source: 'CatalogDefault' },
    { key: 'CustomerServiceRequests', isEnabled: true, source: 'CatalogDefault' },
    { key: 'Tips', isEnabled: false, source: 'CatalogDefault' },
    { key: 'SplitBilling', isEnabled: false, source: 'CatalogDefault' },
    { key: 'OnlinePayments', isEnabled: false, source: 'CatalogDefault' },
    { key: 'KitchenDisplay', isEnabled: true, source: 'CatalogDefault' },
    { key: 'BarDisplay', isEnabled: true, source: 'CatalogDefault' },
    { key: 'Reservations', isEnabled: false, source: 'CatalogDefault' },
    { key: 'KioskMode', isEnabled: false, source: 'CatalogDefault' },
  ],
  evaluatedFlags: {
    CustomerQrOrdering: true,
    CustomerServiceRequests: true,
    Tips: false,
    SplitBilling: false,
    OnlinePayments: false,
    KitchenDisplay: true,
    BarDisplay: true,
    Reservations: false,
    KioskMode: false,
  },
};

const mockBranchFlags: BranchFeatureFlagsContract = {
  tenantId: 'tenant-test-1',
  branchId: 'branch-1',
  overrides: [
    { key: 'Tips', isEnabled: true, source: 'BranchOverride' },
  ],
  concurrencyToken: 'token-branch-flags-1',
  updatedAtUtc: '2026-10-02T10:00:00Z',
};

const mockTenantFlags: TenantFeatureFlagsContract = {
  tenantId: 'tenant-test-1',
  flags: [
    { key: 'CustomerQrOrdering', isEnabled: true, source: 'TenantDefault' },
    { key: 'Tips', isEnabled: false, source: 'TenantDefault' },
  ],
  concurrencyToken: 'token-tenant-flags-1',
  updatedAtUtc: '2026-10-02T10:00:00Z',
};

function renderWithProviders(ui: React.ReactElement, user: UserPrincipalDto = mockAdminUser) {
  return render(
    <AuthProvider initialUser={user}>
      <AdminConfigProvider
        initialBranchId="branch-1"
        initialBranches={[{ id: 'branch-1', name: 'Kadıköy Şubesi', slug: 'kadikoy', brandId: 'brand-1', isActive: true, status: 'Active' }]}
      >
        {ui}
      </AdminConfigProvider>
    </AuthProvider>
  );
}

function setupDefaultMocks() {
  vi.spyOn(contracts, 'fetchWithCsrf').mockImplementation(async (input: RequestInfo | URL) => {
    const url = typeof input === 'string' ? input : input.toString();
    if (url === '/api/v1/restaurant-config/branches') {
      return new Response(JSON.stringify([{ id: 'branch-1', name: 'Kadıköy Şubesi' }]), {
        status: 200,
        headers: { 'Content-Type': 'application/json' },
      });
    }
    if (url === '/api/v1/restaurant-config/branding') {
      return new Response(JSON.stringify({ brandDisplayName: 'Restoran' }), {
        status: 200,
        headers: { 'Content-Type': 'application/json' },
      });
    }
    if (url.includes('/dining-areas')) {
      return new Response(JSON.stringify(mockAreas), {
        status: 200,
        headers: { 'Content-Type': 'application/json' },
      });
    }
    if (url.includes('/stations')) {
      return new Response(JSON.stringify(mockStations), {
        status: 200,
        headers: { 'Content-Type': 'application/json' },
      });
    }
    if (url.includes('/features/effective')) {
      return new Response(JSON.stringify(mockEffectiveFlags), {
        status: 200,
        headers: { 'Content-Type': 'application/json' },
      });
    }
    if (url.includes('/features/override')) {
      return new Response(JSON.stringify(mockBranchFlags), {
        status: 200,
        headers: { 'Content-Type': 'application/json' },
      });
    }
    if (url.includes('/tenant/features')) {
      return new Response(JSON.stringify(mockTenantFlags), {
        status: 200,
        headers: { 'Content-Type': 'application/json' },
      });
    }
    return new Response(JSON.stringify({}), { status: 200 });
  });
}

describe('Dining Areas, Stations & Feature Flags Views (Phase 4.5)', () => {
  beforeEach(() => {
    vi.restoreAllMocks();
    setupDefaultMocks();
  });

  describe('DiningAreasView', () => {
    it('renders list of areas with status badges and details', async () => {
      renderWithProviders(<DiningAreasView />);

      expect(screen.getByTestId('dining-areas-loading')).toBeDefined();

      await waitFor(() => {
        expect(screen.getByText('İç Salon')).toBeDefined();
        expect(screen.getByText('ic-salon')).toBeDefined();
        expect(screen.getByText('Aktif')).toBeDefined();
        expect(screen.getByText('Teras')).toBeDefined();
        expect(screen.getByText('Pasif')).toBeDefined();
      });
    });

    it('renders empty state when no dining areas are defined', async () => {
      vi.spyOn(contracts, 'fetchWithCsrf').mockImplementation(async (input: RequestInfo | URL) => {
        const url = typeof input === 'string' ? input : input.toString();
        if (url.includes('/dining-areas')) {
          return new Response(JSON.stringify([]), { status: 200 });
        }
        if (url === '/api/v1/restaurant-config/branches') {
          return new Response(JSON.stringify([{ id: 'branch-1', name: 'Kadıköy Şubesi' }]), { status: 200 });
        }
        return new Response(JSON.stringify({}), { status: 200 });
      });

      renderWithProviders(<DiningAreasView />);

      await waitFor(() => {
        expect(screen.getByText('Henüz Tanımlı Alan Yok')).toBeDefined();
      });
    });

    it('opens create modal when clicking + Yeni Alan Ekle', async () => {
      renderWithProviders(<DiningAreasView />);

      await waitFor(() => {
        expect(screen.getByText('İç Salon')).toBeDefined();
      });

      fireEvent.click(screen.getByTestId('add-dining-area-button'));

      expect(screen.getByTestId('area-name-input')).toBeDefined();
      expect(screen.getByTestId('area-code-input')).toBeDefined();
      expect(screen.getByTestId('save-area-button')).toBeDefined();
    });

    it('displays error banner if fetching dining areas fails', async () => {
      vi.spyOn(contracts, 'fetchWithCsrf').mockImplementation(async (input: RequestInfo | URL) => {
        const url = typeof input === 'string' ? input : input.toString();
        if (url.includes('/dining-areas')) {
          return new Response(JSON.stringify({ detail: 'Sunucu hatası: Alanlar yüklenemedi.' }), { status: 500 });
        }
        if (url === '/api/v1/restaurant-config/branches') {
          return new Response(JSON.stringify([{ id: 'branch-1', name: 'Kadıköy Şubesi' }]), { status: 200 });
        }
        return new Response(JSON.stringify({}), { status: 200 });
      });

      renderWithProviders(<DiningAreasView />);

      await waitFor(() => {
        expect(screen.getByTestId('dining-area-error')).toBeDefined();
        expect(screen.getByText('Sunucu hatası: Alanlar yüklenemedi.')).toBeDefined();
      });
    });
  });

  describe('PreparationStationsView', () => {
    it('renders list of stations with types and code badges', async () => {
      renderWithProviders(<PreparationStationsView />);

      await waitFor(() => {
        expect(screen.getByText('Ana Mutfak')).toBeDefined();
        expect(screen.getByText('kitchen-main')).toBeDefined();
        expect(screen.getByText('Aktif')).toBeDefined();
      });
    });

    it('renders empty state when no stations are defined', async () => {
      vi.spyOn(contracts, 'fetchWithCsrf').mockImplementation(async (input: RequestInfo | URL) => {
        const url = typeof input === 'string' ? input : input.toString();
        if (url.includes('/stations')) {
          return new Response(JSON.stringify([]), { status: 200 });
        }
        if (url === '/api/v1/restaurant-config/branches') {
          return new Response(JSON.stringify([{ id: 'branch-1', name: 'Kadıköy Şubesi' }]), { status: 200 });
        }
        return new Response(JSON.stringify({}), { status: 200 });
      });

      renderWithProviders(<PreparationStationsView />);

      await waitFor(() => {
        expect(screen.getByText('Henüz Tanımlı İstasyon Yok')).toBeDefined();
      });
    });
  });

  describe('FeatureFlagsView', () => {
    it('renders effective feature flags and source indicators', async () => {
      renderWithProviders(<FeatureFlagsView />);

      await waitFor(() => {
        expect(screen.getByText('QR Menü & Sipariş')).toBeDefined();
        expect(screen.getByText('Garson Çağırma & Hizmet Talebi')).toBeDefined();
        expect(screen.getByText('Bahşiş Modülü')).toBeDefined();
      });

      // Switch to Branch Override tab
      fireEvent.click(screen.getByTestId('tab-branch-override'));
      expect(screen.getByTestId('branch-flag-Tips')).toBeDefined();

      // Switch to Tenant Defaults tab
      fireEvent.click(screen.getByTestId('tab-tenant-defaults'));
      expect(screen.getByTestId('tenant-flag-Tips')).toBeDefined();
      expect(screen.getByTestId('save-tenant-flags')).toBeDefined();
    });

    it('shows Tenant Defaults tab for RestaurantAdmin', async () => {
      renderWithProviders(<FeatureFlagsView />);
      await waitFor(() => {
        expect(screen.getByTestId('tab-tenant-defaults')).toBeDefined();
      });
    });

    it('hides Tenant Defaults tab and prevents tenant bypass for SuperAdmin', async () => {
      const superAdminUser: UserPrincipalDto = {
        userId: 'user-super-1',
        email: 'super@platform.com',
        role: 'SuperAdmin',
        securityVersion: 1,
      };
      renderWithProviders(<FeatureFlagsView />, superAdminUser);
      await waitFor(() => {
        expect(screen.getByText('QR Menü & Sipariş')).toBeDefined();
      });
      expect(screen.queryByTestId('tab-tenant-defaults')).toBeNull();
    });

    it('hides Tenant Defaults tab for BranchManager', async () => {
      const branchManagerUser: UserPrincipalDto = {
        userId: 'user-manager-1',
        email: 'manager@branch.com',
        role: 'BranchManager',
        tenantId: 'tenant-test-1',
        branchId: 'branch-1',
        securityVersion: 1,
      };
      renderWithProviders(<FeatureFlagsView />, branchManagerUser);
      await waitFor(() => {
        expect(screen.getByText('QR Menü & Sipariş')).toBeDefined();
      });
      expect(screen.queryByTestId('tab-tenant-defaults')).toBeNull();
    });
  });
});
