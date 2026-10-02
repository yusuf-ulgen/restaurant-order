using RestaurantOrder.Domain.Auth;
using RestaurantOrder.Domain.Branches;
using RestaurantOrder.Domain.Common;
using RestaurantOrder.Domain.Tenants;
using Xunit;

namespace RestaurantOrder.UnitTests.Auth;

public class IamEntityDomainRulesTests
{
    private readonly DateTimeOffset _now = DateTimeOffset.UtcNow;
    private readonly TenantId _tenantId = TenantId.New();
    private readonly BranchId _branchId = BranchId.New();

    [Fact]
    public void User_Create_NormalizesEmail_And_InitializesDefaults()
    {
        var user = User.Create("  Admin.User@Example.COM  ", "hash123", _now);

        Assert.Equal("Admin.User@Example.COM", user.Email);
        Assert.Equal("admin.user@example.com", user.NormalizedEmail);
        Assert.Equal("hash123", user.PasswordHash);
        Assert.Equal(UserStatus.Active, user.Status);
        Assert.Equal(1, user.SecurityVersion);
        Assert.Equal(0, user.FailedLoginAttempts);
        Assert.Null(user.LockoutEndUtc);
    }

    [Fact]
    public void User_ChangePassword_IncrementsSecurityVersion_And_ResetsAttempts()
    {
        var user = User.Create("user@example.com", "oldHash", _now);
        user.RecordFailedLogin(5, TimeSpan.FromMinutes(15), _now);
        Assert.Equal(1, user.FailedLoginAttempts);

        user.ChangePassword("newHash", _now.AddMinutes(1));

        Assert.Equal("newHash", user.PasswordHash);
        Assert.Equal(2, user.SecurityVersion);
        Assert.Equal(0, user.FailedLoginAttempts);
        Assert.Null(user.LockoutEndUtc);
    }

    [Fact]
    public void User_RecordFailedLogin_LocksAccountWhenThresholdReached()
    {
        var user = User.Create("user@example.com", "hash", _now);

        user.RecordFailedLogin(3, TimeSpan.FromMinutes(15), _now);
        Assert.Equal(1, user.FailedLoginAttempts);
        Assert.Equal(UserStatus.Active, user.Status);

        user.RecordFailedLogin(3, TimeSpan.FromMinutes(15), _now);
        Assert.Equal(2, user.FailedLoginAttempts);
        Assert.Equal(UserStatus.Active, user.Status);

        user.RecordFailedLogin(3, TimeSpan.FromMinutes(15), _now);
        Assert.Equal(3, user.FailedLoginAttempts);
        Assert.Equal(UserStatus.Locked, user.Status);
        Assert.True(user.IsLockedOut(_now.AddMinutes(5)));
        Assert.False(user.IsLockedOut(_now.AddMinutes(16))); // Lockout expired
    }

    [Fact]
    public void UserMembership_Create_EnforcesRoleScopeInvariants()
    {
        var userId = UserId.New();

        // Customer cannot have staff membership
        Assert.Throws<InvalidAuthorizationScopeException>(() =>
            UserMembership.Create(_tenantId, userId, AuthRole.Customer, _branchId, _now));

        // SuperAdmin cannot be bound to tenant membership
        Assert.Throws<InvalidAuthorizationScopeException>(() =>
            UserMembership.Create(_tenantId, userId, AuthRole.SuperAdmin, null, _now));

        // RestaurantAdmin cannot have branchId
        Assert.Throws<InvalidAuthorizationScopeException>(() =>
            UserMembership.Create(_tenantId, userId, AuthRole.RestaurantAdmin, _branchId, _now));

        // Branch staff must have branchId
        Assert.Throws<InvalidAuthorizationScopeException>(() =>
            UserMembership.Create(_tenantId, userId, AuthRole.Waiter, null, _now));

        // Valid RestaurantAdmin
        var admin = UserMembership.Create(_tenantId, userId, AuthRole.RestaurantAdmin, null, _now);
        Assert.Equal(AuthRole.RestaurantAdmin, admin.Role);
        Assert.Null(admin.BranchId);

        // Valid Branch staff
        var waiter = UserMembership.Create(_tenantId, userId, AuthRole.Waiter, _branchId, _now);
        Assert.Equal(AuthRole.Waiter, waiter.Role);
        Assert.Equal(_branchId, waiter.BranchId);
    }

