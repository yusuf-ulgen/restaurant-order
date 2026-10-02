using RestaurantOrder.Domain.Common;

namespace RestaurantOrder.Domain.Auth;

/// <summary>
/// Domain model for an account identity in RestaurantOrder.
/// Represents a global user identity across the platform, maintaining credentials,
/// security version, and lockout state.
/// </summary>
public sealed class User
{
    public UserId Id { get; private set; }
    public string Email { get; private set; }
    public string NormalizedEmail { get; private set; }
    public string PasswordHash { get; private set; }
    public UserStatus Status { get; private set; }
    public int SecurityVersion { get; private set; }
    public int FailedLoginAttempts { get; private set; }
    public DateTimeOffset? LockoutEndUtc { get; private set; }
    public DateTimeOffset CreatedAtUtc { get; private set; }
    public DateTimeOffset UpdatedAtUtc { get; private set; }
    public Guid ConcurrencyToken { get; private set; }

    private User()
    {
        // Required by EF Core
        Email = string.Empty;
        NormalizedEmail = string.Empty;
        PasswordHash = string.Empty;
    }

    public static User Create(
        string email,
        string passwordHash,
        DateTimeOffset nowUtc,
        UserId? id = null,
        UserStatus initialStatus = UserStatus.Active)
    {
        if (string.IsNullOrWhiteSpace(email))
        {
            throw new DomainException("User email cannot be empty.");
        }

        if (string.IsNullOrWhiteSpace(passwordHash))
        {
            throw new DomainException("Password hash cannot be empty.");
        }

        var normalized = NormalizeEmail(email);

        return new User
        {
            Id = id ?? UserId.New(),
            Email = email.Trim(),
            NormalizedEmail = normalized,
            PasswordHash = passwordHash,
            Status = initialStatus,
            SecurityVersion = 1,
            FailedLoginAttempts = 0,
            LockoutEndUtc = null,
            CreatedAtUtc = nowUtc,
            UpdatedAtUtc = nowUtc,
            ConcurrencyToken = Guid.CreateVersion7()
        };
    }

    public static string NormalizeEmail(string email)
    {
        if (string.IsNullOrWhiteSpace(email))
        {
            throw new DomainException("Cannot normalize an empty email.");
        }

        return email.Trim().ToLowerInvariant();
    }

    public void ChangePassword(string newPasswordHash, DateTimeOffset nowUtc)
    {
        if (string.IsNullOrWhiteSpace(newPasswordHash))
        {
            throw new DomainException("New password hash cannot be empty.");
        }

        PasswordHash = newPasswordHash;
        FailedLoginAttempts = 0;
        LockoutEndUtc = null;
        IncrementSecurityVersion(nowUtc);
    }

    public void RecordSuccessfulLogin(DateTimeOffset nowUtc)
    {
        FailedLoginAttempts = 0;
        LockoutEndUtc = null;
        UpdatedAtUtc = nowUtc;
        ConcurrencyToken = Guid.CreateVersion7();
    }

    public void RecordFailedLogin(int maxAttempts, TimeSpan lockoutDuration, DateTimeOffset nowUtc)
    {
        FailedLoginAttempts++;
        UpdatedAtUtc = nowUtc;
        ConcurrencyToken = Guid.CreateVersion7();

        if (FailedLoginAttempts >= maxAttempts)
        {
            Status = UserStatus.Locked;
            LockoutEndUtc = nowUtc.Add(lockoutDuration);
        }
    }

    public void Lock(DateTimeOffset? untilUtc, DateTimeOffset nowUtc)
    {
        Status = UserStatus.Locked;
        LockoutEndUtc = untilUtc;
        IncrementSecurityVersion(nowUtc);
    }

    public void Unlock(DateTimeOffset nowUtc)
    {
        Status = UserStatus.Active;
        FailedLoginAttempts = 0;
        LockoutEndUtc = null;
        UpdatedAtUtc = nowUtc;
        ConcurrencyToken = Guid.CreateVersion7();
    }

    public void Suspend(DateTimeOffset nowUtc)
    {
        Status = UserStatus.Suspended;
        IncrementSecurityVersion(nowUtc);
    }

    public void Activate(DateTimeOffset nowUtc)
    {
        Status = UserStatus.Active;
        UpdatedAtUtc = nowUtc;
        ConcurrencyToken = Guid.CreateVersion7();
    }

    public void Disable(DateTimeOffset nowUtc)
    {
        Status = UserStatus.Disabled;
        IncrementSecurityVersion(nowUtc);
    }

    public void IncrementSecurityVersion(DateTimeOffset nowUtc)
    {
        SecurityVersion++;
        UpdatedAtUtc = nowUtc;
        ConcurrencyToken = Guid.CreateVersion7();
    }

    public bool IsLockedOut(DateTimeOffset nowUtc)
    {
        if (Status == UserStatus.Locked)
        {
            if (LockoutEndUtc.HasValue && nowUtc >= LockoutEndUtc.Value)
            {
                return false; // Lockout expired
            }
            return true;
        }

        return false;
    }
}
