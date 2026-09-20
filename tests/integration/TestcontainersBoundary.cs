namespace RestaurantOrder.IntegrationTests;

/// <summary>
/// Architectural boundary specification for Testcontainers integration testing.
/// In Phase 4 (Database & Migrations), integration tests will spin up isolated
/// disposable PostgreSQL 16 and Redis 7 containers per test collection using
/// Testcontainers.PostgreSql and Testcontainers.Redis.
/// </summary>
public interface ITestDatabaseFixture
{
    /// <summary>
    /// Connection string provided dynamically by the ephemeral PostgreSQL container.
    /// </summary>
    string DatabaseConnectionString { get; }

    /// <summary>
    /// Connection endpoint provided dynamically by the ephemeral Redis container.
    /// </summary>
    string RedisEndpoint { get; }

    /// <summary>
    /// Initializes container instances and executes pending database migrations.
    /// </summary>
    Task InitializeAsync();

    /// <summary>
    /// Disposes container instances and cleans up Docker resources.
    /// </summary>
    Task DisposeAsync();
}
