using System.Globalization;
using RestaurantOrder.Domain.Auth;
using RestaurantOrder.Domain.Branches;
using RestaurantOrder.Domain.Tenants;

namespace RestaurantOrder.Application.Auth;

/// <summary>
/// Fail-closed parser that extracts and rigorously validates JWT claim payloads into AuthenticatedPrincipal instances.
/// Validates role boundaries, expiration, security versions, and multi-tenant scoping invariants.
/// </summary>
public sealed class JwtClaimPrincipalParser : IJwtClaimPrincipalParser
{
    private static readonly TimeSpan DefaultClockSkew = TimeSpan.FromSeconds(5);

    public AuthenticatedPrincipal ParsePrincipal(
        IReadOnlyDictionary<string, string> claims,
        DateTimeOffset? currentTime = null)
    {
        if (claims == null || claims.Count == 0)
        {
            throw new JwtClaimValidationException("Claims dictionary cannot be null or empty.");
        }

        return ParseInternal(claims, currentTime ?? DateTimeOffset.UtcNow);
    }

    public AuthenticatedPrincipal ParsePrincipal(
        IEnumerable<KeyValuePair<string, string>> claims,
        DateTimeOffset? currentTime = null)
    {
        if (claims == null)
        {
            throw new JwtClaimValidationException("Claims collection cannot be null.");
        }

        var dict = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var kvp in claims)
        {
            if (!dict.TryAdd(kvp.Key, kvp.Value))
            {
                throw new JwtClaimValidationException(kvp.Key, $"Duplicate claim '{kvp.Key}' detected in token payload.");
            }
        }

        if (dict.Count == 0)
        {
            throw new JwtClaimValidationException("Claims collection cannot be empty.");
        }

