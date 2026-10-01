using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Moq;
using RestaurantOrder.Api;
using Xunit;

namespace RestaurantOrder.UnitTests.Security;

public class SecurityConfigurationValidatorTests
{
    private static IHostEnvironment CreateEnvironment(string environmentName)
    {
        var mock = new Mock<IHostEnvironment>();
        mock.Setup(e => e.EnvironmentName).Returns(environmentName);
        return mock.Object;
    }

    private static string TestHexKey() => Convert.ToHexString(new byte[32]);
    private static string TestSecret(string name) => $"test_{name}_{new string('x', 32)}";

    private static Dictionary<string, string?> CreateValidStagingConfig()
    {
        return new Dictionary<string, string?>
        {
            ["DATABASE_URL"] = "postgres://user:strong_production_pass_987@db.internal:5432/ro_prod",
            ["REDIS_URL"] = "redis://redis.internal:6379",
            ["JWT_SECRET"] = TestSecret("jwt"),
            ["DEPLOYMENT_COLOR"] = "blue",
            ["PIN_PEPPER_SECRET"] = TestSecret("pin_pepper"),
            ["Cors:AllowedOrigins:0"] = "https://admin.restaurantorder.app",
            ["Cors:AllowedOrigins:1"] = "https://ops.restaurantorder.app",
            ["NOTIFICATION_PROVIDER"] = "TransactionalOutbox",
            ["NOTIFICATION_ENCRYPTION_KEY"] = TestHexKey(),
            ["WEBHOOK_NOTIFICATION_URL"] = "https://notifications.internal/webhook",
            ["WEBHOOK_NOTIFICATION_SECRET"] = TestSecret("webhook"),
            ["FORWARDED_HEADERS_ENABLED"] = "false"
        };
    }

    [Theory]
    [InlineData("Production")]
    [InlineData("Staging")]
    public void Validate_ValidStagingAndProduction_Passes(string envName)
    {
        var env = CreateEnvironment(envName);
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(CreateValidStagingConfig())
            .Build();

        ConfigurationValidator.Validate(config, env);
    }

    [Fact]
    public void Validate_OnlyAppPinPepperProvidedInProduction_ThrowsFailFast()
    {
        var env = CreateEnvironment("Production");
        var dict = CreateValidStagingConfig();
        dict.Remove("PIN_PEPPER_SECRET");
        dict["APP_PIN_PEPPER"] = TestSecret("pin_pepper");

        var config = new ConfigurationBuilder().AddInMemoryCollection(dict).Build();

        var ex = Assert.Throws<InvalidOperationException>(() => ConfigurationValidator.Validate(config, env));
        Assert.Contains("PIN_PEPPER_SECRET", ex.Message);
    }

    [Fact]
    public void Validate_HttpCorsOriginInProduction_ThrowsFailFast()
    {
        var env = CreateEnvironment("Production");
        var dict = CreateValidStagingConfig();
        dict["Cors:AllowedOrigins:0"] = "http://example.com";

        var config = new ConfigurationBuilder().AddInMemoryCollection(dict).Build();

        var ex = Assert.Throws<InvalidOperationException>(() => ConfigurationValidator.Validate(config, env));
        Assert.Contains("HTTPS origins", ex.Message);
    }

    [Fact]
    public void Validate_ForwardedHeadersEnabledWithoutProxiesOrNetworks_ThrowsFailFast()
    {
        var env = CreateEnvironment("Production");
        var dict = CreateValidStagingConfig();
        dict["FORWARDED_HEADERS_ENABLED"] = "true";

        var config = new ConfigurationBuilder().AddInMemoryCollection(dict).Build();

        var ex = Assert.Throws<InvalidOperationException>(() => ConfigurationValidator.Validate(config, env));
        Assert.Contains("neither KnownProxies nor KnownNetworks", ex.Message);
    }

    [Fact]
    public void Validate_ForwardedHeadersInvalidIpProxy_ThrowsFailFast()
    {
        var env = CreateEnvironment("Production");
        var dict = CreateValidStagingConfig();
        dict["FORWARDED_HEADERS_ENABLED"] = "true";
        dict["ForwardedHeaders:KnownProxies:0"] = "999.999.999.999";

        var config = new ConfigurationBuilder().AddInMemoryCollection(dict).Build();

        var ex = Assert.Throws<InvalidOperationException>(() => ConfigurationValidator.Validate(config, env));
        Assert.Contains("invalid IP address", ex.Message);
    }

