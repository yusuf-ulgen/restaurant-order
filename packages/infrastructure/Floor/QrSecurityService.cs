using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using Net.Codecrete.QrCodeGenerator;
using RestaurantOrder.Application.Floor;
using RestaurantOrder.Domain.Branches;
using RestaurantOrder.Domain.Floor;
using RestaurantOrder.Domain.Tenants;

namespace RestaurantOrder.Infrastructure.Floor;

/// <summary>
/// Cryptographic security service for table QR codes.
/// Enforces canonical serialization, HMAC-SHA256 signatures, constant-time verification,
/// key rotation via key_id, and fail-fast validation in staging/production.
/// </summary>
public sealed class QrSecurityService : IQrSecurityService
{
    // Valid Base64-encoded synthetic key decoding to 45 random/synthetic bytes for Development/Testing only
    private const string SyntheticDevKey = "c3ludGhldGljLWRldi10ZXN0LXFyLXNpZ25pbmcta2V5LTMyYi1lbnRyb3B5IQ==";

    private readonly QrSecurityOptions _options;
    private readonly Dictionary<string, byte[]> _keyBytes = new(StringComparer.Ordinal);

    public string CurrentKeyId => _options.CurrentKeyId;

    public QrSecurityService(IOptions<QrSecurityOptions> options, IHostEnvironment environment)
    {
        _options = options?.Value ?? throw new ArgumentNullException(nameof(options));
        var isProdOrStaging = environment.IsProduction() || environment.IsEnvironment("Staging");

        if (string.IsNullOrWhiteSpace(_options.CurrentKeyId))
        {
            _options.CurrentKeyId = "k1";
        }

        if (_options.Keys.Count == 0 || !_options.Keys.ContainsKey(_options.CurrentKeyId))
        {
            if (isProdOrStaging)
            {
                throw new InvalidOperationException(
                    "FATAL CONFIGURATION ERROR: QR signing key is missing or CurrentKeyId is not registered in Staging/Production.");
            }

            _options.Keys[_options.CurrentKeyId] = SyntheticDevKey;
        }

        foreach (var (keyId, base64Secret) in _options.Keys)
        {
            if (string.IsNullOrWhiteSpace(base64Secret))
            {
                throw new InvalidOperationException(
                    $"FATAL CONFIGURATION ERROR: QR signing key '{keyId}' must not be empty.");
            }

            byte[] decodedKey;
            try
            {
                decodedKey = Convert.FromBase64String(base64Secret);
            }
            catch (FormatException ex)
            {
                throw new InvalidOperationException(
                    $"FATAL CONFIGURATION ERROR: QR signing key '{keyId}' must be a valid Base64 encoded string.", ex);
            }

            if (decodedKey.Length < 32)
            {
                throw new InvalidOperationException(
                    $"FATAL CONFIGURATION ERROR: QR signing key '{keyId}' must decode to at least 32 bytes (256 bits entropy). Decoded length was {decodedKey.Length} bytes.");
            }

            _keyBytes[keyId] = decodedKey;
        }
    }

    public string GenerateToken(QrPayload payload)
    {
        ArgumentNullException.ThrowIfNull(payload);

        if (!_keyBytes.TryGetValue(payload.KeyId, out var key))
        {
            throw new InvalidOperationException($"QR signing key '{payload.KeyId}' is not configured.");
        }

        var wire = new QrWirePayload(
            v: payload.SchemaVersion,
            kid: payload.KeyId,
            mode: payload.Mode == QrMode.Static ? "static" : "dynamic",
            tid: payload.TenantId.Value,
            bid: payload.BranchId.Value,
            tcode: payload.PublicTableCode,
            qv: payload.QrVersion,
            iat: payload.IssuedAtUnix,
            sid: payload.TableSessionId?.Value,
            exp: payload.ExpiresAtUnix,
            nonce: payload.Nonce);

        var jsonBytes = JsonSerializer.SerializeToUtf8Bytes(wire);
        var payloadPart = WebEncoders.Base64UrlEncode(jsonBytes);

        var canonicalString = BuildCanonicalString(wire);
        var canonicalBytes = Encoding.UTF8.GetBytes(canonicalString);

        using var hmac = new HMACSHA256(key);
        var signatureBytes = hmac.ComputeHash(canonicalBytes);
        var signaturePart = WebEncoders.Base64UrlEncode(signatureBytes);

        return $"{payloadPart}.{signaturePart}";
    }

