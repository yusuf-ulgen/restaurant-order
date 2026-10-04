export interface MenuContract {
  id: string;
  tenantId: string;
  branchId: string;
  name: string;
  slug: string;
  description: string | null;
  status: 'Draft' | 'Active' | 'Archived' | string;
  sortOrder: number;
  concurrencyToken: string;
}

export interface CategoryContract {
  id: string;
  tenantId: string;
  branchId: string;
  menuId: string;
  name: string;
  slug: string;
  description: string | null;
  sortOrder: number;
  isActive: boolean;
  concurrencyToken: string;
}

export interface VariantContract {
  id: string;
  name: string;
  code: string;
  absolutePriceMinorUnits: number;
  sortOrder: number;
  isDefault: boolean;
  isActive: boolean;
  concurrencyToken: string;
}

export interface ModifierOptionContract {
  id: string;
  modifierGroupId: string;
  name: string;
  priceDeltaMinorUnits: number;
  sortOrder: number;
  isDefault: boolean;
  isActive: boolean;
  concurrencyToken: string;
}

export interface ModifierGroupContract {
  id: string;
  branchId: string;
  name: string;
  minSelections: number;
  maxSelections: number;
  sortOrder: number;
  isActive: boolean;
  concurrencyToken: string;
  options?: ModifierOptionContract[];
}

export interface ItemModifierGroupContract {
  modifierGroupId: string;
  name: string;
  minSelections: number;
  maxSelections: number;
  sortOrder: number;
  isActive: boolean;
  options?: ModifierOptionContract[];
}

export interface MenuItemContract {
  id: string;
  tenantId: string;
  branchId: string;
  menuId: string;
  categoryId: string;
  name: string;
  slug: string;
  shortDescription: string | null;
  fullDescription: string | null;
  imageUrl: string | null;
  basePriceMinorUnits: number;
  sortOrder: number;
  isActive: boolean;
  spicyLevel: number;
  dietaryTags: string[];
  allergenTags: string[];
  concurrencyToken: string;
  preparationStationId?: string | null;
  variants?: VariantContract[];
  modifierGroups?: ItemModifierGroupContract[];
}

export interface AvailabilityContract {
  id: string;
  branchId: string;
  menuItemId: string;
  itemVariantId: string | null;
  isAvailable: boolean;
  reasonCode: string;
  note: string | null;
  expectedAvailableAtUtc: string | null;
  concurrencyToken: string;
}

export interface ProblemDetailsContract {
  title?: string;
  detail?: string;
  status?: number;
  correlationId?: string;
}
