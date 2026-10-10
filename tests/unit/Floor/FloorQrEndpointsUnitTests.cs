using System.Text.Json;
using Microsoft.AspNetCore.Http;
using Moq;
using RestaurantOrder.Api.Auth;
using RestaurantOrder.Api.Floor;
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

    [Fact]
    public void HandleCustomerException_MapsAllQrExceptionTypesProperly()
    {
        var ctx1 = new DefaultHttpContext();
        var rateLimitRes = QrCustomerEndpoints.HandleCustomerException(new QrRateLimitException(45, "Too many requests"), ctx1);
        Assert.NotNull(rateLimitRes);
        Assert.Equal("45", ctx1.Response.Headers.RetryAfter.ToString());

        var ctx2 = new DefaultHttpContext();
        var secRes = QrCustomerEndpoints.HandleCustomerException(new QrSecurityException("Signature invalid"), ctx2);
        Assert.NotNull(secRes);

        var ctx3 = new DefaultHttpContext();
        var revokedRes = QrCustomerEndpoints.HandleCustomerException(new QrRevokedException("QR has been rotated"), ctx3);
        Assert.NotNull(revokedRes);

        var ctx4 = new DefaultHttpContext();
        var closedRes = QrCustomerEndpoints.HandleCustomerException(new QrSessionClosedException("Dining session is closed"), ctx4);
        Assert.NotNull(closedRes);

        var ctx5 = new DefaultHttpContext();
        var inactiveRes = QrCustomerEndpoints.HandleCustomerException(new QrTableInactiveException("Table is inactive"), ctx5);
        Assert.NotNull(inactiveRes);

        var ctx6 = new DefaultHttpContext();
        var unavailRes = QrCustomerEndpoints.HandleCustomerException(new DistributedSecurityStateUnavailableException("Store unavailable"), ctx6);
        Assert.NotNull(unavailRes);

        var ctx7 = new DefaultHttpContext();
        var genericRes = QrCustomerEndpoints.HandleCustomerException(new InvalidOperationException("Unexpected error"), ctx7);
        Assert.NotNull(genericRes);
    }
}