    public QrPayload VerifyAndDecodeToken(string token, DateTimeOffset nowUtc)
    {
        if (string.IsNullOrWhiteSpace(token))
        {
            throw new QrSecurityException("QR token cannot be null or empty.");
        }

        if (token.Length > _options.MaxTokenLength)
        {
            throw new QrSecurityException($"QR token length ({token.Length}) exceeds maximum allowed limit of {_options.MaxTokenLength}.");
        }

        var parts = token.Split('.');
        if (parts.Length != 2)
        {
            throw new QrSecurityException("Malformed QR token structure. Expected 2 segments.");
        }

        byte[] payloadBytes;
        byte[] providedSignatureBytes;
        try
        {
            payloadBytes = WebEncoders.Base64UrlDecode(parts[0]);
            providedSignatureBytes = WebEncoders.Base64UrlDecode(parts[1]);
        }
        catch (Exception ex)
        {
            throw new QrSecurityException("Failed to decode base64url QR token segments.", ex);
        }

        QrWirePayload? wire;
        try
        {
            wire = JsonSerializer.Deserialize<QrWirePayload>(payloadBytes);
        }
        catch (Exception ex)
        {
            throw new QrSecurityException("Failed to parse QR token payload JSON.", ex);
        }

        if (wire == null)
        {
            throw new QrSecurityException("QR token payload was null.");
        }

        if (wire.v is not (1 or 2))
        {
            throw new QrSecurityException($"Unsupported QR schema version '{wire.v}'. Expected 1 or {QrPayload.CurrentSchemaVersion}.");
        }

        if (string.IsNullOrWhiteSpace(wire.kid) || !_keyBytes.TryGetValue(wire.kid, out var key))
        {
            throw new QrSecurityException($"Unknown or untrusted QR key_id '{wire.kid}'.");
        }

        // Canonical signature verification with constant-time equality check
        var canonicalString = BuildCanonicalString(wire);
        var canonicalBytes = Encoding.UTF8.GetBytes(canonicalString);

        using var hmac = new HMACSHA256(key);
        var expectedSignatureBytes = hmac.ComputeHash(canonicalBytes);

        if (!CryptographicOperations.FixedTimeEquals(expectedSignatureBytes, providedSignatureBytes))
        {
            throw new QrSecurityException("Invalid or tampered QR token signature.");
        }

        if (wire.tid == Guid.Empty)
        {
            throw new QrSecurityException("QR token tenant_id cannot be empty.");
        }

        if (wire.bid == Guid.Empty)
        {
            throw new QrSecurityException("QR token branch_id cannot be empty.");
        }

        if (string.IsNullOrWhiteSpace(wire.tcode))
        {
            throw new QrSecurityException("QR token table code cannot be empty.");
        }

        if (wire.qv < 1)
        {
            throw new QrSecurityException("QR token qr_version must be >= 1.");
        }

        var nowUnix = nowUtc.ToUnixTimeSeconds();

        // Enforce issued_at (iat) timestamp validation and future-issuance rejection
        if (wire.iat.HasValue)
        {
            if (wire.iat.Value > nowUnix + _options.ClockSkewSeconds)
            {
                throw new QrSecurityException("QR token was issued in the future beyond allowed clock skew.");
            }
        }
        else if (wire.v == 2)
        {
            throw new QrSecurityException("QR token schema version 2 requires an issued_at (iat) timestamp.");
        }

        var mode = string.Equals(wire.mode, "dynamic", StringComparison.OrdinalIgnoreCase)
            ? QrMode.Dynamic
            : string.Equals(wire.mode, "static", StringComparison.OrdinalIgnoreCase)
                ? QrMode.Static
                : throw new QrSecurityException($"Unknown QR mode '{wire.mode}'.");

        if (mode == QrMode.Dynamic)
        {
            if (!wire.sid.HasValue || wire.sid.Value == Guid.Empty)
            {
                throw new QrSecurityException("Dynamic QR token requires a valid session ID.");
            }

            if (!wire.exp.HasValue)
            {
                throw new QrSecurityException("Dynamic QR token requires an expiration timestamp.");
            }

            if (nowUnix > wire.exp.Value + _options.ClockSkewSeconds)
            {
                throw new QrSecurityException("Dynamic QR token has expired.");
            }

            if (wire.exp.Value > nowUtc.AddDays(7).ToUnixTimeSeconds())
            {
                throw new QrSecurityException("Dynamic QR token expiration timestamp is invalid.");
            }

            if (wire.iat.HasValue && wire.exp.Value < wire.iat.Value)
            {
                throw new QrSecurityException("Dynamic QR token expiration timestamp precedes issued_at timestamp.");
            }
        }

        return new QrPayload(
            schemaVersion: wire.v,
            keyId: wire.kid,
            mode: mode,
            tenantId: TenantId.From(wire.tid),
            branchId: BranchId.From(wire.bid),
            publicTableCode: wire.tcode,
            qrVersion: wire.qv,
            issuedAtUnix: wire.iat,
            tableSessionId: wire.sid.HasValue ? DiningSessionId.From(wire.sid.Value) : null,
            expiresAtUnix: wire.exp,
            nonce: wire.nonce);
    }