        return ParseInternal(dict, currentTime ?? DateTimeOffset.UtcNow);
    }

    private static AuthenticatedPrincipal ParseInternal(
        IReadOnlyDictionary<string, string> claims,
        DateTimeOffset now)
    {
        ValidateTimestamps(claims, now);

        var subjectId = ParseGuidClaim(claims, JwtClaimNames.Subject, required: true)!.Value;
        var sessionId = ParseGuidClaim(claims, JwtClaimNames.SessionId, required: true)!.Value;
        var principalType = ParsePrincipalType(claims);
        var role = ParseRole(claims);
        var authMethod = ParseAuthMethod(claims);
        var securityVersion = ParseSecurityVersion(claims, principalType);

        var scope = BuildAndValidateScope(claims, role);

        try
        {
            return new AuthenticatedPrincipal(
                subjectId: subjectId,
                principalType: principalType,
                role: role,
                scope: scope,
                sessionId: sessionId,
                authMethod: authMethod,
                securityVersion: securityVersion);
        }
        catch (InvalidAuthorizationScopeException ex)
        {
            throw new JwtClaimValidationException($"Domain authorization invariant failed: {ex.Message}");
        }
    }

    private static void ValidateTimestamps(IReadOnlyDictionary<string, string> claims, DateTimeOffset now)
    {
        if (claims.TryGetValue(JwtClaimNames.Expiration, out var expStr))
        {
            if (!long.TryParse(expStr, NumberStyles.Integer, CultureInfo.InvariantCulture, out var expUnix))
            {
                throw new JwtClaimValidationException(JwtClaimNames.Expiration, "Value must be a valid integer timestamp.");
            }

            var expiration = DateTimeOffset.FromUnixTimeSeconds(expUnix);
            if (now > expiration + DefaultClockSkew)
            {
                throw new JwtClaimValidationException(JwtClaimNames.Expiration, $"Token has expired at {expiration:O}.");
            }
        }

        if (claims.TryGetValue(JwtClaimNames.NotBefore, out var nbfStr))
        {
            if (!long.TryParse(nbfStr, NumberStyles.Integer, CultureInfo.InvariantCulture, out var nbfUnix))
            {
                throw new JwtClaimValidationException(JwtClaimNames.NotBefore, "Value must be a valid integer timestamp.");
            }

            var notBefore = DateTimeOffset.FromUnixTimeSeconds(nbfUnix);
            if (now < notBefore - DefaultClockSkew)
            {
                throw new JwtClaimValidationException(JwtClaimNames.NotBefore, $"Token is not valid before {notBefore:O}.");
            }
        }
    }

    private static PrincipalType ParsePrincipalType(IReadOnlyDictionary<string, string> claims)
    {
        if (!claims.TryGetValue(JwtClaimNames.PrincipalType, out var raw) || string.IsNullOrWhiteSpace(raw))
        {
            throw new JwtClaimValidationException(JwtClaimNames.PrincipalType, "Claim is required.");
        }

        return raw.Trim().ToLowerInvariant() switch
        {
            "staff" => PrincipalType.Staff,
            "customer" => PrincipalType.Customer,
            _ => throw new JwtClaimValidationException(JwtClaimNames.PrincipalType, $"Invalid principal type '{raw}'.")
        };
    }

    private static AuthRole ParseRole(IReadOnlyDictionary<string, string> claims)
    {
        if (!claims.TryGetValue(JwtClaimNames.Role, out var raw) || string.IsNullOrWhiteSpace(raw))
        {
            throw new JwtClaimValidationException(JwtClaimNames.Role, "Claim is required.");
        }

        if (!Enum.TryParse<AuthRole>(raw.Trim(), ignoreCase: true, out var role) || !Enum.IsDefined(role))
        {
            throw new JwtClaimValidationException(JwtClaimNames.Role, $"Unknown or invalid role '{raw}'.");
        }

        return role;
    }

    private static AuthenticationMethod ParseAuthMethod(IReadOnlyDictionary<string, string> claims)
    {
        if (!claims.TryGetValue(JwtClaimNames.AuthMethod, out var raw) || string.IsNullOrWhiteSpace(raw))
        {
            throw new JwtClaimValidationException(JwtClaimNames.AuthMethod, "Claim is required.");
        }

        return raw.Trim().ToLowerInvariant() switch
        {
            "password" => AuthenticationMethod.Password,
            "pin" => AuthenticationMethod.Pin,
            "customer_qr_session" => AuthenticationMethod.CustomerQrSession,
            _ => throw new JwtClaimValidationException(JwtClaimNames.AuthMethod, $"Invalid authentication method '{raw}'.")
        };
    }

    private static int ParseSecurityVersion(IReadOnlyDictionary<string, string> claims, PrincipalType principalType)
    {
        if (principalType == PrincipalType.Customer)
        {
            if (claims.TryGetValue(JwtClaimNames.SecurityVersion, out var customerVersionStr))
            {
                if (int.TryParse(customerVersionStr, NumberStyles.Integer, CultureInfo.InvariantCulture, out var cv) && cv != 0)
                {
                    throw new JwtClaimValidationException(
                        JwtClaimNames.SecurityVersion,
                        "Customer tokens must have security_version 0.");
                }
            }
            return 0;
        }

        if (!claims.TryGetValue(JwtClaimNames.SecurityVersion, out var staffVersionStr) ||
            string.IsNullOrWhiteSpace(staffVersionStr))
        {
            throw new JwtClaimValidationException(
                JwtClaimNames.SecurityVersion,
                "Staff principal requires a security_version claim.");
        }

        if (!int.TryParse(staffVersionStr, NumberStyles.Integer, CultureInfo.InvariantCulture, out var version) || version < 1)
        {
            throw new JwtClaimValidationException(
                JwtClaimNames.SecurityVersion,
                "Staff principal security_version must be an integer >= 1.");
        }

        return version;
    }

    private static AuthorizationScope BuildAndValidateScope(IReadOnlyDictionary<string, string> claims, AuthRole role)
    {
        var tenantGuid = ParseGuidClaim(claims, JwtClaimNames.TenantId, required: false);
        var branchGuid = ParseGuidClaim(claims, JwtClaimNames.BranchId, required: false);
        var tableSessionGuid = ParseGuidClaim(claims, JwtClaimNames.TableSessionId, required: false);

        switch (role)
        {
            case AuthRole.SuperAdmin:
                if (tenantGuid.HasValue)
                {
                    throw new JwtClaimValidationException(
                        JwtClaimNames.TenantId,
                        "SuperAdmin token must not contain tenant_id claim.");
                }
                if (branchGuid.HasValue)
                {
                    throw new JwtClaimValidationException(
                        JwtClaimNames.BranchId,
                        "SuperAdmin token must not contain branch_id claim.");
                }
                if (tableSessionGuid.HasValue)
                {
                    throw new JwtClaimValidationException(
                        JwtClaimNames.TableSessionId,
                        "SuperAdmin token must not contain table_session_id claim.");
                }
                return AuthorizationScope.Platform();

            case AuthRole.RestaurantAdmin:
                if (!tenantGuid.HasValue)
                {
                    throw new JwtClaimValidationException(
                        JwtClaimNames.TenantId,
                        "RestaurantAdmin token requires a valid tenant_id claim.");
                }
                if (branchGuid.HasValue)
                {
                    throw new JwtClaimValidationException(
                        JwtClaimNames.BranchId,
                        "RestaurantAdmin token must not contain branch_id claim.");
                }
                if (tableSessionGuid.HasValue)
                {
                    throw new JwtClaimValidationException(
                        JwtClaimNames.TableSessionId,
                        "RestaurantAdmin token must not contain table_session_id claim.");
                }
                return AuthorizationScope.ForTenant(TenantId.From(tenantGuid.Value));

            case AuthRole.BranchManager:
            case AuthRole.Cashier:
            case AuthRole.Kitchen:
            case AuthRole.Bar:
            case AuthRole.Waiter:
                if (!tenantGuid.HasValue)
                {
                    throw new JwtClaimValidationException(
                        JwtClaimNames.TenantId,
                        $"{role} token requires a valid tenant_id claim.");
                }
                if (!branchGuid.HasValue)
                {
                    throw new JwtClaimValidationException(
                        JwtClaimNames.BranchId,
                        $"{role} token requires a valid branch_id claim.");
                }
                if (tableSessionGuid.HasValue)
                {
                    throw new JwtClaimValidationException(
                        JwtClaimNames.TableSessionId,
                        $"{role} token must not contain table_session_id claim.");
                }
                return AuthorizationScope.ForBranch(
                    TenantId.From(tenantGuid.Value),
                    BranchId.From(branchGuid.Value));

            case AuthRole.Customer:
                if (!tenantGuid.HasValue)
                {
                    throw new JwtClaimValidationException(
                        JwtClaimNames.TenantId,
                        "Customer token requires a valid tenant_id claim.");
                }
                if (!branchGuid.HasValue)
                {
                    throw new JwtClaimValidationException(
                        JwtClaimNames.BranchId,
                        "Customer token requires a valid branch_id claim.");
                }
                if (!tableSessionGuid.HasValue)
                {
                    throw new JwtClaimValidationException(
                        JwtClaimNames.TableSessionId,
                        "Customer token requires a valid table_session_id claim.");
                }

                // In customer tokens, sub must match table_session_id
                var subGuid = ParseGuidClaim(claims, JwtClaimNames.Subject, required: true)!.Value;
                if (subGuid != tableSessionGuid.Value)
                {
                    throw new JwtClaimValidationException(
                        JwtClaimNames.Subject,
                        "Customer token subject ID must match table_session_id.");
                }

                return AuthorizationScope.ForTableSession(
                    TenantId.From(tenantGuid.Value),
                    BranchId.From(branchGuid.Value),
                    tableSessionGuid.Value);

            default:
                throw new JwtClaimValidationException(JwtClaimNames.Role, $"Unhandled role '{role}'.");
        }
    }

    private static Guid? ParseGuidClaim(IReadOnlyDictionary<string, string> claims, string key, bool required)
    {
        if (!claims.TryGetValue(key, out var raw) || string.IsNullOrWhiteSpace(raw))
        {
            if (required)
            {
                throw new JwtClaimValidationException(key, $"Required claim '{key}' is missing or empty.");
            }
            return null;
        }

        if (!Guid.TryParse(raw.Trim(), out var guid) || guid == Guid.Empty)
        {
            throw new JwtClaimValidationException(key, $"Claim '{key}' must be a non-empty Guid.");
        }

        return guid;
    }
}
