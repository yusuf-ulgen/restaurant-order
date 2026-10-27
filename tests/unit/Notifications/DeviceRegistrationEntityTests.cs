using System;
using RestaurantOrder.Domain.Auth;
using RestaurantOrder.Domain.Branches;
using RestaurantOrder.Domain.Common;
using RestaurantOrder.Domain.Floor;
using RestaurantOrder.Domain.Notifications;
using RestaurantOrder.Domain.Tenants;
using Xunit;

namespace RestaurantOrder.UnitTests.Notifications;

public class DeviceRegistrationEntityTests
{
    [Fact]
    public void CreateForStaff_InitializesStaffBoundRegistration()
    {
        var tenantId = new TenantId(Guid.NewGuid());
        var branchId = new BranchId(Guid.NewGuid());
        var userId = new UserId(Guid.NewGuid());
        var now = DateTimeOffset.UtcNow;
        var ttl = TimeSpan.FromDays(30);

        var reg = DeviceRegistration.CreateForStaff(
            tenantId,
            branchId,
            userId,
            "device-token-abc123_456",
            "android",
            now,
            ttl);

        Assert.NotEqual(Guid.Empty, reg.Id);
        Assert.Equal(tenantId, reg.TenantId);
        Assert.Equal(branchId, reg.BranchId);
        Assert.Equal(userId, reg.UserId);
        Assert.Null(reg.DiningSessionId);
        Assert.Equal("device-token-abc123_456", reg.InstallationId);
        Assert.Equal("android", reg.Platform);
        Assert.True(reg.IsActive);
        Assert.Equal(now, reg.CreatedAtUtc);
        Assert.Equal(now.Add(ttl), reg.ExpiresAtUtc);
    }

    [Fact]
    public void CreateForCustomer_InitializesSessionBoundRegistration()
    {
        var tenantId = new TenantId(Guid.NewGuid());
        var branchId = new BranchId(Guid.NewGuid());
        var sessionId = new DiningSessionId(Guid.NewGuid());
        var now = DateTimeOffset.UtcNow;
        var ttl = TimeSpan.FromHours(4);

        var reg = DeviceRegistration.CreateForCustomer(
            tenantId,
            branchId,
            sessionId,
            "web-push-install-xyz_890",
            "web",
            now,
            ttl);

        Assert.NotEqual(Guid.Empty, reg.Id);
        Assert.Equal(tenantId, reg.TenantId);
        Assert.Equal(branchId, reg.BranchId);
        Assert.Null(reg.UserId);
        Assert.Equal(sessionId, reg.DiningSessionId);
        Assert.Equal("web-push-install-xyz_890", reg.InstallationId);
        Assert.Equal("web", reg.Platform);
        Assert.True(reg.IsActive);
        Assert.Equal(now, reg.CreatedAtUtc);
        Assert.Equal(now.Add(ttl), reg.ExpiresAtUtc);
    }

    [Fact]
    public void Deactivate_SetsIsActiveFalseAndUpdatesTimestamp()
    {
        var reg = DeviceRegistration.CreateForCustomer(
            new TenantId(Guid.NewGuid()),
            new BranchId(Guid.NewGuid()),
            new DiningSessionId(Guid.NewGuid()),
            "token-123",
            "web",
            DateTimeOffset.UtcNow,
            TimeSpan.FromHours(2));

        var deactivateTime = DateTimeOffset.UtcNow.AddMinutes(30);
        reg.Deactivate(deactivateTime);

        Assert.False(reg.IsActive);
        Assert.Equal(deactivateTime, reg.UpdatedAtUtc);
    }

    [Fact]
    public void Renew_ReactivatesAndExtendsExpiry()
    {
        var now = DateTimeOffset.UtcNow;
        var reg = DeviceRegistration.CreateForStaff(
            new TenantId(Guid.NewGuid()),
            new BranchId(Guid.NewGuid()),
            new UserId(Guid.NewGuid()),
            "token-123",
            "ios",
            now,
            TimeSpan.FromDays(7));

        reg.Deactivate(now.AddDays(1));
        Assert.False(reg.IsActive);

        var renewTime = now.AddDays(2);
        var newExpiry = renewTime.AddDays(14);
        reg.Renew(newExpiry, renewTime);

        Assert.True(reg.IsActive);
        Assert.Equal(newExpiry, reg.ExpiresAtUtc);
        Assert.Equal(renewTime, reg.UpdatedAtUtc);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("invalid token with spaces")]
    [InlineData("token<script>alert(1)</script>")]
    public void Create_ThrowsDomainException_OnInvalidInstallationId(string invalidId)
    {
        Assert.Throws<DomainException>(() => DeviceRegistration.CreateForCustomer(
            new TenantId(Guid.NewGuid()),
            new BranchId(Guid.NewGuid()),
            new DiningSessionId(Guid.NewGuid()),
            invalidId,
            "web",
            DateTimeOffset.UtcNow,
            TimeSpan.FromHours(1)));
    }

    [Theory]
    [InlineData("windows")]
    [InlineData("macos")]
    [InlineData("blackberry")]
    [InlineData("")]
    public void Create_ThrowsDomainException_OnUnsupportedPlatform(string platform)
    {
        Assert.Throws<DomainException>(() => DeviceRegistration.CreateForCustomer(
            new TenantId(Guid.NewGuid()),
            new BranchId(Guid.NewGuid()),
            new DiningSessionId(Guid.NewGuid()),
            "token-123",
            platform,
            DateTimeOffset.UtcNow,
            TimeSpan.FromHours(1)));
    }
}
