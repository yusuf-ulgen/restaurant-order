import React from 'react';
import { Card, FormField, Input } from '@restaurant-order/ui';

export interface ThemeBrandIdentity {
  displayName: string;
  logoUrl: string;
  faviconUrl: string;
}

export interface ThemeBrandIdentityFieldsProps {
  values: ThemeBrandIdentity;
  onChange: (field: keyof ThemeBrandIdentity, value: string) => void;
}

export const ThemeBrandIdentityFields: React.FC<ThemeBrandIdentityFieldsProps> = ({
  values,
  onChange,
}) => {
  return (
    <Card padding="md">
      <h3 style={{ fontSize: 'var(--ro-font-size-md)', fontWeight: 'bold', marginBottom: 'var(--ro-space-3)' }}>
        Temel Marka & Logo Bilgileri
      </h3>
      <div style={{ display: 'grid', gridTemplateColumns: 'repeat(auto-fit, minmax(240px, 1fr))', gap: 'var(--ro-space-4)' }}>
        <FormField id="displayName" label="Marka Görünen Adı" required>
          <Input
            value={values.displayName}
            onChange={(e) => onChange('displayName', e.target.value)}
            placeholder="Restoran Marka Adı"
            required
          />
        </FormField>

        <FormField id="logoUrl" label="Logo URL (HTTPS veya /...)">
          <Input
            value={values.logoUrl}
            onChange={(e) => onChange('logoUrl', e.target.value)}
            placeholder="https://cdn.example.com/logo.png"
          />
        </FormField>

        <FormField id="faviconUrl" label="Favicon URL">
          <Input
            value={values.faviconUrl}
            onChange={(e) => onChange('faviconUrl', e.target.value)}
            placeholder="/favicon.ico"
          />
        </FormField>
      </div>
    </Card>
  );
};
