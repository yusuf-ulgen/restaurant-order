using RestaurantOrder.Application.Auth;
using RestaurantOrder.Domain.Auth;
using Xunit;

namespace RestaurantOrder.UnitTests.Auth;

public class JwtClaimPrincipalParserTests
{
    private readonly IJwtClaimPrincipalParser _parser = new JwtClaimPrincipalParser();

    private readonly Guid _userId = Guid.NewGuid();
    private readonly Guid _sessionId = Guid.NewGuid();
    private readonly Guid _tenantId = Guid.NewGuid();
    private readonly Guid _branchId = Guid.NewGuid();
    private readonly Guid _tableSessionId = Guid.NewGuid();

    [Fact]
    public void ParsePrincipal_ValidSuperAdmin_ParsesSuccessfully()
    {
        var claims = new Dictionary<string, string>
        {
            [JwtClaimNames.Subject] = _userId.ToString(),
            [JwtClaimNames.SessionId] = _sessionId.ToString(),
            [JwtClaimNames.PrincipalType] = "staff",
            [JwtClaimNames.Role] = "SuperAdmin",
            [JwtClaimNames.AuthMethod] = "password",
            [JwtClaimNames.SecurityVersion] = "1"
        };

        var principal = _parser.ParsePrincipal(claims);

        Assert.Equal(_userId, principal.SubjectId);
        Assert.Equal(PrincipalType.Staff, principal.PrincipalType);
        Assert.Equal(AuthRole.SuperAdmin, principal.Role);
        Assert.Equal(AuthorizationScopeType.Platform, principal.Scope.ScopeType);
        Assert.Null(principal.TenantId);
        Assert.Null(principal.BranchId);
        Assert.True(principal.IsSuperAdmin);
    }

    [Fact]
    public void ParsePrincipal_ValidRestaurantAdmin_ParsesSuccessfully()
    {
        var claims = new Dictionary<string, string>
        {
            [JwtClaimNames.Subject] = _userId.ToString(),
            [JwtClaimNames.SessionId] = _sessionId.ToString(),
            [JwtClaimNames.PrincipalType] = "staff",
            [JwtClaimNames.Role] = "RestaurantAdmin",
            [JwtClaimNames.TenantId] = _tenantId.ToString(),
            [JwtClaimNames.AuthMethod] = "password",
            [JwtClaimNames.SecurityVersion] = "2"
        };

        var principal = _parser.ParsePrincipal(claims);

        Assert.Equal(_userId, principal.SubjectId);
        Assert.Equal(AuthRole.RestaurantAdmin, principal.Role);
        Assert.Equal(AuthorizationScopeType.Tenant, principal.Scope.ScopeType);
        Assert.Equal(_tenantId, principal.TenantId!.Value.Value);
        Assert.Null(principal.BranchId);
        Assert.Equal(2, principal.SecurityVersion);
    }

    [Theory]
    [InlineData("BranchManager", "password")]
    [InlineData("Cashier", "pin")]
    [InlineData("Kitchen", "pin")]
    [InlineData("Bar", "password")]
    [InlineData("Waiter", "pin")]
    public void ParsePrincipal_ValidBranchStaffRoles_ParseSuccessfully(string roleStr, string authMethodStr)
    {
        var claims = new Dictionary<string, string>
        {
            [JwtClaimNames.Subject] = _userId.ToString(),
            [JwtClaimNames.SessionId] = _sessionId.ToString(),
            [JwtClaimNames.PrincipalType] = "staff",
            [JwtClaimNames.Role] = roleStr,
            [JwtClaimNames.TenantId] = _tenantId.ToString(),
            [JwtClaimNames.BranchId] = _branchId.ToString(),
            [JwtClaimNames.AuthMethod] = authMethodStr,
            [JwtClaimNames.SecurityVersion] = "1"
        };

        var principal = _parser.ParsePrincipal(claims);

        Assert.Equal(PrincipalType.Staff, principal.PrincipalType);
        Assert.Equal(AuthorizationScopeType.Branch, principal.Scope.ScopeType);
        Assert.Equal(_tenantId, principal.TenantId!.Value.Value);
        Assert.Equal(_branchId, principal.BranchId!.Value.Value);
        Assert.Null(principal.TableSessionId);
    }

