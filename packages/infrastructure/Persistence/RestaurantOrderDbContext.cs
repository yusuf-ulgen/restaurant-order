using System.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using RestaurantOrder.Application.Tenancy;
using RestaurantOrder.Domain.Auth;
using RestaurantOrder.Domain.Branches;
using RestaurantOrder.Domain.Brands;
using RestaurantOrder.Domain.Tenants;

namespace RestaurantOrder.Infrastructure.Persistence;

/// <summary>
/// Primary Entity Framework Core database context for RestaurantOrder.
/// Configured for PostgreSQL with row-level security and tenant isolation support.
/// Includes Global Query Filters as a secondary defense-in-depth layer.
/// </summary>
public class RestaurantOrderDbContext : DbContext
{
    private readonly ITenantContext _tenantContext;

    public DbSet<Tenant> Tenants => Set<Tenant>();
    public DbSet<Brand> Brands => Set<Brand>();
    public DbSet<Branch> Branches => Set<Branch>();
    public DbSet<RestaurantOrder.Domain.Branding.BrandAppearance> BrandAppearances => Set<RestaurantOrder.Domain.Branding.BrandAppearance>();
    public DbSet<RestaurantOrder.Domain.Branding.BranchThemeOverride> BranchThemeOverrides => Set<RestaurantOrder.Domain.Branding.BranchThemeOverride>();
    public DbSet<BranchSettings> BranchSettings => Set<BranchSettings>();
    public DbSet<BranchOperatingHours> BranchOperatingHours => Set<BranchOperatingHours>();
    public DbSet<DiningArea> DiningAreas => Set<DiningArea>();
    public DbSet<PreparationStation> PreparationStations => Set<PreparationStation>();
    public DbSet<RestaurantOrder.Domain.FeatureFlags.TenantFeatureFlags> TenantFeatureFlags => Set<RestaurantOrder.Domain.FeatureFlags.TenantFeatureFlags>();
    public DbSet<RestaurantOrder.Domain.FeatureFlags.BranchFeatureFlags> BranchFeatureFlags => Set<RestaurantOrder.Domain.FeatureFlags.BranchFeatureFlags>();
    public DbSet<RestaurantOrder.Domain.Catalog.Menu> Menus => Set<RestaurantOrder.Domain.Catalog.Menu>();
    public DbSet<RestaurantOrder.Domain.Catalog.MenuCategory> MenuCategories => Set<RestaurantOrder.Domain.Catalog.MenuCategory>();
    public DbSet<RestaurantOrder.Domain.Catalog.MenuItem> MenuItems => Set<RestaurantOrder.Domain.Catalog.MenuItem>();
    public DbSet<RestaurantOrder.Domain.Catalog.ItemVariant> ItemVariants => Set<RestaurantOrder.Domain.Catalog.ItemVariant>();
    public DbSet<RestaurantOrder.Domain.Catalog.ModifierGroup> ModifierGroups => Set<RestaurantOrder.Domain.Catalog.ModifierGroup>();
    public DbSet<RestaurantOrder.Domain.Catalog.ModifierOption> ModifierOptions => Set<RestaurantOrder.Domain.Catalog.ModifierOption>();
    public DbSet<RestaurantOrder.Domain.Catalog.MenuItemModifierGroupAssignment> MenuItemModifierGroupAssignments => Set<RestaurantOrder.Domain.Catalog.MenuItemModifierGroupAssignment>();
    public DbSet<RestaurantOrder.Domain.Catalog.BranchItemAvailability> BranchItemAvailabilities => Set<RestaurantOrder.Domain.Catalog.BranchItemAvailability>();
    public DbSet<RestaurantOrder.Domain.Catalog.CatalogAvailabilityOutboxMessage> CatalogAvailabilityOutbox => Set<RestaurantOrder.Domain.Catalog.CatalogAvailabilityOutboxMessage>();

