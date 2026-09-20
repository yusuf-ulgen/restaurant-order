import React from 'react';
import { HeaderConfig } from './types';
import { IconButton } from '../components/IconButton';

export interface AppHeaderProps extends HeaderConfig {
  className?: string;
  style?: React.CSSProperties;
}

export const AppHeader: React.FC<AppHeaderProps> = ({
  title,
  subtitle,
  logo,
  logoUrl,
  actions,
  showMobileMenuToggle = false,
  onMobileMenuToggle,
  isMobileMenuOpen = false,
  className,
  style,
}) => {
  return (
    <header
      role="banner"
      data-testid="app-header"
      className={className}
      style={{
        display: 'flex',
        alignItems: 'center',
        justifyContent: 'space-between',
        padding: 'var(--ro-space-3) var(--ro-space-4)',
        backgroundColor: 'var(--ro-color-surface)',
        borderBottom: '1px solid var(--ro-color-border)',
        minHeight: '56px',
        boxSizing: 'border-box',
        width: '100%',
        maxWidth: '100vw',
        gap: 'var(--ro-space-3)',
        ...style,
      }}
    >
      <div
        style={{
          display: 'flex',
          alignItems: 'center',
          gap: 'var(--ro-space-3)',
          minWidth: 0,
          flex: 1,
        }}
      >
        {showMobileMenuToggle && (
          <IconButton
            data-testid="mobile-menu-toggle"
            aria-label={isMobileMenuOpen ? 'Menüyü Kapat' : 'Menüyü Aç'}
            aria-expanded={isMobileMenuOpen}
            variant="ghost"
            size="sm"
            onClick={onMobileMenuToggle}
            icon={
              <span aria-hidden="true" style={{ fontSize: '1.25rem', lineHeight: 1 }}>
                {isMobileMenuOpen ? '✕' : '☰'}
              </span>
            }
          />
        )}

        {logo ? (
          <div style={{ display: 'flex', alignItems: 'center', flexShrink: 0 }}>{logo}</div>
        ) : logoUrl ? (
          <img
            src={logoUrl}
            alt="Logo"
            style={{
              maxHeight: '32px',
              maxWidth: '32px',
              objectFit: 'contain',
              flexShrink: 0,
            }}
          />
        ) : null}

        <div style={{ minWidth: 0, display: 'flex', flexDirection: 'column' }}>
          <div
            data-testid="header-title"
            style={{
              fontSize: 'var(--ro-font-size-md)',
              fontWeight: 'var(--ro-font-weight-bold)',
              color: 'var(--ro-color-text-primary)',
              whiteSpace: 'nowrap',
              overflow: 'hidden',
              textOverflow: 'ellipsis',
            }}
          >
            {title}
          </div>
          {subtitle && (
            <div
              data-testid="header-subtitle"
              style={{
                fontSize: 'var(--ro-font-size-xs)',
                color: 'var(--ro-color-text-muted)',
                whiteSpace: 'nowrap',
                overflow: 'hidden',
                textOverflow: 'ellipsis',
              }}
            >
              {subtitle}
            </div>
          )}
        </div>
      </div>

      {actions && (
        <div
          data-testid="header-actions"
          style={{
            display: 'flex',
            alignItems: 'center',
            gap: 'var(--ro-space-2)',
            flexShrink: 0,
          }}
        >
          {actions}
        </div>
      )}
    </header>
  );
};
