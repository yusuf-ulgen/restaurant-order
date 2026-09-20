import React from 'react';

export interface NavItemConfig {
  id: string;
  label: string;
  icon?: React.ReactNode;
  href?: string;
  onClick?: () => void;
  isActive?: boolean;
  badge?: React.ReactNode;
  order?: number;
  isVisible?: boolean;
  disabled?: boolean;
  isExternal?: boolean;
  target?: string;
}

export interface NavSectionConfig {
  id: string;
  title?: string;
  items: NavItemConfig[];
  order?: number;
  isVisible?: boolean;
}

export interface HeaderConfig {
  title: React.ReactNode;
  subtitle?: React.ReactNode;
  logo?: React.ReactNode;
  logoUrl?: string;
  actions?: React.ReactNode;
  showMobileMenuToggle?: boolean;
  onMobileMenuToggle?: () => void;
  isMobileMenuOpen?: boolean;
}

export interface FooterLinkConfig {
  id: string;
  label: string;
  href?: string;
  onClick?: () => void;
  isExternal?: boolean;
}

export interface FooterConfig {
  copyright?: string;
  businessText?: string;
  links?: FooterLinkConfig[];
  visible?: boolean;
  showCopyright?: boolean;
}

export interface ShellVariantConfig {
  type: 'customer' | 'operations' | 'admin' | 'custom';
  hasSidebar?: boolean;
  sidebarCollapsed?: boolean;
  showBottomNav?: boolean;
}