    public string GenerateSvg(string content)
    {
        if (string.IsNullOrWhiteSpace(content))
        {
            throw new ArgumentException("Content cannot be empty for QR encoding.", nameof(content));
        }

        var qr = QrCode.EncodeText(content, QrCode.Ecc.Medium);
        return qr.ToSvgString(border: 4);
    }

    public string GetFingerprint(string token)
    {
        if (string.IsNullOrWhiteSpace(token))
        {
            return "qr-fp-empty";
        }

        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(token));
        return $"qr-fp-{Convert.ToHexString(hash)[..12].ToLowerInvariant()}";
    }

    private static string BuildCanonicalString(QrWirePayload wire)
    {
        var sb = new StringBuilder(160);
        sb.Append("v=").Append(wire.v)
          .Append("&kid=").Append(wire.kid)
          .Append("&mode=").Append(wire.mode)
          .Append("&tid=").Append(wire.tid.ToString("D"))
          .Append("&bid=").Append(wire.bid.ToString("D"))
          .Append("&tcode=").Append(wire.tcode)
          .Append("&qv=").Append(wire.qv);

        if (wire.iat.HasValue)
        {
            sb.Append("&iat=").Append(wire.iat.Value);
        }

        if (string.Equals(wire.mode, "dynamic", StringComparison.OrdinalIgnoreCase))
        {
            if (wire.sid.HasValue)
            {
                sb.Append("&sid=").Append(wire.sid.Value.ToString("D"));
            }
            if (wire.exp.HasValue)
            {
                sb.Append("&exp=").Append(wire.exp.Value);
            }
            if (!string.IsNullOrWhiteSpace(wire.nonce))
            {
                sb.Append("&nonce=").Append(wire.nonce);
            }
        }

        return sb.ToString();
    }

    private sealed record QrWirePayload(
        [property: JsonPropertyName("v")] int v,
        [property: JsonPropertyName("kid")] string kid,
        [property: JsonPropertyName("mode")] string mode,
        [property: JsonPropertyName("tid")] Guid tid,
        [property: JsonPropertyName("bid")] Guid bid,
        [property: JsonPropertyName("tcode")] string tcode,
        [property: JsonPropertyName("qv")] int qv,
        [property: JsonPropertyName("iat")] long? iat = null,
        [property: JsonPropertyName("sid")] Guid? sid = null,
        [property: JsonPropertyName("exp")] long? exp = null,
        [property: JsonPropertyName("nonce")] string? nonce = null);
}
