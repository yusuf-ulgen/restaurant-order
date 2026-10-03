using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using RestaurantOrder.Api.RestaurantConfig;
using RestaurantOrder.Application.RestaurantConfig;
using RestaurantOrder.Domain.Branches;
using RestaurantOrder.Infrastructure.Persistence;
using Xunit;

namespace RestaurantOrder.IntegrationTests;

public class RestaurantConfigOperatingHoursIntegrationTests : IClassFixture<TestcontainersFixture>
{
    private readonly TestcontainersFixture _fixture;
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public RestaurantConfigOperatingHoursIntegrationTests(TestcontainersFixture fixture)
    {
        _fixture = fixture;
    }

    private async Task EnsureMigrationsAppliedAsync()
    {
        var options = new DbContextOptionsBuilder<RestaurantOrderDbContext>()
            .UseNpgsql(_fixture.DatabaseConnectionString, npgsqlOptions =>
            {
                npgsqlOptions.MigrationsAssembly(typeof(RestaurantOrderDbContext).Assembly.FullName);
            })
            .Options;

        await using var context = new RestaurantOrderDbContext(options);
        await context.Database.MigrateAsync();
    }

    [Fact]
    public async Task GetOperatingHours_ReturnsDefault7DaySchedule_WhenNotCustomized()
    {
        if (!TestcontainersGuard.ShouldRun(_fixture)) return;
        await EnsureMigrationsAppliedAsync();

        var tenantId = Guid.NewGuid();
        await RestaurantConfigTestHelpers.SeedTenantAsync(_fixture.DatabaseConnectionString, tenantId, "OH Tenant", $"oht-{Guid.NewGuid():N}");

        var client = RestaurantConfigTestHelpers.CreateTestClient(_fixture);
        var adminToken = RestaurantConfigTestHelpers.GenerateToken(Guid.NewGuid(), Guid.NewGuid(), tenantId, role: "RestaurantAdmin");

        var brandReq = RestaurantConfigTestHelpers.CreateAuthenticatedRequest(HttpMethod.Post, "/api/v1/restaurant-config/brands", adminToken);
        brandReq.Content = JsonContent.Create(new CreateBrandApiRequest("OH Brand", $"ohb-{Guid.NewGuid():N}"));
        var brandResp = await client.SendAsync(brandReq);
        var brand = await brandResp.Content.ReadFromJsonAsync<BrandDto>();
        Assert.NotNull(brand);

        var branchReq = RestaurantConfigTestHelpers.CreateAuthenticatedRequest(HttpMethod.Post, "/api/v1/restaurant-config/branches", adminToken);
        branchReq.Content = JsonContent.Create(new CreateBranchApiRequest(brand.Id, "OH Branch", $"ohbr-{Guid.NewGuid():N}"));
        var branchResp = await client.SendAsync(branchReq);
        var branch = await branchResp.Content.ReadFromJsonAsync<BranchDto>();
        Assert.NotNull(branch);

        var getReq = RestaurantConfigTestHelpers.CreateAuthenticatedRequest(HttpMethod.Get, $"/api/v1/restaurant-config/branches/{branch.Id}/operating-hours", adminToken);
        var getResp = await client.SendAsync(getReq);
        Assert.Equal(HttpStatusCode.OK, getResp.StatusCode);
        Assert.True(getResp.Headers.Contains("ETag"));

        var hours = await getResp.Content.ReadFromJsonAsync<BranchOperatingHoursDto>(JsonOptions);
        Assert.NotNull(hours);
        Assert.Equal(branch.Id, hours.BranchId);
        Assert.Equal(7, hours.Days.Count);
        Assert.All(hours.Days, d =>
        {
            Assert.False(d.IsClosed);
            Assert.Single(d.Slots);
            Assert.Equal("09:00", d.Slots[0].OpenTime);
            Assert.Equal("22:00", d.Slots[0].CloseTime);
            Assert.False(d.Slots[0].IsOvernight);
        });
    }

