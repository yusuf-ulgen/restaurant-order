import { NavSectionConfig, NavItemConfig, NavigationItemOverrideContract } from '@restaurant-order/ui';

export type NavigationRegistryId =
  | 'dashboard'
  | 'brand-settings'
  | 'branch-settings'
  | 'operating-hours'
  | 'dining-areas'
  | 'preparation-stations'
  | 'feature-settings'
  | 'staff'
  | 'menu'
  | 'tables'
  | 'printers'
  | 'reports';

export type NavSectionId = 'main' | 'operations' | 'settings' | 'system';

export interface NavigationRegistryItem {
  id: NavigationRegistryId;
  defaultLabel: string;
  defaultSection: NavSectionId;
  defaultOrder: number;
  defaultDisabled: boolean;
  requiredPermission?: string;
  allowedRoles?: string[];
  href: string;
}

export const SECTION_METADATA: Record<NavSectionId, { title: string; order: number }> = {
  main: { title: 'Ana Menü', order: 1 },
  operations: { title: 'Operasyon', order: 2 },
  settings: { title: 'Yapılandırma & Ayarlar', order: 3 },
  system: { title: 'Sistem & Altyapı', order: 4 },
};

export const NAVIGATION_REGISTRY: Record<NavigationRegistryId, NavigationRegistryItem> = {
  dashboard: {
    id: 'dashboard',
    defaultLabel: 'Kontrol Paneli',
    defaultSection: 'main',
    defaultOrder: 1,
    defaultDisabled: false,
    href: '/',
  },
  menu: {
    id: 'menu',
    defaultLabel: 'Menü Yönetimi (Yakında)',
    defaultSection: 'main',
    defaultOrder: 2,
    defaultDisabled: true,
    requiredPermission: 'menu.catalog.manage',
    href: '#/menu',
  },
  tables: {
    id: 'tables',
    defaultLabel: 'Şube & Masalar (Yakında)',
    defaultSection: 'main',
    defaultOrder: 3,
    defaultDisabled: true,
    requiredPermission: 'branch.tables.manage',
    href: '#/tables',
  },
  'dining-areas': {
    id: 'dining-areas',
    defaultLabel: 'Masa Alanları (Yakında)',
    defaultSection: 'operations',
    defaultOrder: 1,
    defaultDisabled: true,
    requiredPermission: 'tenant.branches.manage',
    href: '#/operations/dining-areas',
  },
  'preparation-stations': {
    id: 'preparation-stations',
    defaultLabel: 'Hazırlık İstasyonları (Yakında)',
    defaultSection: 'operations',
    defaultOrder: 2,
    defaultDisabled: true,
    requiredPermission: 'tenant.branches.manage',
    href: '#/operations/preparation-stations',
  },
  staff: {
    id: 'staff',
    defaultLabel: 'Personel & Vardiya (Yakında)',
    defaultSection: 'operations',
    defaultOrder: 3,
    defaultDisabled: true,
    requiredPermission: 'branch.staff.manage',
    href: '#/operations/staff',
  },
  'brand-settings': {
    id: 'brand-settings',
    defaultLabel: 'Restoran / Marka Ayarları',
    defaultSection: 'settings',
    defaultOrder: 1,
    defaultDisabled: false,
    requiredPermission: 'tenant.branding.manage',
    allowedRoles: ['RestaurantAdmin'],
    href: '#/settings/branding',
  },
  'branch-settings': {
    id: 'branch-settings',
    defaultLabel: 'Şube Ayarları',
    defaultSection: 'settings',
    defaultOrder: 2,
    defaultDisabled: false,
    requiredPermission: 'branch.configuration.manage',
    allowedRoles: ['RestaurantAdmin', 'BranchManager'],
    href: '#/settings/branches',
  },
  'operating-hours': {
    id: 'operating-hours',
    defaultLabel: 'Çalışma Saatleri',
    defaultSection: 'settings',
    defaultOrder: 3,
    defaultDisabled: false,
    requiredPermission: 'branch.configuration.manage',
    allowedRoles: ['RestaurantAdmin', 'BranchManager'],
    href: '#/settings/operating-hours',
  },
  'feature-settings': {
    id: 'feature-settings',
    defaultLabel: 'Özellik Ayarları (Yakında)',
    defaultSection: 'settings',
    defaultOrder: 4,
    defaultDisabled: true,
    requiredPermission: 'tenant.brands.manage',
    allowedRoles: ['RestaurantAdmin'],
    href: '#/settings/features',
  },
  printers: {
    id: 'printers',
    defaultLabel: 'Yazıcılar & ESC/POS (Yakında)',
    defaultSection: 'system',
    defaultOrder: 1,
    defaultDisabled: true,
    requiredPermission: 'branch.printers.manage',
    href: '#/system/printers',
  },
  reports: {
    id: 'reports',
    defaultLabel: 'Raporlar & Analiz (Yakında)',
    defaultSection: 'system',
    defaultOrder: 2,
    defaultDisabled: true,
    requiredPermission: 'reports.branch.revenue',
    href: '#/system/reports',
  },
};

