namespace RestaurantOrder.Application.Floor;

/// <summary>
/// Thrown when a QR token has an invalid signature, malformed payload, unknown key, or tampering.
/// </summary>
public class QrSecurityException : Exception
{
    public QrSecurityException(string message, Exception? inner = null)
        : base(message, inner)
    {
    }
}

/// <summary>
/// Thrown when attempting to resolve or exchange a QR code for an inactive or non-existent table.
/// </summary>
public class QrTableInactiveException : Exception
{
    public QrTableInactiveException(string message)
        : base(message)
    {
    }
}

/// <summary>
/// Thrown when a table QR version has been rotated, rendering the presented QR code revoked.
/// </summary>
public class QrRevokedException : Exception
{
    public QrRevokedException(string message)
        : base(message)
    {
    }
}

/// <summary>
/// Thrown when a dynamic QR token or exchange target references a dining session that is already Closed.
/// </summary>
public class QrSessionClosedException : Exception
{
    public QrSessionClosedException(string message)
        : base(message)
    {
    }
}

/// <summary>
/// Thrown when rate limiting thresholds for public QR operations are exceeded.
/// </summary>
public class QrRateLimitException : Exception
{
    public int RetryAfterSeconds { get; }

    public QrRateLimitException(int retryAfterSeconds, string message = "Rate limit exceeded for QR operations.")
        : base(message)
    {
        RetryAfterSeconds = retryAfterSeconds;
    }
}
