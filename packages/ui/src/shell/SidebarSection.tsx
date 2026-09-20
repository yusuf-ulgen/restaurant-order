import React from 'react';
import { NavItemConfig, NavSectionConfig } from './types';

export interface SidebarSectionProps extends NavSectionConfig {
  isCollapsed?: boolean;
  onItemClick?: (item: NavItemConfig) => void;
  onMobileClose?: () => void;
  className?: string;
  style?: React.CSSProperties;
}

export const SidebarSection: React.FC<SidebarSectionProps> = ({
  title,
  items,
  isCollapsed = false,
  onItemClick,
  onMobileClose,
  className,
  style,
}) => {
  const visibleItems = items
    .filter((item) => item.isVisible !== false)
    .sort((a, b) => (a.order ?? 0) - (b.order ?? 0));

  if (visibleItems.length === 0) return null;

  return (
    <div
      data-testid="sidebar-section"
      className={className}
      style={{
        display: 'flex',
        flexDirection: 'column',
        gap: 'var(--ro-space-1)',
        marginBottom: 'var(--ro-space-3)',
        ...style,
      }}
    >
      {title && !isCollapsed && (
        <div
          data-testid="sidebar-section-title"
          style={{
            fontSize: 'var(--ro-font-size-xs)',
            fontWeight: 'var(--ro-font-weight-semibold)',
            color: 'var(--ro-color-text-muted)',
            textTransform: 'uppercase',
            letterSpacing: '0.05em',
            padding: 'var(--ro-space-1) var(--ro-space-3)',
          }}
        >
          {title}
        </div>
      )}

      {visibleItems.map((item) => {
        const isActive = !!item.isActive;
        const hasHref = Boolean(item.href);
        const hasAction = Boolean(item.onClick) || Boolean(onItemClick);
        const isInteractive = hasHref || hasAction;
        const isDisabled = !!item.disabled || !isInteractive;

        const commonStyle: React.CSSProperties = {
          display: 'flex',
          alignItems: 'center',
          gap: isCollapsed ? 0 : 'var(--ro-space-3)',
          justifyContent: isCollapsed ? 'center' : 'flex-start',
          padding: isCollapsed
            ? 'var(--ro-space-2)'
            : 'var(--ro-space-2) var(--ro-space-3)',
          borderRadius: 'var(--ro-radius-md)',
          border: '1px solid transparent',
          backgroundColor: isActive
            ? 'var(--ro-color-primary-subtle, rgba(37, 99, 235, 0.1))'
            : 'transparent',
          color: isActive
            ? 'var(--ro-color-primary)'
            : isDisabled
            ? 'var(--ro-color-text-muted)'
            : 'var(--ro-color-text-secondary)',
          fontWeight: isActive
            ? 'var(--ro-font-weight-semibold)'
            : 'var(--ro-font-weight-regular)',
          cursor: isDisabled ? 'not-allowed' : 'pointer',
          opacity: isDisabled ? 0.5 : 1,
          textAlign: 'left',
          width: '100%',
          boxSizing: 'border-box',
          transition: 'all var(--ro-duration-fast) var(--ro-ease-standard)',
          outline: 'none',
          textDecoration: 'none',
        };

        const content = (
          <>
            {item.icon && (
              <span
                aria-hidden="true"
                style={{
                  display: 'inline-flex',
                  alignItems: 'center',
                  justifyContent: 'center',
                  fontSize: '1.125rem',
                  flexShrink: 0,
                }}
              >
                {item.icon}
              </span>
            )}

            {!isCollapsed && (
              <>
                <span
                  style={{
                    flex: 1,
                    fontSize: 'var(--ro-font-size-sm)',
                    overflow: 'hidden',
                    textOverflow: 'ellipsis',
                    whiteSpace: 'nowrap',
                  }}
                >
                  {item.label}
                </span>

                {item.badge && (
                  <span style={{ flexShrink: 0, marginLeft: 'auto' }}>{item.badge}</span>
                )}
              </>
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
              data-testid={`sidebar-item-${item.id}`}
              data-active={isActive ? 'true' : undefined}
              href={isDisabled ? undefined : item.href}
              target={isExternal ? '_blank' : item.target}
              rel={isExternal ? 'noopener noreferrer' : undefined}
              aria-disabled={isDisabled ? 'true' : undefined}
              aria-current={isActive ? 'page' : undefined}
              title={isCollapsed ? item.label : undefined}
              aria-label={item.label}
              tabIndex={isDisabled ? -1 : undefined}
              onClick={(e) => {
                if (isDisabled) {
                  e.preventDefault();
                  return;
                }
                item.onClick?.();
                onItemClick?.(item);
                onMobileClose?.();
              }}
              style={commonStyle}
              onFocus={(e) => {
                if (!isDisabled) {
                  e.currentTarget.style.boxShadow = '0 0 0 2px var(--ro-color-focus)';
                }
              }}
              onBlur={(e) => {
                e.currentTarget.style.boxShadow = 'none';
              }}
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
              data-testid={`sidebar-item-${item.id}`}
              data-active={isActive ? 'true' : undefined}
              disabled={item.disabled}
              aria-disabled={item.disabled ? 'true' : undefined}
              aria-current={isActive ? 'page' : undefined}
              title={isCollapsed ? item.label : undefined}
              aria-label={item.label}
              onClick={() => {
                if (item.disabled) return;
                item.onClick?.();
                onItemClick?.(item);
                onMobileClose?.();
              }}
              style={commonStyle}
              onFocus={(e) => {
                if (!item.disabled) {
                  e.currentTarget.style.boxShadow = '0 0 0 2px var(--ro-color-focus)';
                }
              }}
              onBlur={(e) => {
                e.currentTarget.style.boxShadow = 'none';
              }}
            >
              {content}
            </button>
          );
        }

        // Neither href nor action: render non-interactive element that is clearly inactive
        return (
          <div
            key={item.id}
            data-testid={`sidebar-item-${item.id}`}
            data-active={isActive ? 'true' : undefined}
            aria-disabled="true"
            aria-current={isActive ? 'page' : undefined}
            title={isCollapsed ? item.label : undefined}
            aria-label={item.label}
            style={commonStyle}
          >
            {content}
          </div>
        );
      })}
    </div>
  );
};
