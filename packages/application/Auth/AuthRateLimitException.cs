namespace RestaurantOrder.Application.Auth;

/// <summary>
/// Thrown when an authentication rate limit is exceeded.
/// Maps to RFC 7807 429 Too Many Requests.
/// </summary>
public sealed class AuthRateLimitException : Exception
{
    public int RetryAfterSeconds { get; }

    public AuthRateLimitException(int retryAfterSeconds = 60)
        : base("Too many login attempts. Please try again later.")
    {
        RetryAfterSeconds = retryAfterSeconds;
    }

    public AuthRateLimitException(int retryAfterSeconds, string message)
        : base(message)
    {
        RetryAfterSeconds = retryAfterSeconds;
    }
}
