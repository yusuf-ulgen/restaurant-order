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

    private static Dictionary<string, string?> CreateValidStagingConfig()
    {
        return new Dictionary<string, string?>
        {
            ["DATABASE_URL"] = "postgres://user:strong_production_pass_987@db.internal:5432/ro_prod",
            ["REDIS_URL"] = "redis://redis.internal:6379",
            ["JWT_SECRET"] = "a-very-strong-production-jwt-secret-min-32-chars-long-12345",
            ["DEPLOYMENT_COLOR"] = "blue",
            ["PIN_PEPPER_SECRET"] = "high-entropy-server-pin-pepper-secret-32-chars-min",
            ["Cors:AllowedOrigins:0"] = "https://admin.restaurantorder.app",
            ["Cors:AllowedOrigins:1"] = "https://ops.restaurantorder.app"
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
        Assert.Contains("not a valid absolute http/https URI", ex.Message);
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
}
