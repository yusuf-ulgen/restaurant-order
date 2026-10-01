namespace RestaurantOrder.Application.Auth;

public sealed class AuthSettings
{
    public const string SectionName = "Auth";

    public List<string> SuperAdminEmails { get; set; } = new()
    {
        "superadmin@platform.local"
    };

    public int MaxFailedLoginAttempts { get; set; } = 5;
    public int LockoutMinutes { get; set; } = 15;
    public int RefreshTokenLifetimeDays { get; set; } = 7;
}
