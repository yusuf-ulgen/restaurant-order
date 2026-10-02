using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace RestaurantOrder.Infrastructure.Auth;

/// <summary>
/// Authenticated encryption provider for outbox payloads using AES-256-GCM.
/// Enforces high-entropy encryption keys in Staging and Production.
/// </summary>
public sealed class AesGcmIdentityOutboxPayloadProtector : IIdentityOutboxPayloadProtector
{
    private const int NonceByteSize = 12;
    private const int TagByteSize = 16;
    private const int KeyByteSize = 32;

    private static readonly byte[] DevFallbackKey = Encoding.UTF8.GetBytes("dev-only-aes-256-key-32-chars!!!");

    private readonly byte[] _key;
    private readonly ILogger<AesGcmIdentityOutboxPayloadProtector> _logger;

    public AesGcmIdentityOutboxPayloadProtector(
        IConfiguration configuration,
        IHostEnvironment environment,
        ILogger<AesGcmIdentityOutboxPayloadProtector> logger)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));

        var rawKey = configuration["NOTIFICATION_ENCRYPTION_KEY"]
            ?? configuration["Notification:EncryptionKey"];

        if (string.IsNullOrWhiteSpace(rawKey))
        {
            if (environment.IsProduction() || environment.IsEnvironment("Staging"))
            {
                throw new InvalidOperationException(
                    $"FATAL CONFIGURATION ERROR: NOTIFICATION_ENCRYPTION_KEY is required in '{environment.EnvironmentName}' environment.");
            }

            _logger.LogWarning("[DEV ONLY] Using fallback development AES key for identity outbox encryption.");
            _key = DevFallbackKey;
            return;
        }

        _key = ParseKey(rawKey.Trim());
    }

    public string Protect(string plaintextPayload)
    {
        if (plaintextPayload == null)
        {
            throw new ArgumentNullException(nameof(plaintextPayload));
        }

        var plaintextBytes = Encoding.UTF8.GetBytes(plaintextPayload);
        var nonce = new byte[NonceByteSize];
        RandomNumberGenerator.Fill(nonce);

        var tag = new byte[TagByteSize];
        var ciphertext = new byte[plaintextBytes.Length];

        using (var aesGcm = new AesGcm(_key, TagByteSize))
        {
            aesGcm.Encrypt(nonce, plaintextBytes, ciphertext, tag);
        }

        var result = new byte[NonceByteSize + TagByteSize + ciphertext.Length];
        Buffer.BlockCopy(nonce, 0, result, 0, NonceByteSize);
        Buffer.BlockCopy(tag, 0, result, NonceByteSize, TagByteSize);
        Buffer.BlockCopy(ciphertext, 0, result, NonceByteSize + TagByteSize, ciphertext.Length);

        return Convert.ToBase64String(result);
    }

    public string Unprotect(string cipherTextPayload)
    {
        if (string.IsNullOrWhiteSpace(cipherTextPayload))
        {
            throw new ArgumentException("Ciphertext cannot be empty.", nameof(cipherTextPayload));
        }

        var data = Convert.FromBase64String(cipherTextPayload);
        if (data.Length < NonceByteSize + TagByteSize)
        {
            throw new CryptographicException("Ciphertext payload is truncated or invalid.");
        }

        var nonce = new byte[NonceByteSize];
        Buffer.BlockCopy(data, 0, nonce, 0, NonceByteSize);

        var tag = new byte[TagByteSize];
        Buffer.BlockCopy(data, NonceByteSize, tag, 0, TagByteSize);

        var cipherLength = data.Length - NonceByteSize - TagByteSize;
        var ciphertext = new byte[cipherLength];
        Buffer.BlockCopy(data, NonceByteSize + TagByteSize, ciphertext, 0, cipherLength);

        var plaintextBytes = new byte[cipherLength];
        using (var aesGcm = new AesGcm(_key, TagByteSize))
        {
            aesGcm.Decrypt(nonce, ciphertext, tag, plaintextBytes);
        }

        return Encoding.UTF8.GetString(plaintextBytes);
    }

    private static byte[] ParseKey(string raw)
    {
        return AesKeyValidator.ParseKey(raw);
    }
}
