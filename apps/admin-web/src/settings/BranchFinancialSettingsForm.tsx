import React, { useState, useEffect } from 'react';
import {
  Card,
  Button,
  Input,
  Select,
  Switch,
  FormField,
  FormError,
  Badge,
} from '@restaurant-order/ui';
import {
  EffectiveBranchSettingsContract,
  UpdateBranchSettingsRequest,
} from '@restaurant-order/contracts';

export interface BranchFinancialSettingsFormProps {
  settings: EffectiveBranchSettingsContract;
  isSubmitting: boolean;
  onSave: (payload: UpdateBranchSettingsRequest) => Promise<void>;
  onReset: () => void;
  onHasUnsavedChanges?: (hasChanges: boolean) => void;
}

export const BranchFinancialSettingsForm: React.FC<BranchFinancialSettingsFormProps> = ({
  settings,
  isSubmitting,
  onSave,
  onReset,
  onHasUnsavedChanges,
}) => {
  const [timezone, setTimezone] = useState(settings.timezone);
  const [currency, setCurrency] = useState(settings.currency);
  const [defaultLocale, setDefaultLocale] = useState(settings.defaultLocale);
  const [supportedLocalesInput, setSupportedLocalesInput] = useState(
    settings.supportedLocales.join(', ')
  );
  const [pricesIncludeTax, setPricesIncludeTax] = useState(settings.pricesIncludeTax);
  const [taxPercent, setTaxPercent] = useState(
    (settings.defaultTaxRateBps / 100).toFixed(2)
  );
  const [serviceChargeEnabled, setServiceChargeEnabled] = useState(
    settings.isServiceChargeEnabled
  );
  const [serviceChargePercent, setServiceChargePercent] = useState(
    (settings.serviceChargeRateBps / 100).toFixed(2)
  );
  const [orderTakingEnabled, setOrderTakingEnabled] = useState(
    settings.isOrderTakingEnabled
  );
  const [displayName, setDisplayName] = useState(settings.displayName || '');
  const [phoneNumber, setPhoneNumber] = useState(settings.phoneNumber || '');
  const [email, setEmail] = useState(settings.email || '');
  const [address, setAddress] = useState(settings.address || '');
  const [validationError, setValidationError] = useState<string | null>(null);

  // Synchronize when settings prop changes (e.g. after save or branch change)
  useEffect(() => {
    setTimezone(settings.timezone);
    setCurrency(settings.currency);
    setDefaultLocale(settings.defaultLocale);
    setSupportedLocalesInput(settings.supportedLocales.join(', '));
    setPricesIncludeTax(settings.pricesIncludeTax);
    setTaxPercent((settings.defaultTaxRateBps / 100).toFixed(2));
    setServiceChargeEnabled(settings.isServiceChargeEnabled);
    setServiceChargePercent((settings.serviceChargeRateBps / 100).toFixed(2));
    setOrderTakingEnabled(settings.isOrderTakingEnabled);
    setDisplayName(settings.displayName || '');
    setPhoneNumber(settings.phoneNumber || '');
    setEmail(settings.email || '');
    setAddress(settings.address || '');
    setValidationError(null);
  }, [settings]);

  // Check unsaved changes
  const hasChanges =
    timezone !== settings.timezone ||
    currency !== settings.currency ||
    defaultLocale !== settings.defaultLocale ||
    supportedLocalesInput !== settings.supportedLocales.join(', ') ||
    pricesIncludeTax !== settings.pricesIncludeTax ||
    taxPercent !== (settings.defaultTaxRateBps / 100).toFixed(2) ||
    serviceChargeEnabled !== settings.isServiceChargeEnabled ||
    serviceChargePercent !== (settings.serviceChargeRateBps / 100).toFixed(2) ||
    orderTakingEnabled !== settings.isOrderTakingEnabled ||
    displayName !== (settings.displayName || '') ||
    phoneNumber !== (settings.phoneNumber || '') ||
    email !== (settings.email || '') ||
    address !== (settings.address || '');

  useEffect(() => {
    onHasUnsavedChanges?.(hasChanges);
  }, [hasChanges, onHasUnsavedChanges]);

  const handleSubmit = async (e: React.FormEvent) => {
    e.preventDefault();
    setValidationError(null);

    const taxVal = parseFloat(taxPercent);
    if (isNaN(taxVal) || taxVal < 0 || taxVal > 100) {
      setValidationError('Vergi oranı %0 ile %100 arasında olmalıdır.');
      return;
    }

    const serviceVal = parseFloat(serviceChargePercent);
    if (serviceChargeEnabled) {
      if (isNaN(serviceVal) || serviceVal < 0 || serviceVal > 50) {
        setValidationError('Servis ücreti aktifken oran %0 ile %50 arasında olmalıdır.');
        return;
      }
    }

    const locales = supportedLocalesInput
      .split(',')
      .map((l) => l.trim())
      .filter(Boolean);

    if (locales.length === 0) {
      setValidationError('En az bir desteklenen dil (locale) girilmelidir.');
      return;
    }

    if (!locales.includes(defaultLocale)) {
      setValidationError(`Varsayılan dil (${defaultLocale}) desteklenen diller listesinde bulunmalıdır.`);
      return;
    }

    const taxBps = Math.round(taxVal * 100);
    const serviceBps = serviceChargeEnabled ? Math.round(serviceVal * 100) : 0;

    await onSave({
      timezone,
      currency,
      defaultLocale,
      supportedLocales: locales,
      pricesIncludeTax,
      defaultTaxRateBps: taxBps,
      isServiceChargeEnabled: serviceChargeEnabled,
      serviceChargeRateBps: serviceBps,
      isOrderTakingEnabled: orderTakingEnabled,
      displayName: displayName.trim() || null,
      phoneNumber: phoneNumber.trim() || null,
      email: email.trim() || null,
      address: address.trim() || null,
      concurrencyToken: settings.concurrencyToken,
    });
  };

  return (
    <form onSubmit={handleSubmit} noValidate data-testid="branch-financial-form">
      {hasChanges && (
        <div style={{ marginBottom: 'var(--ro-space-3)' }}>
          <Badge variant="warning">Kaydedilmemiş değişiklikler var</Badge>
        </div>
      )}

      {validationError && (
        <div style={{ marginBottom: 'var(--ro-space-4)' }}>
          <FormError id="financial-form-error">{validationError}</FormError>
        </div>
      )}

      {/* Operasyonel Finans & Vergi */}
      <div style={{ marginBottom: 'var(--ro-space-4)' }}>
        <Card padding="md">
          <h3 style={{ fontSize: 'var(--ro-font-size-md)', fontWeight: 600, marginTop: 0, marginBottom: 'var(--ro-space-3)' }}>
            Finansal ve Fiyatlandırma Ayarları
          </h3>
          <p style={{ color: 'var(--ro-color-text-muted)', fontSize: 'var(--ro-font-size-xs)', marginBottom: 'var(--ro-space-4)' }}>
            Vergi dahil gösterim ve servis ücreti kuralları sipariş fiyatlandırma motoru tarafından referans alınır.
          </p>

          <div style={{ display: 'grid', gridTemplateColumns: 'repeat(auto-fit, minmax(240px, 1fr))', gap: 'var(--ro-space-4)' }}>
            <FormField label="Para Birimi" id="currency">
              <Select
                id="currency"
                data-testid="input-currency"
                value={currency}
                onChange={(e) => setCurrency(e.target.value)}
              >
                <option value="TRY">Türk Lirası (TRY)</option>
                <option value="USD">Amerikan Doları (USD)</option>
                <option value="EUR">Euro (EUR)</option>
                <option value="GBP">İngiliz Sterlini (GBP)</option>
              </Select>
            </FormField>

            <FormField label="Varsayılan Vergi Oranı (%)" id="taxRate">
              <Input
                id="taxRate"
                data-testid="input-tax-rate"
                type="number"
                step="0.01"
                min="0"
                max="100"
                value={taxPercent}
                onChange={(e) => setTaxPercent(e.target.value)}
              />
            </FormField>
          </div>

          <div style={{ marginTop: 'var(--ro-space-4)', display: 'flex', flexDirection: 'column', gap: 'var(--ro-space-3)' }}>
            <div style={{ display: 'flex', alignItems: 'center', justifyContent: 'space-between', padding: 'var(--ro-space-2) 0' }}>
              <div>
                <span style={{ fontWeight: 500, fontSize: 'var(--ro-font-size-sm)' }}>Vergiler Fiyata Dahil</span>
                <p style={{ color: 'var(--ro-color-text-muted)', fontSize: 'var(--ro-font-size-xs)', margin: 0 }}>
                  Menüde görüntülenen fiyatların vergi dahil olarak hesaplanması
                </p>
              </div>
              <Switch
                checked={pricesIncludeTax}
                data-testid="switch-prices-include-tax"
                onCheckedChange={(checked) => setPricesIncludeTax(checked)}
                label="Vergi dahil fiyatlandırma"
              />
            </div>

            <div style={{ display: 'flex', alignItems: 'center', justifyContent: 'space-between', padding: 'var(--ro-space-2) 0', borderTop: '1px solid var(--ro-color-border)' }}>
              <div>
                <span style={{ fontWeight: 500, fontSize: 'var(--ro-font-size-sm)' }}>Servis Ücreti Uygula</span>
                <p style={{ color: 'var(--ro-color-text-muted)', fontSize: 'var(--ro-font-size-xs)', margin: 0 }}>
                  Sipariş toplamı üzerinden otomatik servis bedeli eklenmesi
                </p>
              </div>
              <Switch
                checked={serviceChargeEnabled}
                data-testid="switch-service-charge"
                onCheckedChange={(checked) => {
                  setServiceChargeEnabled(checked);
                  if (!checked) setServiceChargePercent('0.00');
                }}
                label="Servis ücreti aktif"
              />
            </div>

            {serviceChargeEnabled && (
              <div style={{ paddingLeft: 'var(--ro-space-4)', marginTop: 'var(--ro-space-2)' }}>
                <FormField label="Servis Ücreti Oranı (%)" id="serviceChargeRate">
                  <Input
                    id="serviceChargeRate"
                    data-testid="input-service-charge-rate"
                    type="number"
                    step="0.01"
                    min="0"
                    max="50"
                    value={serviceChargePercent}
                    onChange={(e) => setServiceChargePercent(e.target.value)}
                  />
                </FormField>
              </div>
            )}
          </div>
        </Card>
      </div>

      {/* Yerelleştirme & Operasyonel Durum */}
      <div style={{ marginBottom: 'var(--ro-space-4)' }}>
        <Card padding="md">
          <h3 style={{ fontSize: 'var(--ro-font-size-md)', fontWeight: 600, marginTop: 0, marginBottom: 'var(--ro-space-3)' }}>
            Yerelleştirme ve Sipariş Kabul
          </h3>

          <div style={{ display: 'grid', gridTemplateColumns: 'repeat(auto-fit, minmax(240px, 1fr))', gap: 'var(--ro-space-4)' }}>
            <FormField label="Şube Saat Dilimi (Timezone)" id="timezone">
              <Select
                id="timezone"
                data-testid="input-timezone"
                value={timezone}
                onChange={(e) => setTimezone(e.target.value)}
              >
                <option value="Europe/Istanbul">Europe/Istanbul (UTC+3)</option>
                <option value="Europe/London">Europe/London (UTC+0 / UTC+1)</option>
                <option value="Europe/Berlin">Europe/Berlin (UTC+1 / UTC+2)</option>
                <option value="America/New_York">America/New_York (UTC-5 / UTC-4)</option>
                <option value="Asia/Dubai">Asia/Dubai (UTC+4)</option>
              </Select>
            </FormField>

            <FormField label="Varsayılan Dil (Locale)" id="defaultLocale">
              <Select
                id="defaultLocale"
                data-testid="input-default-locale"
                value={defaultLocale}
                onChange={(e) => setDefaultLocale(e.target.value)}
              >
                <option value="tr-TR">Türkçe (tr-TR)</option>
                <option value="en-US">İngilizce (en-US)</option>
                <option value="en-GB">İngilizce Birleşik Krallık (en-GB)</option>
                <option value="de-DE">Almanca (de-DE)</option>
                <option value="fr-FR">Fransızca (fr-FR)</option>
              </Select>
            </FormField>
          </div>

          <div style={{ marginTop: 'var(--ro-space-3)' }}>
            <FormField label="Desteklenen Diller (virgülle ayırın)" id="supportedLocales">
              <Input
                id="supportedLocales"
                data-testid="input-supported-locales"
                value={supportedLocalesInput}
                onChange={(e) => setSupportedLocalesInput(e.target.value)}
                placeholder="tr-TR, en-US"
              />
            </FormField>
          </div>

          <div style={{ marginTop: 'var(--ro-space-4)', display: 'flex', alignItems: 'center', justifyContent: 'space-between', borderTop: '1px solid var(--ro-color-border)', paddingTop: 'var(--ro-space-3)' }}>
            <div>
              <span style={{ fontWeight: 500, fontSize: 'var(--ro-font-size-sm)' }}>Sipariş Kabulü Aktif</span>
              <p style={{ color: 'var(--ro-color-text-muted)', fontSize: 'var(--ro-font-size-xs)', margin: 0 }}>
                Kapatıldığında müşteriler QR menüden yeni sipariş veremez
              </p>
            </div>
            <Switch
              checked={orderTakingEnabled}
              data-testid="switch-order-taking"
              onCheckedChange={(checked) => setOrderTakingEnabled(checked)}
              label="Sipariş kabul açık"
            />
          </div>
        </Card>
      </div>

      {/* Şube İletişim & Görünen Ad */}
      <div style={{ marginBottom: 'var(--ro-space-4)' }}>
        <Card padding="md">
          <h3 style={{ fontSize: 'var(--ro-font-size-md)', fontWeight: 600, marginTop: 0, marginBottom: 'var(--ro-space-3)' }}>
            Şube Kimlik ve İletişim Bilgileri
          </h3>

          <div style={{ display: 'grid', gridTemplateColumns: 'repeat(auto-fit, minmax(240px, 1fr))', gap: 'var(--ro-space-4)' }}>
            <FormField label="Şube Görünen Adı" id="displayName">
              <Input
                id="displayName"
                data-testid="input-display-name"
                value={displayName}
                onChange={(e) => setDisplayName(e.target.value)}
                placeholder={settings.branchName}
              />
            </FormField>

            <FormField label="Telefon Numarası" id="phoneNumber">
              <Input
                id="phoneNumber"
                data-testid="input-phone-number"
                value={phoneNumber}
                onChange={(e) => setPhoneNumber(e.target.value)}
                placeholder="+90 212 555 0100"
              />
            </FormField>
          </div>

          <div style={{ display: 'grid', gridTemplateColumns: 'repeat(auto-fit, minmax(240px, 1fr))', gap: 'var(--ro-space-4)', marginTop: 'var(--ro-space-3)' }}>
            <FormField label="İletişim E-posta" id="email">
              <Input
                id="email"
                data-testid="input-email"
                type="email"
                value={email}
                onChange={(e) => setEmail(e.target.value)}
                placeholder="sube@restoran.com"
              />
            </FormField>

            <FormField label="Fiziksel Adres" id="address">
              <Input
                id="address"
                data-testid="input-address"
                value={address}
                onChange={(e) => setAddress(e.target.value)}
                placeholder="Bağdat Cad. No: 123 Kadıköy / İstanbul"
              />
            </FormField>
          </div>
        </Card>
      </div>

      {/* Form Butonları */}
      <div style={{ display: 'flex', gap: 'var(--ro-space-3)', justifyContent: 'flex-end', marginTop: 'var(--ro-space-4)' }}>
        <Button
          type="button"
          variant="outline"
          onClick={onReset}
          disabled={isSubmitting || !hasChanges}
        >
          Sıfırla
        </Button>
        <Button
          type="submit"
          variant="primary"
          loading={isSubmitting}
          disabled={isSubmitting}
          data-testid="btn-save-financial"
        >
          Finansal Ayarları Kaydet
        </Button>
      </div>
    </form>
  );
};
