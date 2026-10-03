using Microsoft.EntityFrameworkCore;
using RestaurantOrder.Application.Auth;
using RestaurantOrder.Application.RestaurantConfig;
using RestaurantOrder.Domain.Auth;
using RestaurantOrder.Domain.Branches;
using RestaurantOrder.Domain.FeatureFlags;
using RestaurantOrder.Domain.Tenants;

namespace RestaurantOrder.Infrastructure.RestaurantConfig;

public partial class RestaurantConfigService
{
    // ==========================================
    // Feature Flags Operations
    // ==========================================

    public async Task<TenantFeatureFlagsDto> GetTenantFeatureFlagsAsync(
        TenantId tenantId,
        AuthenticatedPrincipal actor,
        CancellationToken ct = default)
    {
        EnsureTenantAccess(tenantId, actor);

        var tenantFlags = await _dbContext.TenantFeatureFlags
            .FirstOrDefaultAsync(tf => tf.TenantId == tenantId, ct);

        var customFlags = tenantFlags?.GetFlags() ?? new Dictionary<string, bool>();
        var items = FeatureFlagKey.All.Select(k => new FeatureFlagItemDto(
            k,
            customFlags.TryGetValue(k, out var val) ? val : FeatureFlagKey.SystemDefaults[k],
            customFlags.ContainsKey(k) ? "Tenant" : "Default"
        )).ToList();

        return new TenantFeatureFlagsDto(
            tenantId.Value,
            items,
            tenantFlags?.ConcurrencyToken ?? Guid.Empty,
            tenantFlags?.UpdatedAtUtc);
    }

    public async Task<TenantFeatureFlagsDto> UpdateTenantFeatureFlagsAsync(
        TenantId tenantId,
        UpdateFeatureFlagsCommand command,
        AuthenticatedPrincipal actor,
        CancellationToken ct = default)
    {
        EnsureTenantAccess(tenantId, actor);
        if (actor.Role != AuthRole.RestaurantAdmin)
        {
            throw new InvalidAuthorizationScopeException("Only RestaurantAdmin can modify tenant-wide feature flag defaults.");
        }

        var tenantFlags = await _dbContext.TenantFeatureFlags
            .FirstOrDefaultAsync(tf => tf.TenantId == tenantId, ct);

        if (tenantFlags == null)
        {
            tenantFlags = TenantFeatureFlags.Create(tenantId, command.Flags.ToDictionary(k => k.Key, v => v.Value));
            _dbContext.TenantFeatureFlags.Add(tenantFlags);
        }
        else
        {
            VerifyConcurrencyToken(tenantFlags.ConcurrencyToken, command.ConcurrencyToken);
            tenantFlags.Update(command.Flags.ToDictionary(k => k.Key, v => v.Value));
        }

        AddAuditEvent(tenantId, SecurityAuditEventType.TenantFeatureFlagsUpdated, actor, null,
            new { UpdatedKeys = command.Flags.Keys.ToArray() });

        await _dbContext.SaveChangesAsync(ct);
        return await GetTenantFeatureFlagsAsync(tenantId, actor, ct);
    }

    public async Task<BranchFeatureFlagsDto> GetBranchFeatureFlagsOverrideAsync(
        TenantId tenantId,
        BranchId branchId,
        AuthenticatedPrincipal actor,
        CancellationToken ct = default)
    {
        EnsureBranchAccess(tenantId, branchId, actor);

        var branchFlags = await _dbContext.BranchFeatureFlags
            .FirstOrDefaultAsync(bf => bf.TenantId == tenantId && bf.BranchId == branchId, ct);

        var overrides = branchFlags?.GetOverrides() ?? new Dictionary<string, bool>();
        var items = overrides.Select(kvp => new FeatureFlagItemDto(
            kvp.Key,
            kvp.Value,
            "BranchOverride"
        )).ToList();

        return new BranchFeatureFlagsDto(
            tenantId.Value,
            branchId.Value,
            items,
            branchFlags?.ConcurrencyToken ?? Guid.Empty,
            branchFlags?.UpdatedAtUtc);
    }

