using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using RestaurantOrder.Api.Auth;
using RestaurantOrder.Application.Auth;
using RestaurantOrder.Application.Floor;

namespace RestaurantOrder.Api.Floor;

public static class QrCustomerEndpoints
{
    public static IEndpointRouteBuilder MapQrCustomerEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/v1/qr")
            .WithTags("Customer QR");

        group.MapGet("/resolve", async (
            [FromQuery] string? token,
            HttpContext context,
            IQrPublicService qrPublicService,
            CancellationToken ct) =>
        {
            if (string.IsNullOrWhiteSpace(token))
            {
                return ProblemResult(context, StatusCodes.Status400BadRequest, "Bad Request", "QR token query parameter is required.");
            }

            var ip = context.Connection.RemoteIpAddress?.ToString() ?? "127.0.0.1";
            try
            {
                var response = await qrPublicService.ResolveQrAsync(token, ip, ct);
                return Results.Ok(response);
            }
            catch (Exception ex)
            {
                return HandleCustomerException(ex, context);
            }
        })
        .WithName("ResolveQrGet")
        .WithSummary("Resolve a signed QR token and retrieve table/welcome info (GET)");

        group.MapPost("/resolve", async (
            [FromBody] QrExchangeRequest request,
            HttpContext context,
            IQrPublicService qrPublicService,
            CancellationToken ct) =>
        {
            if (string.IsNullOrWhiteSpace(request?.Token))
            {
                return ProblemResult(context, StatusCodes.Status400BadRequest, "Bad Request", "QR token is required.");
            }

            var ip = context.Connection.RemoteIpAddress?.ToString() ?? "127.0.0.1";
            try
            {
                var response = await qrPublicService.ResolveQrAsync(request.Token, ip, ct);
                return Results.Ok(response);
            }
            catch (Exception ex)
            {
                return HandleCustomerException(ex, context);
            }
        })
        .WithName("ResolveQrPost")
        .WithSummary("Resolve a signed QR token and retrieve table/welcome info (POST)");

        group.MapPost("/exchange", async (
            [FromBody] QrExchangeRequest request,
            HttpContext context,
            IQrPublicService qrPublicService,
            IAuthCookieService cookieService,
            CancellationToken ct) =>
        {
            if (string.IsNullOrWhiteSpace(request?.Token))
            {
                return ProblemResult(context, StatusCodes.Status400BadRequest, "Bad Request", "QR token is required.");
            }

            var ip = context.Connection.RemoteIpAddress?.ToString() ?? "127.0.0.1";
            try
            {
                var result = await qrPublicService.ExchangeQrAsync(request.Token, ip, ct);

                // Set HttpOnly, Secure, SameSite=Lax cookie for customer session
                var isHttps = context.Request.IsHttps;
                cookieService.SetCustomerSessionCookie(
                    context.Response,
                    result.AccessToken,
                    result.AccessTokenExpiresAt,
                    isHttps);

                return Results.Ok(new
                {
                    sessionId = result.SessionId,
                    sessionStatus = result.SessionStatus,
                    accessTokenExpiresAt = result.AccessTokenExpiresAt,
                    tenantId = result.TenantId,
                    branchId = result.BranchId,
                    tableNumber = result.TableNumber,
                    tableName = result.TableName
                });
            }
            catch (Exception ex)
            {
                return HandleCustomerException(ex, context);
            }
        })
        .WithName("ExchangeQr")
        .WithSummary("Exchange a signed QR token for customer dining session credentials");

        return app;
    }

    internal static IResult HandleCustomerException(Exception ex, HttpContext context)
    {
        return ex switch
        {
            QrRateLimitException rle => RateLimitProblem(context, rle),
            QrSecurityException se => ProblemResult(context, StatusCodes.Status400BadRequest, "Bad Request", se.Message),
            QrRevokedException re => ProblemResult(context, StatusCodes.Status410Gone, "QR Code Revoked", re.Message),
            QrSessionClosedException sce => ProblemResult(context, StatusCodes.Status410Gone, "Session Closed", sce.Message),
            QrTableInactiveException tie => ProblemResult(context, StatusCodes.Status400BadRequest, "Table Inactive", tie.Message),
            DistributedSecurityStateUnavailableException => ProblemResult(context, StatusCodes.Status503ServiceUnavailable, "Service Unavailable", "Rate limiting service temporarily unavailable."),
            _ => ProblemResult(context, StatusCodes.Status500InternalServerError, "Internal Server Error", "An unexpected error occurred while processing the QR request.")
        };
    }

    private static IResult RateLimitProblem(HttpContext context, QrRateLimitException rle)
    {
        context.Response.Headers.RetryAfter = rle.RetryAfterSeconds.ToString();
        return Results.Json(new
        {
            type = "https://httpstatuses.com/429",
            title = "Too Many Requests",
            status = StatusCodes.Status429TooManyRequests,
            detail = rle.Message,
            instance = context.Request.Path.Value,
            retryAfterSeconds = rle.RetryAfterSeconds
        }, statusCode: StatusCodes.Status429TooManyRequests, contentType: "application/problem+json");
    }

    private static IResult ProblemResult(HttpContext context, int status, string title, string detail)
    {
        return Results.Json(new
        {
            type = $"https://httpstatuses.com/{status}",
            title,
            status,
            detail,
            instance = context.Request.Path.Value
        }, statusCode: status, contentType: "application/problem+json");
    }
}