/**
 * Checks whether user has permission to view a registry navigation item.
 */
export function hasNavigationAccess(
  item: NavigationRegistryItem,
  userRole?: string | null,
  userPermissions: string[] = []
): boolean {
  if (!userRole) return false;

  // SuperAdmin has full platform access
  if (userRole === 'SuperAdmin') return true;

  // Role whitelist constraint
  if (item.allowedRoles && !item.allowedRoles.includes(userRole)) {
    return false;
  }

  // Required permission constraint
  if (item.requiredPermission) {
    if (userPermissions.length > 0) {
      return userPermissions.includes(item.requiredPermission);
    }
    // Default fallback by role if userPermissions array is empty
    if (userRole === 'RestaurantAdmin') return true;
    if (userRole === 'BranchManager') {
      const branchManagerPermissions = [
        'tenant.branding.view',
        'tenant.branding.manage',
        'branch.tables.manage',
        'branch.printers.manage',
        'branch.staff.manage',
        'reports.branch.revenue',
      ];
      return branchManagerPermissions.includes(item.requiredPermission);
    }
    return false;
  }

  return true;
}

/**
 * Resolves dynamic navigation sections based on static registry, database overrides, and user RBAC.
 * Guarantees that:
 * 1. Unknown navigation IDs in database overrides are strictly ignored.
 * 2. Feature flags / config cannot bypass user permission checks.
 * 3. Raw HTML in label overrides is sanitized.
 */
export function resolveAdminNavigation(
  overrides?: NavigationItemOverrideContract[] | null,
  userRole?: string | null,
  userPermissions: string[] = [],
  activeViewId?: string,
  onNavigate?: (id: NavigationRegistryId) => void
): NavSectionConfig[] {
  const overrideMap = new Map<string, NavigationItemOverrideContract>();
  if (overrides && Array.isArray(overrides)) {
    for (const ov of overrides) {
      if (ov && ov.id && Object.prototype.hasOwnProperty.call(NAVIGATION_REGISTRY, ov.id)) {
        overrideMap.set(ov.id, ov);
      }
    }
  }

  // Group items by resolved section
  const sectionBuckets: Record<NavSectionId, NavItemConfig[]> = {
    main: [],
    operations: [],
    settings: [],
    system: [],
  };

  const registryItems = Object.values(NAVIGATION_REGISTRY);

  for (const regItem of registryItems) {
    // 1. Strict RBAC permission boundary (cannot be bypassed by overrides)
    if (!hasNavigationAccess(regItem, userRole, userPermissions)) {
      continue;
    }

    const override = overrideMap.get(regItem.id);

    // 2. Check visibility override (default true)
    const isVisible = override?.isVisible !== undefined && override?.isVisible !== null
      ? override.isVisible
      : true;

    if (!isVisible) {
      continue;
    }

    // 3. Resolve order
    const order = typeof override?.order === 'number' && override.order >= 1
      ? override.order
      : regItem.defaultOrder;

    // 4. Resolve label (strip potential HTML tags for safety)
    let label = regItem.defaultLabel;
    if (override?.labelOverride && typeof override.labelOverride === 'string') {
      const sanitized = override.labelOverride.replace(/[<>]/g, '').trim();
      if (sanitized.length > 0) {
        label = sanitized;
      }
    }

    // 5. Resolve section
    let targetSection: NavSectionId = regItem.defaultSection;
    if (override?.section && Object.prototype.hasOwnProperty.call(SECTION_METADATA, override.section)) {
      targetSection = override.section as NavSectionId;
    }

    // 6. Resolve disabled
    const disabled = override?.disabled !== undefined && override?.disabled !== null
      ? override.disabled
      : regItem.defaultDisabled;

    const navItem: NavItemConfig = {
      id: regItem.id,
      label,
      href: regItem.href,
      order,
      disabled,
      isActive: activeViewId === regItem.id,
      onClick: () => {
        if (!disabled && onNavigate) {
          onNavigate(regItem.id);
        }
      },
    };

    sectionBuckets[targetSection].push(navItem);
  }

  // Build sorted section configs
  const sections: NavSectionConfig[] = [];
  const sectionIds: NavSectionId[] = ['main', 'operations', 'settings', 'system'];

  for (const secId of sectionIds) {
    const items = sectionBuckets[secId];
    if (items.length === 0) continue;

    items.sort((a, b) => (a.order ?? 0) - (b.order ?? 0));

    const meta = SECTION_METADATA[secId];
    sections.push({
      id: secId,
      title: meta.title,
      order: meta.order,
      items,
    });
  }

  sections.sort((a, b) => (a.order ?? 0) - (b.order ?? 0));
  return sections;
}
