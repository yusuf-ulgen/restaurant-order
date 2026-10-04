namespace RestaurantOrder.Domain.Auth;

/// <summary>
/// Machine-readable security audit event type constants.
/// </summary>
public static class SecurityAuditEventType
{
    public const string LoginSucceeded = "login_succeeded";
    public const string LoginFailed = "login_failed";
    public const string SessionRefreshed = "session_refreshed";
    public const string SessionRevoked = "session_revoked";
    public const string RefreshTokenReuseDetected = "refresh_token_reuse_detected";
    public const string AccountLocked = "account_locked";
    public const string AccountUnlocked = "account_unlocked";
    public const string PasswordChanged = "password_changed";
    public const string PasswordReset = "password_reset";
    public const string RoleAssigned = "role_assigned";
    public const string RoleRemoved = "role_removed";
    public const string PinChanged = "pin_changed";
    public const string PinFailed = "pin_failed";
    public const string PinLocked = "pin_locked";
    public const string TrustedTerminalEnrolled = "trusted_terminal_enrolled";
    public const string TrustedTerminalRevoked = "trusted_terminal_revoked";

    // Restaurant Configuration
    public const string BrandCreated = "brand_created";
    public const string BrandUpdated = "brand_updated";
    public const string BrandActivated = "brand_activated";
    public const string BrandDeactivated = "brand_deactivated";
    public const string BranchCreated = "branch_created";
    public const string BranchUpdated = "branch_updated";
    public const string BranchActivated = "branch_activated";
    public const string BranchSuspended = "branch_suspended";
    public const string BranchClosed = "branch_closed";
    public const string BrandThemeUpdated = "brand_theme_updated";
    public const string BranchThemeOverrideUpdated = "branch_theme_override_updated";
    public const string BranchThemeOverrideCleared = "branch_theme_override_cleared";
    public const string BranchSettingsUpdated = "branch_settings_updated";
    public const string BranchOperatingHoursUpdated = "branch_operating_hours_updated";
    public const string DiningAreaCreated = "dining_area_created";
    public const string DiningAreaUpdated = "dining_area_updated";
    public const string DiningAreaActivated = "dining_area_activated";
    public const string DiningAreaDeactivated = "dining_area_deactivated";
    public const string DiningAreasReordered = "dining_areas_reordered";
    public const string PreparationStationCreated = "preparation_station_created";
    public const string PreparationStationUpdated = "preparation_station_updated";
    public const string PreparationStationActivated = "preparation_station_activated";
    public const string PreparationStationDeactivated = "preparation_station_deactivated";
    public const string PreparationStationsReordered = "preparation_stations_reordered";
    public const string TenantFeatureFlagsUpdated = "tenant_feature_flags_updated";
    public const string BranchFeatureFlagsUpdated = "branch_feature_flags_updated";
    public const string BranchFeatureFlagsCleared = "branch_feature_flags_cleared";

    // Menu & Catalog
    public const string MenuCreated = "menu_created";
    public const string MenuUpdated = "menu_updated";
    public const string MenuActivated = "menu_activated";
    public const string MenuArchived = "menu_archived";
    public const string MenuCategoryCreated = "menu_category_created";
    public const string MenuCategoryUpdated = "menu_category_updated";
    public const string MenuCategoryActivated = "menu_category_activated";
    public const string MenuCategoryDeactivated = "menu_category_deactivated";
    public const string MenuCategoriesReordered = "menu_categories_reordered";
    public const string MenuItemCreated = "menu_item_created";
    public const string MenuItemUpdated = "menu_item_updated";
    public const string MenuItemPriceUpdated = "menu_item_price_updated";
    public const string MenuItemActivated = "menu_item_activated";
    public const string MenuItemDeactivated = "menu_item_deactivated";
    public const string MenuItemsReordered = "menu_items_reordered";
    public const string ItemVariantCreated = "item_variant_created";
    public const string ItemVariantUpdated = "item_variant_updated";
    public const string ItemVariantPriceUpdated = "item_variant_price_updated";
    public const string ItemVariantActivated = "item_variant_activated";
    public const string ItemVariantDeactivated = "item_variant_deactivated";
    public const string ItemVariantsReordered = "item_variants_reordered";
}
