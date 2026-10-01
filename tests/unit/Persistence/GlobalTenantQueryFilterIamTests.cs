using Microsoft.EntityFrameworkCore;
using RestaurantOrder.Application.Tenancy;
using RestaurantOrder.Domain.Tenants;
using RestaurantOrder.Infrastructure.Persistence;
using Xunit;

namespace RestaurantOrder.UnitTests.Persistence;

public class GlobalTenantQueryFilterIamTests
{
    private static RestaurantOrderDbContext CreateDbContext(ITenantContext? tenantContext = null)
    {
        var options = new DbContextOptionsBuilder<RestaurantOrderDbContext>()
            .UseNpgsql("Host=localhost;Database=test;Username=postgres;Password=dummy")
            .Options;

        return new RestaurantOrderDbContext(options, tenantContext);
    }

    [Fact]
    public void GlobalQueryFilter_IamEntities_WhenTenantPresent_TranslatesToQueryStringSuccessfully()
    {
        var tenantId = Guid.NewGuid();
        var context = new TenantContext(tenantId, isAuthenticated: true);
        using var dbContext = CreateDbContext(context);

        // Memberships
        var mSql = dbContext.Memberships.ToQueryString();
        Assert.Contains("FROM iam.memberships", mSql);
        Assert.Contains("WHERE", mSql);

        // Sessions
        var sSql = dbContext.Sessions.ToQueryString();
        Assert.Contains("FROM iam.sessions", sSql);
        Assert.Contains("WHERE", sSql);

        // RefreshTokens
        var rtSql = dbContext.RefreshTokens.ToQueryString();
        Assert.Contains("FROM iam.refresh_tokens", rtSql);
        Assert.Contains("WHERE", rtSql);

        // PinCredentials
        var pSql = dbContext.PinCredentials.ToQueryString();
        Assert.Contains("FROM iam.pin_credentials", pSql);
        Assert.Contains("WHERE", pSql);

        // TrustedTerminals
        var tSql = dbContext.TrustedTerminals.ToQueryString();
        Assert.Contains("FROM iam.trusted_terminals", tSql);
        Assert.Contains("WHERE", tSql);

        // SecurityAuditEvents
        var aSql = dbContext.SecurityAuditEvents.ToQueryString();
        Assert.Contains("FROM iam.security_audit_events", aSql);
        Assert.Contains("WHERE", aSql);

        // InvitationTokens
        var itSql = dbContext.InvitationTokens.ToQueryString();
        Assert.Contains("FROM iam.invitation_tokens", itSql);
        Assert.Contains("WHERE", itSql);

        // PasswordResetTokens
        var prSql = dbContext.PasswordResetTokens.ToQueryString();
        Assert.Contains("FROM iam.password_reset_tokens", prSql);
        Assert.Contains("WHERE", prSql);
    }

    [Fact]
    public void GlobalQueryFilter_IamEntities_WhenTenantAbsent_TranslatesFailClosed()
    {
        using var dbContext = CreateDbContext(TenantContext.Empty);

        Assert.False(dbContext.HasTenant);
        Assert.Equal(default(TenantId), dbContext.CurrentTenantId);

        // Ensure all query filters still compile to valid server SQL in fail-closed state
        var mSql = dbContext.Memberships.ToQueryString();
        Assert.Contains("FROM iam.memberships", mSql);

        var sSql = dbContext.Sessions.ToQueryString();
        Assert.Contains("FROM iam.sessions", sSql);

        var aSql = dbContext.SecurityAuditEvents.ToQueryString();
        Assert.Contains("FROM iam.security_audit_events", aSql);
    }
}
