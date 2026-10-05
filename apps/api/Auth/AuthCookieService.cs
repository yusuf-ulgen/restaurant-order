using System.Security.Cryptography;

namespace RestaurantOrder.Api.Auth;

public interface IAuthCookieService
{
    void SetAuthCookies(
        HttpResponse response,
        string accessToken,
        string refreshToken,
        DateTimeOffset accessExpiresAt,
        DateTimeOffset refreshExpiresAt,
        bool isHttps);

    void ClearAuthCookies(HttpResponse response, bool isHttps);

    void SetCustomerSessionCookie(
        HttpResponse response,
        string accessToken,
        DateTimeOffset accessExpiresAt,
        bool isHttps);

    void ClearCustomerSessionCookie(HttpResponse response, bool isHttps);

    void SetTerminalCookie(HttpResponse response, Guid terminalId, string deviceSecret, bool isHttps);

    void ClearTerminalCookie(HttpResponse response, bool isHttps);

    void SetCsrfCookie(HttpResponse response, bool isHttps, DateTimeOffset? expires = null);

    void ClearCsrfCookie(HttpResponse response, bool isHttps);

    (Guid TerminalId, string DeviceSecret)? GetTerminalCredentials(HttpRequest request);

    string? GetRefreshToken(HttpRequest request);

    string? GetAccessToken(HttpRequest request);

    string GenerateCsrfToken();
}

public sealed class AuthCookieService : IAuthCookieService
{
    public const string AccessTokenCookieName = "restaurant_access_token";
    public const string RefreshTokenCookieName = "restaurant_refresh_token";
    public const string CsrfTokenCookieName = "restaurant_csrf_token";
    public const string CsrfHeaderName = "X-CSRF-Token";
    public const string TerminalCredCookieName = "restaurant_terminal_cred";

    private readonly bool _isProductionOrStaging;

    public AuthCookieService(IHostEnvironment? environment = null)
    {
        _isProductionOrStaging = environment?.IsProduction() == true || environment?.IsEnvironment("Staging") == true;
    }

    private bool ResolveSecure(bool isHttps) => isHttps || _isProductionOrStaging;

    public void SetAuthCookies(
        HttpResponse response,
        string accessToken,
        string refreshToken,
        DateTimeOffset accessExpiresAt,
        DateTimeOffset refreshExpiresAt,
        bool isHttps)
    {
        var csrfToken = GenerateCsrfToken();
        var secure = ResolveSecure(isHttps);

        // 1. Short-lived Access Token Cookie
        response.Cookies.Append(AccessTokenCookieName, accessToken, new CookieOptions
        {
            HttpOnly = true,
            Secure = secure,
            SameSite = SameSiteMode.Lax,
            Path = "/",
            Expires = accessExpiresAt
        });

        // 2. Rotating Refresh Token Cookie (strictly bounded to /api/v1/auth)
        response.Cookies.Append(RefreshTokenCookieName, refreshToken, new CookieOptions
        {
            HttpOnly = true,
            Secure = secure,
            SameSite = SameSiteMode.Strict,
            Path = "/api/v1/auth",
            Expires = refreshExpiresAt
        });

        // 3. Double-submit CSRF Cookie (readable by client to set in X-CSRF-Token header)
        response.Cookies.Append(CsrfTokenCookieName, csrfToken, new CookieOptions
        {
            HttpOnly = false,
            Secure = secure,
            SameSite = SameSiteMode.Strict,
            Path = "/",
            Expires = refreshExpiresAt
        });
    }

    public void ClearAuthCookies(HttpResponse response, bool isHttps)
    {
        var secure = ResolveSecure(isHttps);

        response.Cookies.Delete(AccessTokenCookieName, new CookieOptions
        {
            HttpOnly = true,
            Secure = secure,
            SameSite = SameSiteMode.Lax,
            Path = "/"
        });

        response.Cookies.Delete(RefreshTokenCookieName, new CookieOptions
        {
            HttpOnly = true,
            Secure = secure,
            SameSite = SameSiteMode.Strict,
            Path = "/api/v1/auth"
        });

        response.Cookies.Delete(CsrfTokenCookieName, new CookieOptions
        {
            HttpOnly = false,
            Secure = secure,
            SameSite = SameSiteMode.Strict,
            Path = "/"
        });
    }

