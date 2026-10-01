using RestaurantOrder.Domain.Auth;

namespace RestaurantOrder.Application.Auth;

/// <summary>
/// Contract evaluating fine-grained resource ownership or assignment for permissions
/// classified as PermissionGrantType.OwnOrAssigned ('O' in the RBAC matrix).
/// </summary>
public interface IResourceOwnershipRequirement
{
    /// <summary>
    /// Evaluates whether the authenticated principal satisfies ownership or assignment for the requested resource.
    /// </summary>
    /// <param name="principal">The verified authenticated principal.</param>
    /// <param name="permission">The permission capability key being requested.</param>
    /// <param name="context">The contextual resource metadata.</param>
    /// <returns>True if the ownership/assignment criteria are met; otherwise false.</returns>
    bool Satisfies(AuthenticatedPrincipal principal, string permission, ResourceOwnershipContext context);
}
