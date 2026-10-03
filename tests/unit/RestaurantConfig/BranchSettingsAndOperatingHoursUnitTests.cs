using RestaurantOrder.Domain.Branches;
using RestaurantOrder.Domain.Common;
using RestaurantOrder.Domain.Tenants;
using Xunit;

namespace RestaurantOrder.UnitTests.RestaurantConfig;

public class BranchSettingsAndOperatingHoursUnitTests
{
    private readonly TenantId _tenantId = TenantId.New();
    private readonly BranchId _branchId = BranchId.New();

    [Fact]
    public void BasisPointsRate_FromTaxRateBps_ValidBounds_Succeeds()
    {
        var zero = BasisPointsRate.FromTaxRateBps(0);
        Assert.Equal(0, zero.Value);
        Assert.Equal(0.00m, zero.AsPercentage);

        var tenPercent = BasisPointsRate.FromTaxRateBps(1000);
        Assert.Equal(1000, tenPercent.Value);
        Assert.Equal(0.10m, tenPercent.AsDecimal);
        Assert.Equal(10.00m, tenPercent.AsPercentage);
        Assert.Equal("10.00%", tenPercent.ToString());

        var max = BasisPointsRate.FromTaxRateBps(10000);
        Assert.Equal(10000, max.Value);
        Assert.Equal(100.00m, max.AsPercentage);
    }

