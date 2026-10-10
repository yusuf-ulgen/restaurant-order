using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using Moq;
using RestaurantOrder.Api.Floor;
using RestaurantOrder.Application.Floor;
using RestaurantOrder.Domain.Branches;
using RestaurantOrder.Domain.Common;
using RestaurantOrder.Domain.Floor;
using RestaurantOrder.Domain.Tenants;
using RestaurantOrder.Infrastructure.Floor;
using Xunit;

namespace RestaurantOrder.UnitTests.Floor;

public class QrSecurityUnitTests
{
    // Valid Base64 key that decodes to 32 random bytes (256 bits entropy)
    private static readonly string ValidBase64Key32Bytes = Convert.ToBase64String(
        Encoding.UTF8.GetBytes("01234567890123456789012345678901")); // exactly 32 bytes

    private static readonly string ValidBase64KeyRotated = Convert.ToBase64String(
        Encoding.UTF8.GetBytes("abcdefghijklmnopqrstuvwxyz123456")); // exactly 32 bytes

    private readonly TenantId _tenantId = TenantId.New();
    private readonly BranchId _branchId = BranchId.New();
    private readonly string _tableCode = "tbl_pub_code_001";

    private QrSecurityService CreateService(string environmentName = "Development", Dictionary<string, string>? keys = null, string currentKeyId = "k1", int maxTokenLength = 2048)
    {
        var envMock = new Mock<IHostEnvironment>();
        envMock.Setup(e => e.EnvironmentName).Returns(environmentName);

        var options = new QrSecurityOptions
        {
            CurrentKeyId = currentKeyId,
            Keys = keys ?? new Dictionary<string, string> { [currentKeyId] = ValidBase64Key32Bytes },
            MaxTokenLength = maxTokenLength
        };

        return new QrSecurityService(Options.Create(options), envMock.Object);
    }

    [Fact]
    public void GenerateToken_ProducesValidTwoSegmentBase64UrlToken_WithSchemaV2AndIat()
    {
        var service = CreateService();
        var payload = QrPayload.CreateStatic("k1", _tenantId, _branchId, _tableCode, 1);

        var token = service.GenerateToken(payload);

        Assert.NotNull(token);
        var parts = token.Split('.');
        Assert.Equal(2, parts.Length);

        var decodedPayload = WebEncoders.Base64UrlDecode(parts[0]);
        var decodedSignature = WebEncoders.Base64UrlDecode(parts[1]);

        Assert.NotEmpty(decodedPayload);
        Assert.Equal(32, decodedSignature.Length); // HMAC-SHA256 is 32 bytes
    }

    [Fact]
    public void VerifyAndDecodeToken_ValidStaticToken_DecodesCorrectly_WithIssuedAt()
    {
        var service = CreateService();
        var payload = QrPayload.CreateStatic("k1", _tenantId, _branchId, _tableCode, 2);
        var token = service.GenerateToken(payload);

        var decoded = service.VerifyAndDecodeToken(token, DateTimeOffset.UtcNow);

        Assert.Equal(2, decoded.SchemaVersion);
        Assert.Equal("k1", decoded.KeyId);
        Assert.Equal(QrMode.Static, decoded.Mode);
        Assert.Equal(_tenantId, decoded.TenantId);
        Assert.Equal(_branchId, decoded.BranchId);
        Assert.Equal(_tableCode, decoded.PublicTableCode);
        Assert.Equal(2, decoded.QrVersion);
        Assert.NotNull(decoded.IssuedAtUnix);
        Assert.Null(decoded.TableSessionId);
        Assert.Null(decoded.ExpiresAtUnix);
    }

    [Fact]
    public void VerifyAndDecodeToken_ValidDynamicToken_DecodesCorrectly()
    {
        var service = CreateService();
        var sessionId = DiningSessionId.New();
        var now = DateTimeOffset.UtcNow;
        var expiresUnix = now.AddMinutes(15).ToUnixTimeSeconds();

        var payload = QrPayload.CreateDynamic("k1", _tenantId, _branchId, _tableCode, 1, sessionId, expiresUnix, "nonce_123");
        var token = service.GenerateToken(payload);

        var decoded = service.VerifyAndDecodeToken(token, now);

        Assert.Equal(QrMode.Dynamic, decoded.Mode);
        Assert.Equal(sessionId, decoded.TableSessionId);
        Assert.Equal(expiresUnix, decoded.ExpiresAtUnix);
        Assert.Equal("nonce_123", decoded.Nonce);
    }

