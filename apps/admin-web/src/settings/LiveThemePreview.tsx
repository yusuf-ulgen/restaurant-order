import React from 'react';
import { Badge } from '@restaurant-order/ui';

export interface LiveThemePreviewProps {
  backgroundColor: string;
  surfaceColor: string;
  primaryColor: string;
  accentColor: string;
  logoUrl?: string;
  shellTitle?: string;
  displayName?: string;
  shellSubtitle?: string;
  footerBranchInfo?: string;
  footerText?: string;
}

export const LiveThemePreview: React.FC<LiveThemePreviewProps> = ({
  backgroundColor,
  surfaceColor,
  primaryColor,
  accentColor,
  logoUrl,
  shellTitle,
  displayName,
  shellSubtitle,
  footerBranchInfo,
  footerText,
}) => {
  return (
    <div
      data-testid="branding-preview"
      style={{
        border: '1px solid var(--ro-color-border)',
        borderRadius: 'var(--ro-radius-md)',
        overflow: 'hidden',
        backgroundColor,
      }}
    >
      <div
        style={{
          padding: 'var(--ro-space-3) var(--ro-space-4)',
          backgroundColor: surfaceColor,
          borderBottom: '1px solid var(--ro-color-border)',
          display: 'flex',
          alignItems: 'center',
          justifyContent: 'space-between',
        }}
      >
        <div style={{ display: 'flex', alignItems: 'center', gap: 'var(--ro-space-3)' }}>
          {logoUrl ? (
            <img
              src={logoUrl}
              alt="Logo"
              style={{ maxHeight: '28px', maxWidth: '28px', objectFit: 'contain' }}
            />
          ) : (
            <div
              style={{
                width: '28px',
                height: '28px',
                borderRadius: 'var(--ro-radius-sm)',
                backgroundColor: primaryColor,
                color: '#fff',
                display: 'flex',
                alignItems: 'center',
                justifyContent: 'center',
                fontSize: '0.75rem',
                fontWeight: 'bold',
              }}
            >
              R
            </div>
          )}
          <div>
            <div style={{ fontWeight: 'bold', fontSize: 'var(--ro-font-size-sm)', color: '#111827' }}>
              {shellTitle || displayName || 'Restoran Yönetim Paneli'}
            </div>
            {shellSubtitle && (
              <div style={{ fontSize: 'var(--ro-font-size-xs)', color: '#6b7280' }}>
                {shellSubtitle}
              </div>
            )}
          </div>
        </div>
        <Badge variant="primary">Önizleme</Badge>
      </div>

      <div style={{ padding: 'var(--ro-space-4)', display: 'flex', gap: 'var(--ro-space-3)', flexWrap: 'wrap' }}>
        <button
          type="button"
          style={{
            backgroundColor: primaryColor,
            color: '#ffffff',
            padding: '8px 16px',
            borderRadius: '6px',
            border: 'none',
            fontWeight: 500,
            cursor: 'pointer',
          }}
        >
          Primary Buton
        </button>
        <button
          type="button"
          style={{
            backgroundColor: accentColor,
            color: '#ffffff',
            padding: '8px 16px',
            borderRadius: '6px',
            border: 'none',
            fontWeight: 500,
            cursor: 'pointer',
          }}
        >
          Accent Vurgu
        </button>
      </div>

      <div
        style={{
          padding: 'var(--ro-space-2) var(--ro-space-4)',
          backgroundColor: surfaceColor,
          borderTop: '1px solid var(--ro-color-border)',
          fontSize: 'var(--ro-font-size-xs)',
          color: '#6b7280',
        }}
      >
        {footerBranchInfo || footerText || '© 2026 Restaurant Order Platform'}
      </div>
    </div>
  );
};
