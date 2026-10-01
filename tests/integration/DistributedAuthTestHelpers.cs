using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using RestaurantOrder.Application.Auth;
using RestaurantOrder.Infrastructure.Auth;
using RestaurantOrder.Infrastructure.Persistence;
using RestaurantOrder.Infrastructure.Redis;

namespace RestaurantOrder.IntegrationTests;

/// <summary>
/// Helper instance simulating an independent API instance in a distributed deployment.
/// </summary>
internal sealed class TestApiInstance : IAsyncDisposable
{
    public string Name { get; }
    public RestaurantOrderDbContext DbContext { get; }
    public StackExchangeRedisDatabaseProvider RedisProvider { get; }
    public PostgreSqlIamBootstrapGateway BootstrapGateway { get; }
    public PostgreSqlIamUserLookupGateway UserLookupGateway { get; }
    public PostgreSqlPlatformSessionStore PlatformSessionStore { get; }
    public RedisLoginRateLimiter LoginRateLimiter { get; }
    public RedisTerminalEnrollmentStore TerminalEnrollmentStore { get; }
    public RedisTerminalPinRateLimiter PinRateLimiter { get; }
    public DistributedTokenRevocationValidator TokenValidator { get; }
    public AuthSessionManager SessionManager { get; }
    public AuthService AuthService { get; }
    public TrustedTerminalService TerminalService { get; }
    public StaffPinAuthService PinAuthService { get; }
    public IPasswordHasher PasswordHasher { get; }

    public TestApiInstance(
        string name,
        RestaurantOrderDbContext dbContext,
        StackExchangeRedisDatabaseProvider redisProvider,
        PostgreSqlIamBootstrapGateway bootstrapGateway,
        PostgreSqlIamUserLookupGateway userLookupGateway,
        PostgreSqlPlatformSessionStore platformSessionStore,
        RedisLoginRateLimiter loginRateLimiter,
        RedisTerminalEnrollmentStore terminalEnrollmentStore,
        RedisTerminalPinRateLimiter pinRateLimiter,
        DistributedTokenRevocationValidator tokenValidator,
        AuthSessionManager sessionManager,
        AuthService authService,
        TrustedTerminalService terminalService,
        StaffPinAuthService pinAuthService,
        IPasswordHasher passwordHasher)
    {
        Name = name;
        DbContext = dbContext;
        RedisProvider = redisProvider;
        BootstrapGateway = bootstrapGateway;
        UserLookupGateway = userLookupGateway;
        PlatformSessionStore = platformSessionStore;
        LoginRateLimiter = loginRateLimiter;
        TerminalEnrollmentStore = terminalEnrollmentStore;
        PinRateLimiter = pinRateLimiter;
        TokenValidator = tokenValidator;
        SessionManager = sessionManager;
        AuthService = authService;
        TerminalService = terminalService;
        PinAuthService = pinAuthService;
        PasswordHasher = passwordHasher;
    }

    public async ValueTask DisposeAsync()
    {
        await DbContext.DisposeAsync();
        await RedisProvider.DisposeAsync();
    }
}

/// <summary>
/// Stub host environment for distributed auth integration tests.
/// </summary>
internal sealed class DistributedTestHostEnvironment : IHostEnvironment
{
    public string EnvironmentName { get; set; } = "Testing";
    public string ApplicationName { get; set; } = "RestaurantOrder.IntegrationTests";
    public string ContentRootPath { get; set; } = AppContext.BaseDirectory;
    public IFileProvider ContentRootFileProvider { get; set; } = null!;
}