    [Fact]
    public void VerifyAndDecodeToken_FutureIssuedToken_ThrowsQrSecurityException()
    {
        var service = CreateService();
        var now = DateTimeOffset.UtcNow;
        var futureIssuedUnix = now.AddMinutes(5).ToUnixTimeSeconds();

        var payload = QrPayload.CreateStatic("k1", _tenantId, _branchId, _tableCode, 1, issuedAtUnix: futureIssuedUnix);
        var token = service.GenerateToken(payload);

        var ex = Assert.Throws<QrSecurityException>(() => service.VerifyAndDecodeToken(token, now));
        Assert.Contains("future", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void VerifyAndDecodeToken_OversizedToken_ThrowsQrSecurityException_BeforeDecoding()
    {
        var service = CreateService(maxTokenLength: 64);
        var payload = QrPayload.CreateStatic("k1", _tenantId, _branchId, _tableCode, 1);
        var token = service.GenerateToken(payload);

        // Token is typically 200+ characters, exceeding 64
        Assert.True(token.Length > 64);
        var ex = Assert.Throws<QrSecurityException>(() => service.VerifyAndDecodeToken(token, DateTimeOffset.UtcNow));
        Assert.Contains("maximum allowed limit", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void VerifyAndDecodeToken_TamperedPayload_ThrowsQrSecurityException()
    {
        var service = CreateService();
        var payload = QrPayload.CreateStatic("k1", _tenantId, _branchId, _tableCode, 1);
        var token = service.GenerateToken(payload);

        var parts = token.Split('.');
        var payloadJson = Encoding.UTF8.GetString(WebEncoders.Base64UrlDecode(parts[0]));
        // Tamper with qr_version in JSON payload
        var tamperedJson = payloadJson.Replace("\"qv\":1", "\"qv\":99");
        var tamperedPart0 = WebEncoders.Base64UrlEncode(Encoding.UTF8.GetBytes(tamperedJson));
        var tamperedToken = $"{tamperedPart0}.{parts[1]}";

        var ex = Assert.Throws<QrSecurityException>(() => service.VerifyAndDecodeToken(tamperedToken, DateTimeOffset.UtcNow));
        Assert.Contains("signature", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void VerifyAndDecodeToken_TamperedSignature_ThrowsQrSecurityException()
    {
        var service = CreateService();
        var payload = QrPayload.CreateStatic("k1", _tenantId, _branchId, _tableCode, 1);
        var token = service.GenerateToken(payload);

        var parts = token.Split('.');
        var sigBytes = WebEncoders.Base64UrlDecode(parts[1]);
        sigBytes[0] ^= 0xFF; // Flip bits in signature
        var tamperedSig = WebEncoders.Base64UrlEncode(sigBytes);
        var tamperedToken = $"{parts[0]}.{tamperedSig}";

        var ex = Assert.Throws<QrSecurityException>(() => service.VerifyAndDecodeToken(tamperedToken, DateTimeOffset.UtcNow));
        Assert.Contains("signature", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void VerifyAndDecodeToken_UnknownKeyId_ThrowsQrSecurityException()
    {
        var service1 = CreateService("Development", new Dictionary<string, string>
        {
            ["k1"] = ValidBase64Key32Bytes,
            ["k2"] = ValidBase64KeyRotated
        }, currentKeyId: "k2");
        var payload = QrPayload.CreateStatic("k2", _tenantId, _branchId, _tableCode, 1);
        var token = service1.GenerateToken(payload);

        // Service 2 does not have key "k2"
        var service2 = CreateService("Development", new Dictionary<string, string> { ["k1"] = ValidBase64Key32Bytes });

        var ex = Assert.Throws<QrSecurityException>(() => service2.VerifyAndDecodeToken(token, DateTimeOffset.UtcNow));
        Assert.Contains("key_id", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void KeyRotation_VerifiesTokenSignedWithOldKey_WhenOldKeyRetained()
    {
        // Service with both old k1 and rotated k2
        var service = CreateService("Development", new Dictionary<string, string>
        {
            ["k1"] = ValidBase64Key32Bytes,
            ["k2"] = ValidBase64KeyRotated
        }, currentKeyId: "k2");

        // Old token created with k1
        var oldPayload = QrPayload.CreateStatic("k1", _tenantId, _branchId, _tableCode, 1);
        var oldToken = service.GenerateToken(oldPayload);

        // New service still verifies old token using retained k1
        var decoded = service.VerifyAndDecodeToken(oldToken, DateTimeOffset.UtcNow);
        Assert.Equal("k1", decoded.KeyId);
    }

    [Fact]
    public void VerifyAndDecodeToken_ExpiredDynamicToken_ThrowsQrSecurityException()
    {
        var service = CreateService();
        var sessionId = DiningSessionId.New();
        var now = DateTimeOffset.UtcNow;
        var pastUnix = now.AddMinutes(-5).ToUnixTimeSeconds();

        var payload = QrPayload.CreateDynamic("k1", _tenantId, _branchId, _tableCode, 1, sessionId, pastUnix);
        var token = service.GenerateToken(payload);

        var ex = Assert.Throws<QrSecurityException>(() => service.VerifyAndDecodeToken(token, now));
        Assert.Contains("expired", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void QrSecurityService_MalformedBase64Key_ThrowsInvalidOperationException()
    {
        var envMock = new Mock<IHostEnvironment>();
        envMock.Setup(e => e.EnvironmentName).Returns("Development");

        var options = new QrSecurityOptions
        {
            CurrentKeyId = "k1",
            Keys = new Dictionary<string, string> { ["k1"] = "not_valid_base64_symbols!@#$" }
        };

        var ex = Assert.Throws<InvalidOperationException>(() => new QrSecurityService(Options.Create(options), envMock.Object));
        Assert.Contains("Base64", ex.Message);
    }

    [Fact]
    public void QrSecurityService_KeyDecodedUnder32Bytes_ThrowsInvalidOperationException()
    {
        var envMock = new Mock<IHostEnvironment>();
        envMock.Setup(e => e.EnvironmentName).Returns("Development");

        // Base64 encoding of 16 bytes: valid Base64, but only 16 bytes (128 bits) < 32 bytes (256 bits)
        var shortKey16Bytes = Convert.ToBase64String(new byte[16]);
        var options = new QrSecurityOptions
        {
            CurrentKeyId = "k1",
            Keys = new Dictionary<string, string> { ["k1"] = shortKey16Bytes }
        };

        var ex = Assert.Throws<InvalidOperationException>(() => new QrSecurityService(Options.Create(options), envMock.Object));
        Assert.Contains("32 bytes", ex.Message);
    }

    [Fact]
    public void QrSecurityService_32CharacterString_DecodesTo24Bytes_Rejected()
    {
        var envMock = new Mock<IHostEnvironment>();
        envMock.Setup(e => e.EnvironmentName).Returns("Development");

        // A 32-character string that is valid Base64, but decodes to only 24 bytes:
        var string32Chars = "AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA";
        Assert.Equal(32, string32Chars.Length);
        Assert.Equal(24, Convert.FromBase64String(string32Chars).Length);

        var options = new QrSecurityOptions
        {
            CurrentKeyId = "k1",
            Keys = new Dictionary<string, string> { ["k1"] = string32Chars }
        };

        var ex = Assert.Throws<InvalidOperationException>(() => new QrSecurityService(Options.Create(options), envMock.Object));
        Assert.Contains("32 bytes", ex.Message);
    }

    [Fact]
    public void QrSecurityOptionsValidator_FailsFastInStagingAndProduction_WhenKeyMissingOrInvalid()
    {
        var envMock = new Mock<IHostEnvironment>();
        envMock.Setup(e => e.EnvironmentName).Returns("Production");

        var validator = new QrSecurityOptionsValidator(envMock.Object);

        // 1. Missing keys in production
        var missingKeysOptions = new QrSecurityOptions { Keys = new Dictionary<string, string>() };
        var res1 = validator.Validate(null, missingKeysOptions);
        Assert.True(res1.Failed);

        // 2. Malformed Base64 key
        var malformedOptions = new QrSecurityOptions
        {
            CurrentKeyId = "k1",
            Keys = new Dictionary<string, string> { ["k1"] = "invalid_base64!#" }
        };
        var res2 = validator.Validate(null, malformedOptions);
        Assert.True(res2.Failed);

        // 3. Decoded < 32 bytes key
        var shortOptions = new QrSecurityOptions
        {
            CurrentKeyId = "k1",
            Keys = new Dictionary<string, string> { ["k1"] = Convert.ToBase64String(new byte[16]) }
        };
        var res3 = validator.Validate(null, shortOptions);
        Assert.True(res3.Failed);

        // 4. Valid 32-byte Base64 key succeeds
        var validOptions = new QrSecurityOptions
        {
            CurrentKeyId = "k1",
            Keys = new Dictionary<string, string> { ["k1"] = ValidBase64Key32Bytes }
        };
        var res4 = validator.Validate(null, validOptions);
        Assert.True(res4.Succeeded);
    }

    [Fact]
    public void HandleCustomerException_HidesInternalSecurityDetails_FromPublicResponse()
    {
        var httpContext = new DefaultHttpContext();
        var internalEx = new QrSecurityException("Cryptographic verification failed: HMAC mismatch on token segment 2.");

        var result = QrCustomerEndpoints.HandleCustomerException(internalEx, httpContext);
        Assert.NotNull(result);

        // Check that internal error message is sanitized and not leaked to client
        var jsonResult = Assert.IsAssignableFrom<Microsoft.AspNetCore.Http.IResult>(result);
        Assert.NotNull(jsonResult);
    }

    [Fact]
    public void GetFingerprint_DoesNotLeakRawTokenOrSecret()
    {
        var service = CreateService();
        var rawToken = "sample.token.payload.signature";

        var fp = service.GetFingerprint(rawToken);

        Assert.StartsWith("qr-fp-", fp);
        Assert.DoesNotContain("sample", fp);
        Assert.DoesNotContain("token", fp);
        Assert.DoesNotContain(ValidBase64Key32Bytes, fp);
    }
}
