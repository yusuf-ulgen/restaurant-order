using System.Security.Claims;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using RestaurantOrder.Api.Auth;
using RestaurantOrder.Application.Auth;
using RestaurantOrder.Application.Floor;
using Xunit;

namespace RestaurantOrder.UnitTests.Auth;

public class CustomerTokenValidationHelperTests
{
    private readonly Mock<ICustomerSessionValidator> _validatorMock = new();

    private TokenValidatedContext CreateContext(ClaimsPrincipal? principal)
    {
        var services = new ServiceCollection();
        services.AddSingleton(_validatorMock.Object);
        var serviceProvider = services.BuildServiceProvider();

        var httpContext = new DefaultHttpContext
        {
            RequestServices = serviceProvider
        };

        var authScheme = new AuthenticationScheme("Bearer", "Bearer", typeof(JwtBearerHandler));
        var options = new JwtBearerOptions();

        return new TokenValidatedContext(httpContext, authScheme, options)
        {
            Principal = principal
        };
    }

    [Fact]
    public async Task ValidateCustomerTokenAsync_WhenPrincipalIsNull_ReturnsFalse()
    {
        var context = CreateContext(null);
        var result = await CustomerTokenValidationHelper.ValidateCustomerTokenAsync(context);

        Assert.False(result);
        Assert.NotNull(context.Result);
        Assert.False(context.Result.Succeeded);
    }

