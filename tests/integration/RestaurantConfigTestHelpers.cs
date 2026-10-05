using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.IdentityModel.Tokens;
using Moq;
using Npgsql;
using RestaurantOrder.Api.Auth;
using RestaurantOrder.Application.Auth;
using RestaurantOrder.Infrastructure.Persistence;

namespace RestaurantOrder.IntegrationTests;

public static class RestaurantConfigTestHelpers
{
    public static HttpClient CreateTestClient(TestcontainersFixture fixture)
    {
        var factory = new WebApplicationFactory<RestaurantOrder.Api.Program>().WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment("Development");
            builder.UseSetting("ConnectionStrings:Database", fixture.DatabaseConnectionString);
            builder.UseSetting("DATABASE_URL", fixture.DatabaseConnectionString);
            builder.UseSetting("REDIS_URL", fixture.RedisEndpoint);
            builder.UseSetting("JWT_SECRET", RestaurantOrder.Api.ConfigurationValidator.InsecureDevJwtSecret);
            builder.UseSetting("DEPLOYMENT_COLOR", "blue");
            builder.ConfigureServices(services =>
            {
                var mockValidator = new Mock<ITokenRevocationValidator>();
                mockValidator.Setup(v => v.ValidateTokenActiveAsync(
                        It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
                    .ReturnsAsync(true);
                services.AddScoped(_ => mockValidator.Object);
            });
        });

        return factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            HandleCookies = false
        });
    }

    public static string GenerateToken(
        Guid userId,
        Guid sessionId,
        Guid? tenantId = null,
        Guid? branchId = null,
        string role = "RestaurantAdmin")
    {
        var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(RestaurantOrder.Api.ConfigurationValidator.InsecureDevJwtSecret))
        {
            KeyId = "k1"
        };
        var creds = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);
        var now = DateTimeOffset.UtcNow;
        var expires = now.AddMinutes(15);
        var claims = new List<Claim>
        {
            new(JwtClaimNames.Subject, userId.ToString()),
            new(JwtClaimNames.SessionId, sessionId.ToString()),
            new(JwtClaimNames.JwtId, Guid.NewGuid().ToString("N")),
            new(JwtClaimNames.PrincipalType, "staff"),
            new(JwtClaimNames.Role, role),
            new(JwtClaimNames.AuthMethod, "password"),
            new(JwtClaimNames.SecurityVersion, "1"),
            new(JwtRegisteredClaimNames.Iss, "restaurant-order"),
            new(JwtRegisteredClaimNames.Aud, "restaurant-order-clients"),
            new(JwtRegisteredClaimNames.Iat, now.ToUnixTimeSeconds().ToString(), ClaimValueTypes.Integer64),
            new(JwtRegisteredClaimNames.Nbf, now.ToUnixTimeSeconds().ToString(), ClaimValueTypes.Integer64),
            new(JwtRegisteredClaimNames.Exp, expires.ToUnixTimeSeconds().ToString(), ClaimValueTypes.Integer64)
        };

        if (tenantId.HasValue)
        {
            claims.Add(new Claim(JwtClaimNames.TenantId, tenantId.Value.ToString()));
        }

        if (branchId.HasValue)
        {
            claims.Add(new Claim(JwtClaimNames.BranchId, branchId.Value.ToString()));
        }

        var token = new JwtSecurityToken(
            issuer: "restaurant-order",
            audience: "restaurant-order-clients",
            claims: claims,
            expires: expires.UtcDateTime,
            signingCredentials: creds);

        return new JwtSecurityTokenHandler().WriteToken(token);
    }

    public static string GenerateCustomerToken(
        Guid tenantId,
        Guid branchId,
        Guid tableSessionId)
    {
        var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(RestaurantOrder.Api.ConfigurationValidator.InsecureDevJwtSecret))
        {
            KeyId = "k1"
        };
        var creds = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);
        var now = DateTimeOffset.UtcNow;
        var expires = now.AddMinutes(15);
        var claims = new List<Claim>
        {
            new(JwtClaimNames.Subject, tableSessionId.ToString()),
            new(JwtClaimNames.SessionId, tableSessionId.ToString()),
            new(JwtClaimNames.JwtId, Guid.NewGuid().ToString("N")),
            new(JwtClaimNames.PrincipalType, "customer"),
            new(JwtClaimNames.Role, "Customer"),
            new(JwtClaimNames.AuthMethod, "customer_qr_session"),
            new(JwtClaimNames.SecurityVersion, "0"),
            new(JwtClaimNames.TenantId, tenantId.ToString()),
            new(JwtClaimNames.BranchId, branchId.ToString()),
            new(JwtClaimNames.TableSessionId, tableSessionId.ToString()),
            new(JwtRegisteredClaimNames.Iss, "restaurant-order"),
            new(JwtRegisteredClaimNames.Aud, "restaurant-order-clients"),
            new(JwtRegisteredClaimNames.Iat, now.ToUnixTimeSeconds().ToString(), ClaimValueTypes.Integer64),
            new(JwtRegisteredClaimNames.Nbf, now.ToUnixTimeSeconds().ToString(), ClaimValueTypes.Integer64),
            new(JwtRegisteredClaimNames.Exp, expires.ToUnixTimeSeconds().ToString(), ClaimValueTypes.Integer64)
        };

        var token = new JwtSecurityToken(
            issuer: "restaurant-order",
            audience: "restaurant-order-clients",
            claims: claims,
            expires: expires.UtcDateTime,
            signingCredentials: creds);

        return new JwtSecurityTokenHandler().WriteToken(token);
    }

    public static async Task SeedTenantAsync(
        string connectionString,
        Guid tenantId,
        string name,
        string slug)
    {
        await using var conn = new NpgsqlConnection(connectionString);
        await conn.OpenAsync();
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = @"
            INSERT INTO tenancy.tenants (id, name, slug, status, created_at, concurrency_token)
            VALUES (@id, @name, @slug, 'Active', NOW(), gen_random_uuid())
            ON CONFLICT (id) DO NOTHING;";
        cmd.Parameters.AddWithValue("id", tenantId);
        cmd.Parameters.AddWithValue("name", name);
        cmd.Parameters.AddWithValue("slug", slug);
        await cmd.ExecuteNonQueryAsync();
    }

    public static HttpRequestMessage CreateAuthenticatedRequest(
        HttpMethod method,
        string uri,
        string token,
        string? csrfToken = "valid_csrf_token_secret")
    {
        var request = new HttpRequestMessage(method, uri);
        request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token);

        if (!string.IsNullOrEmpty(csrfToken))
        {
            request.Headers.Add(AuthCookieService.CsrfHeaderName, csrfToken);
            request.Headers.Add("Cookie", $"{AuthCookieService.CsrfTokenCookieName}={csrfToken}");
        }

        return request;
    }
}
