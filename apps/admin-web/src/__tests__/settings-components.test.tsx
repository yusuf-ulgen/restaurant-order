import { describe, it, expect, vi } from 'vitest';
import { render, screen, fireEvent, waitFor } from '@testing-library/react';
import { ThemeColorFields, ThemeColors } from '../settings/ThemeColorFields';
import { ThemeBrandIdentityFields, ThemeBrandIdentity } from '../settings/ThemeBrandIdentityFields';
import { ThemeTextHeaderFooterFields, ThemeTextHeaderFooter } from '../settings/ThemeTextHeaderFooterFields';
import { NavigationConfigTable } from '../settings/NavigationConfigTable';
import { BranchFinancialSettingsForm } from '../settings/BranchFinancialSettingsForm';
import { EffectiveBranchSettingsContract } from '@restaurant-order/contracts';

describe('Settings Subcomponents Unit & Interactive Tests', () => {
  it('ThemeColorFields triggers onChange for all color inputs', () => {
    const initialColors: ThemeColors = {
      primaryColor: '#111827',
      primaryHoverColor: '#1f2937',
      secondaryColor: '#4b5563',
      accentColor: '#2563eb',
      surfaceColor: '#ffffff',
      backgroundColor: '#f9fafb',
    };
    const handleChange = vi.fn();
    render(<ThemeColorFields colors={initialColors} onChange={handleChange} />);

    const inputs = screen.getAllByRole('textbox');
    fireEvent.change(inputs[0]!, { target: { value: '#000000' } });
    expect(handleChange).toHaveBeenCalledWith('primaryColor', '#000000');

    fireEvent.change(inputs[1]!, { target: { value: '#111111' } });
    expect(handleChange).toHaveBeenCalledWith('primaryHoverColor', '#111111');

    fireEvent.change(inputs[2]!, { target: { value: '#222222' } });
    expect(handleChange).toHaveBeenCalledWith('secondaryColor', '#222222');

    fireEvent.change(inputs[3]!, { target: { value: '#333333' } });
    expect(handleChange).toHaveBeenCalledWith('accentColor', '#333333');

    fireEvent.change(inputs[4]!, { target: { value: '#444444' } });
    expect(handleChange).toHaveBeenCalledWith('surfaceColor', '#444444');

    fireEvent.change(inputs[5]!, { target: { value: '#555555' } });
    expect(handleChange).toHaveBeenCalledWith('backgroundColor', '#555555');
  });

  it('ThemeBrandIdentityFields triggers onChange for identity fields', () => {
    const initialIdentity: ThemeBrandIdentity = {
      displayName: 'Restoran',
      logoUrl: '',
      faviconUrl: '',
    };
    const handleChange = vi.fn();
    render(<ThemeBrandIdentityFields values={initialIdentity} onChange={handleChange} />);

    const inputs = screen.getAllByRole('textbox');
    fireEvent.change(inputs[0]!, { target: { value: 'Yeni Restoran AdÄ±' } });
    expect(handleChange).toHaveBeenCalledWith('displayName', 'Yeni Restoran AdÄ±');

    fireEvent.change(inputs[1]!, { target: { value: 'https://img.png' } });
    expect(handleChange).toHaveBeenCalledWith('logoUrl', 'https://img.png');

    fireEvent.change(inputs[2]!, { target: { value: '/fav.ico' } });
    expect(handleChange).toHaveBeenCalledWith('faviconUrl', '/fav.ico');
  });

  it('ThemeTextHeaderFooterFields triggers onChange for text fields', () => {
    const initialTexts: ThemeTextHeaderFooter = {
      shellTitle: 'BaÅŸlÄ±k',
      shellSubtitle: 'Alt BaÅŸlÄ±k',
      footerText: 'Telif',
    };
    const handleChange = vi.fn();
    render(<ThemeTextHeaderFooterFields values={initialTexts} onChange={handleChange} />);

    const inputs = screen.getAllByRole('textbox');
    fireEvent.change(inputs[0]!, { target: { value: 'Yeni BaÅŸlÄ±k' } });
    expect(handleChange).toHaveBeenCalledWith('shellTitle', 'Yeni BaÅŸlÄ±k');

    fireEvent.change(inputs[1]!, { target: { value: 'Yeni Alt BaÅŸlÄ±k' } });
    expect(handleChange).toHaveBeenCalledWith('shellSubtitle', 'Yeni Alt BaÅŸlÄ±k');

    fireEvent.change(inputs[2]!, { target: { value: 'Yeni Footer' } });
    expect(handleChange).toHaveBeenCalledWith('footerText', 'Yeni Footer');
  });

  it('NavigationConfigTable triggers onChange for checkbox, number and text input', () => {
    const handleChange = vi.fn();
    render(<NavigationConfigTable navigationOverrides={[]} onChange={handleChange} />);

    const checkboxes = screen.getAllByRole('checkbox');
    fireEvent.click(checkboxes[0]!);
    expect(handleChange).toHaveBeenCalledWith(expect.any(String), { isVisible: false });

    const numberInputs = screen.getAllByRole('spinbutton');
    fireEvent.change(numberInputs[0]!, { target: { value: '5' } });
    expect(handleChange).toHaveBeenCalledWith(expect.any(String), { order: 5 });

    const textInputs = screen.getAllByRole('textbox');
    fireEvent.change(textInputs[0]!, { target: { value: 'Ã–zel MenÃ¼' } });
    expect(handleChange).toHaveBeenCalledWith(expect.any(String), { labelOverride: 'Ã–zel MenÃ¼' });
  });

  it('BranchFinancialSettingsForm validates invalid tax and service charge', async () => {
    const mockSettings: EffectiveBranchSettingsContract = {
      branchId: 'branch-1',
      branchName: 'Kadikoy Branch',
      timezone: 'Europe/Istanbul',
      currency: 'TRY',
      defaultLocale: 'tr-TR',
      supportedLocales: ['tr-TR', 'en-US'],
      pricesIncludeTax: true,
      defaultTaxRateBps: 1000,
      isServiceChargeEnabled: true,
      serviceChargeRateBps: 500,
      isOrderTakingEnabled: true,
      hasCustomSettings: true,
      concurrencyToken: 'token-settings-1',
    };
    const handleSave = vi.fn();
    render(
      <BranchFinancialSettingsForm
        settings={mockSettings}
        isSubmitting={false}
        onSave={handleSave}
        onReset={vi.fn()}
      />
    );

    // Test invalid tax percent
    const taxInput = screen.getByTestId('input-tax-rate');
    fireEvent.change(taxInput, { target: { value: '150' } });
    const submitBtn = screen.getByTestId('btn-save-financial');
    fireEvent.click(submitBtn);

    expect(await screen.findByText(/Vergi oran/)).toBeDefined();
    expect(handleSave).not.toHaveBeenCalled();

    // Test invalid service charge percent
    fireEvent.change(taxInput, { target: { value: '10' } });
    const serviceInput = screen.getByTestId('input-service-charge-rate');
    fireEvent.change(serviceInput, { target: { value: '75' } });
    fireEvent.click(submitBtn);

    expect(await screen.findByText(/Servis ücreti aktifken oran/)).toBeDefined();

    // Test missing default locale in supported locales
    fireEvent.change(serviceInput, { target: { value: '5' } });
    const localesInput = screen.getByTestId('input-supported-locales');
    fireEvent.change(localesInput, { target: { value: 'de-DE, fr-FR' } });
    fireEvent.click(submitBtn);

    expect(await screen.findByRole('alert')).toBeDefined();

    // Test reset button
    const resetBtn = screen.getAllByRole('button')[0]!;
    fireEvent.click(resetBtn);
    expect((taxInput as HTMLInputElement).value).toBe('10');
  });

  it('submits valid financial settings as basis points', async () => {
    const settings: EffectiveBranchSettingsContract = {
      branchId: 'branch-1',
      branchName: 'Kadikoy Branch',
      timezone: 'Europe/Istanbul',
      currency: 'TRY',
      defaultLocale: 'tr-TR',
      supportedLocales: ['tr-TR', 'en-US'],
      pricesIncludeTax: true,
      defaultTaxRateBps: 1000,
      isServiceChargeEnabled: false,
      serviceChargeRateBps: 0,
      isOrderTakingEnabled: true,
      hasCustomSettings: true,
      concurrencyToken: 'token-settings-1',
    };
    const onSave = vi.fn().mockResolvedValue(undefined);
    const onHasUnsavedChanges = vi.fn();

    render(
      <BranchFinancialSettingsForm
        settings={settings}
        isSubmitting={false}
        onSave={onSave}
        onReset={vi.fn()}
        onHasUnsavedChanges={onHasUnsavedChanges}
      />
    );
    fireEvent.change(screen.getByTestId('input-timezone'), { target: { value: 'Europe/London' } });
    fireEvent.change(screen.getByTestId('input-currency'), { target: { value: 'GBP' } });
    fireEvent.change(screen.getByTestId('input-default-locale'), { target: { value: 'en-US' } });
    fireEvent.change(screen.getByTestId('input-supported-locales'), { target: { value: 'en-US, tr-TR' } });
    fireEvent.click(screen.getByTestId('switch-prices-include-tax'));
    fireEvent.click(screen.getByTestId('switch-service-charge'));
    fireEvent.change(screen.getByTestId('input-service-charge-rate'), { target: { value: '5' } });
    fireEvent.click(screen.getByTestId('switch-order-taking'));
    fireEvent.change(screen.getByTestId('input-display-name'), { target: { value: '  Kadikoy  ' } });
    fireEvent.change(screen.getByTestId('input-phone-number'), { target: { value: '5550100' } });
    fireEvent.change(screen.getByTestId('input-email'), { target: { value: 'branch@example.test' } });
    fireEvent.change(screen.getByTestId('input-address'), { target: { value: 'Main Street' } });
    fireEvent.click(screen.getByTestId('btn-save-financial'));

    await waitFor(() =>
      expect(onSave).toHaveBeenCalledWith(
        expect.objectContaining({
          defaultTaxRateBps: 1000,
          currency: 'GBP',
          timezone: 'Europe/London',
          defaultLocale: 'en-US',
          isServiceChargeEnabled: true,
          serviceChargeRateBps: 500,
          supportedLocales: ['en-US', 'tr-TR'],
          displayName: 'Kadikoy',
          phoneNumber: '5550100',
          email: 'branch@example.test',
          address: 'Main Street',
          concurrencyToken: 'token-settings-1',
        })
      )
    );
  });
});
