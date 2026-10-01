using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using RestaurantOrder.Application.Auth;
using RestaurantOrder.Domain.Auth;

namespace RestaurantOrder.Infrastructure.Auth;

/// <summary>
/// Central ASP.NET Core authorization handler evaluating PermissionRequirement against
/// the centralized 8-role x 31-capability matrix and resource ownership requirements.
/// </summary>
public sealed class PermissionAuthorizationHandler : AuthorizationHandler<PermissionRequirement>
{
    private readonly IPermissionRegistry _permissionRegistry;
    private readonly IJwtClaimPrincipalParser _principalParser;
    private readonly IResourceOwnershipRequirement _ownershipRequirement;

    public PermissionAuthorizationHandler(
        IPermissionRegistry permissionRegistry,
        IJwtClaimPrincipalParser principalParser,
        IResourceOwnershipRequirement? ownershipRequirement = null)
    {
        _permissionRegistry = permissionRegistry ?? throw new ArgumentNullException(nameof(permissionRegistry));
        _principalParser = principalParser ?? throw new ArgumentNullException(nameof(principalParser));
        _ownershipRequirement = ownershipRequirement ?? new DefaultResourceOwnershipRequirement();
    }

    protected override Task HandleRequirementAsync(
        AuthorizationHandlerContext context,
        PermissionRequirement requirement)
    {
        if (context.User.Identity?.IsAuthenticated != true)
        {
            return Task.CompletedTask;
        }

        AuthenticatedPrincipal principal;
        try
        {
            var claimsDict = context.User.Claims
                .GroupBy(c => c.Type)
                .ToDictionary(g => g.Key, g => g.First().Value, StringComparer.Ordinal);

            principal = _principalParser.ParsePrincipal(claimsDict);
        }
        catch (JwtClaimValidationException)
        {
            context.Fail();
            return Task.CompletedTask;
        }

        var grantType = _permissionRegistry.GetGrantType(principal.Role, requirement.Permission);

        switch (grantType)
        {
            case PermissionGrantType.Full:
                context.Succeed(requirement);
                break;

            case PermissionGrantType.OwnOrAssigned:
                if (context.Resource is ResourceOwnershipContext resourceContext &&
                    _ownershipRequirement.Satisfies(principal, requirement.Permission, resourceContext))
                {
                    context.Succeed(requirement);
                }
                else
                {
                    context.Fail();
                }
                break;

            case PermissionGrantType.Denied:
            default:
                context.Fail();
                break;
        }

        return Task.CompletedTask;
    }
}
