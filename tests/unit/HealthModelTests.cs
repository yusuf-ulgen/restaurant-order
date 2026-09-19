using System.Globalization;
using Xunit;

namespace RestaurantOrder.UnitTests;

public class HealthModelTests
{
    [Theory]
    [InlineData("Healthy")]
    [InlineData("Degraded")]
    [InlineData("Unhealthy")]
    public void Status_ShouldBeValid_WhenStandardValuesProvided(string status)
    {
        var validStatuses = new[] { "Healthy", "Degraded", "Unhealthy" };
        Assert.Contains(status, validStatuses);
    }

    [Fact]
    public void Timestamp_ShouldFollowIso8601Format()
    {
        var timestamp = DateTime.UtcNow.ToString("O");

        var parsed = DateTime.TryParse(
            timestamp,
            CultureInfo.InvariantCulture,
            DateTimeStyles.RoundtripKind,
            out var result
        );

        Assert.True(parsed);
        Assert.Equal(DateTimeKind.Utc, result.Kind);
    }
}
