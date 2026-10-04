import { describe, it, expect, vi, beforeEach } from 'vitest';
import { render, screen, fireEvent, waitFor } from '@testing-library/react';
import React from 'react';
import * as contracts from '@restaurant-order/contracts';
import { AuthProvider } from '../auth/AuthContext';
import { AdminConfigProvider } from '../config/AdminConfigContext';
import { DiningAreasView } from '../settings/DiningAreasView';
import { PreparationStationsView } from '../settings/PreparationStationsView';
import { FeatureFlagsView } from '../settings/FeatureFlagsView';
import { LiveThemePreview } from '../settings/LiveThemePreview';
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
    name: 'Ä°Ã§ Salon',
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
  {
    id: 'station-2',
    tenantId: 'tenant-test-1',
    branchId: 'branch-1',
    code: 'bar-main',
    displayName: 'Ana Bar',
    stationType: 'Bar',
    sortOrder: 1,
    isActive: false,
    createdAtUtc: '2026-10-02T10:00:00Z',
    updatedAtUtc: '2026-10-02T10:00:00Z',
    concurrencyToken: 'token-station-2',
  },
];

const mockEffectiveFlags: EffectiveFeatureFlagsContract = {
  tenantId: 'tenant-test-1',
  branchId: 'branch-1',
  flags: [
    { key: 'CustomerQrOrdering', isEnabled: true, source: 'CatalogDefault' },
    { key: 'Tips', isEnabled: false, source: 'CatalogDefault' },
  ],
  evaluatedFlags: {
    CustomerQrOrdering: true,
    Tips: false,
  },
};

const mockBranchFlags: BranchFeatureFlagsContract = {
  tenantId: 'tenant-test-1',
  branchId: 'branch-1',
  overrides: [{ key: 'Tips', isEnabled: true, source: 'BranchOverride' }],
  concurrencyToken: 'token-branch-flags-1',
  updatedAtUtc: '2026-10-02T10:00:00Z',
};

const mockTenantFlags: TenantFeatureFlagsContract = {
  tenantId: 'tenant-test-1',
  flags: [{ key: 'Tips', isEnabled: false, source: 'TenantDefault' }],
  concurrencyToken: 'token-tenant-flags-1',
  updatedAtUtc: '2026-10-02T10:00:00Z',
};

function renderWithProviders(ui: React.ReactElement) {
  return render(
    <AuthProvider initialUser={mockAdminUser}>
      <AdminConfigProvider
        initialBranchId="branch-1"
        initialBranches={[
          { id: 'branch-1', name: 'KadÄ±kÃ¶y Åubesi', slug: 'kadikoy', brandId: 'brand-1', isActive: true, status: 'Active' },
        ]}
      >
        {ui}
      </AdminConfigProvider>
    </AuthProvider>
  );
}

