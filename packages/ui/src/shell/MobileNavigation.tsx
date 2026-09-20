import React from 'react';
import { NavItemConfig } from './types';

export interface MobileNavigationProps {
  items: NavItemConfig[];
  onItemClick?: (item: NavItemConfig) => void;
  ariaLabel?: string;
  className?: string;
  style?: React.CSSProperties;
}

export const MobileNavigation: React.FC<MobileNavigationProps> = ({
  items,
  onItemClick,
  ariaLabel = 'Mobil Gezinti',
  className,
  style,
}) => {
  const visibleItems = items
    .filter((item) => item.isVisible !== false)
    .sort((a, b) => (a.order ?? 0) - (b.order ?? 0));

  if (visibleItems.length === 0) return null;

  return (
    <nav
      aria-label={ariaLabel}
      data-testid="mobile-navigation"
      className={`ro-mobile-nav ${className || ''}`}
      style={{
        position: 'fixed',
        bottom: 0,
        left: 0,
        right: 0,
        backgroundColor: 'var(--ro-color-surface)',
        borderTop: '1px solid var(--ro-color-border)',
        zIndex: 'var(--ro-z-sticky, 1100)',
        display: 'flex',
        alignItems: 'center',
        justifyContent: 'space-around',
        paddingTop: 'var(--ro-space-2)',
        paddingBottom: 'max(var(--ro-space-2), var(--ro-safe-area-bottom))',
        paddingLeft: 'var(--ro-space-2)',
        paddingRight: 'var(--ro-space-2)',
        boxSizing: 'border-box',
        width: '100%',
        maxWidth: '100vw',
        ...style,
      }}
    >
      {visibleItems.map((item) => {
        const isActive = !!item.isActive;

        return (
          <button
            key={item.id}
            data-testid={`mobile-nav-item-${item.id}`}
            data-active={isActive ? 'true' : undefined}
            disabled={item.disabled}
            aria-label={item.label}
            aria-current={isActive ? 'page' : undefined}
            onClick={() => {
              item.onClick?.();
              onItemClick?.(item);
            }}
            style={{
              display: 'flex',
              flexDirection: 'column',
              alignItems: 'center',
              justifyContent: 'center',
              gap: '2px',
              minWidth: 'var(--ro-touch-target-dense, 44px)',
              minHeight: 'var(--ro-touch-target-dense, 44px)',
              padding: 'var(--ro-space-1)',
              border: 'none',
              background: 'transparent',
              color: isActive
                ? 'var(--ro-color-primary)'
                : 'var(--ro-color-text-muted)',
              cursor: item.disabled ? 'not-allowed' : 'pointer',
              opacity: item.disabled ? 0.5 : 1,
              position: 'relative',
              outline: 'none',
              flex: 1,
              maxWidth: '96px',
            }}
          >
            {item.icon && (
              <span
                aria-hidden="true"
                style={{
                  fontSize: '1.25rem',
                  lineHeight: 1,
                  display: 'flex',
                  alignItems: 'center',
                  justifyContent: 'center',
                }}
              >
                {item.icon}
              </span>
            )}
            <span
              style={{
                fontSize: '0.6875rem',
                fontWeight: isActive
                  ? 'var(--ro-font-weight-semibold)'
                  : 'var(--ro-font-weight-normal)',
                whiteSpace: 'nowrap',
                overflow: 'hidden',
                textOverflow: 'ellipsis',
                maxWidth: '100%',
              }}
            >
              {item.label}
            </span>
            {item.badge && (
              <span
                style={{
                  position: 'absolute',
                  top: '2px',
                  right: 'calc(50% - 16px)',
                }}
              >
                {item.badge}
              </span>
            )}
          </button>
        );
      })}
    </nav>
  );
};