    [Fact]
    public void BasisPointsRate_FromTaxRateBps_NegativeRate_ThrowsDomainException()
    {
        var ex = Assert.Throws<DomainException>(() => BasisPointsRate.FromTaxRateBps(-1));
        Assert.Contains("cannot be negative", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void BasisPointsRate_FromTaxRateBps_ExcessiveRate_ThrowsDomainException()
    {
        var ex = Assert.Throws<DomainException>(() => BasisPointsRate.FromTaxRateBps(10001));
        Assert.Contains("cannot exceed 100%", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void BasisPointsRate_FromServiceChargeRateBps_WhenDisabledAndNonZero_ThrowsDomainException()
    {
        var ex = Assert.Throws<DomainException>(() =>
            BasisPointsRate.FromServiceChargeRateBps(500, isServiceChargeEnabled: false));
        Assert.Contains("must be 0 when service charge is disabled", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void BasisPointsRate_FromServiceChargeRateBps_WhenEnabled_ExcessiveRate_ThrowsDomainException()
    {
        var ex = Assert.Throws<DomainException>(() =>
            BasisPointsRate.FromServiceChargeRateBps(5001, isServiceChargeEnabled: true));
        Assert.Contains("cannot exceed 50%", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void BasisPointsRate_FromServiceChargeRateBps_WhenEnabled_NegativeRate_ThrowsDomainException()
    {
        var ex = Assert.Throws<DomainException>(() =>
            BasisPointsRate.FromServiceChargeRateBps(-50, isServiceChargeEnabled: true));
        Assert.Contains("cannot be negative", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void SupportedLocales_Validation_DuplicateLocales_ThrowsDomainException()
    {
        var ex = Assert.Throws<DomainException>(() =>
            SupportedLocales.Validate("tr-TR", new[] { "tr-TR", "en-US", "tr-TR" }));
        Assert.Contains("Duplicate locale", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void SupportedLocales_Validation_DefaultLocaleMissingFromSupported_ThrowsDomainException()
    {
        var ex = Assert.Throws<DomainException>(() =>
            SupportedLocales.Validate("en-US", new[] { "tr-TR", "de-DE" }));
        Assert.Contains("must be present in the supported locales list", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void SupportedLocales_Validation_InvalidLocaleFormat_ThrowsDomainException()
    {
        var ex = Assert.Throws<DomainException>(() =>
            SupportedLocales.Validate("invalid-locale-format", new[] { "invalid-locale-format" }));
        Assert.Contains("not a valid BCP 47 language tag", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void TimeSlot_ZeroDuration_ThrowsDomainException()
    {
        var time = new TimeOnly(10, 0);
        var ex = Assert.Throws<DomainException>(() => new TimeSlot(time, time));
        Assert.Contains("cannot be identical", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void TimeSlot_OvernightDetection_IdentifiesCorrectly()
    {
        var daySlot = new TimeSlot(new TimeOnly(9, 0), new TimeOnly(18, 0));
        Assert.False(daySlot.IsOvernight);

        var nightSlot = new TimeSlot(new TimeOnly(20, 0), new TimeOnly(2, 0));
        Assert.True(nightSlot.IsOvernight);
    }

    [Fact]
    public void TimeSlot_TouchingBoundaries_DoNotOverlap()
    {
        var slot1 = new TimeSlot(new TimeOnly(8, 0), new TimeOnly(14, 0));
        var slot2 = new TimeSlot(new TimeOnly(14, 0), new TimeOnly(22, 0));

        Assert.False(slot1.OverlapsWith(slot2));
        Assert.False(slot2.OverlapsWith(slot1));
    }

    [Fact]
    public void TimeSlot_OverlappingIntervals_DetectedCorrectly()
    {
        var slot1 = new TimeSlot(new TimeOnly(8, 0), new TimeOnly(14, 0));
        var slot2 = new TimeSlot(new TimeOnly(13, 30), new TimeOnly(20, 0));

        Assert.True(slot1.OverlapsWith(slot2));
        Assert.True(slot2.OverlapsWith(slot1));
    }

    [Fact]
    public void OperatingDaySchedule_ClosedDayWithTimeSlots_ThrowsDomainException()
    {
        var slot = new TimeSlot(new TimeOnly(9, 0), new TimeOnly(18, 0));
        var ex = Assert.Throws<DomainException>(() =>
            new OperatingDaySchedule(DayOfWeek.Monday, isClosed: true, new[] { slot }));

        Assert.Contains("Cannot add time slots to closed day", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void OperatingDaySchedule_IntraDayOverlappingSlots_ThrowsDomainException()
    {
        var slot1 = new TimeSlot(new TimeOnly(9, 0), new TimeOnly(14, 0));
        var slot2 = new TimeSlot(new TimeOnly(12, 0), new TimeOnly(18, 0));

        var ex = Assert.Throws<DomainException>(() =>
            new OperatingDaySchedule(DayOfWeek.Monday, isClosed: false, new[] { slot1, slot2 }));

        Assert.Contains("Overlapping time slots detected", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void WeeklySchedule_CrossDayOvernightSpillover_Conflict_ThrowsDomainException()
    {
        // Sunday has overnight 20:00 - 02:00 (spills into Monday 00:00 - 02:00)
        // Monday has slot 01:00 - 12:00 -> conflicts between 01:00 and 02:00!
        var sundaySlot = new TimeSlot(new TimeOnly(20, 0), new TimeOnly(2, 0));
        var mondaySlot = new TimeSlot(new TimeOnly(1, 0), new TimeOnly(12, 0));
        var regularSlot = new TimeSlot(new TimeOnly(9, 0), new TimeOnly(18, 0));

        var days = new List<OperatingDaySchedule>
        {
            OperatingDaySchedule.Open(DayOfWeek.Sunday, sundaySlot),
            OperatingDaySchedule.Open(DayOfWeek.Monday, mondaySlot),
            OperatingDaySchedule.Open(DayOfWeek.Tuesday, regularSlot),
            OperatingDaySchedule.Open(DayOfWeek.Wednesday, regularSlot),
            OperatingDaySchedule.Open(DayOfWeek.Thursday, regularSlot),
            OperatingDaySchedule.Open(DayOfWeek.Friday, regularSlot),
            OperatingDaySchedule.Open(DayOfWeek.Saturday, regularSlot)
        };

        var ex = Assert.Throws<DomainException>(() => new WeeklySchedule(days));
        Assert.Contains("spills over", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void WeeklySchedule_CrossDayOvernightSpillover_TouchingBoundary_Succeeds()
    {
        // Sunday has overnight 20:00 - 02:00 (spills into Monday 00:00 - 02:00)
        // Monday starts exactly at 02:00 (touching boundary) -> allowed!
        var sundaySlot = new TimeSlot(new TimeOnly(20, 0), new TimeOnly(2, 0));
        var mondaySlot = new TimeSlot(new TimeOnly(2, 0), new TimeOnly(12, 0));
        var regularSlot = new TimeSlot(new TimeOnly(9, 0), new TimeOnly(18, 0));

        var days = new List<OperatingDaySchedule>
        {
            OperatingDaySchedule.Open(DayOfWeek.Sunday, sundaySlot),
            OperatingDaySchedule.Open(DayOfWeek.Monday, mondaySlot),
            OperatingDaySchedule.Open(DayOfWeek.Tuesday, regularSlot),
            OperatingDaySchedule.Open(DayOfWeek.Wednesday, regularSlot),
            OperatingDaySchedule.Open(DayOfWeek.Thursday, regularSlot),
            OperatingDaySchedule.Open(DayOfWeek.Friday, regularSlot),
            OperatingDaySchedule.Open(DayOfWeek.Saturday, regularSlot)
        };

        var schedule = new WeeklySchedule(days);
        Assert.NotNull(schedule);
    }

    [Fact]
    public void BranchSettings_CreateAndUpdate_EnforcesInvariantsAndRotatesConcurrencyToken()
    {
        var settings = BranchSettings.Create(
            tenantId: _tenantId,
            branchId: _branchId,
            timezone: "Europe/Istanbul",
            currency: "TRY",
            defaultLocale: "tr-TR",
            supportedLocales: new[] { "tr-TR", "en-US" },
            pricesIncludeTax: true,
            defaultTaxRateBps: 2000,
            isServiceChargeEnabled: true,
            serviceChargeRateBps: 1000,
            isOrderTakingEnabled: true,
            displayName: "Kadıköy Şubesi");

        Assert.Equal(2000, settings.DefaultTaxRateBps);
        Assert.Equal(1000, settings.ServiceChargeRateBps);
        Assert.True(settings.IsOrderTakingEnabled);
        Assert.True(settings.PricesIncludeTax);

        var initialToken = settings.ConcurrencyToken;

        settings.UpdateDetails(
            timezone: "Europe/London",
            currency: "GBP",
            defaultLocale: "en-US",
            supportedLocales: new[] { "en-US", "tr-TR" },
            pricesIncludeTax: false,
            defaultTaxRateBps: 1500,
            isServiceChargeEnabled: false,
            serviceChargeRateBps: 0,
            isOrderTakingEnabled: false,
            displayName: "Updated Name");

        Assert.NotEqual(initialToken, settings.ConcurrencyToken);
        Assert.Equal("GBP", settings.Currency.Code);
        Assert.Equal("Europe/London", settings.Timezone.Id);
        Assert.Equal(1500, settings.DefaultTaxRateBps);
        Assert.False(settings.PricesIncludeTax);
        Assert.False(settings.IsServiceChargeEnabled);
        Assert.Equal(0, settings.ServiceChargeRateBps);
        Assert.False(settings.IsOrderTakingEnabled);
    }
}