describe('Settings Views Comprehensive Interactions', () => {
  beforeEach(() => {
    vi.restoreAllMocks();
  });

  describe('LiveThemePreview', () => {
    it('renders with logo and subtitle', () => {
      render(
        <LiveThemePreview
          backgroundColor="#f0f0f0"
          surfaceColor="#ffffff"
          primaryColor="#112233"
          accentColor="#445566"
          logoUrl="https://example.com/logo.png"
          shellTitle="Test Restaurant"
          shellSubtitle="Best Food in Town"
          footerBranchInfo="Branch: Central"
        />
      );
      expect(screen.getByAltText('Logo')).toBeDefined();
      expect(screen.getByText('Test Restaurant')).toBeDefined();
      expect(screen.getByText('Best Food in Town')).toBeDefined();
      expect(screen.getByText('Branch: Central')).toBeDefined();
      expect(screen.getByText('Primary Buton')).toBeDefined();
      expect(screen.getByText('Accent Vurgu')).toBeDefined();
    });

    it('renders without logo and uses fallbacks', () => {
      render(
        <LiveThemePreview
          backgroundColor="#f0f0f0"
          surfaceColor="#ffffff"
          primaryColor="#112233"
          accentColor="#445566"
          displayName="Display Name Restoran"
          footerText="Footer Text Copyright"
        />
      );
      expect(screen.getByText('R')).toBeDefined();
      expect(screen.getByText('Display Name Restoran')).toBeDefined();
      expect(screen.getByText('Footer Text Copyright')).toBeDefined();
    });
  });

  describe('DiningAreasView interactions', () => {
    it('creates a new dining area via modal submission', async () => {
      const fetchSpy = vi.spyOn(contracts, 'fetchWithCsrf').mockImplementation(async (input: RequestInfo | URL) => {
        const url = typeof input === 'string' ? input : input.toString();
        if (url.includes('/dining-areas') && !url.includes('/dining-areas/')) {
          return new Response(JSON.stringify(mockAreas), { status: 200 });
        }
        if (url === '/api/v1/restaurant-config/branches') {
          return new Response(JSON.stringify([{ id: 'branch-1', name: 'KadÄ±kÃ¶y Åubesi' }]), { status: 200 });
        }
        return new Response(JSON.stringify({}), { status: 200 });
      });

      renderWithProviders(<DiningAreasView />);
      await waitFor(() => expect(screen.getByText('Ä°Ã§ Salon')).toBeDefined());

      fireEvent.click(screen.getByTestId('add-dining-area-button'));

      const nameInput = screen.getByTestId('area-name-input');
      const codeInput = screen.getByTestId('area-code-input');
      fireEvent.change(nameInput, { target: { value: 'BahÃ§e AlanÄ±' } });
      fireEvent.change(codeInput, { target: { value: 'bahce' } });

      fireEvent.click(screen.getByTestId('save-area-button'));

      await waitFor(() => {
        expect(fetchSpy).toHaveBeenCalledWith(
          expect.stringContaining('/dining-areas'),
          expect.objectContaining({ method: 'POST' })
        );
      });
    });

    it('toggles dining area active/deactive status and reorders', async () => {
      const fetchSpy = vi.spyOn(contracts, 'fetchWithCsrf').mockImplementation(async (input: RequestInfo | URL) => {
        const url = typeof input === 'string' ? input : input.toString();
        if (url.includes('/dining-areas') && !url.includes('/deactivate') && !url.includes('/activate') && !url.includes('/reorder')) {
          return new Response(JSON.stringify(mockAreas), { status: 200 });
        }
        if (url === '/api/v1/restaurant-config/branches') {
          return new Response(JSON.stringify([{ id: 'branch-1', name: 'KadÄ±kÃ¶y Åubesi' }]), { status: 200 });
        }
        return new Response(JSON.stringify({ ok: true }), { status: 200 });
      });

      renderWithProviders(<DiningAreasView />);
      await waitFor(() => expect(screen.getByText('Ä°Ã§ Salon')).toBeDefined());

      // Click Pasife Al for area-1
      const pasifBtn = screen.getByTestId('toggle-area-area-1');
      fireEvent.click(pasifBtn);

      await waitFor(() => {
        expect(fetchSpy).toHaveBeenCalledWith(
          expect.stringContaining('/area-1/deactivate'),
          expect.objectContaining({ method: 'POST' })
        );
      });

      // Click AktifleÅŸtir for area-2
      const aktifBtn = screen.getByTestId('toggle-area-area-2');
      fireEvent.click(aktifBtn);

      await waitFor(() => {
        expect(fetchSpy).toHaveBeenCalledWith(
          expect.stringContaining('/area-2/activate'),
          expect.objectContaining({ method: 'POST' })
        );
      });

      // Move down area-1
      const moveDownBtn = screen.getAllByRole('button', { name: /^A/ })[0]!;
      fireEvent.click(moveDownBtn);

      await waitFor(() => {
        expect(fetchSpy).toHaveBeenCalledWith(
          expect.stringContaining('/dining-areas/reorder'),
          expect.objectContaining({ method: 'POST' })
        );
      });
    });
  });

  describe('PreparationStationsView interactions', () => {
    it('creates, edits, toggles, and reorders preparation stations', async () => {
      const fetchSpy = vi.spyOn(contracts, 'fetchWithCsrf').mockImplementation(async (input: RequestInfo | URL) => {
        const url = typeof input === 'string' ? input : input.toString();
        if (url.includes('/stations') && !url.includes('/deactivate') && !url.includes('/activate') && !url.includes('/reorder')) {
          return new Response(JSON.stringify(mockStations), { status: 200 });
        }
        if (url === '/api/v1/restaurant-config/branches') {
          return new Response(JSON.stringify([{ id: 'branch-1', name: 'KadÄ±kÃ¶y Åubesi' }]), { status: 200 });
        }
        return new Response(JSON.stringify({ ok: true }), { status: 200 });
      });

      renderWithProviders(<PreparationStationsView />);
      await waitFor(() => expect(screen.getByText('Ana Mutfak')).toBeDefined());

      // Create new station
      fireEvent.click(screen.getByTestId('add-station-button'));
      const nameInput = screen.getByTestId('station-display-name-input');
      const codeInput = screen.getByTestId('station-code-input');
      fireEvent.change(nameInput, { target: { value: 'TatlÄ± Ä°stasyonu' } });
      fireEvent.change(codeInput, { target: { value: 'DESSERT' } });
      fireEvent.click(screen.getByTestId('save-station-button'));

      await waitFor(() => {
        expect(fetchSpy).toHaveBeenCalledWith(
          expect.stringContaining('/stations'),
          expect.objectContaining({ method: 'POST' })
        );
      });

      // Edit station-1
      const editBtn = screen.getByTestId('edit-station-station-1');
      fireEvent.click(editBtn);
      const editNameInput = screen.getByTestId('station-display-name-input');
      fireEvent.change(editNameInput, { target: { value: 'Ana Mutfak SÄ±cak' } });
      fireEvent.click(screen.getByTestId('save-station-button'));

      await waitFor(() => {
        expect(fetchSpy).toHaveBeenCalledWith(
          expect.stringContaining('/stations/station-1'),
          expect.objectContaining({ method: 'PUT' })
        );
      });

      // Toggle deactivate
      const deactBtn = screen.getByTestId('toggle-station-station-1');
      fireEvent.click(deactBtn);

      await waitFor(() => {
        expect(fetchSpy).toHaveBeenCalledWith(
          expect.stringContaining('/station-1/deactivate'),
          expect.objectContaining({ method: 'POST' })
        );
      });

      // Reorder
      const moveDownBtn = screen.getAllByRole('button', { name: /^A/ })[0]!;
      fireEvent.click(moveDownBtn);

      await waitFor(() => {
        expect(fetchSpy).toHaveBeenCalledWith(
          expect.stringContaining('/stations/reorder'),
          expect.objectContaining({ method: 'POST' })
        );
      });
    });
  });

  describe('FeatureFlagsView interactions', () => {
    it('saves branch overrides and tenant defaults', async () => {
      const fetchSpy = vi.spyOn(contracts, 'fetchWithCsrf').mockImplementation(async (input: RequestInfo | URL) => {
        const url = typeof input === 'string' ? input : input.toString();
        if (url.includes('/features/effective')) {
          return new Response(JSON.stringify(mockEffectiveFlags), { status: 200 });
        }
        if (url.includes('/features/override')) {
          return new Response(JSON.stringify(mockBranchFlags), { status: 200 });
        }
        if (url.includes('/tenant/features')) {
          return new Response(JSON.stringify(mockTenantFlags), { status: 200 });
        }
        if (url === '/api/v1/restaurant-config/branches') {
          return new Response(JSON.stringify([{ id: 'branch-1', name: 'KadÄ±kÃ¶y Åubesi' }]), { status: 200 });
        }
        return new Response(JSON.stringify({ ok: true }), { status: 200 });
      });

      renderWithProviders(<FeatureFlagsView />);
      await waitFor(() => expect(screen.getByText(/^QR Men/)).toBeDefined());

      // Go to branch override
      fireEvent.click(screen.getByTestId('tab-branch-override'));
      fireEvent.click(screen.getByTestId('branch-flag-Tips'));
      const branchSaveBtn = screen.getByTestId('save-branch-flags');
      fireEvent.click(branchSaveBtn);

      await waitFor(() => {
        expect(fetchSpy).toHaveBeenCalledWith(
          expect.stringContaining('/features/override'),
          expect.objectContaining({ method: 'PUT' })
        );
      });

      // Go to tenant defaults
      fireEvent.click(screen.getByTestId('tab-tenant-defaults'));
      fireEvent.click(screen.getByTestId('tenant-flag-Tips'));
      const tenantSaveBtn = screen.getByTestId('save-tenant-flags');
      fireEvent.click(tenantSaveBtn);

      await waitFor(() => {
        expect(fetchSpy).toHaveBeenCalledWith(
          expect.stringContaining('/tenant/features'),
          expect.objectContaining({ method: 'PUT' })
        );
      });
    });

    it('confirms and clears branch overrides', async () => {
      const confirmSpy = vi.spyOn(window, 'confirm').mockReturnValue(true);
      const fetchSpy = vi.spyOn(contracts, 'fetchWithCsrf').mockImplementation(async (input: RequestInfo | URL) => {
        const url = typeof input === 'string' ? input : input.toString();
        if (url.includes('/features/effective')) {
          return new Response(JSON.stringify(mockEffectiveFlags), { status: 200 });
        }
        if (url.includes('/features/override')) {
          return new Response(JSON.stringify(mockBranchFlags), { status: 200 });
        }
        if (url.includes('/tenant/features')) {
          return new Response(JSON.stringify(mockTenantFlags), { status: 200 });
        }
        if (url === '/api/v1/restaurant-config/branches') {
          return new Response(JSON.stringify([{ id: 'branch-1', name: 'Test Branch' }]), { status: 200 });
        }
        return new Response(JSON.stringify({ ok: true }), { status: 200 });
      });

      renderWithProviders(<FeatureFlagsView />);
      await waitFor(() => expect(screen.getByText(/^QR Men/)).toBeDefined());
      fireEvent.click(screen.getByTestId('tab-branch-override'));
      fireEvent.click(screen.getByTestId('clear-branch-overrides'));

      await waitFor(() =>
        expect(fetchSpy).toHaveBeenCalledWith(
          expect.stringContaining('/features/override'),
          expect.objectContaining({ method: 'DELETE' })
        )
      );
      expect(confirmSpy).toHaveBeenCalledOnce();
    });
  });
});
