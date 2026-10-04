using Microsoft.EntityFrameworkCore;
using RestaurantOrder.Application.Auth;
using RestaurantOrder.Application.RestaurantConfig;
using RestaurantOrder.Domain.Auth;
using RestaurantOrder.Domain.Branches;
using RestaurantOrder.Domain.Common;
using RestaurantOrder.Domain.Tenants;

namespace RestaurantOrder.Infrastructure.RestaurantConfig;

public partial class RestaurantConfigService
{
    public async Task<EffectiveBranchSettingsDto> GetEffectiveBranchSettingsAsync(
        TenantId tenantId,
        BranchId branchId,
        AuthenticatedPrincipal actor,
        CancellationToken ct = default)
    {
        EnsureBranchAccess(tenantId, branchId, actor);

        var branch = await _dbContext.Branches
            .AsNoTracking()
            .FirstOrDefaultAsync(br => br.TenantId == tenantId && br.Id == branchId, ct)
            ?? throw new ResourceNotFoundException($"Branch with ID '{branchId.Value}' was not found.");

        var settings = await _dbContext.BranchSettings
            .AsNoTracking()
            .FirstOrDefaultAsync(bs => bs.TenantId == tenantId && bs.BranchId == branchId, ct);

        if (settings != null)
        {
            return new EffectiveBranchSettingsDto(
                BranchId: branch.Id.Value,
                BranchName: branch.Name,
                Timezone: settings.Timezone.Id,
                Currency: settings.Currency.Code,
                DefaultLocale: settings.DefaultLocale,
                SupportedLocales: SupportedLocales.Deserialize(settings.SupportedLocalesJson),
                PricesIncludeTax: settings.PricesIncludeTax,
                DefaultTaxRateBps: settings.DefaultTaxRateBps,
                IsServiceChargeEnabled: settings.IsServiceChargeEnabled,
                ServiceChargeRateBps: settings.ServiceChargeRateBps,
                IsOrderTakingEnabled: settings.IsOrderTakingEnabled,
                DisplayName: settings.DisplayName,
                PhoneNumber: settings.PhoneNumber,
                Email: settings.Email,
                Address: settings.Address,
                HasCustomSettings: true,
                ConcurrencyToken: settings.ConcurrencyToken);
        }

        return new EffectiveBranchSettingsDto(
            BranchId: branch.Id.Value,
            BranchName: branch.Name,
            Timezone: branch.Timezone.Id,
            Currency: branch.Currency.Code,
            DefaultLocale: SupportedLocales.DefaultLocale,
            SupportedLocales: new[] { SupportedLocales.DefaultLocale },
            PricesIncludeTax: true,
            DefaultTaxRateBps: BasisPointsRate.DefaultTaxRateBps,
            IsServiceChargeEnabled: false,
            ServiceChargeRateBps: 0,
            IsOrderTakingEnabled: true,
            DisplayName: branch.Name,
            PhoneNumber: null,
            Email: null,
            Address: null,
            HasCustomSettings: false,
            ConcurrencyToken: branch.ConcurrencyToken);
    }

