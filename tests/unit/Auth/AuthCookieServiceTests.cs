using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Hosting;
using Moq;
using RestaurantOrder.Api.Auth;
using Xunit;

namespace RestaurantOrder.UnitTests.Auth;

public class AuthCookieServiceTests
{
    private static IHostEnvironment CreateEnv(string name)
    {
        var mock = new Mock<IHostEnvironment>();
        mock.Setup(e => e.EnvironmentName).Returns(name);
        return mock.Object;
    }

    [Theory]
    [InlineData("Development", false, false)]
    [InlineData("Development", true, true)]
    [InlineData("Production", false, true)]
    [InlineData("Production", true, true)]
    [InlineData("Staging", false, true)]
    [InlineData("Staging", true, true)]
    public void SetAuthCookies_EnforcesSecureBasedOnEnvironmentAndScheme(string envName, bool isHttps, bool expectedSecure)
    {
        var env = CreateEnv(envName);
        var service = new AuthCookieService(env);
        var httpContext = new DefaultHttpContext();

        var accessExpires = DateTimeOffset.UtcNow.AddMinutes(15);
        var refreshExpires = DateTimeOffset.UtcNow.AddHours(8);

        service.SetAuthCookies(httpContext.Response, "access-token-123", "refresh-token-456", accessExpires, refreshExpires, isHttps);

        var setCookies = httpContext.Response.Headers.SetCookie.ToArray();
        Assert.True(setCookies.Length >= 3);

        foreach (var cookie in setCookies)
        {
            Assert.NotNull(cookie);
            var hasSecure = cookie!.Contains("secure", StringComparison.OrdinalIgnoreCase);
            Assert.Equal(expectedSecure, hasSecure);
        }
    }

    [Fact]
    public void SetTerminalCookie_And_Clear_WorksCorrectly()
    {
        var service = new AuthCookieService(CreateEnv("Development"));
        var httpContext = new DefaultHttpContext();
        var terminalId = Guid.NewGuid();
        var sampleToken = "sample-device-token-123";

        service.SetTerminalCookie(httpContext.Response, terminalId, sampleToken, isHttps: false);

        var cookies = httpContext.Response.Headers.SetCookie.ToArray();
        var terminalCookie = cookies.FirstOrDefault(c => c != null && c.Contains(AuthCookieService.TerminalCredCookieName));
        Assert.NotNull(terminalCookie);
        Assert.Contains("httponly", terminalCookie, StringComparison.OrdinalIgnoreCase);

        // Clear terminal credentials
        var clearContext = new DefaultHttpContext();
        service.ClearTerminalCookie(clearContext.Response, isHttps: false);
        var cleared = clearContext.Response.Headers.SetCookie.ToArray();
        var clearedCookie = cleared.FirstOrDefault(c => c != null && c.Contains(AuthCookieService.TerminalCredCookieName));
        Assert.NotNull(clearedCookie);
        Assert.Contains("expires=", clearedCookie, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ClearAuthCookies_RemovesAccessRefreshAndCsrfCookies()
    {
        var service = new AuthCookieService(CreateEnv("Development"));
        var httpContext = new DefaultHttpContext();

        service.ClearAuthCookies(httpContext.Response, isHttps: false);

        var cookies = httpContext.Response.Headers.SetCookie.ToArray();
        Assert.Contains(cookies, c => c != null && c.Contains(AuthCookieService.AccessTokenCookieName) && c.Contains("expires="));
        Assert.Contains(cookies, c => c != null && c.Contains(AuthCookieService.RefreshTokenCookieName) && c.Contains("expires="));
        Assert.Contains(cookies, c => c != null && c.Contains(AuthCookieService.CsrfTokenCookieName) && c.Contains("expires="));
    }

    [Fact]
    public void GetTerminalCredentials_ValidCookie_ReturnsTerminalIdAndSecret()
    {
        var service = new AuthCookieService(CreateEnv("Development"));
        var httpContext = new DefaultHttpContext();
        var terminalId = Guid.NewGuid();
        var expectedToken = "sample-device-token-123";

        var payload = Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes($"{terminalId:D}:{expectedToken}"));
        httpContext.Request.Headers.Cookie = $"{AuthCookieService.TerminalCredCookieName}={payload}";

        var result = service.GetTerminalCredentials(httpContext.Request);
        Assert.NotNull(result);
        Assert.Equal(terminalId, result.Value.TerminalId);
        Assert.Equal(expectedToken, result.Value.DeviceSecret);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("invalid-base64-%%!")]
    [InlineData("bm9zZXBhcmF0b3I=")] // "noseparator"
    [InlineData("OnNlY3JldA==")] // ":secret" (no terminal ID)
    [InlineData("bm90LWEtZ3VpZDpzZWNyZXQ=")] // "not-a-guid:secret"
    public void GetTerminalCredentials_MalformedCookies_ReturnsNullSafely(string? cookieValue)
    {
        var service = new AuthCookieService(CreateEnv("Development"));
        var httpContext = new DefaultHttpContext();
        if (cookieValue != null)
        {
            httpContext.Request.Headers.Cookie = $"{AuthCookieService.TerminalCredCookieName}={cookieValue}";
        }

        var result = service.GetTerminalCredentials(httpContext.Request);
        Assert.Null(result);
    }

    [Fact]
    public void GetTerminalCredentials_WhitespaceSecret_ReturnsNull()
    {
        var service = new AuthCookieService(CreateEnv("Development"));
        var httpContext = new DefaultHttpContext();
        var payload = Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes($"{Guid.NewGuid():D}:   "));
        httpContext.Request.Headers.Cookie = $"{AuthCookieService.TerminalCredCookieName}={payload}";

        var result = service.GetTerminalCredentials(httpContext.Request);
        Assert.Null(result);
    }

    [Fact]
    public void GetAccessTokenAndRefreshToken_ExtractsCookies()
    {
        var service = new AuthCookieService(CreateEnv("Development"));
        var httpContext = new DefaultHttpContext();
        httpContext.Request.Headers.Cookie = $"{AuthCookieService.AccessTokenCookieName}=access.token; {AuthCookieService.RefreshTokenCookieName}=refresh.token";

        Assert.Equal("access.token", service.GetAccessToken(httpContext.Request));
        Assert.Equal("refresh.token", service.GetRefreshToken(httpContext.Request));
    }

    [Fact]
    public void GenerateCsrfToken_Returns64CharHexString()
    {
        var service = new AuthCookieService(CreateEnv("Development"));
        var token1 = service.GenerateCsrfToken();
        var token2 = service.GenerateCsrfToken();

        Assert.NotNull(token1);
        Assert.Equal(64, token1.Length);
        Assert.NotEqual(token1, token2);
    }
}
