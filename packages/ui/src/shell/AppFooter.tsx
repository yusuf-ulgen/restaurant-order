import React from 'react';
import { FooterConfig } from './types';

export interface AppFooterProps extends FooterConfig {
  className?: string;
  style?: React.CSSProperties;
}

export const AppFooter: React.FC<AppFooterProps> = ({
  copyright,
  businessText,
  links = [],
  visible = true,
  showCopyright = true,
  className,
  style,
}) => {
  if (!visible) return null;

  return (
    <footer
      role="contentinfo"
      data-testid="app-footer"
      className={className}
      style={{
        display: 'flex',
        flexWrap: 'wrap',
        alignItems: 'center',
        justifyContent: 'space-between',
        padding: 'var(--ro-space-4)',
        backgroundColor: 'var(--ro-color-surface)',
        borderTop: '1px solid var(--ro-color-border)',
        gap: 'var(--ro-space-3)',
        fontSize: 'var(--ro-font-size-xs)',
        color: 'var(--ro-color-text-muted)',
        width: '100%',
        maxWidth: '100vw',
        boxSizing: 'border-box',
        ...style,
      }}
    >
      <div style={{ display: 'flex', flexDirection: 'column', gap: 'var(--ro-space-1)' }}>
        {showCopyright && copyright && (
          <div data-testid="footer-copyright">{copyright}</div>
        )}
        {businessText && (
          <div data-testid="footer-business" style={{ color: 'var(--ro-color-text-secondary)' }}>
            {businessText}
          </div>
        )}
      </div>

      {links.length > 0 && (
        <nav
          aria-label="Alt Bilgi Bağlantıları"
          style={{
            display: 'flex',
            flexWrap: 'wrap',
            gap: 'var(--ro-space-3)',
            alignItems: 'center',
          }}
        >
          {links.map((link) => (
            <a
              key={link.id}
              href={link.href || '#'}
              onClick={(e) => {
                if (link.onClick) {
                  e.preventDefault();
                  link.onClick();
                }
              }}
              target={link.isExternal ? '_blank' : undefined}
              rel={link.isExternal ? 'noopener noreferrer' : undefined}
              style={{
                color: 'var(--ro-color-text-muted)',
                textDecoration: 'none',
                transition: 'color var(--ro-duration-fast) var(--ro-ease-standard)',
              }}
            >
              {link.label}
            </a>
          ))}
        </nav>
      )}
    </footer>
  );
};
