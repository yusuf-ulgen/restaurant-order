using System.Security.Cryptography;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;

namespace RestaurantOrder.Infrastructure.Floor;

/// <summary>
/// Validator for QrSecurityOptions enforcing startup fail-fast in Staging/Production:
/// - Base64 encoded keys that decode to at least 32 random bytes (256 bits of entropy).
/// - CurrentKeyId present and non-empty.
/// - Rejection of malformed Base64 and keys decoding to less than 32 bytes.
/// </summary>
public sealed class QrSecurityOptionsValidator : IValidateOptions<QrSecurityOptions>
{
    private readonly IHostEnvironment _environment;

    public QrSecurityOptionsValidator(IHostEnvironment environment)
    {
        _environment = environment ?? throw new ArgumentNullException(nameof(environment));
    }

    public ValidateOptionsResult Validate(string? name, QrSecurityOptions options)
    {
        if (options == null)
        {
            return ValidateOptionsResult.Fail("QrSecurityOptions instance cannot be null.");
        }

        var isProdOrStaging = _environment.IsProduction() || _environment.IsEnvironment("Staging");
        var failures = new List<string>();

        if (string.IsNullOrWhiteSpace(options.CurrentKeyId))
        {
            failures.Add("QrSecurity: CurrentKeyId must not be empty.");
        }

        if (options.DynamicQrLifetimeMinutes <= 0)
        {
            failures.Add("QrSecurity: DynamicQrLifetimeMinutes must be greater than 0.");
        }

        if (options.ClockSkewSeconds < 0)
        {
            failures.Add("QrSecurity: ClockSkewSeconds must be greater than or equal to 0.");
        }

        if (options.MaxTokenLength < 64)
        {
            failures.Add("QrSecurity: MaxTokenLength must be at least 64 characters.");
        }

        if (options.Keys == null || options.Keys.Count == 0)
        {
            if (isProdOrStaging)
            {
                failures.Add("FATAL CONFIGURATION ERROR: At least one QR signing key must be configured in Staging/Production.");
            }
        }
        else
        {
            if (!string.IsNullOrWhiteSpace(options.CurrentKeyId) && !options.Keys.ContainsKey(options.CurrentKeyId))
            {
                failures.Add($"FATAL CONFIGURATION ERROR: CurrentKeyId '{options.CurrentKeyId}' does not exist in QrSecurity:Keys.");
            }

            foreach (var (keyId, base64Secret) in options.Keys)
            {
                if (string.IsNullOrWhiteSpace(base64Secret))
                {
                    failures.Add($"FATAL CONFIGURATION ERROR: QR signing key '{keyId}' must not be empty.");
                    continue;
                }

                try
                {
                    var decoded = Convert.FromBase64String(base64Secret);
                    if (decoded.Length < 32)
                    {
                        failures.Add(
                            $"FATAL CONFIGURATION ERROR: QR signing key '{keyId}' must decode to at least 32 bytes (256 bits entropy). Decoded length was {decoded.Length} bytes.");
                    }
                }
                catch (FormatException)
                {
                    failures.Add($"FATAL CONFIGURATION ERROR: QR signing key '{keyId}' is not a valid Base64 encoded string.");
                }
            }
        }

        return failures.Count > 0
            ? ValidateOptionsResult.Fail(failures)
            : ValidateOptionsResult.Success;
    }
}
