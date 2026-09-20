using System;
using Xunit;

namespace RestaurantOrder.IntegrationTests;

/// <summary>
/// Enforces strict fail-closed policy for Testcontainers integration tests.
/// Guarantees that tests never pass as false-positives when Docker is absent or containers fail to start.
/// 
/// Policy:
/// 1. Container Initialization Failure: If Testcontainers throws an exception during startup,
///    tests FAIL immediately with the exception details.
/// 2. CI Environment (CI=true or GITHUB_ACTIONS=true or CONTINUOUS_INTEGRATION=true):
///    Docker is strictly mandatory. If Docker daemon is absent or containers are not running,
///    tests FAIL immediately (fail-closed, never green).
/// 3. Local Environment (default):
///    Docker is required by default. Tests FAIL immediately if Docker daemon is not running.
/// 4. Local Environment (explicit skip):
///    Only when SKIP_TESTCONTAINERS=true is explicitly set, tests are skipped via early return.
///    Note: Since xUnit v2 (2.9.3) does not support runtime dynamic test skipping in the runner without
///    third-party discovery extensions, early return is the documented standard mechanism for local-only skips.
/// </summary>
public static class TestcontainersGuard
{
    public static bool ShouldRun(TestcontainersFixture fixture)
    {
        if (fixture.InitializationException != null)
        {
            Assert.Fail($"Testcontainers failed to initialize containers: {fixture.InitializationException.Message}");
            return false;
        }

        if (fixture.IsDockerRunning)
        {
            return true;
        }

        var isCi = string.Equals(Environment.GetEnvironmentVariable("CI"), "true", StringComparison.OrdinalIgnoreCase) ||
                   string.Equals(Environment.GetEnvironmentVariable("GITHUB_ACTIONS"), "true", StringComparison.OrdinalIgnoreCase) ||
                   string.Equals(Environment.GetEnvironmentVariable("CONTINUOUS_INTEGRATION"), "true", StringComparison.OrdinalIgnoreCase);

        if (isCi)
        {
            Assert.Fail("Docker is required in CI for Testcontainers integration tests, but the Docker daemon is not running.");
            return false;
        }

        var explicitSkip = string.Equals(Environment.GetEnvironmentVariable("SKIP_TESTCONTAINERS"), "true", StringComparison.OrdinalIgnoreCase);
        if (explicitSkip)
        {
            return false;
        }

        Assert.Fail("Docker daemon is not running. To run integration tests locally, start Docker or explicitly set SKIP_TESTCONTAINERS=true.");
        return false;
    }
}

