export interface CatalogPermissions {
  canView: boolean;
  canManage: boolean;
  canManagePricing: boolean;
  canQuick86: boolean;
}

const ROLE_PERMISSIONS: Record<string, string[]> = {
  SuperAdmin: ['menu.catalog.view'],
  RestaurantAdmin: ['menu.catalog.view', 'menu.catalog.manage', 'menu.pricing.manage', 'menu.inventory.quick86'],
  BranchManager: ['menu.catalog.view', 'menu.catalog.manage', 'menu.pricing.manage', 'menu.inventory.quick86'],
  Cashier: ['menu.catalog.view', 'menu.inventory.quick86'],
  Kitchen: ['menu.catalog.view', 'menu.inventory.quick86'],
  Bar: ['menu.catalog.view', 'menu.inventory.quick86'],
  Waiter: ['menu.catalog.view'],
  Customer: ['menu.catalog.view'],
};

export function getCatalogPermissions(role?: string | null): CatalogPermissions {
  const permissions = ROLE_PERMISSIONS[role ?? ''] ?? [];
  return {
    canView: permissions.includes('menu.catalog.view'),
    canManage: permissions.includes('menu.catalog.manage'),
    canManagePricing: permissions.includes('menu.pricing.manage'),
    canQuick86: permissions.includes('menu.inventory.quick86'),
  };
}
