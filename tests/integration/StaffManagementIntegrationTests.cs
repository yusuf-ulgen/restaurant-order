using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using RestaurantOrder.Api.Auth;
using RestaurantOrder.Application.Auth;
using Xunit;

namespace RestaurantOrder.IntegrationTests;

public class StaffManagementIntegrationTests : IClassFixture<WebApplicationFactory<RestaurantOrder.Api.Program>>
{
    private readonly WebApplicationFactory<RestaurantOrder.Api.Program> _factory;

    public StaffManagementIntegrationTests(WebApplicationFactory<RestaurantOrder.Api.Program> factory)
    {
        _factory = factory;
    }

    private HttpClient CreateClient(Mock<IStaffIdentityService>? mockStaffService = null)
    {
        return _factory.WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment("Development");
            builder.ConfigureServices(services =>
            {
                if (mockStaffService != null)
                {
                    services.AddScoped(_ => mockStaffService.Object);
                }
            });
        }).CreateClient(new WebApplicationFactoryClientOptions
        {
            HandleCookies = false
        });
    }

    [Fact]
    public async Task AcceptInvitation_ValidToken_Returns200()
    {
        var mockStaffService = new Mock<IStaffIdentityService>();
        mockStaffService.Setup(s => s.AcceptInvitationAsync(It.IsAny<AcceptInvitationCommand>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        var client = CreateClient(mockStaffService);

        var request = new AcceptInvitationApiRequest("valid_token_123", "NewPassword123!");
        var response = await client.PostAsJsonAsync("/api/v1/staff/accept-invitation", request);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task AcceptInvitation_InvalidToken_Returns400()
    {
        var mockStaffService = new Mock<IStaffIdentityService>();
        mockStaffService.Setup(s => s.AcceptInvitationAsync(It.IsAny<AcceptInvitationCommand>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("Invalid, expired, or already consumed invitation token."));

        var client = CreateClient(mockStaffService);

        var request = new AcceptInvitationApiRequest("invalid_token", "NewPassword123!");
        var response = await client.PostAsJsonAsync("/api/v1/staff/accept-invitation", request);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task ForgotPassword_Returns200GenericResponse()
    {
        var mockStaffService = new Mock<IStaffIdentityService>();
        mockStaffService.Setup(s => s.RequestPasswordResetAsync(It.IsAny<RequestPasswordResetCommand>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync("raw_reset_token");

        var client = CreateClient(mockStaffService);

        var request = new ForgotPasswordApiRequest("staff@test.com", "test-tenant");
        var response = await client.PostAsJsonAsync("/api/v1/staff/forgot-password", request);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var content = await response.Content.ReadAsStringAsync();
        Assert.Contains("password reset link has been dispatched", content);
    }

    [Fact]
    public async Task ResetPassword_ValidToken_Returns200()
    {
        var mockStaffService = new Mock<IStaffIdentityService>();
        mockStaffService.Setup(s => s.ResetPasswordAsync(It.IsAny<ResetPasswordCommand>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        var client = CreateClient(mockStaffService);

        var request = new ResetPasswordApiRequest("valid_reset_token", "BrandNewPassword123!");
        var response = await client.PostAsJsonAsync("/api/v1/staff/reset-password", request);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task ResetPassword_InvalidToken_Returns400()
    {
        var mockStaffService = new Mock<IStaffIdentityService>();
        mockStaffService.Setup(s => s.ResetPasswordAsync(It.IsAny<ResetPasswordCommand>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("Invalid, expired, or already consumed password reset token."));

        var client = CreateClient(mockStaffService);

        var request = new ResetPasswordApiRequest("invalid_token", "BrandNewPassword123!");
        var response = await client.PostAsJsonAsync("/api/v1/staff/reset-password", request);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }
}