    public async Task<BranchSettingsDto> UpdateBranchSettingsAsync(
        TenantId tenantId,
        BranchId branchId,
        UpdateBranchSettingsCommand command,
        AuthenticatedPrincipal actor,
        CancellationToken ct = default)
    {
        EnsureBranchAccess(tenantId, branchId, actor);

        var branch = await _dbContext.Branches
            .FirstOrDefaultAsync(br => br.TenantId == tenantId && br.Id == branchId, ct)
            ?? throw new ResourceNotFoundException($"Branch with ID '{branchId.Value}' was not found.");

        if (branch.Status == BranchStatus.Closed)
        {
            throw new DomainException("Modifications are not allowed on a closed branch.");
        }

        var settings = await _dbContext.BranchSettings
            .FirstOrDefaultAsync(bs => bs.TenantId == tenantId && bs.BranchId == branchId, ct);

        if (settings == null)
        {
            VerifyConcurrencyToken(branch.ConcurrencyToken, command.ConcurrencyToken);

            settings = BranchSettings.Create(
                tenantId: tenantId,
                branchId: branchId,
                timezone: command.Timezone,
                currency: command.Currency,
                defaultLocale: command.DefaultLocale,
                supportedLocales: command.SupportedLocales,
                pricesIncludeTax: command.PricesIncludeTax,
                defaultTaxRateBps: command.DefaultTaxRateBps,
                isServiceChargeEnabled: command.IsServiceChargeEnabled,
                serviceChargeRateBps: command.ServiceChargeRateBps,
                isOrderTakingEnabled: command.IsOrderTakingEnabled,
                displayName: command.DisplayName,
                phoneNumber: command.PhoneNumber,
                email: command.Email,
                address: command.Address);

            _dbContext.BranchSettings.Add(settings);
        }
        else
        {
            VerifyConcurrencyToken(settings.ConcurrencyToken, command.ConcurrencyToken);

            settings.UpdateDetails(
                timezone: command.Timezone,
                currency: command.Currency,
                defaultLocale: command.DefaultLocale,
                supportedLocales: command.SupportedLocales,
                pricesIncludeTax: command.PricesIncludeTax,
                defaultTaxRateBps: command.DefaultTaxRateBps,
                isServiceChargeEnabled: command.IsServiceChargeEnabled,
                serviceChargeRateBps: command.ServiceChargeRateBps,
                isOrderTakingEnabled: command.IsOrderTakingEnabled,
                displayName: command.DisplayName,
                phoneNumber: command.PhoneNumber,
                email: command.Email,
                address: command.Address);
        }

        // Keep branch timezone and currency in sync
        branch.UpdateDetails(branch.Name, command.Timezone, command.Currency);

        AddAuditEvent(
            tenantId: tenantId,
            eventType: SecurityAuditEventType.BranchSettingsUpdated,
            actor: actor,
            branchId: branchId,
            details: new
            {
                BranchId = branchId.Value,
                Timezone = command.Timezone,
                Currency = command.Currency,
                PricesIncludeTax = command.PricesIncludeTax,
                DefaultTaxRateBps = command.DefaultTaxRateBps,
                IsServiceChargeEnabled = command.IsServiceChargeEnabled,
                ServiceChargeRateBps = command.ServiceChargeRateBps,
                IsOrderTakingEnabled = command.IsOrderTakingEnabled
            });

        await _dbContext.SaveChangesAsync(ct);
        return MapBranchSettings(settings);
    }

    public async Task<BranchOperatingHoursDto> GetBranchOperatingHoursAsync(
        TenantId tenantId,
        BranchId branchId,
        AuthenticatedPrincipal actor,
        CancellationToken ct = default)
    {
        EnsureBranchAccess(tenantId, branchId, actor);

        var branch = await _dbContext.Branches
            .AsNoTracking()
            .FirstOrDefaultAsync(br => br.TenantId == tenantId && br.Id == branchId, ct)
            ?? throw new ResourceNotFoundException($"Branch with ID '{branchId.Value}' was not found.");

        var hours = await _dbContext.BranchOperatingHours
            .AsNoTracking()
            .FirstOrDefaultAsync(boh => boh.TenantId == tenantId && boh.BranchId == branchId, ct);

        var schedule = hours != null ? hours.GetSchedule() : WeeklySchedule.CreateDefault();
        var token = hours?.ConcurrencyToken ?? branch.ConcurrencyToken;

        return MapBranchOperatingHours(branchId.Value, schedule, token, hours?.UpdatedAtUtc);
    }