    [Theory]
    [InlineData(null, "68d6fbb3-3932-475f-9e76-3f110c714399", "77d6fbb3-3932-475f-9e76-3f110c714399", "77d6fbb3-3932-475f-9e76-3f110c714399")]
    [InlineData("not-a-guid", "68d6fbb3-3932-475f-9e76-3f110c714399", "77d6fbb3-3932-475f-9e76-3f110c714399", "77d6fbb3-3932-475f-9e76-3f110c714399")]
    [InlineData("00000000-0000-0000-0000-000000000000", "68d6fbb3-3932-475f-9e76-3f110c714399", "77d6fbb3-3932-475f-9e76-3f110c714399", "77d6fbb3-3932-475f-9e76-3f110c714399")]
    [InlineData("55d6fbb3-3932-475f-9e76-3f110c714399", null, "77d6fbb3-3932-475f-9e76-3f110c714399", "77d6fbb3-3932-475f-9e76-3f110c714399")]
    [InlineData("55d6fbb3-3932-475f-9e76-3f110c714399", "00000000-0000-0000-0000-000000000000", "77d6fbb3-3932-475f-9e76-3f110c714399", "77d6fbb3-3932-475f-9e76-3f110c714399")]
    [InlineData("55d6fbb3-3932-475f-9e76-3f110c714399", "68d6fbb3-3932-475f-9e76-3f110c714399", null, "77d6fbb3-3932-475f-9e76-3f110c714399")]
    [InlineData("55d6fbb3-3932-475f-9e76-3f110c714399", "68d6fbb3-3932-475f-9e76-3f110c714399", "00000000-0000-0000-0000-000000000000", "77d6fbb3-3932-475f-9e76-3f110c714399")]
    public async Task ValidateCustomerTokenAsync_WhenRequiredScopeClaimsInvalid_ReturnsFalse(
        string? tenant, string? branch, string? session, string? sub)
    {
        var claims = new List<Claim>();
        if (tenant != null) claims.Add(new Claim(JwtClaimNames.TenantId, tenant));
        if (branch != null) claims.Add(new Claim(JwtClaimNames.BranchId, branch));
        if (session != null) claims.Add(new Claim(JwtClaimNames.TableSessionId, session));
        if (sub != null) claims.Add(new Claim(JwtClaimNames.Subject, sub));

        var principal = new ClaimsPrincipal(new ClaimsIdentity(claims, "Bearer"));
        var context = CreateContext(principal);

        var result = await CustomerTokenValidationHelper.ValidateCustomerTokenAsync(context);
        Assert.False(result);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public async Task ValidateCustomerTokenAsync_WhenMissingSubClaim_ReturnsFalse(string? sub)
    {
        var tenantId = Guid.NewGuid();
        var branchId = Guid.NewGuid();
        var sessionId = Guid.NewGuid();

        var claims = new List<Claim>
        {
            new(JwtClaimNames.TenantId, tenantId.ToString()),
            new(JwtClaimNames.BranchId, branchId.ToString()),
            new(JwtClaimNames.TableSessionId, sessionId.ToString())
        };
        if (sub != null) claims.Add(new Claim(JwtClaimNames.Subject, sub));

        var principal = new ClaimsPrincipal(new ClaimsIdentity(claims, "Bearer"));
        var context = CreateContext(principal);

        var result = await CustomerTokenValidationHelper.ValidateCustomerTokenAsync(context);
        Assert.False(result);
    }

    [Theory]
    [InlineData("not-a-valid-guid")]
    [InlineData("00000000-0000-0000-0000-000000000000")]
    public async Task ValidateCustomerTokenAsync_WhenMalformedSubGuid_ReturnsFalse(string malformedSub)
    {
        var tenantId = Guid.NewGuid();
        var branchId = Guid.NewGuid();
        var sessionId = Guid.NewGuid();

        var claims = new List<Claim>
        {
            new(JwtClaimNames.TenantId, tenantId.ToString()),
            new(JwtClaimNames.BranchId, branchId.ToString()),
            new(JwtClaimNames.TableSessionId, sessionId.ToString()),
            new(JwtClaimNames.Subject, malformedSub)
        };

        var principal = new ClaimsPrincipal(new ClaimsIdentity(claims, "Bearer"));
        var context = CreateContext(principal);

        var result = await CustomerTokenValidationHelper.ValidateCustomerTokenAsync(context);
        Assert.False(result);
    }

    [Fact]
    public async Task ValidateCustomerTokenAsync_WhenSubMismatchesSession_ReturnsFalse()
    {
        var tenantId = Guid.NewGuid();
        var branchId = Guid.NewGuid();
        var sessionId = Guid.NewGuid();
        var differentSub = Guid.NewGuid();

        var claims = new List<Claim>
        {
            new(JwtClaimNames.TenantId, tenantId.ToString()),
            new(JwtClaimNames.BranchId, branchId.ToString()),
            new(JwtClaimNames.TableSessionId, sessionId.ToString()),
            new(JwtClaimNames.Subject, differentSub.ToString())
        };

        var principal = new ClaimsPrincipal(new ClaimsIdentity(claims, "Bearer"));
        var context = CreateContext(principal);

        var result = await CustomerTokenValidationHelper.ValidateCustomerTokenAsync(context);
        Assert.False(result);
    }

    [Fact]
    public async Task ValidateCustomerTokenAsync_WhenSessionInactive_ReturnsFalse()
    {
        var tenantId = Guid.NewGuid();
        var branchId = Guid.NewGuid();
        var sessionId = Guid.NewGuid();

        _validatorMock
            .Setup(v => v.ValidateSessionActiveAsync(tenantId, branchId, sessionId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);

        var claims = new List<Claim>
        {
            new(JwtClaimNames.TenantId, tenantId.ToString()),
            new(JwtClaimNames.BranchId, branchId.ToString()),
            new(JwtClaimNames.TableSessionId, sessionId.ToString()),
            new(JwtClaimNames.Subject, sessionId.ToString())
        };

        var principal = new ClaimsPrincipal(new ClaimsIdentity(claims, "Bearer"));
        var context = CreateContext(principal);

        var result = await CustomerTokenValidationHelper.ValidateCustomerTokenAsync(context);
        Assert.False(result);
    }

    [Fact]
    public async Task ValidateCustomerTokenAsync_WhenValidatorThrows_ReturnsFalse()
    {
        var tenantId = Guid.NewGuid();
        var branchId = Guid.NewGuid();
        var sessionId = Guid.NewGuid();

        _validatorMock
            .Setup(v => v.ValidateSessionActiveAsync(tenantId, branchId, sessionId, It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("Redis unavailable"));

        var claims = new List<Claim>
        {
            new(JwtClaimNames.TenantId, tenantId.ToString()),
            new(JwtClaimNames.BranchId, branchId.ToString()),
            new(JwtClaimNames.TableSessionId, sessionId.ToString()),
            new(JwtClaimNames.Subject, sessionId.ToString())
        };

        var principal = new ClaimsPrincipal(new ClaimsIdentity(claims, "Bearer"));
        var context = CreateContext(principal);

        var result = await CustomerTokenValidationHelper.ValidateCustomerTokenAsync(context);
        Assert.False(result);
    }

    [Fact]
    public async Task ValidateCustomerTokenAsync_WhenCanonicalClaimsAndSessionActive_ReturnsTrue()
    {
        var tenantId = Guid.NewGuid();
        var branchId = Guid.NewGuid();
        var sessionId = Guid.NewGuid();

        _validatorMock
            .Setup(v => v.ValidateSessionActiveAsync(tenantId, branchId, sessionId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        var claims = new List<Claim>
        {
            new(JwtClaimNames.TenantId, tenantId.ToString()),
            new(JwtClaimNames.BranchId, branchId.ToString()),
            new(JwtClaimNames.TableSessionId, sessionId.ToString()),
            new(JwtClaimNames.Subject, sessionId.ToString())
        };

        var principal = new ClaimsPrincipal(new ClaimsIdentity(claims, "Bearer"));
        var context = CreateContext(principal);

        var result = await CustomerTokenValidationHelper.ValidateCustomerTokenAsync(context);
        Assert.True(result);
    }

    [Fact]
    public async Task ValidateCustomerTokenAsync_WhenFallbackClaimsAndSessionActive_ReturnsTrue()
    {
        var tenantId = Guid.NewGuid();
        var branchId = Guid.NewGuid();
        var sessionId = Guid.NewGuid();

        _validatorMock
            .Setup(v => v.ValidateSessionActiveAsync(tenantId, branchId, sessionId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        var claims = new List<Claim>
        {
            new("tenant_id", tenantId.ToString()),
            new("branch_id", branchId.ToString()),
            new("table_session_id", sessionId.ToString()),
            new("sub", sessionId.ToString())
        };

        var principal = new ClaimsPrincipal(new ClaimsIdentity(claims, "Bearer"));
        var context = CreateContext(principal);

        var result = await CustomerTokenValidationHelper.ValidateCustomerTokenAsync(context);
        Assert.True(result);
    }
}