    [Fact]
    public async Task UpdateOperatingHours_ValidScheduleWithOvernight_PersistsSuccessfully()
    {
        if (!TestcontainersGuard.ShouldRun(_fixture)) return;
        await EnsureMigrationsAppliedAsync();

        var tenantId = Guid.NewGuid();
        await RestaurantConfigTestHelpers.SeedTenantAsync(_fixture.DatabaseConnectionString, tenantId, "OH Update Tenant", $"ohut-{Guid.NewGuid():N}");

        var client = RestaurantConfigTestHelpers.CreateTestClient(_fixture);
        var adminToken = RestaurantConfigTestHelpers.GenerateToken(Guid.NewGuid(), Guid.NewGuid(), tenantId, role: "RestaurantAdmin");

        var brandReq = RestaurantConfigTestHelpers.CreateAuthenticatedRequest(HttpMethod.Post, "/api/v1/restaurant-config/brands", adminToken);
        brandReq.Content = JsonContent.Create(new CreateBrandApiRequest("OH Upd Brand", $"ohub-{Guid.NewGuid():N}"));
        var brandResp = await client.SendAsync(brandReq);
        var brand = await brandResp.Content.ReadFromJsonAsync<BrandDto>();
        Assert.NotNull(brand);

        var branchReq = RestaurantConfigTestHelpers.CreateAuthenticatedRequest(HttpMethod.Post, "/api/v1/restaurant-config/branches", adminToken);
        branchReq.Content = JsonContent.Create(new CreateBranchApiRequest(brand.Id, "OH Upd Branch", $"ohubr-{Guid.NewGuid():N}"));
        var branchResp = await client.SendAsync(branchReq);
        var branch = await branchResp.Content.ReadFromJsonAsync<BranchDto>();
        Assert.NotNull(branch);

        // Schedule: Monday closed, Friday overnight (18:00 - 02:00), other days normal
        var days = new List<OperatingDayScheduleApiRequest>();
        for (int i = 0; i < 7; i++)
        {
            if (i == (int)DayOfWeek.Monday)
            {
                days.Add(new OperatingDayScheduleApiRequest(i, IsClosed: true, Slots: Array.Empty<TimeSlotApiRequest>()));
            }
            else if (i == (int)DayOfWeek.Friday)
            {
                days.Add(new OperatingDayScheduleApiRequest(i, IsClosed: false, Slots: new[]
                {
                    new TimeSlotApiRequest("18:00", "02:00")
                }));
            }
            else if (i == (int)DayOfWeek.Saturday)
            {
                // Opens at 10:00, after Friday overnight finishes at 02:00
                days.Add(new OperatingDayScheduleApiRequest(i, IsClosed: false, Slots: new[]
                {
                    new TimeSlotApiRequest("10:00", "23:00")
                }));
            }
            else
            {
                days.Add(new OperatingDayScheduleApiRequest(i, IsClosed: false, Slots: new[]
                {
                    new TimeSlotApiRequest("10:00", "22:00")
                }));
            }
        }

        var updateReq = RestaurantConfigTestHelpers.CreateAuthenticatedRequest(HttpMethod.Put, $"/api/v1/restaurant-config/branches/{branch.Id}/operating-hours", adminToken);
        updateReq.Content = JsonContent.Create(new UpdateBranchOperatingHoursApiRequest(days, branch.ConcurrencyToken));
        var updateResp = await client.SendAsync(updateReq);
        Assert.Equal(HttpStatusCode.OK, updateResp.StatusCode);

        var updated = await updateResp.Content.ReadFromJsonAsync<BranchOperatingHoursDto>(JsonOptions);
        Assert.NotNull(updated);

        var monday = updated.Days.First(d => d.DayOfWeek == (int)DayOfWeek.Monday);
        Assert.True(monday.IsClosed);
        Assert.Empty(monday.Slots);

        var friday = updated.Days.First(d => d.DayOfWeek == (int)DayOfWeek.Friday);
        Assert.False(friday.IsClosed);
        Assert.Single(friday.Slots);
        Assert.Equal("18:00", friday.Slots[0].OpenTime);
        Assert.Equal("02:00", friday.Slots[0].CloseTime);
        Assert.True(friday.Slots[0].IsOvernight);
    }