    public async Task<BranchOperatingHoursDto> UpdateBranchOperatingHoursAsync(
        TenantId tenantId,
        BranchId branchId,
        UpdateBranchOperatingHoursCommand command,
        AuthenticatedPrincipal actor,
        CancellationToken ct = default)
    {
        EnsureBranchAccess(tenantId, branchId, actor);

        var branch = await _dbContext.Branches
            .FirstOrDefaultAsync(br => br.TenantId == tenantId && br.Id == branchId, ct)
            ?? throw new ResourceNotFoundException($"Branch with ID '{branchId.Value}' was not found.");

        if (branch.Status == BranchStatus.Closed)
        {
            throw new DomainException("Modifications are not allowed on a closed branch.");
        }

        var daySchedules = new List<OperatingDaySchedule>();
        foreach (var dayDto in command.Days ?? Enumerable.Empty<OperatingDayScheduleDto>())
        {
            var dow = (DayOfWeek)dayDto.DayOfWeek;
            if (dayDto.IsClosed)
            {
                var slots = (dayDto.Slots ?? Enumerable.Empty<TimeSlotDto>())
                    .Select(s => TimeSlot.FromStrings(s.OpenTime, s.CloseTime));
                daySchedules.Add(new OperatingDaySchedule(dow, isClosed: true, slots));
            }
            else
            {
                var slots = (dayDto.Slots ?? Enumerable.Empty<TimeSlotDto>())
                    .Select(s => TimeSlot.FromStrings(s.OpenTime, s.CloseTime));
                daySchedules.Add(new OperatingDaySchedule(dow, isClosed: false, slots));
            }
        }

        var weeklySchedule = new WeeklySchedule(daySchedules);

        var hours = await _dbContext.BranchOperatingHours
            .FirstOrDefaultAsync(boh => boh.TenantId == tenantId && boh.BranchId == branchId, ct);

        if (hours == null)
        {
            VerifyConcurrencyToken(branch.ConcurrencyToken, command.ConcurrencyToken);
            hours = BranchOperatingHours.Create(tenantId, branchId, weeklySchedule);
            _dbContext.BranchOperatingHours.Add(hours);
        }
        else
        {
            VerifyConcurrencyToken(hours.ConcurrencyToken, command.ConcurrencyToken);
            hours.SetSchedule(weeklySchedule);
        }

        AddAuditEvent(
            tenantId: tenantId,
            eventType: SecurityAuditEventType.BranchOperatingHoursUpdated,
            actor: actor,
            branchId: branchId,
            details: new
            {
                BranchId = branchId.Value,
                DaysCount = daySchedules.Count
            });

        await _dbContext.SaveChangesAsync(ct);
        return MapBranchOperatingHours(branchId.Value, weeklySchedule, hours.ConcurrencyToken, hours.UpdatedAtUtc);
    }

    private static BranchSettingsDto MapBranchSettings(BranchSettings settings) =>
        new(
            Id: settings.Id.Value,
            TenantId: settings.TenantId.Value,
            BranchId: settings.BranchId.Value,
            Timezone: settings.Timezone.Id,
            Currency: settings.Currency.Code,
            DefaultLocale: settings.DefaultLocale,
            SupportedLocales: SupportedLocales.Deserialize(settings.SupportedLocalesJson),
            PricesIncludeTax: settings.PricesIncludeTax,
            DefaultTaxRateBps: settings.DefaultTaxRateBps,
            IsServiceChargeEnabled: settings.IsServiceChargeEnabled,
            ServiceChargeRateBps: settings.ServiceChargeRateBps,
            IsOrderTakingEnabled: settings.IsOrderTakingEnabled,
            DisplayName: settings.DisplayName,
            PhoneNumber: settings.PhoneNumber,
            Email: settings.Email,
            Address: settings.Address,
            ConcurrencyToken: settings.ConcurrencyToken,
            CreatedAtUtc: settings.CreatedAtUtc,
            UpdatedAtUtc: settings.UpdatedAtUtc);

    private static BranchOperatingHoursDto MapBranchOperatingHours(
        Guid branchId,
        WeeklySchedule schedule,
        Guid concurrencyToken,
        DateTime? updatedAtUtc)
    {
        var days = schedule.Days.Select(d => new OperatingDayScheduleDto(
            DayOfWeek: (int)d.DayOfWeek,
            IsClosed: d.IsClosed,
            Slots: d.TimeSlots.Select(s => new TimeSlotDto(
                OpenTime: s.OpenTime.ToString("HH:mm"),
                CloseTime: s.CloseTime.ToString("HH:mm"),
                IsOvernight: s.IsOvernight
            )).ToList()
        )).ToList();

        return new BranchOperatingHoursDto(branchId, days, concurrencyToken, updatedAtUtc);
    }
}
