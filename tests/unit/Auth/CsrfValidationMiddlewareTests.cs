using System.IO;
using Microsoft.AspNetCore.Http;
using RestaurantOrder.Api.Auth;
using Xunit;

namespace RestaurantOrder.UnitTests.Auth;

public class CsrfValidationMiddlewareTests
{
    [Theory]
    [InlineData("GET")]
    [InlineData("HEAD")]
    [InlineData("OPTIONS")]
    public async Task NonMutationMethod_BypassesCsrfValidation(string method)
    {
        var nextCalled = false;
        RequestDelegate next = ctx =>
        {
            nextCalled = true;
            return Task.CompletedTask;
        };

        var middleware = new CsrfValidationMiddleware(next);
        var context = new DefaultHttpContext();
        context.Request.Method = method;
        context.Request.Path = "/api/v1/orders";

        await middleware.InvokeAsync(context);

        Assert.True(nextCalled);
    }

    [Theory]
    [InlineData("/api/v1/auth/login")]
    [InlineData("/api/v1/dev/seed")]
    [InlineData("/health/ready")]
    public async Task ExemptEndpoints_BypassCsrfValidation_EvenOnPost(string path)
    {
        var nextCalled = false;
        RequestDelegate next = ctx =>
        {
            nextCalled = true;
            return Task.CompletedTask;
        };

        var middleware = new CsrfValidationMiddleware(next);
        var context = new DefaultHttpContext();
        context.Request.Method = "POST";
        context.Request.Path = path;

        await middleware.InvokeAsync(context);

        Assert.True(nextCalled);
    }

    [Fact]
    public async Task MutationMethod_WithoutAuthCookie_BypassesCsrfValidation()
    {
        var nextCalled = false;
        RequestDelegate next = ctx =>
        {
            nextCalled = true;
            return Task.CompletedTask;
        };

        var middleware = new CsrfValidationMiddleware(next);
        var context = new DefaultHttpContext();
        context.Request.Method = "POST";
        context.Request.Path = "/api/v1/orders";

        await middleware.InvokeAsync(context);

        Assert.True(nextCalled);
    }

    [Fact]
    public async Task MutationMethod_WithAuthCookie_MissingCsrfCookie_Returns403()
    {
        var nextCalled = false;
        RequestDelegate next = ctx =>
        {
            nextCalled = true;
            return Task.CompletedTask;
        };

        var middleware = new CsrfValidationMiddleware(next);
        var context = new DefaultHttpContext();
        context.Request.Method = "POST";
        context.Request.Path = "/api/v1/orders";
        context.Request.Headers.Cookie = $"{AuthCookieService.AccessTokenCookieName}=jwt_token_val";
        context.Response.Body = new MemoryStream();

        await middleware.InvokeAsync(context);

        Assert.False(nextCalled);
        Assert.Equal(StatusCodes.Status403Forbidden, context.Response.StatusCode);
        Assert.Equal("application/problem+json", context.Response.ContentType);
    }

    [Fact]
    public async Task MutationMethod_WithAuthCookie_MissingCsrfHeader_Returns403()
    {
        var nextCalled = false;
        RequestDelegate next = ctx =>
        {
            nextCalled = true;
            return Task.CompletedTask;
        };

        var middleware = new CsrfValidationMiddleware(next);
        var context = new DefaultHttpContext();
        context.Request.Method = "POST";
        context.Request.Path = "/api/v1/orders";
        context.Request.Headers.Cookie = $"{AuthCookieService.AccessTokenCookieName}=jwt_token_val; {AuthCookieService.CsrfTokenCookieName}=csrf_secret_val";
        context.Response.Body = new MemoryStream();

        await middleware.InvokeAsync(context);

        Assert.False(nextCalled);
        Assert.Equal(StatusCodes.Status403Forbidden, context.Response.StatusCode);
    }

    [Fact]
    public async Task MutationMethod_WithAuthCookie_MismatchedCsrfToken_Returns403()
    {
        var nextCalled = false;
        RequestDelegate next = ctx =>
        {
            nextCalled = true;
            return Task.CompletedTask;
        };

        var middleware = new CsrfValidationMiddleware(next);
        var context = new DefaultHttpContext();
        context.Request.Method = "POST";
        context.Request.Path = "/api/v1/orders";
        context.Request.Headers.Cookie = $"{AuthCookieService.AccessTokenCookieName}=jwt_token_val; {AuthCookieService.CsrfTokenCookieName}=csrf_secret_val";
        context.Request.Headers[AuthCookieService.CsrfHeaderName] = "different_secret_val";
        context.Response.Body = new MemoryStream();

        await middleware.InvokeAsync(context);

        Assert.False(nextCalled);
        Assert.Equal(StatusCodes.Status403Forbidden, context.Response.StatusCode);
    }

    [Fact]
    public async Task MutationMethod_WithAuthCookie_MatchingCsrfToken_PassesThrough()
    {
        var nextCalled = false;
        RequestDelegate next = ctx =>
        {
            nextCalled = true;
            return Task.CompletedTask;
        };

        var token = "secret_csrf_token_123456789";
        var middleware = new CsrfValidationMiddleware(next);
        var context = new DefaultHttpContext();
        context.Request.Method = "POST";
        context.Request.Path = "/api/v1/orders";
        context.Request.Headers.Cookie = $"{AuthCookieService.AccessTokenCookieName}=jwt_token_val; {AuthCookieService.CsrfTokenCookieName}={token}";
        context.Request.Headers[AuthCookieService.CsrfHeaderName] = token;

        await middleware.InvokeAsync(context);

        Assert.True(nextCalled);
    }
}