    [Fact]
    public void ParsePrincipal_ValidCustomer_ParsesSuccessfully()
    {
        var claims = new Dictionary<string, string>
        {
            [JwtClaimNames.Subject] = _tableSessionId.ToString(),
            [JwtClaimNames.SessionId] = _tableSessionId.ToString(),
            [JwtClaimNames.PrincipalType] = "customer",
            [JwtClaimNames.Role] = "Customer",
            [JwtClaimNames.TenantId] = _tenantId.ToString(),
            [JwtClaimNames.BranchId] = _branchId.ToString(),
            [JwtClaimNames.TableSessionId] = _tableSessionId.ToString(),
            [JwtClaimNames.AuthMethod] = "customer_qr_session",
            [JwtClaimNames.SecurityVersion] = "0"
        };

        var principal = _parser.ParsePrincipal(claims);

        Assert.Equal(_tableSessionId, principal.SubjectId);
        Assert.Equal(PrincipalType.Customer, principal.PrincipalType);
        Assert.Equal(AuthRole.Customer, principal.Role);
        Assert.Equal(AuthorizationScopeType.TableSession, principal.Scope.ScopeType);
        Assert.Equal(_tenantId, principal.TenantId!.Value.Value);
        Assert.Equal(_branchId, principal.BranchId!.Value.Value);
        Assert.Equal(_tableSessionId, principal.TableSessionId);
        Assert.Equal(0, principal.SecurityVersion);
        Assert.True(principal.IsCustomer);
    }

    [Fact]
    public void ParsePrincipal_TokenExpired_ThrowsJwtClaimValidationException()
    {
        var now = DateTimeOffset.UtcNow;
        var expiredTime = now.AddMinutes(-10).ToUnixTimeSeconds();

        var claims = new Dictionary<string, string>
        {
            [JwtClaimNames.Subject] = _userId.ToString(),
            [JwtClaimNames.SessionId] = _sessionId.ToString(),
            [JwtClaimNames.PrincipalType] = "staff",
            [JwtClaimNames.Role] = "SuperAdmin",
            [JwtClaimNames.AuthMethod] = "password",
            [JwtClaimNames.SecurityVersion] = "1",
            [JwtClaimNames.Expiration] = expiredTime.ToString()
        };

        var ex = Assert.Throws<JwtClaimValidationException>(() =>
            _parser.ParsePrincipal(claims, now));

        Assert.Equal(JwtClaimNames.Expiration, ex.ClaimName);
    }

    [Fact]
    public void ParsePrincipal_TokenNotYetValid_ThrowsJwtClaimValidationException()
    {
        var now = DateTimeOffset.UtcNow;
        var futureTime = now.AddMinutes(10).ToUnixTimeSeconds();

        var claims = new Dictionary<string, string>
        {
            [JwtClaimNames.Subject] = _userId.ToString(),
            [JwtClaimNames.SessionId] = _sessionId.ToString(),
            [JwtClaimNames.PrincipalType] = "staff",
            [JwtClaimNames.Role] = "SuperAdmin",
            [JwtClaimNames.AuthMethod] = "password",
            [JwtClaimNames.SecurityVersion] = "1",
            [JwtClaimNames.NotBefore] = futureTime.ToString()
        };

        var ex = Assert.Throws<JwtClaimValidationException>(() =>
            _parser.ParsePrincipal(claims, now));

        Assert.Equal(JwtClaimNames.NotBefore, ex.ClaimName);
    }

    [Fact]
    public void ParsePrincipal_DuplicateClaimsInEnumerable_ThrowsJwtClaimValidationException()
    {
        var claims = new List<KeyValuePair<string, string>>
        {
            new(JwtClaimNames.Subject, _userId.ToString()),
            new(JwtClaimNames.Subject, Guid.NewGuid().ToString())
        };

        Assert.Throws<JwtClaimValidationException>(() =>
            _parser.ParsePrincipal(claims));
    }

