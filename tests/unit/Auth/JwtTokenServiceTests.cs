using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using RestaurantOrder.Application.Auth;
using RestaurantOrder.Domain.Auth;
using RestaurantOrder.Domain.Branches;
using RestaurantOrder.Domain.Tenants;
using RestaurantOrder.Infrastructure.Auth;
using Xunit;

namespace RestaurantOrder.UnitTests.Auth;

public class JwtTokenServiceTests
{
    private const string ValidSecret = "super-secret-key-that-is-at-least-32-chars-long-12345";
    private readonly JwtSettings _settings = new()
    {
        Secret = ValidSecret,
        Issuer = "test-issuer",
        Audience = "test-audience",
        AccessTokenLifetimeMinutes = 10,
        KeyId = "k1",
        ClockSkewSeconds = 5
    };

    [Fact]
    public void Constructor_WhenSecretTooShort_ThrowsInvalidOperationException()
    {
        var options = Options.Create(new JwtSettings { Secret = "short" });
        Assert.Throws<InvalidOperationException>(() => new JwtTokenService(options));
    }

    [Fact]
    public void GenerateAccessToken_EmitsRequiredClaimsAndValidSignature()
    {
        var service = new JwtTokenService(Options.Create(_settings));
        var userId = UserId.New();
        var sessionId = Guid.NewGuid();
        var tenantId = TenantId.New();
        var branchId = BranchId.New();
        var scope = AuthorizationScope.ForBranch(tenantId, branchId);
        var now = DateTimeOffset.UtcNow;

        var result = service.GenerateAccessToken(
            userId,
            sessionId,
            PrincipalType.Staff,
            AuthRole.Waiter,
            scope,
            AuthenticationMethod.Password,
            securityVersion: 2,
            now);

        Assert.NotNull(result.Token);
        Assert.Equal(now.AddMinutes(10), result.ExpiresAtUtc);
        Assert.False(string.IsNullOrWhiteSpace(result.Jti));

        var handler = new JwtSecurityTokenHandler { MapInboundClaims = false };
        var validationParams = service.GetTokenValidationParameters();
        var principal = handler.ValidateToken(result.Token, validationParams, out var validatedToken);

        Assert.NotNull(principal);
        Assert.Equal(userId.Value.ToString(), principal.FindFirst(JwtClaimNames.Subject)?.Value);
        Assert.Equal(sessionId.ToString(), principal.FindFirst(JwtClaimNames.SessionId)?.Value);
        Assert.Equal("staff", principal.FindFirst(JwtClaimNames.PrincipalType)?.Value);
        Assert.Equal("Waiter", principal.FindFirst(JwtClaimNames.Role)?.Value);
        Assert.Equal(tenantId.Value.ToString(), principal.FindFirst(JwtClaimNames.TenantId)?.Value);
        Assert.Equal(branchId.Value.ToString(), principal.FindFirst(JwtClaimNames.BranchId)?.Value);
        Assert.Equal("2", principal.FindFirst(JwtClaimNames.SecurityVersion)?.Value);
        Assert.Equal("password", principal.FindFirst(JwtClaimNames.AuthMethod)?.Value);
    }

    [Fact]
    public void ValidateToken_WhenSignedWithDifferentSecret_ThrowsSecurityTokenInvalidSignatureException()
    {
        var service = new JwtTokenService(Options.Create(_settings));
        var attackerSettings = new JwtSettings
        {
            Secret = "another-secret-key-that-is-at-least-32-chars-long-67890",
            Issuer = _settings.Issuer,
            Audience = _settings.Audience
        };
        var attackerService = new JwtTokenService(Options.Create(attackerSettings));

        var forgedToken = attackerService.GenerateAccessToken(
            UserId.New(),
            Guid.NewGuid(),
            PrincipalType.Staff,
            AuthRole.RestaurantAdmin,
            AuthorizationScope.ForTenant(TenantId.New()),
            AuthenticationMethod.Password,
            1,
            DateTimeOffset.UtcNow);

        var handler = new JwtSecurityTokenHandler();
        var validationParams = service.GetTokenValidationParameters();

        Assert.Throws<SecurityTokenInvalidSignatureException>(() =>
            handler.ValidateToken(forgedToken.Token, validationParams, out _));
    }

    [Fact]
    public void ValidateToken_WhenExpired_ThrowsSecurityTokenExpiredException()
    {
        var service = new JwtTokenService(Options.Create(_settings));
        var past = DateTimeOffset.UtcNow.AddMinutes(-30);

        var expiredToken = service.GenerateAccessToken(
            UserId.New(),
            Guid.NewGuid(),
            PrincipalType.Staff,
            AuthRole.RestaurantAdmin,
            AuthorizationScope.ForTenant(TenantId.New()),
            AuthenticationMethod.Password,
            1,
            past);

        var handler = new JwtSecurityTokenHandler();
        var validationParams = service.GetTokenValidationParameters();

        Assert.Throws<SecurityTokenExpiredException>(() =>
            handler.ValidateToken(expiredToken.Token, validationParams, out _));
    }
}
