namespace RestaurantOrder.Application.Auth;

/// <summary>
/// Canonical JWT claim type names adhering to ADR-0009.
/// </summary>
public static class JwtClaimNames
{
    /// <summary>Subject identifier (User ID for staff, TableSession ID for customer).</summary>
    public const string Subject = "sub";

    /// <summary>Active session tracking identifier.</summary>
    public const string SessionId = "sid";

    /// <summary>Unique JWT token identifier used for revocation and replay detection.</summary>
    public const string JwtId = "jti";

    /// <summary>Principal classification: "staff" or "customer".</summary>
    public const string PrincipalType = "principal_type";

    /// <summary>Assigned role name (one of the 8 canonical AuthRole names).</summary>
    public const string Role = "role";

    /// <summary>Tenant UUIDv7 identifier (omitted for SuperAdmin).</summary>
    public const string TenantId = "tenant_id";

    /// <summary>Branch UUIDv7 identifier (omitted for SuperAdmin and RestaurantAdmin).</summary>
    public const string BranchId = "branch_id";

    /// <summary>Table session UUIDv7 identifier (customer QR sessions only).</summary>
    public const string TableSessionId = "table_session_id";

    /// <summary>Integer security version incremented on credential/role changes (staff only, 0 for customer).</summary>
    public const string SecurityVersion = "security_version";

    /// <summary>Authentication mechanism: "password", "pin", or "customer_qr_session".</summary>
    public const string AuthMethod = "auth_method";

    /// <summary>Token issuer.</summary>
    public const string Issuer = "iss";

    /// <summary>Token audience.</summary>
    public const string Audience = "aud";

    /// <summary>Issued-at timestamp (Unix epoch seconds).</summary>
    public const string IssuedAt = "iat";

    /// <summary>Not-before timestamp (Unix epoch seconds).</summary>
    public const string NotBefore = "nbf";

    /// <summary>Expiration timestamp (Unix epoch seconds).</summary>
    public const string Expiration = "exp";
}