    public async Task<BranchFeatureFlagsDto> UpdateBranchFeatureFlagsOverrideAsync(
        TenantId tenantId,
        BranchId branchId,
        UpdateFeatureFlagsCommand command,
        AuthenticatedPrincipal actor,
        CancellationToken ct = default)
    {
        var branch = await GetBranchWithAccessCheckAsync(tenantId, branchId, actor, ct);
        branch.EnsureNotClosed();

        var branchFlags = await _dbContext.BranchFeatureFlags
            .FirstOrDefaultAsync(bf => bf.TenantId == tenantId && bf.BranchId == branchId, ct);

        if (branchFlags == null)
        {
            branchFlags = BranchFeatureFlags.Create(tenantId, branchId, command.Flags.ToDictionary(k => k.Key, v => v.Value));
            _dbContext.BranchFeatureFlags.Add(branchFlags);
        }
        else
        {
            VerifyConcurrencyToken(branchFlags.ConcurrencyToken, command.ConcurrencyToken);
            branchFlags.Update(command.Flags.ToDictionary(k => k.Key, v => v.Value));
        }

        AddAuditEvent(tenantId, SecurityAuditEventType.BranchFeatureFlagsUpdated, actor, branchId,
            new { UpdatedKeys = command.Flags.Keys.ToArray() });

        await _dbContext.SaveChangesAsync(ct);
        return await GetBranchFeatureFlagsOverrideAsync(tenantId, branchId, actor, ct);
    }

    public async Task<EffectiveFeatureFlagsDto> ClearBranchFeatureFlagsOverrideAsync(
        TenantId tenantId,
        BranchId branchId,
        AuthenticatedPrincipal actor,
        CancellationToken ct = default)
    {
        var branch = await GetBranchWithAccessCheckAsync(tenantId, branchId, actor, ct);
        branch.EnsureNotClosed();

        var branchFlags = await _dbContext.BranchFeatureFlags
            .FirstOrDefaultAsync(bf => bf.TenantId == tenantId && bf.BranchId == branchId, ct);

        if (branchFlags != null)
        {
            _dbContext.BranchFeatureFlags.Remove(branchFlags);
            AddAuditEvent(tenantId, SecurityAuditEventType.BranchFeatureFlagsCleared, actor, branchId, new { });
            await _dbContext.SaveChangesAsync(ct);
        }

        return await GetEffectiveFeatureFlagsAsync(tenantId, branchId, actor, ct);
    }

    public async Task<EffectiveFeatureFlagsDto> GetEffectiveFeatureFlagsAsync(
        TenantId tenantId,
        BranchId branchId,
        AuthenticatedPrincipal actor,
        CancellationToken ct = default)
    {
        EnsureBranchAccess(tenantId, branchId, actor);

        var tenantFlags = await _dbContext.TenantFeatureFlags
            .FirstOrDefaultAsync(tf => tf.TenantId == tenantId, ct);
        var branchFlags = await _dbContext.BranchFeatureFlags
            .FirstOrDefaultAsync(bf => bf.TenantId == tenantId && bf.BranchId == branchId, ct);

        var tenantMap = tenantFlags?.GetFlags() ?? new Dictionary<string, bool>();
        var branchMap = branchFlags?.GetOverrides() ?? new Dictionary<string, bool>();

        var evaluated = new Dictionary<string, bool>(StringComparer.Ordinal);
        var items = new List<FeatureFlagItemDto>();

        foreach (var key in FeatureFlagKey.All)
        {
            if (branchMap.TryGetValue(key, out var branchVal))
            {
                evaluated[key] = branchVal;
                items.Add(new FeatureFlagItemDto(key, branchVal, "BranchOverride"));
            }
            else if (tenantMap.TryGetValue(key, out var tenantVal))
            {
                evaluated[key] = tenantVal;
                items.Add(new FeatureFlagItemDto(key, tenantVal, "Tenant"));
            }
            else
            {
                var defaultVal = FeatureFlagKey.SystemDefaults[key];
                evaluated[key] = defaultVal;
                items.Add(new FeatureFlagItemDto(key, defaultVal, "Default"));
            }
        }

        return new EffectiveFeatureFlagsDto(branchId.Value, items, evaluated);
    }
}