    // IAM Entities
    public DbSet<User> Users => Set<User>();
    public DbSet<UserMembership> Memberships => Set<UserMembership>();
    public DbSet<AuthSession> Sessions => Set<AuthSession>();
    public DbSet<RefreshToken> RefreshTokens => Set<RefreshToken>();
    public DbSet<PinCredential> PinCredentials => Set<PinCredential>();
    public DbSet<TrustedTerminal> TrustedTerminals => Set<TrustedTerminal>();
    public DbSet<SecurityAuditEvent> SecurityAuditEvents => Set<SecurityAuditEvent>();
    public DbSet<InvitationToken> InvitationTokens => Set<InvitationToken>();
    public DbSet<PasswordResetToken> PasswordResetTokens => Set<PasswordResetToken>();
    public DbSet<PlatformSession> PlatformSessions => Set<PlatformSession>();
    public DbSet<PlatformRefreshToken> PlatformRefreshTokens => Set<PlatformRefreshToken>();
    public DbSet<IdentityNotificationOutboxMessage> IdentityNotificationOutbox => Set<IdentityNotificationOutboxMessage>();

    public RestaurantOrderDbContext(
        DbContextOptions<RestaurantOrderDbContext> options,
        ITenantContext? tenantContext = null)
        : base(options)
    {
        _tenantContext = tenantContext ?? TenantContext.Empty;
    }

    private Guid? _transactionTenantId;

    internal void ResetTransactionTenant()
    {
        _transactionTenantId = null;
    }

    /// <summary>
    /// Evaluates whether a valid tenant context is present either via ambient ITenantContext or active tenant transaction.
    /// Parameterized directly by EF Core in global query filters to enforce server-side fail-closed execution.
    /// </summary>
    public bool HasTenant => (_transactionTenantId.HasValue && _transactionTenantId.Value != Guid.Empty) || (_tenantContext.HasTenant && _tenantContext.TenantId.HasValue);