    [Theory]
    [InlineData(JwtClaimNames.Subject)]
    [InlineData(JwtClaimNames.SessionId)]
    [InlineData(JwtClaimNames.PrincipalType)]
    [InlineData(JwtClaimNames.Role)]
    [InlineData(JwtClaimNames.AuthMethod)]
    public void ParsePrincipal_MissingMandatoryClaim_ThrowsValidationException(string missingClaim)
    {
        var claims = new Dictionary<string, string>
        {
            [JwtClaimNames.Subject] = _userId.ToString(),
            [JwtClaimNames.SessionId] = _sessionId.ToString(),
            [JwtClaimNames.PrincipalType] = "staff",
            [JwtClaimNames.Role] = "SuperAdmin",
            [JwtClaimNames.AuthMethod] = "password",
            [JwtClaimNames.SecurityVersion] = "1"
        };

        claims.Remove(missingClaim);

        Assert.Throws<JwtClaimValidationException>(() => _parser.ParsePrincipal(claims));
    }

    [Fact]
    public void ParsePrincipal_SuperAdminWithTenantClaim_ThrowsJwtClaimValidationException()
    {
        var claims = new Dictionary<string, string>
        {
            [JwtClaimNames.Subject] = _userId.ToString(),
            [JwtClaimNames.SessionId] = _sessionId.ToString(),
            [JwtClaimNames.PrincipalType] = "staff",
            [JwtClaimNames.Role] = "SuperAdmin",
            [JwtClaimNames.AuthMethod] = "password",
            [JwtClaimNames.SecurityVersion] = "1",
            [JwtClaimNames.TenantId] = _tenantId.ToString()
        };

        var ex = Assert.Throws<JwtClaimValidationException>(() => _parser.ParsePrincipal(claims));
        Assert.Equal(JwtClaimNames.TenantId, ex.ClaimName);
    }

    [Fact]
    public void ParsePrincipal_RestaurantAdminWithoutTenantClaim_ThrowsJwtClaimValidationException()
    {
        var claims = new Dictionary<string, string>
        {
            [JwtClaimNames.Subject] = _userId.ToString(),
            [JwtClaimNames.SessionId] = _sessionId.ToString(),
            [JwtClaimNames.PrincipalType] = "staff",
            [JwtClaimNames.Role] = "RestaurantAdmin",
            [JwtClaimNames.AuthMethod] = "password",
            [JwtClaimNames.SecurityVersion] = "1"
        };

        var ex = Assert.Throws<JwtClaimValidationException>(() => _parser.ParsePrincipal(claims));
        Assert.Equal(JwtClaimNames.TenantId, ex.ClaimName);
    }

    [Fact]
    public void ParsePrincipal_RestaurantAdminWithBranchClaim_ThrowsJwtClaimValidationException()
    {
        var claims = new Dictionary<string, string>
        {
            [JwtClaimNames.Subject] = _userId.ToString(),
            [JwtClaimNames.SessionId] = _sessionId.ToString(),
            [JwtClaimNames.PrincipalType] = "staff",
            [JwtClaimNames.Role] = "RestaurantAdmin",
            [JwtClaimNames.TenantId] = _tenantId.ToString(),
            [JwtClaimNames.BranchId] = _branchId.ToString(),
            [JwtClaimNames.AuthMethod] = "password",
            [JwtClaimNames.SecurityVersion] = "1"
        };

        var ex = Assert.Throws<JwtClaimValidationException>(() => _parser.ParsePrincipal(claims));
        Assert.Equal(JwtClaimNames.BranchId, ex.ClaimName);
    }

    [Fact]
    public void ParsePrincipal_CustomerSubDoesNotMatchTableSessionId_ThrowsJwtClaimValidationException()
    {
        var claims = new Dictionary<string, string>
        {
            [JwtClaimNames.Subject] = _userId.ToString(), // mismatch!
            [JwtClaimNames.SessionId] = _tableSessionId.ToString(),
            [JwtClaimNames.PrincipalType] = "customer",
            [JwtClaimNames.Role] = "Customer",
            [JwtClaimNames.TenantId] = _tenantId.ToString(),
            [JwtClaimNames.BranchId] = _branchId.ToString(),
            [JwtClaimNames.TableSessionId] = _tableSessionId.ToString(),
            [JwtClaimNames.AuthMethod] = "customer_qr_session"
        };

        var ex = Assert.Throws<JwtClaimValidationException>(() => _parser.ParsePrincipal(claims));
        Assert.Equal(JwtClaimNames.Subject, ex.ClaimName);
    }