    [Fact]
    public void Validate_ForwardedHeadersInvalidCidrNetwork_ThrowsFailFast()
    {
        var env = CreateEnvironment("Production");
        var dict = CreateValidStagingConfig();
        dict["FORWARDED_HEADERS_ENABLED"] = "true";
        dict["ForwardedHeaders:KnownNetworks:0"] = "10.0.0.1/99"; // Invalid prefix

        var config = new ConfigurationBuilder().AddInMemoryCollection(dict).Build();

        var ex = Assert.Throws<InvalidOperationException>(() => ConfigurationValidator.Validate(config, env));
        Assert.Contains("invalid CIDR notation", ex.Message);
    }

    [Fact]
    public void Validate_ForwardedHeadersValidCidrAndProxy_Passes()
    {
        var env = CreateEnvironment("Production");
        var dict = CreateValidStagingConfig();
        dict["FORWARDED_HEADERS_ENABLED"] = "true";
        dict["ForwardedHeaders:KnownProxies:0"] = "10.0.1.5";
        dict["ForwardedHeaders:KnownNetworks:0"] = "10.0.1.0/24";

        var config = new ConfigurationBuilder().AddInMemoryCollection(dict).Build();

        ConfigurationValidator.Validate(config, env);
    }

    [Fact]
    public void Validate_MissingNotificationProvider_ThrowsFailFast()
    {
        var env = CreateEnvironment("Production");
        var dict = CreateValidStagingConfig();
        dict.Remove("NOTIFICATION_PROVIDER");

        var config = new ConfigurationBuilder().AddInMemoryCollection(dict).Build();

        var ex = Assert.Throws<InvalidOperationException>(() => ConfigurationValidator.Validate(config, env));
        Assert.Contains("Notification:Provider", ex.Message);
    }

    [Fact]
    public void Validate_UnsupportedNotificationProvider_ThrowsFailFast()
    {
        var env = CreateEnvironment("Production");
        var dict = CreateValidStagingConfig();
        dict["NOTIFICATION_PROVIDER"] = "UnsupportedEmailService";

        var config = new ConfigurationBuilder().AddInMemoryCollection(dict).Build();

        var ex = Assert.Throws<InvalidOperationException>(() => ConfigurationValidator.Validate(config, env));
        Assert.Contains("Unsupported Notification:Provider", ex.Message);
    }

    [Fact]
    public void Validate_TestSinkInProduction_ThrowsFailFast()
    {
        var env = CreateEnvironment("Production");
        var dict = CreateValidStagingConfig();
        dict["NOTIFICATION_PROVIDER"] = "TestSink";

        var config = new ConfigurationBuilder().AddInMemoryCollection(dict).Build();

        var ex = Assert.Throws<InvalidOperationException>(() => ConfigurationValidator.Validate(config, env));
        Assert.Contains("TransactionalOutbox", ex.Message);
    }

    [Fact]
    public void Validate_MissingNotificationEncryptionKey_ThrowsFailFast()
    {
        var env = CreateEnvironment("Production");
        var dict = CreateValidStagingConfig();
        dict.Remove("NOTIFICATION_ENCRYPTION_KEY");

        var config = new ConfigurationBuilder().AddInMemoryCollection(dict).Build();

        var ex = Assert.Throws<InvalidOperationException>(() => ConfigurationValidator.Validate(config, env));
        Assert.Contains("NOTIFICATION_ENCRYPTION_KEY", ex.Message);
    }

    [Fact]
    public void Validate_HttpWebhookNotificationUrl_ThrowsFailFast()
    {
        var env = CreateEnvironment("Production");
        var dict = CreateValidStagingConfig();
        dict["WEBHOOK_NOTIFICATION_URL"] = "http://notifications.internal/webhook";

        var config = new ConfigurationBuilder().AddInMemoryCollection(dict).Build();

        var ex = Assert.Throws<InvalidOperationException>(() => ConfigurationValidator.Validate(config, env));
        Assert.Contains("HTTPS endpoints", ex.Message);
    }