    [Fact]
    public async Task UpdateOperatingHours_OverlapOrInvalidClosedDay_ReturnsBadRequest400()
    {
        if (!TestcontainersGuard.ShouldRun(_fixture)) return;
        await EnsureMigrationsAppliedAsync();

        var tenantId = Guid.NewGuid();
        await RestaurantConfigTestHelpers.SeedTenantAsync(_fixture.DatabaseConnectionString, tenantId, "OH Overlap Tenant", $"ohot-{Guid.NewGuid():N}");

        var client = RestaurantConfigTestHelpers.CreateTestClient(_fixture);
        var adminToken = RestaurantConfigTestHelpers.GenerateToken(Guid.NewGuid(), Guid.NewGuid(), tenantId, role: "RestaurantAdmin");

        var brandReq = RestaurantConfigTestHelpers.CreateAuthenticatedRequest(HttpMethod.Post, "/api/v1/restaurant-config/brands", adminToken);
        brandReq.Content = JsonContent.Create(new CreateBrandApiRequest("OH Inv Brand", $"ohib-{Guid.NewGuid():N}"));
        var brandResp = await client.SendAsync(brandReq);
        var brand = await brandResp.Content.ReadFromJsonAsync<BrandDto>();
        Assert.NotNull(brand);

        var branchReq = RestaurantConfigTestHelpers.CreateAuthenticatedRequest(HttpMethod.Post, "/api/v1/restaurant-config/branches", adminToken);
        branchReq.Content = JsonContent.Create(new CreateBranchApiRequest(brand.Id, "OH Inv Branch", $"ohibr-{Guid.NewGuid():N}"));
        var branchResp = await client.SendAsync(branchReq);
        var branch = await branchResp.Content.ReadFromJsonAsync<BranchDto>();
        Assert.NotNull(branch);

        // Case 1: Overlapping slots on Monday
        var overlapDays = new List<OperatingDayScheduleApiRequest>();
        for (int i = 0; i < 7; i++)
        {
            if (i == (int)DayOfWeek.Monday)
            {
                overlapDays.Add(new OperatingDayScheduleApiRequest(i, false, new[]
                {
                    new TimeSlotApiRequest("09:00", "15:00"),
                    new TimeSlotApiRequest("14:00", "22:00") // Overlaps 14:00 - 15:00
                }));
            }
            else
            {
                overlapDays.Add(new OperatingDayScheduleApiRequest(i, false, new[] { new TimeSlotApiRequest("09:00", "22:00") }));
            }
        }

        var req1 = RestaurantConfigTestHelpers.CreateAuthenticatedRequest(HttpMethod.Put, $"/api/v1/restaurant-config/branches/{branch.Id}/operating-hours", adminToken);
        req1.Content = JsonContent.Create(new UpdateBranchOperatingHoursApiRequest(overlapDays, branch.ConcurrencyToken));
        var resp1 = await client.SendAsync(req1);
        Assert.Equal(HttpStatusCode.BadRequest, resp1.StatusCode);

        // Case 2: Closed day with slots
        var closedWithSlotsDays = new List<OperatingDayScheduleApiRequest>();
        for (int i = 0; i < 7; i++)
        {
            if (i == (int)DayOfWeek.Tuesday)
            {
                closedWithSlotsDays.Add(new OperatingDayScheduleApiRequest(i, IsClosed: true, Slots: new[]
                {
                    new TimeSlotApiRequest("09:00", "15:00")
                }));
            }
            else
            {
                closedWithSlotsDays.Add(new OperatingDayScheduleApiRequest(i, false, new[] { new TimeSlotApiRequest("09:00", "22:00") }));
            }
        }

        var req2 = RestaurantConfigTestHelpers.CreateAuthenticatedRequest(HttpMethod.Put, $"/api/v1/restaurant-config/branches/{branch.Id}/operating-hours", adminToken);
        req2.Content = JsonContent.Create(new UpdateBranchOperatingHoursApiRequest(closedWithSlotsDays, branch.ConcurrencyToken));
        var resp2 = await client.SendAsync(req2);
        Assert.Equal(HttpStatusCode.BadRequest, resp2.StatusCode);
    }

    [Fact]
    public async Task MissingCsrfToken_WhenCookiePresent_ReturnsForbidden403()
    {
        if (!TestcontainersGuard.ShouldRun(_fixture)) return;
        await EnsureMigrationsAppliedAsync();

        var tenantId = Guid.NewGuid();
        await RestaurantConfigTestHelpers.SeedTenantAsync(_fixture.DatabaseConnectionString, tenantId, "CSRF Tenant", $"csrf-{Guid.NewGuid():N}");

        var client = RestaurantConfigTestHelpers.CreateTestClient(_fixture);
        var adminToken = RestaurantConfigTestHelpers.GenerateToken(Guid.NewGuid(), Guid.NewGuid(), tenantId, role: "RestaurantAdmin");

        var brandReq = RestaurantConfigTestHelpers.CreateAuthenticatedRequest(HttpMethod.Post, "/api/v1/restaurant-config/brands", adminToken);
        brandReq.Content = JsonContent.Create(new CreateBrandApiRequest("CSRF Brand", $"cb-{Guid.NewGuid():N}"));
        var brandResp = await client.SendAsync(brandReq);
        var brand = await brandResp.Content.ReadFromJsonAsync<BrandDto>();
        Assert.NotNull(brand);

        var branchReq = RestaurantConfigTestHelpers.CreateAuthenticatedRequest(HttpMethod.Post, "/api/v1/restaurant-config/branches", adminToken);
        branchReq.Content = JsonContent.Create(new CreateBranchApiRequest(brand.Id, "CSRF Branch", $"cbr-{Guid.NewGuid():N}"));
        var branchResp = await client.SendAsync(branchReq);
        var branch = await branchResp.Content.ReadFromJsonAsync<BranchDto>();
        Assert.NotNull(branch);

        // Send request with cookie but WITHOUT X-CSRF-Token header
        var req = new HttpRequestMessage(HttpMethod.Put, $"/api/v1/restaurant-config/branches/{branch.Id}/settings");
        req.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", adminToken);
        req.Headers.Add("Cookie", $"{RestaurantOrder.Api.Auth.AuthCookieService.CsrfTokenCookieName}=some_csrf_cookie");
        req.Content = JsonContent.Create(new UpdateBranchSettingsApiRequest(
            "Europe/Istanbul", "TRY", "tr-TR", new[] { "tr-TR" }, true, 1000, false, 0, true, ConcurrencyToken: branch.ConcurrencyToken));

        var resp = await client.SendAsync(req);
        Assert.Equal(HttpStatusCode.Forbidden, resp.StatusCode);
    }
}
