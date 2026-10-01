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

    public RestaurantOrderDbContext(
        DbContextOptions<RestaurantOrderDbContext> options,
        ITenantContext? tenantContext = null)
        : base(options)
    {
        _tenantContext = tenantContext ?? TenantContext.Empty;
    }

    /// <summary>
    /// Evaluates whether a valid tenant context is present.
    /// Parameterized directly by EF Core in global query filters to enforce server-side fail-closed execution.
    /// </summary>
    public bool HasTenant => _tenantContext.HasTenant && _tenantContext.TenantId.HasValue;

    /// <summary>
    /// Current tenant ID exposed as a pre-constructed, stable property on the DbContext.
    /// EF Core parameterizes this property directly in global query filters without invoking constructors in the LINQ expression tree.
    /// When HasTenant is false, returns default (empty) which, combined with HasTenant, guarantees fail-closed behavior.
    /// </summary>
    public TenantId CurrentTenantId => HasTenant
        ? new TenantId(_tenantContext.TenantId!.Value)
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

        // IAM tenant-scoped entity filters
        modelBuilder.Entity<UserMembership>().HasQueryFilter(m => HasTenant && m.TenantId == CurrentTenantId);
        modelBuilder.Entity<AuthSession>().HasQueryFilter(s => HasTenant && s.TenantId == CurrentTenantId);
        modelBuilder.Entity<RefreshToken>().HasQueryFilter(rt => HasTenant && rt.TenantId == CurrentTenantId);
        modelBuilder.Entity<PinCredential>().HasQueryFilter(p => HasTenant && p.TenantId == CurrentTenantId);
        modelBuilder.Entity<TrustedTerminal>().HasQueryFilter(t => HasTenant && t.TenantId == CurrentTenantId);
        modelBuilder.Entity<SecurityAuditEvent>().HasQueryFilter(a => HasTenant && a.TenantId == CurrentTenantId);
        modelBuilder.Entity<InvitationToken>().HasQueryFilter(it => HasTenant && it.TenantId == CurrentTenantId);
        modelBuilder.Entity<PasswordResetToken>().HasQueryFilter(pr => HasTenant && pr.TenantId == CurrentTenantId);
    }

    /// <summary>
    /// Binds the active PostgreSQL transaction or connection to the specified tenant context.
    /// Uses transaction-local 'set_config(..., is_local => true)' so the context does not leak across pooled connections.
    /// </summary>
    public async Task SetTenantSessionAsync(Guid tenantId, CancellationToken cancellationToken = default)
    {
        var conn = Database.GetDbConnection();
        if (conn.State != ConnectionState.Open)
        {
            await conn.OpenAsync(cancellationToken);
        }

        await using var cmd = conn.CreateCommand();
        cmd.Transaction = Database.CurrentTransaction?.GetDbTransaction();
        cmd.CommandText = "SELECT set_config('app.current_tenant_id', @tenantId, true);";

        var param = cmd.CreateParameter();
        param.ParameterName = "tenantId";
        param.Value = tenantId.ToString();
        cmd.Parameters.Add(param);

        await cmd.ExecuteNonQueryAsync(cancellationToken);
    }

    /// <summary>
    /// Clears the PostgreSQL session variable 'app.current_tenant_id' to prevent connection pool leakage.
    /// </summary>
    public async Task ClearTenantSessionAsync(CancellationToken cancellationToken = default)
    {
        var conn = Database.GetDbConnection();
        if (conn.State == ConnectionState.Open)
        {
            await using var cmd = conn.CreateCommand();
            cmd.Transaction = Database.CurrentTransaction?.GetDbTransaction();
            cmd.CommandText = "SELECT set_config('app.current_tenant_id', '', false);";
            await cmd.ExecuteNonQueryAsync(cancellationToken);
        }
    }
}
