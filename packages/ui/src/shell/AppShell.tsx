import React, { useState } from 'react';
import { HeaderConfig, FooterConfig } from './types';
import { SkipLink } from './SkipLink';
import { AppHeader } from './AppHeader';
import { AppFooter } from './AppFooter';
import { Sidebar, SidebarProps } from './Sidebar';
import { MobileNavigation, MobileNavigationProps } from './MobileNavigation';
import { PageContainer } from './PageContainer';

export interface AppShellProps {
  children: React.ReactNode;
  variant?: 'customer' | 'operations' | 'admin' | 'custom';
  header?: HeaderConfig;
  customHeader?: React.ReactNode;
  footer?: FooterConfig;
  customFooter?: React.ReactNode;
  sidebar?: SidebarProps;
  customSidebar?: React.ReactNode;
  mobileNav?: MobileNavigationProps;
  customMobileNav?: React.ReactNode;
  maxWidth?: 'sm' | 'md' | 'lg' | 'xl' | 'full';
  contentPadding?: 'none' | 'sm' | 'md' | 'lg';
  skipLinkLabel?: string;
  className?: string;
  style?: React.CSSProperties;
}

export const AppShell: React.FC<AppShellProps> = ({
  children,
  variant = 'custom',
  header,
  customHeader,
  footer,
  customFooter,
  sidebar,
  customSidebar,
  mobileNav,
  customMobileNav,
  maxWidth = variant === 'customer' ? 'md' : 'xl',
  contentPadding = 'md',
  skipLinkLabel = 'Ana içeriğe atla',
  className,
  style,
}) => {
  const [isMobileMenuOpen, setIsMobileMenuOpen] = useState(false);
  const [isSidebarCollapsed, setIsSidebarCollapsed] = useState(
    sidebar?.isCollapsed ?? false
  );

  // Requirement: Customer screen must not have sidebar
  const showSidebar = variant !== 'customer' && (!!sidebar || !!customSidebar);

  return (
    <div
      data-testid="app-shell"
      data-variant={variant}
      className={`ro-app-shell ${className || ''}`}
      style={{
        display: 'flex',
        minHeight: '100dvh',
        backgroundColor: 'var(--ro-color-background)',
        color: 'var(--ro-color-text-primary)',
        width: '100%',
        maxWidth: '100vw',
        overflowX: 'hidden',
        boxSizing: 'border-box',
        position: 'relative',
        ...style,
      }}
    >
      <SkipLink targetId="main-content" label={skipLinkLabel} />

      {/* Sidebar Area */}
      {showSidebar && (
        <>
          {customSidebar ? (
            customSidebar
          ) : sidebar ? (
            <Sidebar
              {...sidebar}
              isCollapsed={isSidebarCollapsed}
              onToggleCollapse={() => setIsSidebarCollapsed((prev) => !prev)}
              isMobileOpen={isMobileMenuOpen}
              onMobileClose={() => setIsMobileMenuOpen(false)}
            />
          ) : null}
        </>
      )}

      {/* Main Content Column */}
      <div
        style={{
          display: 'flex',
          flexDirection: 'column',
          flex: 1,
          minWidth: 0,
          minHeight: '100dvh',
          boxSizing: 'border-box',
        }}
      >
        {/* Header Area */}
        {customHeader ? (
          customHeader
        ) : header ? (
          <AppHeader
            {...header}
            showMobileMenuToggle={showSidebar || header.showMobileMenuToggle}
            isMobileMenuOpen={isMobileMenuOpen}
            onMobileMenuToggle={() => setIsMobileMenuOpen((prev) => !prev)}
          />
        ) : null}

        {/* Main Body */}
        <main
          id="main-content"
          role="main"
          style={{
            flex: 1,
            display: 'flex',
            flexDirection: 'column',
            width: '100%',
            boxSizing: 'border-box',
            paddingBottom: mobileNav || customMobileNav ? 'calc(56px + var(--ro-safe-area-bottom))' : 0,
          }}
        >
          <PageContainer maxWidth={maxWidth} padding={contentPadding}>
            {children}
          </PageContainer>
        </main>

        {/* Footer Area */}
        {customFooter ? (
          customFooter
        ) : footer ? (
          <AppFooter {...footer} />
        ) : null}

        {/* Mobile Navigation Area */}
        {customMobileNav ? (
          customMobileNav
        ) : mobileNav ? (
          <MobileNavigation {...mobileNav} />
        ) : null}
      </div>
    </div>
  );
};