    [Fact]
    public void Validate_ShortWebhookNotificationSecret_ThrowsFailFast()
    {
        var env = CreateEnvironment("Production");
        var dict = CreateValidStagingConfig();
        dict["WEBHOOK_NOTIFICATION_SECRET"] = "short-secret";

        var config = new ConfigurationBuilder().AddInMemoryCollection(dict).Build();

        var ex = Assert.Throws<InvalidOperationException>(() => ConfigurationValidator.Validate(config, env));
        Assert.Contains("WEBHOOK_NOTIFICATION_SECRET", ex.Message);
    }

    [Fact]
    public void Validate_MissingPinPepperSecret_ThrowsFailFast()
    {
        var env = CreateEnvironment("Production");
        var dict = CreateValidStagingConfig();
        dict.Remove("PIN_PEPPER_SECRET");

        var config = new ConfigurationBuilder().AddInMemoryCollection(dict).Build();

        var ex = Assert.Throws<InvalidOperationException>(() => ConfigurationValidator.Validate(config, env));
        Assert.Contains("PIN_PEPPER_SECRET", ex.Message);
    }

    [Theory]
    [InlineData("short_pepper_123")]
    [InlineData("1234567890123456789012345678901")] // 31 chars
    public void Validate_ShortPinPepperSecret_ThrowsFailFast(string shortPepper)
    {
        var env = CreateEnvironment("Production");
        var dict = CreateValidStagingConfig();
        dict["PIN_PEPPER_SECRET"] = shortPepper;

        var config = new ConfigurationBuilder().AddInMemoryCollection(dict).Build();

        var ex = Assert.Throws<InvalidOperationException>(() => ConfigurationValidator.Validate(config, env));
        Assert.Contains("at least 32 characters", ex.Message);
    }

    [Theory]
    [InlineData("insecure_dummy_pepper_with_lots_of_padding_123456")]
    [InlineData("placeholder_pin_pepper_secret_value_for_testing_123")]
    [InlineData("staging_secure_pass_pin_pepper_long_entropy_string")]
    public void Validate_PlaceholderPinPepperSecret_ThrowsFailFast(string insecurePepper)
    {
        var env = CreateEnvironment("Production");
        var dict = CreateValidStagingConfig();
        dict["PIN_PEPPER_SECRET"] = insecurePepper;

        var config = new ConfigurationBuilder().AddInMemoryCollection(dict).Build();

        var ex = Assert.Throws<InvalidOperationException>(() => ConfigurationValidator.Validate(config, env));
        Assert.Contains("insecure placeholder", ex.Message);
    }

    [Fact]
    public void Validate_MissingCorsAllowedOrigins_ThrowsFailFast()
    {
        var env = CreateEnvironment("Production");
        var dict = CreateValidStagingConfig();
        dict.Remove("Cors:AllowedOrigins:0");
        dict.Remove("Cors:AllowedOrigins:1");

        var config = new ConfigurationBuilder().AddInMemoryCollection(dict).Build();

        var ex = Assert.Throws<InvalidOperationException>(() => ConfigurationValidator.Validate(config, env));
        Assert.Contains("Cors:AllowedOrigins", ex.Message);
    }

    [Fact]
    public void Validate_WildcardCorsOrigin_ThrowsFailFast()
    {
        var env = CreateEnvironment("Production");
        var dict = CreateValidStagingConfig();
        dict["Cors:AllowedOrigins:0"] = "*";

        var config = new ConfigurationBuilder().AddInMemoryCollection(dict).Build();

        var ex = Assert.Throws<InvalidOperationException>(() => ConfigurationValidator.Validate(config, env));
        Assert.Contains("Wildcard", ex.Message);
    }

