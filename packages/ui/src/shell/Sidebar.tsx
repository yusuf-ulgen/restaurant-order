import React from 'react';
import { NavItemConfig, NavSectionConfig } from './types';
import { SidebarSection } from './SidebarSection';
import { Drawer } from '../components/Drawer';
import { IconButton } from '../components/IconButton';

export interface SidebarProps {
  sections: NavSectionConfig[];
  title?: React.ReactNode;
  logo?: React.ReactNode;
  isCollapsed?: boolean;
  onToggleCollapse?: () => void;
  isMobileOpen?: boolean;
  onMobileClose?: () => void;
  onItemClick?: (item: NavItemConfig) => void;
  ariaLabel?: string;
  navAriaLabel?: string;
  footer?: React.ReactNode;
  className?: string;
  style?: React.CSSProperties;
}

export const Sidebar: React.FC<SidebarProps> = ({
  sections,
  title,
  logo,
  isCollapsed = false,
  onToggleCollapse,
  isMobileOpen = false,
  onMobileClose,
  onItemClick,
  ariaLabel = 'Kenar Çubuğu',
  navAriaLabel = 'Ana Gezinti',
  footer,
  className,
  style,
}) => {
  const visibleSections = sections
    .filter((sec) => sec.isVisible !== false)
    .sort((a, b) => (a.order ?? 0) - (b.order ?? 0));

  const sidebarContent = (
    <div
      style={{
        display: 'flex',
        flexDirection: 'column',
        height: '100%',
        width: '100%',
        boxSizing: 'border-box',
        overflow: 'hidden',
      }}
    >
      {/* Sidebar Header */}
      {(title || logo || onToggleCollapse) && (
        <div
          style={{
            display: 'flex',
            alignItems: 'center',
            justifyContent: isCollapsed ? 'center' : 'space-between',
            padding: 'var(--ro-space-4)',
            borderBottom: '1px solid var(--ro-color-border)',
            minHeight: '56px',
            boxSizing: 'border-box',
          }}
        >
          <div
            style={{
              display: 'flex',
              alignItems: 'center',
              gap: 'var(--ro-space-2)',
              overflow: 'hidden',
            }}
          >
            {logo && <div style={{ flexShrink: 0 }}>{logo}</div>}
            {!isCollapsed && title && (
              <h2
                style={{
                  margin: 0,
                  fontSize: 'var(--ro-font-size-md)',
                  fontWeight: 'var(--ro-font-weight-bold)',
                  color: 'var(--ro-color-text-primary)',
                  whiteSpace: 'nowrap',
                  overflow: 'hidden',
                  textOverflow: 'ellipsis',
                }}
              >
                {title}
              </h2>
            )}
          </div>

          {onToggleCollapse && (
            <IconButton
              data-testid="sidebar-collapse-btn"
              aria-label={isCollapsed ? 'Kenar çubuğunu genişlet' : 'Kenar çubuğunu daralt'}
              variant="ghost"
              size="sm"
              onClick={onToggleCollapse}
              icon={
                <span aria-hidden="true">
                  {isCollapsed ? '▶' : '◀'}
                </span>
              }
            />
          )}
        </div>
      )}

      {/* Navigation Sections */}
      <nav
        aria-label={navAriaLabel}
        style={{
          flex: 1,
          overflowY: 'auto',
          padding: 'var(--ro-space-3) var(--ro-space-2)',
        }}
      >
        {visibleSections.map((section) => (
          <SidebarSection
            key={section.id}
            {...section}
            isCollapsed={isCollapsed}
            onItemClick={onItemClick}
            onMobileClose={onMobileClose}
          />
        ))}
      </nav>

      {/* Sidebar Footer */}
      {footer && (
        <div
          style={{
            padding: 'var(--ro-space-3)',
            borderTop: '1px solid var(--ro-color-border)',
          }}
        >
          {footer}
        </div>
      )}
    </div>
  );

  return (
    <>
      {/* Desktop / Tablet Sidebar */}
      <aside
        aria-label={ariaLabel}
        data-testid="desktop-sidebar"
        data-collapsed={isCollapsed ? 'true' : undefined}
        className={`ro-desktop-sidebar ${className || ''}`}
        style={{
          width: isCollapsed ? '72px' : '260px',
          height: '100dvh',
          backgroundColor: 'var(--ro-color-surface)',
          borderRight: '1px solid var(--ro-color-border)',
          flexShrink: 0,
          position: 'sticky',
          top: 0,
          display: 'flex',
          flexDirection: 'column',
          transition: 'width var(--ro-duration-normal) var(--ro-ease-standard)',
          boxSizing: 'border-box',
          ...style,
        }}
      >
        {sidebarContent}
      </aside>

      {/* Mobile Drawer Sidebar */}
      {isMobileOpen && (
        <Drawer
          isOpen={isMobileOpen}
          onClose={onMobileClose || (() => {})}
          placement="left"
          size="sm"
          title={title}
          showCloseButton={true}
        >
          <div style={{ margin: '-16px', height: '100%' }}>
            <nav aria-label={navAriaLabel} style={{ padding: 'var(--ro-space-3)' }}>
              {visibleSections.map((section) => (
                <SidebarSection
                  key={section.id}
                  {...section}
                  isCollapsed={false}
                  onItemClick={
                    onItemClick || onMobileClose
                      ? (item) => {
                          onItemClick?.(item);
                          onMobileClose?.();
                        }
                      : undefined
                  }
                />
              ))}
            </nav>
          </div>
        </Drawer>
      )}

      <style>{`
        @media (max-width: 768px) {
          .ro-desktop-sidebar {
            display: none !important;
          }
        }
      `}</style>
    </>
  );
};
