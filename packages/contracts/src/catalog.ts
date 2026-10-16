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

export interface RuntimeModifierOption {
  id: string;
  name: string;
  priceDeltaMinorUnits: number;
  sortOrder: number;
  isDefault: boolean;
}

export interface RuntimeModifierGroup {
  id: string;
  name: string;
  minSelections: number;
  maxSelections: number;
  sortOrder: number;
  options: RuntimeModifierOption[];
}

export interface RuntimeItemVariant {
  id: string;
  name: string;
  code: string;
  absolutePriceMinorUnits: number;
  sortOrder: number;
  isDefault: boolean;
  isAvailable: boolean;
  expectedAvailableAtUtc: string | null;
}

export interface RuntimeMenuItem {
  id: string;
  name: string;
  slug: string;
  shortDescription: string | null;
  fullDescription: string | null;
  imageUrl: string | null;
  basePriceMinorUnits: number;
  sortOrder: number;
  spicyLevel: number;
  dietaryTags: string[];
  allergenTags: string[];
  isAvailable: boolean;
  expectedAvailableAtUtc: string | null;
  variants: RuntimeItemVariant[];
  modifierGroups: RuntimeModifierGroup[];
}

export interface RuntimeMenuCategory {
  id: string;
  name: string;
  slug: string;
  description: string | null;
  sortOrder: number;
  items: RuntimeMenuItem[];
}

export interface RuntimeMenu {
  id: string;
  name: string;
  slug: string;
  description: string | null;
  sortOrder: number;
  categories: RuntimeMenuCategory[];
}

export interface RuntimeMenuReadModel {
  branchId: string;
  currency: string;
  menus: RuntimeMenu[];
}

// Dto aliases matching backend C# records
export type RuntimeMenuDto = RuntimeMenu;
export type RuntimeMenuCategoryDto = RuntimeMenuCategory;
export type RuntimeMenuItemDto = RuntimeMenuItem;
export type RuntimeItemVariantDto = RuntimeItemVariant;
export type RuntimeModifierGroupDto = RuntimeModifierGroup;
export type RuntimeModifierOptionDto = RuntimeModifierOption;

export const CUSTOMER_NOTE_MAX_LENGTH = 200;

export interface DraftCartLine {
  menuItemId: string;
  variantId: string | null;
  selectedModifierOptionIds: string[];
  customerNote: string | null;
  quantity: number;
  unitPriceMinorUnits: number;
  lineTotalMinorUnits: number;
  currency: string;
  configurationSignature: string;
}

export type CartValidationIssueType =
  | 'item_unavailable'
  | 'item_removed'
  | 'variant_unavailable'
  | 'variant_removed'
  | 'modifier_removed'
  | 'price_changed';

export interface CartValidationIssue {
  lineSignature: string;
  type: CartValidationIssueType;
  message: string;
  previousUnitPriceMinorUnits?: number;
  currentUnitPriceMinorUnits?: number;
}

