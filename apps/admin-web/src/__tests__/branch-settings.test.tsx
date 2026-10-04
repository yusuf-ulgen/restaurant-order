import { describe, it, expect, vi, beforeEach } from 'vitest';
import { render, screen, fireEvent, waitFor } from '@testing-library/react';
import React from 'react';
import { AuthProvider } from '../auth/AuthContext';
import { AdminConfigProvider } from '../config/AdminConfigContext';
import { BranchSettingsView } from '../settings/BranchSettingsView';
import { BranchFinancialSettingsForm } from '../settings/BranchFinancialSettingsForm';
import { BranchOperatingHoursForm } from '../settings/BranchOperatingHoursForm';
import {
  EffectiveBranchSettingsContract,
  BranchOperatingHoursContract,
  UserPrincipalDto,
} from '@restaurant-order/contracts';

const mockAdminUser: UserPrincipalDto = {
  userId: 'user-admin-1',
  email: 'admin@restoran.com',
  role: 'RestaurantAdmin',
  tenantId: 'tenant-test-1',
  securityVersion: 1,
};

const mockSettings: EffectiveBranchSettingsContract = {
  branchId: 'branch-1',
  branchName: 'Kadıköy Şubesi',
  timezone: 'Europe/Istanbul',
  currency: 'TRY',
  defaultLocale: 'tr-TR',
  supportedLocales: ['tr-TR', 'en-US'],
  pricesIncludeTax: true,
  defaultTaxRateBps: 1000,
  isServiceChargeEnabled: false,
  serviceChargeRateBps: 0,
  isOrderTakingEnabled: true,
  displayName: 'Kadıköy Merkez',
  phoneNumber: '+90 216 555 0101',
  email: 'kadikoy@lezzet.com',
  address: 'Moda Cad. No: 12',
  hasCustomSettings: true,
  concurrencyToken: 'token-abc-123',
};

const mockOperatingHours: BranchOperatingHoursContract = {
  branchId: 'branch-1',
  days: [
    { dayOfWeek: 1, isClosed: false, slots: [{ openTime: '09:00', closeTime: '22:00' }] },
    { dayOfWeek: 2, isClosed: false, slots: [{ openTime: '09:00', closeTime: '22:00' }] },
    { dayOfWeek: 3, isClosed: false, slots: [{ openTime: '09:00', closeTime: '22:00' }] },
    { dayOfWeek: 4, isClosed: false, slots: [{ openTime: '09:00', closeTime: '22:00' }] },
    { dayOfWeek: 5, isClosed: false, slots: [{ openTime: '09:00', closeTime: '02:00', isOvernight: true }] },
    { dayOfWeek: 6, isClosed: false, slots: [{ openTime: '10:00', closeTime: '23:00' }] },
    { dayOfWeek: 0, isClosed: true, slots: [] },
  ],
  concurrencyToken: 'token-hours-123',
  updatedAtUtc: '2026-10-02T10:00:00Z',
};

function renderWithProviders(ui: React.ReactElement) {
  return render(
    <AuthProvider initialUser={mockAdminUser}>
      <AdminConfigProvider>
        {ui}
      </AdminConfigProvider>
    </AuthProvider>
  );
}

