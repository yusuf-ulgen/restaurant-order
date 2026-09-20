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
        const hasHref = Boolean(item.href);
        const hasAction = Boolean(item.onClick) || Boolean(onItemClick);
        const isInteractive = hasHref || hasAction;
        const isDisabled = !!item.disabled || !isInteractive;

        const commonStyle: React.CSSProperties = {
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
            : isDisabled
            ? 'var(--ro-color-text-muted)'
            : 'var(--ro-color-text-secondary)',
          cursor: isDisabled ? 'not-allowed' : 'pointer',
          opacity: isDisabled ? 0.5 : 1,
          position: 'relative',
          outline: 'none',
          flex: 1,
          maxWidth: '96px',
          textDecoration: 'none',
          boxSizing: 'border-box',
        };

        const content = (
          <>
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
                  : 'var(--ro-font-weight-regular)',
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
          </>
        );

        if (hasHref) {
          const isExternal =
            item.isExternal ||
            item.target === '_blank' ||
            item.href?.startsWith('http://') ||
            item.href?.startsWith('https://');

          return (
            <a
              key={item.id}
              data-testid={`mobile-nav-item-${item.id}`}
              data-active={isActive ? 'true' : undefined}
              href={isDisabled ? undefined : item.href}
              target={isExternal ? '_blank' : item.target}
              rel={isExternal ? 'noopener noreferrer' : undefined}
              aria-disabled={isDisabled ? 'true' : undefined}
              aria-current={isActive ? 'page' : undefined}
              aria-label={item.label}
              tabIndex={isDisabled ? -1 : undefined}
              onClick={(e) => {
                if (isDisabled) {
                  e.preventDefault();
                  return;
                }
                item.onClick?.();
                onItemClick?.(item);
              }}
              style={commonStyle}
            >
              {content}
            </a>
          );
        }

        if (hasAction) {
          return (
            <button
              key={item.id}
              type="button"
              data-testid={`mobile-nav-item-${item.id}`}
              data-active={isActive ? 'true' : undefined}
              disabled={item.disabled}
              aria-disabled={item.disabled ? 'true' : undefined}
              aria-current={isActive ? 'page' : undefined}
              aria-label={item.label}
              onClick={() => {
                if (item.disabled) return;
                item.onClick?.();
                onItemClick?.(item);
              }}
              style={commonStyle}
            >
              {content}
            </button>
          );
        }

        // Neither href nor action: render non-interactive element that is clearly inactive
        return (
          <div
            key={item.id}
            data-testid={`mobile-nav-item-${item.id}`}
            data-active={isActive ? 'true' : undefined}
            aria-disabled="true"
            aria-current={isActive ? 'page' : undefined}
            aria-label={item.label}
            style={commonStyle}
          >
            {content}
          </div>
        );
      })}
    </nav>
  );
};
