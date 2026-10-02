namespace RestaurantOrder.Infrastructure.Auth;

/// <summary>
/// Central key generator for Redis distributed auth state, enforcing the standard namespace pattern:
/// restaurant-order:{environment}:{tenant/terminal/scope}:{purpose}:{identifier}
/// </summary>
public static class AuthRedisKeyBuilder
{
    public static string LoginRateLimit(string environment, string ipAddress, string normalizedEmail, string? tenantSlug)
    {
        var env = Normalize(environment, "dev");
        var slug = Normalize(tenantSlug, "global");
        var ip = Normalize(ipAddress, "unknown-ip");
        var email = Normalize(normalizedEmail, "unknown-email");
        return $"restaurant-order:{env}:auth:login_rate_limit:{slug}:{ip}:{email}";
    }

    public static string TerminalPinAttempts(string environment, Guid terminalId, Guid? userId)
    {
        var env = Normalize(environment, "dev");
        return userId.HasValue
            ? $"restaurant-order:{env}:terminal:{terminalId:N}:pin_attempts:user:{userId.Value:N}"
            : $"restaurant-order:{env}:terminal:{terminalId:N}:pin_attempts:terminal";
    }

    public static string TerminalPinLocked(string environment, Guid terminalId, Guid? userId)
    {
        var env = Normalize(environment, "dev");
        return userId.HasValue
            ? $"restaurant-order:{env}:terminal:{terminalId:N}:pin_locked:user:{userId.Value:N}"
            : $"restaurant-order:{env}:terminal:{terminalId:N}:pin_locked:terminal";
    }

    public static string TerminalEnrollment(string environment, string codeHash)
    {
        var env = Normalize(environment, "dev");
        return $"restaurant-order:{env}:terminal:enrollment:{codeHash}";
    }

    public static string SessionActiveCache(string environment, Guid sessionId)
    {
        var env = Normalize(environment, "dev");
        return $"restaurant-order:{env}:auth:session_valid:{sessionId:N}";
    }

    public static string UserSecurityVersion(string environment, Guid userId)
    {
        var env = Normalize(environment, "dev");
        return $"restaurant-order:{env}:auth:user_sec_ver:{userId:N}";
    }

    public static string UserSessions(string environment, Guid userId)
    {
        var env = Normalize(environment, "dev");
        return $"restaurant-order:{env}:auth:user_sessions:{userId:N}";
    }

    private static string Normalize(string? value, string fallback) =>
        string.IsNullOrWhiteSpace(value) ? fallback : value.Trim().ToLowerInvariant();
}