    public void SetCustomerSessionCookie(
        HttpResponse response,
        string accessToken,
        DateTimeOffset accessExpiresAt,
        bool isHttps)
    {
        var secure = ResolveSecure(isHttps);

        response.Cookies.Append(AccessTokenCookieName, accessToken, new CookieOptions
        {
            HttpOnly = true,
            Secure = secure,
            SameSite = SameSiteMode.Lax,
            Path = "/",
            Expires = accessExpiresAt
        });
    }

    public void ClearCustomerSessionCookie(HttpResponse response, bool isHttps)
    {
        var secure = ResolveSecure(isHttps);

        response.Cookies.Delete(AccessTokenCookieName, new CookieOptions
        {
            HttpOnly = true,
            Secure = secure,
            SameSite = SameSiteMode.Lax,
            Path = "/"
        });
    }

    public void SetCsrfCookie(HttpResponse response, bool isHttps, DateTimeOffset? expires = null)
    {
        var csrfToken = GenerateCsrfToken();
        var secure = ResolveSecure(isHttps);

        response.Cookies.Append(CsrfTokenCookieName, csrfToken, new CookieOptions
        {
            HttpOnly = false,
            Secure = secure,
            SameSite = SameSiteMode.Strict,
            Path = "/",
            Expires = expires ?? DateTimeOffset.UtcNow.AddDays(7)
        });
    }

    public void ClearCsrfCookie(HttpResponse response, bool isHttps)
    {
        var secure = ResolveSecure(isHttps);
        response.Cookies.Delete(CsrfTokenCookieName, new CookieOptions
        {
            HttpOnly = false,
            Secure = secure,
            SameSite = SameSiteMode.Strict,
            Path = "/"
        });
    }

    public void SetTerminalCookie(HttpResponse response, Guid terminalId, string deviceSecret, bool isHttps)
    {
        var secure = ResolveSecure(isHttps);
        var payload = $"{terminalId:D}:{deviceSecret}";
        var encoded = Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes(payload));

        response.Cookies.Append(TerminalCredCookieName, encoded, new CookieOptions
        {
            HttpOnly = true,
            Secure = secure,
            SameSite = SameSiteMode.Lax,
            Path = "/api/v1",
            Expires = DateTimeOffset.UtcNow.AddYears(1)
        });

        // Concurrently issue fresh CSRF cookie for the enrolled terminal
        SetCsrfCookie(response, isHttps, DateTimeOffset.UtcNow.AddYears(1));
    }

    public void ClearTerminalCookie(HttpResponse response, bool isHttps)
    {
        var secure = ResolveSecure(isHttps);
        response.Cookies.Delete(TerminalCredCookieName, new CookieOptions
        {
            HttpOnly = true,
            Secure = secure,
            SameSite = SameSiteMode.Lax,
            Path = "/api/v1"
        });
        ClearCsrfCookie(response, isHttps);
    }

    public (Guid TerminalId, string DeviceSecret)? GetTerminalCredentials(HttpRequest request)
    {
        var cookie = request.Cookies[TerminalCredCookieName];
        if (string.IsNullOrWhiteSpace(cookie))
        {
            return null;
        }

        try
        {
            var decoded = System.Text.Encoding.UTF8.GetString(Convert.FromBase64String(cookie));
            var separatorIdx = decoded.IndexOf(':');
            if (separatorIdx > 0 &&
                Guid.TryParse(decoded.AsSpan(0, separatorIdx), out var terminalId))
            {
                var deviceSecret = decoded.Substring(separatorIdx + 1);
                if (!string.IsNullOrWhiteSpace(deviceSecret))
                {
                    return (terminalId, deviceSecret);
                }
            }
        }
        catch
        {
            // Ignore malformed terminal cookie
        }

        return null;
    }

    public string? GetRefreshToken(HttpRequest request)
    {
        return request.Cookies[RefreshTokenCookieName];
    }

    public string? GetAccessToken(HttpRequest request)
    {
        return request.Cookies[AccessTokenCookieName];
    }

    public string GenerateCsrfToken()
    {
        var bytes = new byte[32];
        RandomNumberGenerator.Fill(bytes);
        return Convert.ToHexString(bytes).ToLowerInvariant();
    }
}
