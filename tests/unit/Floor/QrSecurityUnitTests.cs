using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using Moq;
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
    private const string ValidKey = "synthetic-dev-qr-key-32chars-minimum-entropy!!";
    private readonly TenantId _tenantId = TenantId.New();
    private readonly BranchId _branchId = BranchId.New();
    private readonly string _tableCode = "tbl_pub_code_001";

    private QrSecurityService CreateService(string environmentName = "Development", Dictionary<string, string>? keys = null)
    {
        var envMock = new Mock<IHostEnvironment>();
        envMock.Setup(e => e.EnvironmentName).Returns(environmentName);

        var options = new QrSecurityOptions
        {
            CurrentKeyId = "k1",
            Keys = keys ?? new Dictionary<string, string> { ["k1"] = ValidKey }
        };

        return new QrSecurityService(Options.Create(options), envMock.Object);
    }

    [Fact]
    public void GenerateToken_ProducesValidTwoSegmentBase64UrlToken()
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
    public void VerifyAndDecodeToken_ValidStaticToken_DecodesCorrectly()
    {
        var service = CreateService();
        var payload = QrPayload.CreateStatic("k1", _tenantId, _branchId, _tableCode, 2);
        var token = service.GenerateToken(payload);

        var decoded = service.VerifyAndDecodeToken(token, DateTimeOffset.UtcNow);

        Assert.Equal(1, decoded.SchemaVersion);
        Assert.Equal("k1", decoded.KeyId);
        Assert.Equal(QrMode.Static, decoded.Mode);
        Assert.Equal(_tenantId, decoded.TenantId);
        Assert.Equal(_branchId, decoded.BranchId);
        Assert.Equal(_tableCode, decoded.PublicTableCode);
        Assert.Equal(2, decoded.QrVersion);
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
    public void VerifyAndDecodeToken_MissingSignatureSegment_ThrowsQrSecurityException()
    {
        var service = CreateService();
        var ex = Assert.Throws<QrSecurityException>(() => service.VerifyAndDecodeToken("just_one_segment", DateTimeOffset.UtcNow));
        Assert.Contains("segments", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void VerifyAndDecodeToken_UnknownKeyId_ThrowsQrSecurityException()
    {
        var service1 = CreateService("Development", new Dictionary<string, string> { ["k1"] = ValidKey, ["k2"] = "another-valid-secret-key-at-least-32-chars-long" });
        var payload = QrPayload.CreateStatic("k2", _tenantId, _branchId, _tableCode, 1);
        var token = service1.GenerateToken(payload);

        // Service 2 does not have key "k2"
        var service2 = CreateService("Development", new Dictionary<string, string> { ["k1"] = ValidKey });

        var ex = Assert.Throws<QrSecurityException>(() => service2.VerifyAndDecodeToken(token, DateTimeOffset.UtcNow));
        Assert.Contains("key_id", ex.Message, StringComparison.OrdinalIgnoreCase);
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
    public void VerifyAndDecodeToken_FutureIssuedBeyondThreshold_ThrowsQrSecurityException()
    {
        var service = CreateService();
        var sessionId = DiningSessionId.New();
        var now = DateTimeOffset.UtcNow;
        var farFuture = now.AddDays(10).ToUnixTimeSeconds();

        var payload = QrPayload.CreateDynamic("k1", _tenantId, _branchId, _tableCode, 1, sessionId, farFuture);
        var token = service.GenerateToken(payload);

        var ex = Assert.Throws<QrSecurityException>(() => service.VerifyAndDecodeToken(token, now));
        Assert.Contains("invalid", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void QrSecurityService_WeakKeyLessThan32Chars_ThrowsInvalidOperationException()
    {
        var envMock = new Mock<IHostEnvironment>();
        envMock.Setup(e => e.EnvironmentName).Returns("Development");

        var options = new QrSecurityOptions
        {
            CurrentKeyId = "k1",
            Keys = new Dictionary<string, string> { ["k1"] = "too-short" }
        };

        var ex = Assert.Throws<InvalidOperationException>(() => new QrSecurityService(Options.Create(options), envMock.Object));
        Assert.Contains("256 bits", ex.Message);
    }

    [Fact]
    public void QrSecurityService_MissingKeyInStagingOrProduction_ThrowsInvalidOperationException()
    {
        var envMock = new Mock<IHostEnvironment>();
        envMock.Setup(e => e.EnvironmentName).Returns("Production");

        var options = new QrSecurityOptions
        {
            CurrentKeyId = "k1",
            Keys = new Dictionary<string, string>() // empty
        };

        var ex = Assert.Throws<InvalidOperationException>(() => new QrSecurityService(Options.Create(options), envMock.Object));
        Assert.Contains("FATAL CONFIGURATION ERROR", ex.Message);
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
        Assert.DoesNotContain(ValidKey, fp);
    }

    [Fact]
    public void GenerateSvg_ProducesValidSvgElement()
    {
        var service = CreateService();
        var svg = service.GenerateSvg("https://example.com/qr?token=test");

        Assert.NotNull(svg);
        Assert.Contains("<svg", svg, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("</svg>", svg, StringComparison.OrdinalIgnoreCase);
    }
}
