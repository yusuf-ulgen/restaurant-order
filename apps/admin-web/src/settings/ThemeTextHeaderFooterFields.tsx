import React from 'react';
import { Card, FormField, Input } from '@restaurant-order/ui';

export interface ThemeTextHeaderFooter {
  shellTitle: string;
  shellSubtitle: string;
  footerText: string;
}

export interface ThemeTextHeaderFooterFieldsProps {
  values: ThemeTextHeaderFooter;
  onChange: (field: keyof ThemeTextHeaderFooter, value: string) => void;
}

export const ThemeTextHeaderFooterFields: React.FC<ThemeTextHeaderFooterFieldsProps> = ({
  values,
  onChange,
}) => {
  return (
    <Card padding="md">
      <h3 style={{ fontSize: 'var(--ro-font-size-md)', fontWeight: 'bold', marginBottom: 'var(--ro-space-3)' }}>
        Başlık ve Alt Bilgi Metinleri
      </h3>
      <div style={{ display: 'grid', gridTemplateColumns: 'repeat(auto-fit, minmax(240px, 1fr))', gap: 'var(--ro-space-4)' }}>
        <FormField id="shellTitle" label="Varsayılan Shell Başlığı">
          <Input
            value={values.shellTitle}
            onChange={(e) => onChange('shellTitle', e.target.value)}
            placeholder="Restoran Yönetim Paneli"
          />
        </FormField>

        <FormField id="shellSubtitle" label="Varsayılan Shell Alt Başlığı">
          <Input
            value={values.shellSubtitle}
            onChange={(e) => onChange('shellSubtitle', e.target.value)}
            placeholder="Şube ve Menü Yönetimi"
          />
        </FormField>

        <FormField id="footerText" label="Footer İşletme Metni">
          <Input
            value={values.footerText}
            onChange={(e) => onChange('footerText', e.target.value)}
            placeholder="Restoran Sipariş Sistemi"
          />
        </FormField>
      </div>
    </Card>
  );
};
