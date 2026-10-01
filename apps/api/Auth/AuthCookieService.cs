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

    public void SetAuthCookies(
        HttpResponse response,
        string accessToken,
        string refreshToken,
        DateTimeOffset accessExpiresAt,
        DateTimeOffset refreshExpiresAt,
        bool isHttps)
    {
        var csrfToken = GenerateCsrfToken();

        // 1. Short-lived Access Token Cookie
        response.Cookies.Append(AccessTokenCookieName, accessToken, new CookieOptions
        {
            HttpOnly = true,
            Secure = isHttps,
            SameSite = SameSiteMode.Lax,
            Path = "/",
            Expires = accessExpiresAt
        });

        // 2. Rotating Refresh Token Cookie (strictly bounded to /api/v1/auth)
        response.Cookies.Append(RefreshTokenCookieName, refreshToken, new CookieOptions
        {
            HttpOnly = true,
            Secure = isHttps,
            SameSite = SameSiteMode.Strict,
            Path = "/api/v1/auth",
            Expires = refreshExpiresAt
        });

        // 3. Double-submit CSRF Cookie (readable by client to set in X-CSRF-Token header)
        response.Cookies.Append(CsrfTokenCookieName, csrfToken, new CookieOptions
        {
            HttpOnly = false,
            Secure = isHttps,
            SameSite = SameSiteMode.Strict,
            Path = "/",
            Expires = refreshExpiresAt
        });
    }

    public void ClearAuthCookies(HttpResponse response, bool isHttps)
    {
        response.Cookies.Delete(AccessTokenCookieName, new CookieOptions
        {
            HttpOnly = true,
            Secure = isHttps,
            SameSite = SameSiteMode.Lax,
            Path = "/"
        });

        response.Cookies.Delete(RefreshTokenCookieName, new CookieOptions
        {
            HttpOnly = true,
            Secure = isHttps,
            SameSite = SameSiteMode.Strict,
            Path = "/api/v1/auth"
        });

        response.Cookies.Delete(CsrfTokenCookieName, new CookieOptions
        {
            HttpOnly = false,
            Secure = isHttps,
            SameSite = SameSiteMode.Strict,
            Path = "/"
        });
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
