using System.Globalization;
using System.Text.Json;
using RestaurantOrder.Api.Health;
using Xunit;

namespace RestaurantOrder.UnitTests;

/// <summary>
/// Verifies the application's Health probe response contracts and serialization.
/// Tests schema adherence, ISO 8601 timestamp compliance, and zero sensitive data leakage.
/// </summary>
public class HealthModelTests
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    [Fact]
    public void HealthLiveResponse_Contract_SerializesToExpectedCamelCaseJson()
    {
        // Arrange
        var timestamp = DateTime.UtcNow.ToString("O");
        var response = new HealthLiveResponse(
            Status: "Healthy",
            Timestamp: timestamp,
            Service: "restaurant-order-api",
            Version: "0.1.0",
            Color: "blue"
        );

        // Act
        var json = JsonSerializer.Serialize(response, JsonOptions);
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;

        // Assert contract schema
        Assert.Equal("Healthy", root.GetProperty("status").GetString());
        Assert.Equal(timestamp, root.GetProperty("timestamp").GetString());
        Assert.Equal("restaurant-order-api", root.GetProperty("service").GetString());
        Assert.Equal("0.1.0", root.GetProperty("version").GetString());
        Assert.Equal("blue", root.GetProperty("color").GetString());
    }

    [Fact]
    public void HealthReadyResponse_WhenHealthy_SerializesAllChecks()
    {
        // Arrange
        var timestamp = DateTime.UtcNow.ToString("O");
        var response = new HealthReadyResponse(
            Status: "Healthy",
            Timestamp: timestamp,
            Service: "restaurant-order-api",
            Version: "0.1.0",
            Color: "green",
            Checks: new HealthChecks(
                Database: "Healthy",
                Redis: "Healthy"
            )
        );

        // Act
        var json = JsonSerializer.Serialize(response, JsonOptions);
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;

        // Assert
        Assert.Equal("Healthy", root.GetProperty("status").GetString());
        var checks = root.GetProperty("checks");
        Assert.Equal("Healthy", checks.GetProperty("database").GetString());
        Assert.Equal("Healthy", checks.GetProperty("redis").GetString());
    }

    [Theory]
    [InlineData("Unhealthy", "Healthy")]
    [InlineData("Healthy", "Unhealthy")]
    [InlineData("Unhealthy", "Unhealthy")]
    public void HealthReadyResponse_WhenDependencyUnhealthy_ReflectsDependencyState(
        string dbStatus,
        string redisStatus)
    {
        // Arrange
        var overallStatus = (dbStatus == "Healthy" && redisStatus == "Healthy") ? "Healthy" : "Unhealthy";
        var response = new HealthReadyResponse(
            Status: overallStatus,
            Timestamp: DateTime.UtcNow.ToString("O"),
            Service: "restaurant-order-api",
            Version: "0.1.0",
            Color: "blue",
            Checks: new HealthChecks(
                Database: dbStatus,
                Redis: redisStatus
            )
        );

        // Act
        var json = JsonSerializer.Serialize(response, JsonOptions);
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;

        // Assert
        Assert.Equal("Unhealthy", root.GetProperty("status").GetString());
        var checks = root.GetProperty("checks");
        Assert.Equal(dbStatus, checks.GetProperty("database").GetString());
        Assert.Equal(redisStatus, checks.GetProperty("redis").GetString());
    }

    [Fact]
    public void HealthResponses_TimestampMustBeValidUtcIso8601()
    {
        var timestamp = DateTime.UtcNow.ToString("O");
        var live = new HealthLiveResponse("Healthy", timestamp, "restaurant-order-api", "0.1.0", "blue");

        var parsed = DateTime.TryParse(
            live.Timestamp,
            CultureInfo.InvariantCulture,
            DateTimeStyles.RoundtripKind,
            out var parsedDate
        );

        Assert.True(parsed);
        Assert.Equal(DateTimeKind.Utc, parsedDate.Kind);
    }

    [Fact]
    public void HealthResponses_MustNeverContainSensitiveKeywordsInJson()
    {
        var response = new HealthReadyResponse(
            Status: "Healthy",
            Timestamp: DateTime.UtcNow.ToString("O"),
            Service: "restaurant-order-api",
            Version: "0.1.0",
            Color: "blue",
            Checks: new HealthChecks("Healthy", "Healthy")
        );

        var json = JsonSerializer.Serialize(response, JsonOptions);

        Assert.DoesNotContain("Host=", json);
        Assert.DoesNotContain("Password=", json);
        Assert.DoesNotContain("User", json);
        Assert.DoesNotContain("Secret", json);
        Assert.DoesNotContain("Key", json);
    }
}
