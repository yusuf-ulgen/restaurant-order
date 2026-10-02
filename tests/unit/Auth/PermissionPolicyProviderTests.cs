using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.Options;
using RestaurantOrder.Api.Auth;
using RestaurantOrder.Infrastructure.Auth;
using Xunit;

namespace RestaurantOrder.UnitTests.Auth;

public class PermissionPolicyProviderTests
{
    [Fact]
    public async Task GetPolicyAsync_WithPermissionPrefix_BuildsPolicyRequiringPermission()
    {
        var options = Options.Create(new AuthorizationOptions());
        var provider = new PermissionPolicyProvider(options);

        var policy = await provider.GetPolicyAsync("Permission:menu.catalog.manage");

        Assert.NotNull(policy);
        Assert.Contains(policy.Requirements, r => r is PermissionRequirement pr && pr.Permission == "menu.catalog.manage");
    }

    [Fact]
    public async Task GetPolicyAsync_WithoutPermissionPrefix_FallsBackToDefaultProvider()
    {
        var authOptions = new AuthorizationOptions();
        authOptions.AddPolicy("CustomRolePolicy", builder => builder.RequireRole("Admin"));

        var options = Options.Create(authOptions);
        var provider = new PermissionPolicyProvider(options);

        var policy = await provider.GetPolicyAsync("CustomRolePolicy");

        Assert.NotNull(policy);
        Assert.Null(await provider.GetPolicyAsync("NonExistentPolicy"));
    }

    [Fact]
    public async Task GetDefaultAndFallbackPolicies_DelegateToFallback()
    {
        var options = Options.Create(new AuthorizationOptions());
        var provider = new PermissionPolicyProvider(options);

        var defaultPolicy = await provider.GetDefaultPolicyAsync();
        Assert.NotNull(defaultPolicy);

        var fallbackPolicy = await provider.GetFallbackPolicyAsync();
        Assert.Null(fallbackPolicy); // Default AuthorizationOptions has null fallback policy
    }
}