describe('Branch Operational & Financial Settings (Phase 4.4)', () => {
  beforeEach(() => {
    vi.restoreAllMocks();
  });

  describe('BranchFinancialSettingsForm Component', () => {
    it('renders fields with initial values and calculates percentage from bps', () => {
      render(
        <BranchFinancialSettingsForm
          settings={mockSettings}
          isSubmitting={false}
          onSave={vi.fn()}
          onReset={vi.fn()}
        />
      );

      const currencySelect = screen.getByTestId('input-currency') as HTMLSelectElement;
      expect(currencySelect.value).toBe('TRY');

      const taxInput = screen.getByTestId('input-tax-rate') as HTMLInputElement;
      expect(taxInput.value).toBe('10.00');

      const taxSwitch = screen.getByTestId('switch-prices-include-tax');
      expect(taxSwitch.getAttribute('aria-checked')).toBe('true');

      const serviceSwitch = screen.getByTestId('switch-service-charge');
      expect(serviceSwitch.getAttribute('aria-checked')).toBe('false');

      const orderSwitch = screen.getByTestId('switch-order-taking');
      expect(orderSwitch.getAttribute('aria-checked')).toBe('true');

      const tzSelect = screen.getByTestId('input-timezone') as HTMLSelectElement;
      expect(tzSelect.value).toBe('Europe/Istanbul');

      const localeSelect = screen.getByTestId('input-default-locale') as HTMLSelectElement;
      expect(localeSelect.value).toBe('tr-TR');

      const nameInput = screen.getByTestId('input-display-name') as HTMLInputElement;
      expect(nameInput.value).toBe('Kadıköy Merkez');
    });

    it('shows unsaved changes badge and toggles service charge input', () => {
      const onHasChanges = vi.fn();
      render(
        <BranchFinancialSettingsForm
          settings={mockSettings}
          isSubmitting={false}
          onSave={vi.fn()}
          onReset={vi.fn()}
          onHasUnsavedChanges={onHasChanges}
        />
      );

      const serviceSwitch = screen.getByTestId('switch-service-charge');
      fireEvent.click(serviceSwitch);

      expect(screen.getByText('Kaydedilmemiş değişiklikler var')).toBeDefined();
      expect(screen.getByTestId('input-service-charge-rate')).toBeDefined();
    });

    it('validates tax rate and locale requirements before saving', async () => {
      const onSave = vi.fn();
      render(
        <BranchFinancialSettingsForm
          settings={mockSettings}
          isSubmitting={false}
          onSave={onSave}
          onReset={vi.fn()}
        />
      );

      // Set tax rate to invalid > 100
      fireEvent.change(screen.getByTestId('input-tax-rate'), { target: { value: '150' } });
      fireEvent.click(screen.getByTestId('btn-save-financial'));

      await waitFor(() => {
        expect(screen.getByText('Vergi oranı %0 ile %100 arasında olmalıdır.')).toBeDefined();
      });
      expect(onSave).not.toHaveBeenCalled();

      // Fix tax rate, but break supported locales (remove defaultLocale)
      fireEvent.change(screen.getByTestId('input-tax-rate'), { target: { value: '18' } });
      fireEvent.change(screen.getByTestId('input-supported-locales'), { target: { value: 'en-US, fr-FR' } });
      fireEvent.click(screen.getByTestId('btn-save-financial'));

      await waitFor(() => {
        expect(screen.getByText(/Varsayılan dil \(tr-TR\) desteklenen diller listesinde bulunmalıdır/)).toBeDefined();
      });
      expect(onSave).not.toHaveBeenCalled();
    });

    it('converts percentage to bps and submits payload successfully', async () => {
      const onSave = vi.fn();
      render(
        <BranchFinancialSettingsForm
          settings={mockSettings}
          isSubmitting={false}
          onSave={onSave}
          onReset={vi.fn()}
        />
      );

      fireEvent.change(screen.getByTestId('input-tax-rate'), { target: { value: '20' } });
      fireEvent.click(screen.getByTestId('btn-save-financial'));

      await waitFor(() => {
        expect(onSave).toHaveBeenCalledWith(
          expect.objectContaining({
            defaultTaxRateBps: 2000,
            currency: 'TRY',
            concurrencyToken: 'token-abc-123',
          })
        );
      });
    });
  });

  describe('BranchOperatingHoursForm Component', () => {
    it('renders 7 days of schedule and displays overnight badge', () => {
      render(
        <BranchOperatingHoursForm
          operatingHours={mockOperatingHours}
          isSubmitting={false}
          onSave={vi.fn()}
          onReset={vi.fn()}
        />
      );

      // Monday to Sunday rendered
      expect(screen.getByText('Pazartesi')).toBeDefined();
      expect(screen.getByText('Cuma')).toBeDefined();
      expect(screen.getByText('Pazar')).toBeDefined();

      // Friday has overnight slot
      expect(screen.getByTestId('overnight-badge-5-0').textContent).toContain('Gece Yarısını Geçer');
    });

    it('allows toggling day to closed and back to open', () => {
      render(
        <BranchOperatingHoursForm
          operatingHours={mockOperatingHours}
          isSubmitting={false}
          onSave={vi.fn()}
          onReset={vi.fn()}
        />
      );

      const mondaySwitch = screen.getByTestId('switch-open-1');
      expect(mondaySwitch.getAttribute('aria-checked')).toBe('true');

      // Toggle to closed
      fireEvent.click(mondaySwitch);
      expect(screen.getByTestId('day-row-1').textContent).toContain('Kapalı');

      // Toggle back to open
      fireEvent.click(mondaySwitch);
      expect(screen.getByTestId('day-row-1').textContent).toContain('Açık');
    });

    it('allows adding and removing time intervals', () => {
      render(
        <BranchOperatingHoursForm
          operatingHours={mockOperatingHours}
          isSubmitting={false}
          onSave={vi.fn()}
          onReset={vi.fn()}
        />
      );

      // Add slot to Monday
      const addBtn = screen.getByTestId('btn-add-slot-1');
      fireEvent.click(addBtn);

      expect(screen.getByTestId('slot-1-1')).toBeDefined();

      fireEvent.change(screen.getByTestId('input-open-1-1'), { target: { value: '23:00' } });
      fireEvent.change(screen.getByTestId('input-close-1-1'), { target: { value: '22:00' } });
      expect(screen.getByTestId('overnight-badge-1-1')).toBeDefined();

      // Remove the second slot
      const removeBtn = screen.getByTestId('btn-remove-slot-1-1');
      fireEvent.click(removeBtn);

      expect(screen.queryByTestId('slot-1-1')).toBeNull();
    });

    it('submits valid schedule payload', async () => {
      const onSave = vi.fn();
      render(
        <BranchOperatingHoursForm
          operatingHours={mockOperatingHours}
          isSubmitting={false}
          onSave={onSave}
          onReset={vi.fn()}
        />
      );

      fireEvent.click(screen.getByTestId('btn-save-hours'));

      await waitFor(() => {
        expect(onSave).toHaveBeenCalledWith(
          expect.objectContaining({
            concurrencyToken: 'token-hours-123',
            days: expect.any(Array),
          })
        );
      });
    });
  });

  describe('BranchSettingsView Container & API Integration', () => {
    beforeEach(() => {
      globalThis.fetch = vi.fn().mockImplementation((url: string) => {
        if (url.includes('/api/v1/restaurant-config/branches') && url.endsWith('/settings')) {
          return Promise.resolve(
            new Response(JSON.stringify(mockSettings), {
              status: 200,
              headers: { 'Content-Type': 'application/json', ETag: '"token-abc-123"' },
            })
          );
        }
        if (url.includes('/api/v1/restaurant-config/branches') && url.endsWith('/operating-hours')) {
          return Promise.resolve(
            new Response(JSON.stringify(mockOperatingHours), {
              status: 200,
              headers: { 'Content-Type': 'application/json', ETag: '"token-hours-123"' },
            })
          );
        }
        if (url === '/api/v1/restaurant-config/branches') {
          return Promise.resolve(
            new Response(JSON.stringify([{ id: 'branch-1', name: 'Kadıköy Şubesi' }]), {
              status: 200,
              headers: { 'Content-Type': 'application/json' },
            })
          );
        }
        return Promise.resolve(new Response(JSON.stringify({}), { status: 200 }));
      });
    });

    it('loads settings and allows tab switching between financial and operating hours', async () => {
      renderWithProviders(<BranchSettingsView />);

      const form = await screen.findByTestId('branch-financial-form');
      expect(form).toBeDefined();

      // Switch to operating hours tab
      fireEvent.click(screen.getByTestId('tab-hours'));
      const hoursForm = await screen.findByTestId('branch-operating-hours-form');
      expect(hoursForm).toBeDefined();

      // Switch back to financial tab
      fireEvent.click(screen.getByTestId('tab-financial'));
      const finForm = await screen.findByTestId('branch-financial-form');
      expect(finForm).toBeDefined();
    });

    it('saves financial and operating-hour settings and invokes the completion callback', async () => {
      const onSaved = vi.fn();
      renderWithProviders(<BranchSettingsView onSaved={onSaved} />);

      await screen.findByTestId('branch-financial-form');
      fireEvent.click(screen.getByTestId('btn-save-financial'));
      await waitFor(() => {
        expect(vi.mocked(globalThis.fetch)).toHaveBeenCalledWith(
          expect.stringContaining('/settings'),
          expect.objectContaining({ method: 'PUT' })
        );
      });
      await waitFor(() => expect(onSaved).toHaveBeenCalledTimes(1));

      fireEvent.click(screen.getByTestId('tab-hours'));
      await screen.findByTestId('branch-operating-hours-form');
      fireEvent.click(screen.getByTestId('btn-save-hours'));
      await waitFor(() => expect(onSaved).toHaveBeenCalledTimes(2));
    });

    it('handles 409 Concurrency Conflict and allows reloading data', async () => {
      renderWithProviders(<BranchSettingsView />);

      await screen.findByTestId('branch-financial-form');

      // Mock 409 conflict on PUT
      vi.mocked(globalThis.fetch).mockImplementationOnce((_url: RequestInfo | URL, init?: RequestInit) => {
        if (init?.method === 'PUT') {
          return Promise.resolve(
            new Response(JSON.stringify({ title: 'Conflict', detail: 'Stale token' }), {
              status: 409,
              headers: { 'Content-Type': 'application/json' },
            })
          );
        }
        return Promise.resolve(new Response(JSON.stringify({}), { status: 200 }));
      });

      fireEvent.change(screen.getByTestId('input-tax-rate'), { target: { value: '15' } });
      fireEvent.click(screen.getByTestId('btn-save-financial'));

      const conflictMsg = await screen.findByTestId('conflict-message');
      expect(conflictMsg).toBeDefined();
      expect(screen.getByTestId('btn-reload-conflict')).toBeDefined();

      // Click reload
      fireEvent.click(screen.getByTestId('btn-reload-conflict'));
      await waitFor(() => {
        expect(screen.queryByTestId('conflict-message')).toBeNull();
      });
    });

    it('opens mobile BottomSheet summary', async () => {
      renderWithProviders(<BranchSettingsView />);

      const openBtn = await screen.findByTestId('btn-open-summary');
      expect(openBtn).toBeDefined();
      fireEvent.click(openBtn);

      const summary = await screen.findByTestId('mobile-summary-sheet');
      expect(summary).toBeDefined();
      expect(summary.textContent).toContain('Europe/Istanbul');
      expect(summary.textContent).toContain('TRY');
      fireEvent.click(screen.getAllByRole('button', { name: 'Kapat' })[1]!);
      await waitFor(() => expect(screen.queryByTestId('mobile-summary-sheet')).toBeNull());
    });
  });
});
