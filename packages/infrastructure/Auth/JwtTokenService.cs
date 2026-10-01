using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using RestaurantOrder.Application.Auth;
using RestaurantOrder.Domain.Auth;

namespace RestaurantOrder.Infrastructure.Auth;

/// <summary>
/// Infrastructure service for issuing cryptographically signed, short-lived JWT access tokens
/// conforming to ADR-0009 and Phase 3.1 claim specifications.
/// </summary>
public sealed class JwtTokenService : IJwtTokenGenerator
{
    private readonly JwtSettings _settings;
    private readonly SymmetricSecurityKey _signingKey;
    private readonly SigningCredentials _signingCredentials;

    public JwtTokenService(IOptions<JwtSettings> settings)
    {
        _settings = settings?.Value ?? throw new ArgumentNullException(nameof(settings));

        if (string.IsNullOrWhiteSpace(_settings.Secret) || _settings.Secret.Length < 32)
        {
            throw new InvalidOperationException("Jwt:Secret must be configured and at least 32 characters (256 bits) in length.");
        }

        var keyBytes = Encoding.UTF8.GetBytes(_settings.Secret);
        _signingKey = new SymmetricSecurityKey(keyBytes)
        {
            KeyId = string.IsNullOrWhiteSpace(_settings.KeyId) ? "k1" : _settings.KeyId
        };

        _signingCredentials = new SigningCredentials(_signingKey, SecurityAlgorithms.HmacSha256);
    }

    public JwtTokenResult GenerateAccessToken(
        UserId userId,
        Guid sessionId,
        PrincipalType principalType,
        AuthRole role,
        AuthorizationScope scope,
        AuthenticationMethod authMethod,
        int securityVersion,
        DateTimeOffset nowUtc)
    {
        var jti = Guid.NewGuid().ToString("N");
        var lifetimeMinutes = _settings.AccessTokenLifetimeMinutes <= 0 ? 10 : _settings.AccessTokenLifetimeMinutes;
        var expiresAtUtc = nowUtc.AddMinutes(lifetimeMinutes);

        var claims = new List<Claim>
        {
            new(JwtClaimNames.Subject, userId.Value.ToString()),
            new(JwtClaimNames.SessionId, sessionId.ToString()),
            new(JwtClaimNames.JwtId, jti),
            new(JwtClaimNames.PrincipalType, principalType == PrincipalType.Staff ? "staff" : "customer"),
            new(JwtClaimNames.Role, role.ToString()),
            new(JwtClaimNames.AuthMethod, authMethod switch
            {
                AuthenticationMethod.Password => "password",
                AuthenticationMethod.Pin => "pin",
                AuthenticationMethod.CustomerQrSession => "customer_qr_session",
                _ => "password"
            }),
            new(JwtClaimNames.SecurityVersion, securityVersion.ToString()),
            new(JwtRegisteredClaimNames.Iss, _settings.Issuer),
            new(JwtRegisteredClaimNames.Aud, _settings.Audience),
            new(JwtRegisteredClaimNames.Iat, nowUtc.ToUnixTimeSeconds().ToString(), ClaimValueTypes.Integer64),
            new(JwtRegisteredClaimNames.Nbf, nowUtc.ToUnixTimeSeconds().ToString(), ClaimValueTypes.Integer64),
            new(JwtRegisteredClaimNames.Exp, expiresAtUtc.ToUnixTimeSeconds().ToString(), ClaimValueTypes.Integer64)
        };

        if (scope.TenantId.HasValue)
        {
            claims.Add(new Claim(JwtClaimNames.TenantId, scope.TenantId.Value.Value.ToString()));
        }

        if (scope.BranchId.HasValue)
        {
            claims.Add(new Claim(JwtClaimNames.BranchId, scope.BranchId.Value.Value.ToString()));
        }

        var tokenDescriptor = new SecurityTokenDescriptor
        {
            Subject = new ClaimsIdentity(claims),
            Expires = expiresAtUtc.UtcDateTime,
            NotBefore = nowUtc.UtcDateTime,
            IssuedAt = nowUtc.UtcDateTime,
            SigningCredentials = _signingCredentials
        };

        var tokenHandler = new JwtSecurityTokenHandler
        {
            SetDefaultTimesOnTokenCreation = false
        };

        var securityToken = tokenHandler.CreateToken(tokenDescriptor);
        var tokenString = tokenHandler.WriteToken(securityToken);

        return new JwtTokenResult(tokenString, expiresAtUtc, jti);
    }

    public TokenValidationParameters GetTokenValidationParameters()
    {
        return new TokenValidationParameters
        {
            ValidateIssuerSigningKey = true,
            IssuerSigningKey = _signingKey,
            ValidateIssuer = true,
            ValidIssuer = _settings.Issuer,
            ValidateAudience = true,
            ValidAudience = _settings.Audience,
            ValidateLifetime = true,
            RequireExpirationTime = true,
            ClockSkew = TimeSpan.FromSeconds(Math.Max(0, _settings.ClockSkewSeconds)),
            ValidAlgorithms = [SecurityAlgorithms.HmacSha256],
            RequireSignedTokens = true
        };
    }
}