    [Theory]
    [InlineData("http://localhost:3000")]
    [InlineData("http://127.0.0.1:8080")]
    [InlineData("https://localhost")]
    public void Validate_LocalhostCorsOrigin_ThrowsFailFast(string localhostOrigin)
    {
        var env = CreateEnvironment("Production");
        var dict = CreateValidStagingConfig();
        dict["Cors:AllowedOrigins:0"] = localhostOrigin;

        var config = new ConfigurationBuilder().AddInMemoryCollection(dict).Build();

        var ex = Assert.Throws<InvalidOperationException>(() => ConfigurationValidator.Validate(config, env));
        Assert.Contains("Localhost origins are prohibited", ex.Message);
    }

    [Fact]
    public void Validate_InvalidUriCorsOrigin_ThrowsFailFast()
    {
        var env = CreateEnvironment("Production");
        var dict = CreateValidStagingConfig();
        dict["Cors:AllowedOrigins:0"] = "not-a-valid-uri";

        var config = new ConfigurationBuilder().AddInMemoryCollection(dict).Build();

        var ex = Assert.Throws<InvalidOperationException>(() => ConfigurationValidator.Validate(config, env));
        Assert.Contains("not a valid absolute https URI", ex.Message);
    }

    [Fact]
    public void Validate_DevelopmentEnvironment_AllowsMissingCorsAndPepper()
    {
        var env = CreateEnvironment("Development");
        var dict = new Dictionary<string, string?>(); // empty config
        var config = new ConfigurationBuilder().AddInMemoryCollection(dict).Build();

        // Must not throw in Development
        ConfigurationValidator.Validate(config, env);
    }

    [Fact]
    public void Validate_WhitespaceCorsOrigin_ThrowsFailFast()
    {
        var env = CreateEnvironment("Production");
        var dict = CreateValidStagingConfig();
        dict["Cors:AllowedOrigins:0"] = "   ";

        var config = new ConfigurationBuilder().AddInMemoryCollection(dict).Build();

        var ex = Assert.Throws<InvalidOperationException>(() => ConfigurationValidator.Validate(config, env));
        Assert.Contains("empty or whitespace entry", ex.Message);
    }

    [Fact]
    public void Validate_CorsViaEnvironmentVariable_Valid_Passes()
    {
        var env = CreateEnvironment("Production");
        var dict = CreateValidStagingConfig();
        dict.Remove("Cors:AllowedOrigins:0");
        dict.Remove("Cors:AllowedOrigins:1");
        dict["CORS_ALLOWED_ORIGINS"] = "https://app1.example.com, https://app2.example.com";

        var config = new ConfigurationBuilder().AddInMemoryCollection(dict).Build();

        ConfigurationValidator.Validate(config, env);
    }

    [Fact]
    public void Validate_ForwardedHeadersViaEnvVars_Valid_Passes()
    {
        var env = CreateEnvironment("Production");
        var dict = CreateValidStagingConfig();
        dict["FORWARDED_HEADERS_ENABLED"] = "true";
        dict["FORWARDED_HEADERS_KNOWN_PROXIES"] = "192.0.2.1, 192.0.2.2";
        dict["FORWARDED_HEADERS_KNOWN_NETWORKS"] = "198.51.100.0/24, 2001:db8::/64";

        var config = new ConfigurationBuilder().AddInMemoryCollection(dict).Build();

        ConfigurationValidator.Validate(config, env);
    }

    [Theory]
    [InlineData("2001:db8::/64", true)]
    [InlineData("2001:db8::/128", true)]
    [InlineData("2001:db8::/129", false)]
    [InlineData("10.0.0.0/0", true)]
    [InlineData("10.0.0.0/32", true)]
    [InlineData("10.0.0.0/33", false)]
    [InlineData("10.0.0.0/-1", false)]
    [InlineData("10.0.0.0", false)]
    [InlineData("10.0.0.0/24/12", false)]
    [InlineData("invalid-ip/24", false)]
    [InlineData("10.0.0.0/not-a-number", false)]
    [InlineData("", false)]
    [InlineData(null, false)]
    public void TryParseCidr_ValidatesCidrFormatsCorrectly(string? cidr, bool expectedResult)
    {
        var result = ConfigurationValidator.TryParseCidr(cidr!, out var ip, out var prefix);
        Assert.Equal(expectedResult, result);
        if (expectedResult)
        {
            Assert.NotNull(ip);
            Assert.True(prefix >= 0);
        }
    }
}
