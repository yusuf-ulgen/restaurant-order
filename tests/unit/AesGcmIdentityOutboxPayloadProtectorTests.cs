using System.Security.Cryptography;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using RestaurantOrder.Infrastructure.Auth;
using Xunit;

namespace RestaurantOrder.UnitTests.Security;

public class AesGcmIdentityOutboxPayloadProtectorTests
{
    private static IHostEnvironment CreateEnvironment(string environmentName)
    {
        var mock = new Mock<IHostEnvironment>();
        mock.Setup(e => e.EnvironmentName).Returns(environmentName);
        return mock.Object;
    }

    public static IEnumerable<object[]> ValidKeyTestData()
    {
        yield return new object[] { Convert.ToHexString(new byte[32]) }; // 64 hex
        yield return new object[] { Convert.ToBase64String(new byte[32]) }; // 32 bytes Base64
        yield return new object[] { new string('a', 32) }; // 32 UTF-8 chars
    }

    [Fact]
    public void Protect_And_Unprotect_RoundTrip_ReturnsOriginalPlaintext()
    {
        var hexKey = Convert.ToHexString(new byte[32] { 1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11, 12, 13, 14, 15, 16, 17, 18, 19, 20, 21, 22, 23, 24, 25, 26, 27, 28, 29, 30, 31, 32 });
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["NOTIFICATION_ENCRYPTION_KEY"] = hexKey
            })
            .Build();

        var protector = new AesGcmIdentityOutboxPayloadProtector(
            config,
            CreateEnvironment("Production"),
            NullLogger<AesGcmIdentityOutboxPayloadProtector>.Instance);

        var original = "{\"email\":\"staff@restaurant.com\",\"token\":\"secure-test-token-12345\"}";
        var ciphertext = protector.Protect(original);

        Assert.NotNull(ciphertext);
        Assert.NotEqual(original, ciphertext);

        var decrypted = protector.Unprotect(ciphertext);
        Assert.Equal(original, decrypted);
    }

    [Theory]
    [MemberData(nameof(ValidKeyTestData))]
    public void KeyParsing_SupportsDifferentFormats(string validKey)
    {
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["NOTIFICATION_ENCRYPTION_KEY"] = validKey
            })
            .Build();

        var protector = new AesGcmIdentityOutboxPayloadProtector(
            config,
            CreateEnvironment("Production"),
            NullLogger<AesGcmIdentityOutboxPayloadProtector>.Instance);

        var encrypted = protector.Protect("test payload");
        Assert.Equal("test payload", protector.Unprotect(encrypted));
    }

    [Theory]
    [InlineData("short-key-16-ch")]
    [InlineData("1234567890123456789012345678901")] // 31 chars
    public void KeyParsing_InvalidLength_ThrowsInvalidOperationException(string invalidKey)
    {
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["NOTIFICATION_ENCRYPTION_KEY"] = invalidKey
            })
            .Build();

        var ex = Assert.Throws<InvalidOperationException>(() => new AesGcmIdentityOutboxPayloadProtector(
            config,
            CreateEnvironment("Production"),
            NullLogger<AesGcmIdentityOutboxPayloadProtector>.Instance));

        Assert.Contains("NOTIFICATION_ENCRYPTION_KEY must represent a valid 256-bit", ex.Message);
    }

    [Fact]
    public void KeyParsing_33HexChars_ThrowsInvalidOperationException()
    {
        var invalidHex = new string('a', 33);
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["NOTIFICATION_ENCRYPTION_KEY"] = invalidHex
            })
            .Build();

        var ex = Assert.Throws<InvalidOperationException>(() => new AesGcmIdentityOutboxPayloadProtector(
            config,
            CreateEnvironment("Production"),
            NullLogger<AesGcmIdentityOutboxPayloadProtector>.Instance));

        Assert.Contains("NOTIFICATION_ENCRYPTION_KEY must represent a valid 256-bit", ex.Message);
    }

    [Fact]
    public void KeyParsing_MultiByteUnicodeLengthMismatch_ThrowsInvalidOperationException()
    {
        var multiByteKey = new string('ğ', 32);
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["NOTIFICATION_ENCRYPTION_KEY"] = multiByteKey
            })
            .Build();

        var ex = Assert.Throws<InvalidOperationException>(() => new AesGcmIdentityOutboxPayloadProtector(
            config,
            CreateEnvironment("Production"),
            NullLogger<AesGcmIdentityOutboxPayloadProtector>.Instance));

        Assert.Contains("NOTIFICATION_ENCRYPTION_KEY must represent a valid 256-bit", ex.Message);
    }

    [Fact]
    public void MissingKey_InProduction_ThrowsInvalidOperationException()
    {
        var config = new ConfigurationBuilder().Build();

        var ex = Assert.Throws<InvalidOperationException>(() => new AesGcmIdentityOutboxPayloadProtector(
            config,
            CreateEnvironment("Production"),
            NullLogger<AesGcmIdentityOutboxPayloadProtector>.Instance));

        Assert.Contains("NOTIFICATION_ENCRYPTION_KEY is required", ex.Message);
    }

    [Fact]
    public void MissingKey_InDevelopment_UsesDevFallbackKey()
    {
        var config = new ConfigurationBuilder().Build();

        var protector = new AesGcmIdentityOutboxPayloadProtector(
            config,
            CreateEnvironment("Development"),
            NullLogger<AesGcmIdentityOutboxPayloadProtector>.Instance);

        var encrypted = protector.Protect("hello dev");
        Assert.Equal("hello dev", protector.Unprotect(encrypted));
    }

    [Fact]
    public void Unprotect_TamperedCiphertext_ThrowsCryptographicException()
    {
        var hexKey = new string('a', 64);
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["NOTIFICATION_ENCRYPTION_KEY"] = hexKey })
            .Build();

        var protector = new AesGcmIdentityOutboxPayloadProtector(
            config,
            CreateEnvironment("Test"),
            NullLogger<AesGcmIdentityOutboxPayloadProtector>.Instance);

        var ciphertext = protector.Protect("secret message");
        var bytes = Convert.FromBase64String(ciphertext);

        // Tamper with the last byte of ciphertext
        bytes[^1] ^= 0xFF;
        var tampered = Convert.ToBase64String(bytes);

        Assert.ThrowsAny<CryptographicException>(() => protector.Unprotect(tampered));
    }

    [Fact]
    public void Unprotect_TamperedTag_ThrowsCryptographicException()
    {
        var hexKey = new string('b', 64);
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["NOTIFICATION_ENCRYPTION_KEY"] = hexKey })
            .Build();

        var protector = new AesGcmIdentityOutboxPayloadProtector(
            config,
            CreateEnvironment("Test"),
            NullLogger<AesGcmIdentityOutboxPayloadProtector>.Instance);

        var ciphertext = protector.Protect("secret message");
        var bytes = Convert.FromBase64String(ciphertext);

        // Nonce is bytes 0..11, tag is bytes 12..27
        bytes[15] ^= 0xAA;
        var tampered = Convert.ToBase64String(bytes);

        Assert.ThrowsAny<CryptographicException>(() => protector.Unprotect(tampered));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null)]
    public void Unprotect_EmptyOrNull_ThrowsArgumentException(string? invalidInput)
    {
        var hexKey = new string('c', 64);
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["NOTIFICATION_ENCRYPTION_KEY"] = hexKey })
            .Build();

        var protector = new AesGcmIdentityOutboxPayloadProtector(
            config,
            CreateEnvironment("Test"),
            NullLogger<AesGcmIdentityOutboxPayloadProtector>.Instance);

        Assert.Throws<ArgumentException>(() => protector.Unprotect(invalidInput!));
    }

    [Fact]
    public void Unprotect_TruncatedData_ThrowsCryptographicException()
    {
        var hexKey = new string('d', 64);
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["NOTIFICATION_ENCRYPTION_KEY"] = hexKey })
            .Build();

        var protector = new AesGcmIdentityOutboxPayloadProtector(
            config,
            CreateEnvironment("Test"),
            NullLogger<AesGcmIdentityOutboxPayloadProtector>.Instance);

        var truncated = Convert.ToBase64String(new byte[10]); // less than Nonce + Tag (28 bytes)
        Assert.Throws<CryptographicException>(() => protector.Unprotect(truncated));
    }

    [Fact]
    public void Protect_NullPayload_ThrowsArgumentNullException()
    {
        var hexKey = new string('e', 64);
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["NOTIFICATION_ENCRYPTION_KEY"] = hexKey })
            .Build();

        var protector = new AesGcmIdentityOutboxPayloadProtector(
            config,
            CreateEnvironment("Test"),
            NullLogger<AesGcmIdentityOutboxPayloadProtector>.Instance);

        Assert.Throws<ArgumentNullException>(() => protector.Protect(null!));
    }
}
