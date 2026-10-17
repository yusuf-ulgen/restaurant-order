using System.Security.Claims;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.Extensions.DependencyInjection;
using RestaurantOrder.Application.Auth;
using RestaurantOrder.Application.Floor;

namespace RestaurantOrder.Api.Auth;

/// <summary>
/// Helper for validating Customer JWT Bearer tokens in the ASP.NET Core pipeline.
/// Enforces mandatory GUID sub claim matching table_session_id, non-future iat,
/// and live session status verification via ICustomerSessionValidator.
/// </summary>
public static class CustomerTokenValidationHelper
{
    public static async Task<bool> ValidateCustomerTokenAsync(TokenValidatedContext context)
    {
        var principal = context.Principal;
        if (principal == null)
        {
            context.Fail("Customer token has no principal.");
            return false;
        }

        var tenantClaim = principal.FindFirst(JwtClaimNames.TenantId)?.Value
            ?? principal.FindFirst("tenant_id")?.Value;

        var branchClaim = principal.FindFirst(JwtClaimNames.BranchId)?.Value
            ?? principal.FindFirst("branch_id")?.Value;

        var tableSessionClaim = principal.FindFirst(JwtClaimNames.TableSessionId)?.Value
            ?? principal.FindFirst("table_session_id")?.Value;

        var customerSubClaim = principal.FindFirst(JwtClaimNames.Subject)?.Value
            ?? principal.FindFirst("sub")?.Value;

        if (!Guid.TryParse(tenantClaim, out var tenantId) || tenantId == Guid.Empty ||
            !Guid.TryParse(branchClaim, out var branchId) || branchId == Guid.Empty ||
            !Guid.TryParse(tableSessionClaim, out var tableSessionId) || tableSessionId == Guid.Empty)
        {
            context.Fail("Customer token is missing or has invalid required claims.");
            return false;
        }

        if (string.IsNullOrWhiteSpace(customerSubClaim))
        {
            context.Fail("Customer token is missing required sub claim.");
            return false;
        }

        if (!Guid.TryParse(customerSubClaim, out var parsedSub) || parsedSub == Guid.Empty)
        {
            context.Fail("Customer token sub claim is not a valid GUID.");
            return false;
        }

        if (parsedSub != tableSessionId)
        {
            context.Fail("Customer token claims mismatch: sub does not match table_session_id.");
            return false;
        }

        // /api/v1/customer/session endpoint'i veritabani uzerinden kapanmis oturumlari kontrol edip RFC 7807 Session Expired dondurur
        if (context.HttpContext.Request.Path.StartsWithSegments("/api/v1/customer/session"))
        {
            return true;
        }

        try
        {
            var customerValidator = context.HttpContext.RequestServices.GetRequiredService<ICustomerSessionValidator>();
            var isValid = await customerValidator.ValidateSessionActiveAsync(tenantId, branchId, tableSessionId, context.HttpContext.RequestAborted);
            if (!isValid)
            {
                context.Fail("Customer dining session is closed or invalid.");
                return false;
            }
        }
        catch (Exception)
        {
            context.Fail("Customer token validation failed.");
            return false;
        }

        return true;
    }
}
