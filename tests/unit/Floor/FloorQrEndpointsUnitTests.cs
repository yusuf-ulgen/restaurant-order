using System.Text.Json;
using Microsoft.AspNetCore.Http;
using Moq;
using RestaurantOrder.Api.Auth;
using RestaurantOrder.Application.Auth;
using RestaurantOrder.Application.Floor;
using RestaurantOrder.Domain.Auth;
using RestaurantOrder.Domain.Branches;
using RestaurantOrder.Domain.Floor;
using RestaurantOrder.Domain.Tenants;
using Xunit;

namespace RestaurantOrder.UnitTests.Floor;

public class FloorQrEndpointsUnitTests
{
    private readonly TenantId _tenantId = TenantId.New();
    private readonly BranchId _branchId = BranchId.New();

    [Fact]
    public void SetCustomerSessionCookie_AppendsHttpOnlyLaxCookie()
    {
        var cookieService = new AuthCookieService();
        var httpContext = new DefaultHttpContext();
        var expiresAt = DateTimeOffset.UtcNow.AddMinutes(15);
        var token = "jwt.customer.access.token";

        cookieService.SetCustomerSessionCookie(httpContext.Response, token, expiresAt, isHttps: true);

        var setCookieHeader = httpContext.Response.Headers.SetCookie.ToString();
        Assert.Contains(AuthCookieService.AccessTokenCookieName, setCookieHeader);
        Assert.Contains("httponly", setCookieHeader, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("samesite=lax", setCookieHeader, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ClearCustomerSessionCookie_DeletesAccessTokenCookie()
    {
        var cookieService = new AuthCookieService();
        var httpContext = new DefaultHttpContext();

        cookieService.ClearCustomerSessionCookie(httpContext.Response, isHttps: true);

        var setCookieHeader = httpContext.Response.Headers.SetCookie.ToString();
        Assert.Contains(AuthCookieService.AccessTokenCookieName, setCookieHeader);
    }

    [Fact]
    public async Task QrResolveResponse_ContainsNoSecretsOrPrivateKeys()
    {
        var response = new QrResolveResponse(
            TenantId: _tenantId.Value,
            BranchId: _branchId.Value,
            BrandName: "Test Brand",
            BranchName: "Downtown Branch",
            TableNumber: "T-01",
            TableName: "Table 1",
            Mode: "static",
            HasActiveSession: false,
            ActiveSessionStatus: null);

        var json = JsonSerializer.Serialize(response);

        Assert.DoesNotContain("secret", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("key", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("private", json, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Downtown Branch", json);
    }

    [Fact]
    public void QrExchangeResult_CarriesCustomerClaims()
    {
        var sessionId = Guid.NewGuid();
        var expiresAt = DateTimeOffset.UtcNow.AddMinutes(15);
        var result = new QrExchangeResult(
            AccessToken: "customer.jwt.token",
            AccessTokenExpiresAt: expiresAt,
            SessionId: sessionId,
            SessionStatus: "Open",
            TenantId: _tenantId.Value,
            BranchId: _branchId.Value,
            TableNumber: "T-01",
            TableName: "Table 1");

        Assert.Equal(sessionId, result.SessionId);
        Assert.Equal("Open", result.SessionStatus);
        Assert.Equal(_tenantId.Value, result.TenantId);
        Assert.Equal(_branchId.Value, result.BranchId);
    }
}
