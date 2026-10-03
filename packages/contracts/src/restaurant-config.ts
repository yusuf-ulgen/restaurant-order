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
