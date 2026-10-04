using RestaurantOrder.Domain.Branches;
using RestaurantOrder.Domain.Brands;
using RestaurantOrder.Domain.Common;
using RestaurantOrder.Domain.FeatureFlags;
using RestaurantOrder.Domain.Tenants;
using Xunit;

namespace RestaurantOrder.UnitTests.RestaurantConfig;

public class DiningAreasAndStationsUnitTests
{
    private readonly TenantId _tenantId = TenantId.New();
    private readonly BranchId _branchId = BranchId.New();

    [Fact]
    public void DiningArea_Create_ValidInputs_InitializesCorrectly()
    {
        var area = DiningArea.Create(
            _tenantId,
            _branchId,
            "Teras Bölümü",
            "teras-1",
            DiningAreaType.Terrace,
            sortOrder: 2);

        Assert.Equal(_tenantId, area.TenantId);
        Assert.Equal(_branchId, area.BranchId);
        Assert.Equal("Teras Bölümü", area.Name);
        Assert.Equal("teras-1", area.Code);
        Assert.Equal(DiningAreaType.Terrace, area.AreaType);
        Assert.Equal(2, area.SortOrder);
        Assert.True(area.IsActive);
        Assert.NotEqual(Guid.Empty, area.ConcurrencyToken);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null)]
    public void DiningArea_Create_InvalidName_ThrowsDomainException(string? invalidName)
    {
        var ex = Assert.Throws<DomainException>(() =>
            DiningArea.Create(_tenantId, _branchId, invalidName!, "indoor", DiningAreaType.Indoor));

        Assert.Contains("Dining area name is required", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null)]
    [InlineData("invalid space")]
    [InlineData("teras@123")]
    public void DiningArea_Create_InvalidCode_ThrowsDomainException(string? invalidCode)
    {
        var ex = Assert.Throws<DomainException>(() =>
            DiningArea.Create(_tenantId, _branchId, "Teras", invalidCode!, DiningAreaType.Terrace));

        Assert.NotNull(ex);
    }

    [Fact]
    public void DiningArea_ActivateDeactivate_Idempotent()
    {
        var area = DiningArea.Create(_tenantId, _branchId, "Bahçe", "bahce", DiningAreaType.Garden);
        Assert.True(area.IsActive);

        // Deactivate
        var token1 = area.ConcurrencyToken;
        area.Deactivate();
        Assert.False(area.IsActive);
        Assert.NotEqual(token1, area.ConcurrencyToken);

        // Deactivate again (idempotent, token shouldn't change)
        var token2 = area.ConcurrencyToken;
        area.Deactivate();
        Assert.False(area.IsActive);
        Assert.Equal(token2, area.ConcurrencyToken);

        // Activate
        area.Activate();
        Assert.True(area.IsActive);
        Assert.NotEqual(token2, area.ConcurrencyToken);

        // Activate again (idempotent)
        var token3 = area.ConcurrencyToken;
        area.Activate();
        Assert.True(area.IsActive);
        Assert.Equal(token3, area.ConcurrencyToken);
    }

    [Fact]
    public void DiningArea_Update_ValidInputs_Succeeds()
    {
        var area = DiningArea.Create(_tenantId, _branchId, "Bahçe", "bahce", DiningAreaType.Garden);
        var initialToken = area.ConcurrencyToken;

        area.Update("Yeni Bahçe", DiningAreaType.Terrace);

        Assert.Equal("Yeni Bahçe", area.Name);
        Assert.Equal(DiningAreaType.Terrace, area.AreaType);
        Assert.NotEqual(initialToken, area.ConcurrencyToken);
    }

    [Fact]
    public void PreparationStation_Create_ValidInputs_InitializesCorrectly()
    {
        var station = PreparationStation.Create(
            _tenantId,
            _branchId,
            "kitchen-main",
            "Ana Mutfak",
            PreparationStationType.Kitchen,
            sortOrder: 1);

        Assert.Equal(_tenantId, station.TenantId);
        Assert.Equal(_branchId, station.BranchId);
        Assert.Equal("kitchen-main", station.Code); // Normalized to lowercase
        Assert.Equal("Ana Mutfak", station.DisplayName);
        Assert.Equal(PreparationStationType.Kitchen, station.StationType);
        Assert.Equal(1, station.SortOrder);
        Assert.True(station.IsActive);
        Assert.NotEqual(Guid.Empty, station.ConcurrencyToken);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null)]
    public void PreparationStation_Create_InvalidDisplayName_ThrowsDomainException(string? invalidName)
    {
        var ex = Assert.Throws<DomainException>(() =>
            PreparationStation.Create(_tenantId, _branchId, "kitchen", invalidName!, PreparationStationType.Kitchen));

        Assert.Contains("Preparation station display name is required", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null)]
    [InlineData("KITCHEN MAIN")] // Space not allowed
    [InlineData("BAR#1")]
    public void PreparationStation_Create_InvalidCode_ThrowsDomainException(string? invalidCode)
    {
        var ex = Assert.Throws<DomainException>(() =>
            PreparationStation.Create(_tenantId, _branchId, invalidCode!, "Mutfak", PreparationStationType.Kitchen));

        Assert.NotNull(ex);
    }

    [Fact]
    public void PreparationStation_ActivateDeactivate_Idempotent()
    {
        var station = PreparationStation.Create(_tenantId, _branchId, "BAR-1", "Ana Bar", PreparationStationType.Bar);
        Assert.True(station.IsActive);

        var token1 = station.ConcurrencyToken;
        station.Deactivate();
        Assert.False(station.IsActive);
        Assert.NotEqual(token1, station.ConcurrencyToken);

        var token2 = station.ConcurrencyToken;
        station.Deactivate();
        Assert.False(station.IsActive);
        Assert.Equal(token2, station.ConcurrencyToken);

        station.Activate();
        Assert.True(station.IsActive);
        Assert.NotEqual(token2, station.ConcurrencyToken);
    }

    [Fact]
    public void FeatureFlagKey_SystemDefaults_UnimplementedFeaturesDefaultToFalse()
    {
        // Unimplemented features must be disabled by default
        Assert.False(FeatureFlagKey.SystemDefaults[FeatureFlagKey.Tips]);
        Assert.False(FeatureFlagKey.SystemDefaults[FeatureFlagKey.SplitBilling]);
        Assert.False(FeatureFlagKey.SystemDefaults[FeatureFlagKey.OnlinePayments]);
        Assert.False(FeatureFlagKey.SystemDefaults[FeatureFlagKey.Reservations]);
        Assert.False(FeatureFlagKey.SystemDefaults[FeatureFlagKey.KioskMode]);

        // Core MVP features default to true
        Assert.True(FeatureFlagKey.SystemDefaults[FeatureFlagKey.CustomerQrOrdering]);
        Assert.True(FeatureFlagKey.SystemDefaults[FeatureFlagKey.CustomerServiceRequests]);
        Assert.True(FeatureFlagKey.SystemDefaults[FeatureFlagKey.KitchenDisplay]);
        Assert.True(FeatureFlagKey.SystemDefaults[FeatureFlagKey.BarDisplay]);
    }

    [Fact]
    public void FeatureFlagKey_UnknownKey_ThrowsDomainException()
    {
        var ex = Assert.Throws<DomainException>(() =>
            FeatureFlagKey.ValidateKey("UnknownExperimentalFeature"));

        Assert.Contains("Unrecognized feature flag key", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void FeatureFlagKey_IsKnown_RecognizesCatalogKeys()
    {
        Assert.True(FeatureFlagKey.IsKnown(FeatureFlagKey.Tips));
        Assert.True(FeatureFlagKey.IsKnown(FeatureFlagKey.CustomerQrOrdering));
        Assert.False(FeatureFlagKey.IsKnown("BypassAuth"));
    }

    [Fact]
    public void TenantFeatureFlags_Create_ValidatesAndInitializes()
    {
        var flags = TenantFeatureFlags.Create(_tenantId, new Dictionary<string, bool>
        {
            [FeatureFlagKey.Tips.ToString()] = true,
            [FeatureFlagKey.CustomerQrOrdering.ToString()] = false
        });

        Assert.Equal(_tenantId, flags.TenantId);
        Assert.True(flags.GetFlags()[FeatureFlagKey.Tips.ToString()]);
        Assert.False(flags.GetFlags()[FeatureFlagKey.CustomerQrOrdering.ToString()]);
        Assert.NotEqual(Guid.Empty, flags.ConcurrencyToken);
    }

    [Fact]
    public void TenantFeatureFlags_Create_UnknownKey_ThrowsDomainException()
    {
        var ex = Assert.Throws<DomainException>(() =>
            TenantFeatureFlags.Create(_tenantId, new Dictionary<string, bool>
            {
                ["ArbitraryFeature"] = true
            }));

        Assert.Contains("Unrecognized feature flag key", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void BranchFeatureFlags_Override_EvaluatesCorrectly()
    {
        var branchFlags = BranchFeatureFlags.Create(_tenantId, _branchId, new Dictionary<string, bool>
        {
            [FeatureFlagKey.Tips.ToString()] = true
        });

        Assert.Equal(_branchId, branchFlags.BranchId);
        var overrides = branchFlags.GetOverrides();
        Assert.True(overrides[FeatureFlagKey.Tips.ToString()]);
    }

    [Fact]
    public void Branch_ClosedState_PreventsModifications()
    {
        var brandId = BrandId.New();
        var branch = Branch.Create(_tenantId, brandId, "Kadıköy", "kadikoy");
        branch.Close();

        var ex = Assert.Throws<DomainException>(() => branch.EnsureNotClosed());
        Assert.Contains("Modifications are not allowed on a closed branch", ex.Message);
    }
}