    [Fact]
    public void AuthSession_Revoke_MarksSessionInactive()
    {
        var session = AuthSession.Create(
            _tenantId,
            UserId.New(),
            Guid.NewGuid(),
            _branchId,
            AuthenticationMethod.Password,
            TimeSpan.FromHours(8),
            _now);

        Assert.True(session.IsActive(_now));

        session.Revoke("user_logged_out", _now.AddMinutes(10));

        Assert.False(session.IsActive(_now.AddMinutes(10)));
        Assert.True(session.IsRevoked);
        Assert.Equal("user_logged_out", session.RevocationReason);
    }

    [Fact]
    public void RefreshToken_Rotate_MarksRevokedAndTracksReplacement()
    {
        var familyId = Guid.NewGuid();
        var sessionId = Guid.NewGuid();
        var token = RefreshToken.Create(
            _tenantId,
            sessionId,
            familyId,
            "hash_abc_123",
            TimeSpan.FromDays(7),
            _now);

        Assert.True(token.IsActive(_now));

        var nextTokenId = Guid.NewGuid();
        token.Revoke(_now.AddHours(1), nextTokenId);

        Assert.False(token.IsActive(_now.AddHours(1)));
        Assert.True(token.IsRevoked);
        Assert.Equal(nextTokenId, token.ReplacedByTokenId);
    }

    [Fact]
    public void PinCredential_FailedAttempts_LocksAfterThreshold()
    {
        var pinCred = PinCredential.Create(
            _tenantId,
            UserId.New(),
            _branchId,
            "hash_123",
            "argon2id",
            "v1",
            _now);

        Assert.False(pinCred.IsLocked(_now));

        pinCred.RecordFailedAttempt(3, TimeSpan.FromMinutes(10), _now);
        pinCred.RecordFailedAttempt(3, TimeSpan.FromMinutes(10), _now);
        Assert.False(pinCred.IsLocked(_now));

        pinCred.RecordFailedAttempt(3, TimeSpan.FromMinutes(10), _now);
        Assert.True(pinCred.IsLocked(_now.AddMinutes(5)));
        Assert.False(pinCred.IsLocked(_now.AddMinutes(11)));
    }

    [Fact]
    public void InvitationAndResetTokens_Consume_ThrowsIfReconsumedOrExpired()
    {
        var invToken = InvitationToken.Create(_tenantId, UserId.New(), "hash1", TimeSpan.FromHours(1), _now);
        Assert.True(invToken.IsValid(_now));

        invToken.Consume(_now.AddMinutes(10));
        Assert.False(invToken.IsValid(_now.AddMinutes(10)));
        Assert.True(invToken.IsConsumed);

        Assert.Throws<DomainException>(() => invToken.Consume(_now.AddMinutes(15)));

        var expiredToken = PasswordResetToken.Create(_tenantId, UserId.New(), "hash2", TimeSpan.FromMinutes(5), _now);
        Assert.Throws<DomainException>(() => expiredToken.Consume(_now.AddMinutes(10)));
    }

    [Fact]
    public void SecurityAuditEvent_Create_PreservesImmutability()
    {
        var audit = SecurityAuditEvent.Create(
            _tenantId,
            SecurityAuditEventType.LoginSucceeded,
            _now,
            userId: UserId.New(),
            branchId: _branchId,
            ipAddress: "192.168.1.1",
            userAgent: "Mozilla/5.0");

        Assert.Equal(SecurityAuditEventType.LoginSucceeded, audit.EventType);
        Assert.Equal(_tenantId, audit.TenantId);
        Assert.Equal("192.168.1.1", audit.IpAddress);
    }
}
