export interface TimeSlotContract {
  openTime: string;
  closeTime: string;
  isOvernight?: boolean;
}

export interface OperatingDayScheduleContract {
  dayOfWeek: number; // 0 = Sunday, 1 = Monday, ..., 6 = Saturday
  isClosed: boolean;
  slots: TimeSlotContract[];
}

export interface BranchOperatingHoursContract {
  branchId: string;
  days: OperatingDayScheduleContract[];
  concurrencyToken: string;
  updatedAtUtc?: string | null;
}

export interface UpdateBranchOperatingHoursRequest {
  days: {
    dayOfWeek: number;
    isClosed: boolean;
    slots: { openTime: string; closeTime: string }[];
  }[];
  concurrencyToken?: string | null;
}

export interface EffectiveBranchSettingsContract {
  branchId: string;
  branchName: string;
  timezone: string;
  currency: string;
  defaultLocale: string;
  supportedLocales: string[];
  pricesIncludeTax: boolean;
  defaultTaxRateBps: number;
  isServiceChargeEnabled: boolean;
  serviceChargeRateBps: number;
  isOrderTakingEnabled: boolean;
  displayName?: string | null;
  phoneNumber?: string | null;
  email?: string | null;
  address?: string | null;
  hasCustomSettings: boolean;
  concurrencyToken: string;
}

export interface UpdateBranchSettingsRequest {
  timezone: string;
  currency: string;
  defaultLocale: string;
  supportedLocales: string[];
  pricesIncludeTax: boolean;
  defaultTaxRateBps: number;
  isServiceChargeEnabled: boolean;
  serviceChargeRateBps: number;
  isOrderTakingEnabled: boolean;
  displayName?: string | null;
  phoneNumber?: string | null;
  email?: string | null;
  address?: string | null;
  concurrencyToken?: string | null;
}

export type DiningAreaType = 'Indoor' | 'Terrace' | 'Garden' | 'BarArea' | 'Other';

export interface DiningAreaContract {
  id: string;
  tenantId: string;
  branchId: string;
  name: string;
  code: string;
  areaType: DiningAreaType;
  sortOrder: number;
  isActive: boolean;
  createdAtUtc: string;
  updatedAtUtc: string;
  concurrencyToken: string;
}

export interface CreateDiningAreaRequest {
  name: string;
  code: string;
  areaType: string;
  sortOrder?: number;
}

export interface UpdateDiningAreaRequest {
  name: string;
  areaType: string;
  concurrencyToken?: string | null;
}

export interface DiningAreaStateRequest {
  concurrencyToken?: string | null;
}

export interface ReorderItemRequest {
  id: string;
  concurrencyToken?: string | null;
}

export interface ReorderDiningAreasRequest {
  items: ReorderItemRequest[];
}

export type PreparationStationType = 'Kitchen' | 'Bar' | 'Other';

export interface PreparationStationContract {
  id: string;
  tenantId: string;
  branchId: string;
  code: string;
  displayName: string;
  stationType: PreparationStationType;
  sortOrder: number;
  isActive: boolean;
  createdAtUtc: string;
  updatedAtUtc: string;
  concurrencyToken: string;
}

export interface PreparationStationRuntimeContract {
  stationId: string;
  code: string;
  displayName: string;
  stationType: PreparationStationType;
  isActive: boolean;
}

export interface CreatePreparationStationRequest {
  code: string;
  displayName: string;
  stationType: string;
  sortOrder?: number;
}

export interface UpdatePreparationStationRequest {
  displayName: string;
  stationType: string;
  concurrencyToken?: string | null;
}

export interface PreparationStationStateRequest {
  concurrencyToken?: string | null;
}

export interface ReorderPreparationStationsRequest {
  items: ReorderItemRequest[];
}

export interface ClearBranchFeatureOverrideRequest {
  concurrencyToken?: string | null;
}

export type FeatureFlagKey =
  | 'CustomerQrOrdering'
  | 'CustomerServiceRequests'
  | 'Tips'
  | 'SplitBilling'
  | 'OnlinePayments'
  | 'KitchenDisplay'
  | 'BarDisplay'
  | 'Reservations'
  | 'KioskMode';

export interface FeatureFlagItemContract {
  key: string;
  isEnabled: boolean;
  source: 'CatalogDefault' | 'TenantDefault' | 'BranchOverride';
}

export interface TenantFeatureFlagsContract {
  tenantId: string;
  flags: FeatureFlagItemContract[];
  concurrencyToken: string;
  updatedAtUtc?: string | null;
}

export interface BranchFeatureFlagsContract {
  tenantId: string;
  branchId: string;
  overrides: FeatureFlagItemContract[];
  concurrencyToken: string;
  updatedAtUtc?: string | null;
}

export interface EffectiveFeatureFlagsContract {
  tenantId: string;
  branchId: string;
  flags: FeatureFlagItemContract[];
  evaluatedFlags: Record<string, boolean>;
}

export interface UpdateFeatureFlagsRequest {
  flags: Record<string, boolean>;
  concurrencyToken?: string | null;
}
