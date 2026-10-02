import React from 'react';
import { Card, FormField, Input } from '@restaurant-order/ui';

export interface ThemeColors {
  primaryColor: string;
  primaryHoverColor: string;
  secondaryColor: string;
  accentColor: string;
  surfaceColor: string;
  backgroundColor: string;
}

export interface ThemeColorFieldsProps {
  colors: ThemeColors;
  onChange: (field: keyof ThemeColors, value: string) => void;
}

export const ThemeColorFields: React.FC<ThemeColorFieldsProps> = ({ colors, onChange }) => {
  return (
    <Card padding="md">
      <h3 style={{ fontSize: 'var(--ro-font-size-md)', fontWeight: 'bold', marginBottom: 'var(--ro-space-3)' }}>
        Tasarım Renkleri (Hex Değerleri)
      </h3>
      <div style={{ display: 'grid', gridTemplateColumns: 'repeat(auto-fit, minmax(180px, 1fr))', gap: 'var(--ro-space-4)' }}>
        <FormField id="primaryColor" label="Primary Renk" required>
          <Input
            value={colors.primaryColor}
            onChange={(e) => onChange('primaryColor', e.target.value)}
            placeholder="#111827"
            required
          />
        </FormField>

        <FormField id="primaryHoverColor" label="Primary Hover" required>
          <Input
            value={colors.primaryHoverColor}
            onChange={(e) => onChange('primaryHoverColor', e.target.value)}
            placeholder="#1f2937"
            required
          />
        </FormField>

        <FormField id="secondaryColor" label="Secondary Renk" required>
          <Input
            value={colors.secondaryColor}
            onChange={(e) => onChange('secondaryColor', e.target.value)}
            placeholder="#4b5563"
            required
          />
        </FormField>

        <FormField id="accentColor" label="Accent Renk" required>
          <Input
            value={colors.accentColor}
            onChange={(e) => onChange('accentColor', e.target.value)}
            placeholder="#2563eb"
            required
          />
        </FormField>

        <FormField id="surfaceColor" label="Surface (Yüzey)" required>
          <Input
            value={colors.surfaceColor}
            onChange={(e) => onChange('surfaceColor', e.target.value)}
            placeholder="#ffffff"
            required
          />
        </FormField>

        <FormField id="backgroundColor" label="Background (Arka Plan)" required>
          <Input
            value={colors.backgroundColor}
            onChange={(e) => onChange('backgroundColor', e.target.value)}
            placeholder="#f9fafb"
            required
          />
        </FormField>
      </div>
    </Card>
  );
};