    [Fact]
    public void ParsePrincipal_SuperAdminWithPin_ThrowsJwtClaimValidationException()
    {
        var claims = new Dictionary<string, string>
        {
            [JwtClaimNames.Subject] = _userId.ToString(),
            [JwtClaimNames.SessionId] = _sessionId.ToString(),
            [JwtClaimNames.PrincipalType] = "staff",
            [JwtClaimNames.Role] = "SuperAdmin",
            [JwtClaimNames.AuthMethod] = "pin",
            [JwtClaimNames.SecurityVersion] = "1"
        };

        Assert.Throws<JwtClaimValidationException>(() => _parser.ParsePrincipal(claims));
    }

    [Fact]
    public void ParsePrincipal_FromValidatedJwtToken_Succeeds()
    {
        var key = new Microsoft.IdentityModel.Tokens.SymmetricSecurityKey(System.Text.Encoding.UTF8.GetBytes("super_secret_development_jwt_key_that_is_long_enough_256bits!"))
        {
            KeyId = "k1"
        };
        var creds = new Microsoft.IdentityModel.Tokens.SigningCredentials(key, Microsoft.IdentityModel.Tokens.SecurityAlgorithms.HmacSha256);
        var claims = new List<System.Security.Claims.Claim>
        {
            new(JwtClaimNames.Subject, _userId.ToString()),
            new(JwtClaimNames.SessionId, _sessionId.ToString()),
            new(JwtClaimNames.JwtId, Guid.NewGuid().ToString("N")),
            new(JwtClaimNames.PrincipalType, "staff"),
            new(JwtClaimNames.Role, "RestaurantAdmin"),
            new(JwtClaimNames.AuthMethod, "password"),
            new(JwtClaimNames.SecurityVersion, "1"),
            new(System.IdentityModel.Tokens.Jwt.JwtRegisteredClaimNames.Iss, "restaurant-order"),
            new(System.IdentityModel.Tokens.Jwt.JwtRegisteredClaimNames.Aud, "restaurant-order-clients"),
            new(JwtClaimNames.TenantId, _tenantId.ToString())
        };

        var token = new System.IdentityModel.Tokens.Jwt.JwtSecurityToken(
            issuer: "restaurant-order",
            audience: "restaurant-order-clients",
            claims: claims,
            expires: DateTime.UtcNow.AddMinutes(15),
            signingCredentials: creds);

        var tokenStr = new System.IdentityModel.Tokens.Jwt.JwtSecurityTokenHandler().WriteToken(token);

        var handler = new System.IdentityModel.Tokens.Jwt.JwtSecurityTokenHandler { MapInboundClaims = false };
        var principal = handler.ValidateToken(tokenStr, new Microsoft.IdentityModel.Tokens.TokenValidationParameters
        {
            ValidateIssuerSigningKey = true,
            IssuerSigningKey = key,
            ValidateIssuer = true,
            ValidIssuer = "restaurant-order",
            ValidateAudience = true,
            ValidAudience = "restaurant-order-clients",
            ValidateLifetime = true,
        }, out _);

        Assert.NotEmpty(principal.Claims);

        var claimsDict = principal.Claims
            .GroupBy(c => c.Type)
            .ToDictionary(g => g.Key, g => g.First().Value, StringComparer.Ordinal);

        var tenantClaim = principal.FindFirst(JwtClaimNames.TenantId)?.Value;
        Assert.NotNull(tenantClaim);
        Assert.Equal(_tenantId.ToString(), tenantClaim);

        var actor = _parser.ParsePrincipal(claimsDict);
        Assert.Equal(_userId, actor.SubjectId);
        Assert.Equal(_tenantId, actor.Scope.TenantId!.Value.Value);
    }
}
