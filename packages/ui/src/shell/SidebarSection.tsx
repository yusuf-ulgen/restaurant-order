import React from 'react';
import { NavItemConfig, NavSectionConfig } from './types';

export interface SidebarSectionProps extends NavSectionConfig {
  isCollapsed?: boolean;
  onItemClick?: (item: NavItemConfig) => void;
  className?: string;
  style?: React.CSSProperties;
}

export const SidebarSection: React.FC<SidebarSectionProps> = ({
  title,
  items,
  isCollapsed = false,
  onItemClick,
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

        return (
          <button
            key={item.id}
            data-testid={`sidebar-item-${item.id}`}
            data-active={isActive ? 'true' : undefined}
            disabled={item.disabled}
            title={isCollapsed ? item.label : undefined}
            aria-label={item.label}
            aria-current={isActive ? 'page' : undefined}
            onClick={() => {
              item.onClick?.();
              onItemClick?.(item);
            }}
            style={{
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
                : 'var(--ro-color-text-secondary)',
              fontWeight: isActive
                ? 'var(--ro-font-weight-semibold)'
                : 'var(--ro-font-weight-normal)',
              cursor: item.disabled ? 'not-allowed' : 'pointer',
              opacity: item.disabled ? 0.5 : 1,
              textAlign: 'left',
              width: '100%',
              boxSizing: 'border-box',
              transition: 'all var(--ro-duration-fast) var(--ro-ease-standard)',
              outline: 'none',
            }}
            onFocus={(e) => {
              e.currentTarget.style.boxShadow = '0 0 0 2px var(--ro-color-focus)';
            }}
            onBlur={(e) => {
              e.currentTarget.style.boxShadow = 'none';
            }}
          >
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
          </button>
        );
      })}
    </div>
  );
};