    /// <summary>
    /// Current tenant ID exposed as a pre-constructed, stable property on the DbContext.
    /// EF Core parameterizes this property directly in global query filters without invoking constructors in the LINQ expression tree.
    /// When HasTenant is false, returns default (empty) which, combined with HasTenant, guarantees fail-closed behavior.
    /// </summary>
    public TenantId CurrentTenantId => HasTenant
        ? new TenantId(_transactionTenantId ?? _tenantContext.TenantId!.Value)
        : default;

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        // Apply entity configurations from the current assembly
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(RestaurantOrderDbContext).Assembly);

        // Secondary defense-in-depth: EF Core Global Query Filters
        // Note: PostgreSQL Row-Level Security (RLS) remains the definitive security boundary.
        // Even if IgnoreQueryFilters() is called, PostgreSQL RLS prevents cross-tenant access.
        modelBuilder.Entity<Tenant>().HasQueryFilter(t => HasTenant && t.Id == CurrentTenantId);
        modelBuilder.Entity<Brand>().HasQueryFilter(b => HasTenant && b.TenantId == CurrentTenantId);
        modelBuilder.Entity<Branch>().HasQueryFilter(br => HasTenant && br.TenantId == CurrentTenantId);
        modelBuilder.Entity<RestaurantOrder.Domain.Branding.BrandAppearance>().HasQueryFilter(ba => HasTenant && ba.TenantId == CurrentTenantId);
        modelBuilder.Entity<RestaurantOrder.Domain.Branding.BranchThemeOverride>().HasQueryFilter(bto => HasTenant && bto.TenantId == CurrentTenantId);
        modelBuilder.Entity<BranchSettings>().HasQueryFilter(bs => HasTenant && bs.TenantId == CurrentTenantId);
        modelBuilder.Entity<BranchOperatingHours>().HasQueryFilter(boh => HasTenant && boh.TenantId == CurrentTenantId);
        modelBuilder.Entity<DiningArea>().HasQueryFilter(da => HasTenant && da.TenantId == CurrentTenantId);
        modelBuilder.Entity<PreparationStation>().HasQueryFilter(ps => HasTenant && ps.TenantId == CurrentTenantId);
        modelBuilder.Entity<RestaurantOrder.Domain.FeatureFlags.TenantFeatureFlags>().HasQueryFilter(tf => HasTenant && tf.TenantId == CurrentTenantId);
        modelBuilder.Entity<RestaurantOrder.Domain.FeatureFlags.BranchFeatureFlags>().HasQueryFilter(bf => HasTenant && bf.TenantId == CurrentTenantId);
        modelBuilder.Entity<RestaurantOrder.Domain.Catalog.Menu>().HasQueryFilter(m => HasTenant && m.TenantId == CurrentTenantId);
        modelBuilder.Entity<RestaurantOrder.Domain.Catalog.MenuCategory>().HasQueryFilter(c => HasTenant && c.TenantId == CurrentTenantId);
        modelBuilder.Entity<RestaurantOrder.Domain.Catalog.MenuItem>().HasQueryFilter(i => HasTenant && i.TenantId == CurrentTenantId);
        modelBuilder.Entity<RestaurantOrder.Domain.Catalog.ItemVariant>().HasQueryFilter(v => HasTenant && v.TenantId == CurrentTenantId);
        modelBuilder.Entity<RestaurantOrder.Domain.Catalog.ModifierGroup>().HasQueryFilter(mg => HasTenant && mg.TenantId == CurrentTenantId);
        modelBuilder.Entity<RestaurantOrder.Domain.Catalog.ModifierOption>().HasQueryFilter(mo => HasTenant && mo.TenantId == CurrentTenantId);
        modelBuilder.Entity<RestaurantOrder.Domain.Catalog.MenuItemModifierGroupAssignment>().HasQueryFilter(a => HasTenant && a.TenantId == CurrentTenantId);
        modelBuilder.Entity<RestaurantOrder.Domain.Catalog.BranchItemAvailability>().HasQueryFilter(bia => HasTenant && bia.TenantId == CurrentTenantId);

        // IAM tenant-scoped entity filters
        modelBuilder.Entity<UserMembership>().HasQueryFilter(m => HasTenant && m.TenantId == CurrentTenantId);
        modelBuilder.Entity<AuthSession>().HasQueryFilter(s => HasTenant && s.TenantId == CurrentTenantId);
        modelBuilder.Entity<RefreshToken>().HasQueryFilter(rt => HasTenant && rt.TenantId == CurrentTenantId);
        modelBuilder.Entity<PinCredential>().HasQueryFilter(p => HasTenant && p.TenantId == CurrentTenantId);
        modelBuilder.Entity<TrustedTerminal>().HasQueryFilter(t => HasTenant && t.TenantId == CurrentTenantId);
        modelBuilder.Entity<SecurityAuditEvent>().HasQueryFilter(a => HasTenant && a.TenantId == CurrentTenantId);
        modelBuilder.Entity<InvitationToken>().HasQueryFilter(it => HasTenant && it.TenantId == CurrentTenantId);
        modelBuilder.Entity<PasswordResetToken>().HasQueryFilter(pr => HasTenant && pr.TenantId == CurrentTenantId);
        modelBuilder.Entity<IdentityNotificationOutboxMessage>().HasQueryFilter(o => HasTenant && o.TenantId == CurrentTenantId);
        modelBuilder.Entity<RestaurantOrder.Domain.Catalog.CatalogAvailabilityOutboxMessage>().HasQueryFilter(o => HasTenant && o.TenantId == CurrentTenantId);
    }

    /// <summary>
    /// Binds the active PostgreSQL transaction to the specified tenant context.
    /// Strictly requires an active database transaction; throws InvalidOperationException if called without one.
    /// Uses transaction-local 'set_config(..., is_local => true)' so the context does not leak across pooled connections.
    /// </summary>
    public async Task SetTenantSessionAsync(Guid tenantId, CancellationToken cancellationToken = default)
    {
        if (Database.CurrentTransaction == null)
        {
            throw new InvalidOperationException(
                "Cannot set tenant session context without an active database transaction. An explicit transaction is required for transaction-local tenant context isolation.");
        }

        var conn = Database.GetDbConnection();
        if (conn.State != ConnectionState.Open)
        {
            await conn.OpenAsync(cancellationToken);
        }

        await using var cmd = conn.CreateCommand();
        cmd.Transaction = Database.CurrentTransaction.GetDbTransaction();
        cmd.CommandText = "SELECT set_config('app.current_tenant_id', @tenantId, true);";

        var param = cmd.CreateParameter();
        param.ParameterName = "tenantId";
        param.Value = tenantId.ToString();
        cmd.Parameters.Add(param);

        await cmd.ExecuteNonQueryAsync(cancellationToken);
        _transactionTenantId = tenantId;
    }

    /// <summary>
    /// Atomically begins a database transaction and binds it to the specified tenant context.
    /// Returns the IDbContextTransaction which manages commit/rollback and automatic context revert.
    /// </summary>
    public async Task<IDbContextTransaction> BeginTenantTransactionAsync(
        Guid tenantId,
        IsolationLevel isolationLevel = IsolationLevel.ReadCommitted,
        CancellationToken cancellationToken = default)
    {
        var tx = await Database.BeginTransactionAsync(isolationLevel, cancellationToken);
        await SetTenantSessionAsync(tenantId, cancellationToken);
        return new TenantDbContextTransaction(tx, this);
    }

    /// <summary>
    /// Clears the PostgreSQL session variable 'app.current_tenant_id' to prevent connection pool leakage.
    /// </summary>
    public async Task ClearTenantSessionAsync(CancellationToken cancellationToken = default)
    {
        _transactionTenantId = null;
        var conn = Database.GetDbConnection();
        if (conn.State == ConnectionState.Open)
        {
            await using var cmd = conn.CreateCommand();
            var currentDbTx = Database.CurrentTransaction?.GetDbTransaction();
            if (currentDbTx != null)
            {
                try
                {
                    cmd.Transaction = currentDbTx;
                }
                catch (Exception)
                {
                    // Ignore if transaction was already closed/completed
                }
            }
            cmd.CommandText = "SELECT set_config('app.current_tenant_id', '', false);";
            try
            {
                await cmd.ExecuteNonQueryAsync(cancellationToken);
            }
            catch (Exception)
            {
                // Disposed or completed connection/transaction is safe to ignore during session cleanup
            }
        }
    }

    private sealed class TenantDbContextTransaction : IDbContextTransaction
    {
        private readonly IDbContextTransaction _inner;
        private readonly RestaurantOrderDbContext _context;

        public TenantDbContextTransaction(IDbContextTransaction inner, RestaurantOrderDbContext context)
        {
            _inner = inner;
            _context = context;
        }

        public Guid TransactionId => _inner.TransactionId;

        public void Commit()
        {
            _inner.Commit();
            _context.ResetTransactionTenant();
        }

        public async Task CommitAsync(CancellationToken cancellationToken = default)
        {
            await _inner.CommitAsync(cancellationToken);
            _context.ResetTransactionTenant();
        }

        public void Rollback()
        {
            _inner.Rollback();
            _context.ResetTransactionTenant();
        }

        public async Task RollbackAsync(CancellationToken cancellationToken = default)
        {
            await _inner.RollbackAsync(cancellationToken);
            _context.ResetTransactionTenant();
        }

        public void Dispose()
        {
            _inner.Dispose();
            _context.ResetTransactionTenant();
        }

        public async ValueTask DisposeAsync()
        {
            await _inner.DisposeAsync();
            _context.ResetTransactionTenant();
        }
    }
}
