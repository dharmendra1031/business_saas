using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using BusinessSaas.Api.Data;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Xunit;

namespace BusinessSaas.Tests;

public sealed class BusinessFlowTests
{
    [Fact]
    public async Task Accounts_are_isolated_and_business_creation_is_idempotent()
    {
        using var database = new SqliteConnection("Data Source=:memory:");
        await database.OpenAsync();

        using var factory = new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment("Development");
            builder.ConfigureServices(services =>
            {
                services.RemoveAll<DbContextOptions<AppDbContext>>();
                services.RemoveAll<IDbContextOptionsConfiguration<AppDbContext>>();
                services.AddDbContext<AppDbContext>(options => options.UseSqlite(database));
            });
        });

        using var owner = factory.CreateClient();
        using var stranger = factory.CreateClient();

        var unauthenticated = await stranger.GetAsync("/api/businesses");
        Assert.Equal(HttpStatusCode.Unauthorized, unauthenticated.StatusCode);

        await RegisterAndLogin(owner, "owner@example.test");
        await RegisterAndLogin(stranger, "stranger@example.test");

        Assert.Equal(HttpStatusCode.OK, (await owner.GetAsync("/api/auth/me")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await stranger.GetAsync("/api/auth/me")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await owner.GetAsync("/api/business-types")).StatusCode);
        var markets = await owner.GetFromJsonAsync<JsonElement>("/api/business-markets");
        Assert.Equal(249, markets.GetArrayLength());
        Assert.Contains(markets.EnumerateArray(), item => item.GetProperty("code").GetString() == "IN");
        Assert.Contains(markets.EnumerateArray(), item => item.GetProperty("code").GetString() == "US");
        Assert.Contains(markets.EnumerateArray(), item => item.GetProperty("code").GetString() == "KW");

        var invalidMarket = await Post(owner, "/api/businesses", new
        {
            name = "Invalid Country Shop",
            businessTypeId = 1,
            countryCode = "XX",
            currencyCode = "USD",
            timeZoneId = "Unknown/Zone",
            requestKey = "invalid-market-20261110"
        });
        Assert.Equal(HttpStatusCode.BadRequest, invalidMarket.StatusCode);

        var invalidCurrency = await Post(owner, "/api/businesses", new
        {
            name = "Wrong Currency Shop",
            businessTypeId = 1,
            countryCode = "IN",
            currencyCode = "USD",
            timeZoneId = "Asia/Kolkata",
            requestKey = "invalid-currency-20261110"
        });
        Assert.Equal(HttpStatusCode.BadRequest, invalidCurrency.StatusCode);

        var usaMarket = await Post(owner, "/api/businesses", new
        {
            name = "West Coast Retail",
            businessTypeId = 1,
            countryCode = "US",
            currencyCode = "USD",
            timeZoneId = "America/Los_Angeles",
            requestKey = "united-states-20261110"
        });
        Assert.Equal(HttpStatusCode.Created, usaMarket.StatusCode);

        var creation = new
        {
            name = "Main Retail Shop",
            businessTypeId = 1,
            countryCode = "IN",
            currencyCode = "INR",
            timeZoneId = "Asia/Kolkata",
            requestKey = "request-main-20261109"
        };

        var first = await Post(owner, "/api/businesses", creation);
        Assert.True(first.StatusCode == HttpStatusCode.Created, await first.Content.ReadAsStringAsync());
        var firstId = (await first.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetInt64();
        Assert.True(firstId > 0);

        var retry = await Post(owner, "/api/businesses", creation);
        Assert.Equal(HttpStatusCode.OK, retry.StatusCode);
        var sameId = (await retry.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetInt64();
        Assert.Equal(firstId, sameId);

        var owned = await owner.GetFromJsonAsync<JsonElement>("/api/businesses");
        Assert.Equal(2, owned.GetArrayLength());

        var anotherOwned = await Post(owner, "/api/businesses", new
        {
            name = "Owner's Second Shop",
            businessTypeId = 1,
            countryCode = "KW",
            currencyCode = "KWD",
            timeZoneId = "Asia/Kuwait",
            requestKey = "request-second-owned-20261109"
        });
        Assert.Equal(HttpStatusCode.Created, anotherOwned.StatusCode);
        var otherOwnedId = (await anotherOwned.Content.ReadFromJsonAsync<JsonElement>())
            .GetProperty("id").GetInt64();
        Assert.NotEqual(firstId, otherOwnedId);

        owned = await owner.GetFromJsonAsync<JsonElement>("/api/businesses");
        Assert.Equal(3, owned.GetArrayLength());
        Assert.Equal(HttpStatusCode.OK,
            (await owner.GetAsync($"/api/businesses/{otherOwnedId}/dashboard")).StatusCode);
        var ownerDashboardData = await owner.GetFromJsonAsync<JsonElement>(
            $"/api/businesses/{otherOwnedId}/dashboard");
        Assert.True(ownerDashboardData.GetProperty("canOperate").GetBoolean());
        Assert.Equal("Trial", ownerDashboardData.GetProperty("subscription").GetProperty("status").GetString());
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var subscription = await db.BusinessSubscriptions
                .SingleAsync(x => x.BusinessId == otherOwnedId);
            subscription.TrialEndsAtUtc = DateTime.UtcNow.AddDays(-1);
            await db.SaveChangesAsync();
        }
        ownerDashboardData = await owner.GetFromJsonAsync<JsonElement>(
            $"/api/businesses/{otherOwnedId}/dashboard");
        Assert.False(ownerDashboardData.GetProperty("canOperate").GetBoolean());

        var second = await Post(stranger, "/api/businesses", new
        {
            name = "Another Shop",
            businessTypeId = 1,
            countryCode = "IN",
            currencyCode = "INR",
            timeZoneId = "Asia/Kolkata",
            requestKey = "request-other-20261109"
        });
        Assert.True(second.StatusCode == HttpStatusCode.Created, await second.Content.ReadAsStringAsync());
        var secondId = (await second.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetInt64();
        Assert.NotEqual(firstId, secondId);

        var ownerDashboard = await owner.GetAsync($"/api/businesses/{firstId}/dashboard");
        Assert.Equal(HttpStatusCode.OK, ownerDashboard.StatusCode);

        Assert.Equal(HttpStatusCode.NotFound,
            (await stranger.GetAsync($"/api/businesses/{firstId}/dashboard")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound,
            (await owner.GetAsync($"/api/businesses/{secondId}/dashboard")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound,
            (await stranger.GetAsync($"/api/businesses/{otherOwnedId}/dashboard")).StatusCode);

        var strangerList = await stranger.GetFromJsonAsync<JsonElement>("/api/businesses");
        Assert.Equal(1, strangerList.GetArrayLength());
        Assert.Equal(secondId, strangerList[0].GetProperty("id").GetInt64());
    }

    [Fact]
    public async Task Platform_license_access_requires_explicit_permission()
    {
        using var database = new SqliteConnection("Data Source=:memory:");
        await database.OpenAsync();

        using var factory = new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment("Development");
            builder.ConfigureServices(services =>
            {
                services.RemoveAll<DbContextOptions<AppDbContext>>();
                services.RemoveAll<IDbContextOptionsConfiguration<AppDbContext>>();
                services.AddDbContext<AppDbContext>(options => options.UseSqlite(database));
            });
        });

        using var owner = factory.CreateClient();
        Assert.Equal(HttpStatusCode.Unauthorized,
            (await owner.GetAsync("/api/platform/license-access")).StatusCode);

        await RegisterAndLogin(owner, "license-admin@example.test");
        Assert.Equal(HttpStatusCode.Forbidden,
            (await owner.GetAsync("/api/platform/license-access")).StatusCode);

        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var user = await db.Users.SingleAsync(x => x.Email == "license-admin@example.test");
            db.Set<Microsoft.AspNetCore.Identity.IdentityUserClaim<long>>().Add(new()
            {
                UserId = user.Id,
                ClaimType = "platform.permission",
                ClaimValue = "licenses.manage"
            });
            await db.SaveChangesAsync();
        }

        var granted = await owner.GetFromJsonAsync<JsonElement>("/api/platform/license-access");
        Assert.True(granted.GetProperty("canManageLicenses").GetBoolean());

        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var claim = await db.Set<Microsoft.AspNetCore.Identity.IdentityUserClaim<long>>()
                .SingleAsync(x => x.ClaimType == "platform.permission" && x.ClaimValue == "licenses.manage");
            db.Remove(claim);
            await db.SaveChangesAsync();
        }

        Assert.Equal(HttpStatusCode.Forbidden,
            (await owner.GetAsync("/api/platform/license-access")).StatusCode);
    }

    [Fact]
    public async Task Production_registration_stays_disabled_without_email_verification()
    {
        using var factory = new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment("Production");
            builder.UseSetting("ConnectionStrings:Default",
                "Server=localhost;Database=BusinessSaasUnused;Trusted_Connection=True;Encrypt=False");
        });

        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            BaseAddress = new Uri("https://localhost")
        });

        var response = await Post(client, "/api/auth/register", new
        {
            email = "unverified@example.test",
            password = "SecurePassword123!"
        });

        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
    }

    [Fact]
    public async Task Writes_require_an_antiforgery_token()
    {
        using var database = new SqliteConnection("Data Source=:memory:");
        await database.OpenAsync();

        using var factory = new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment("Development");
            builder.ConfigureServices(services =>
            {
                services.RemoveAll<DbContextOptions<AppDbContext>>();
                services.RemoveAll<IDbContextOptionsConfiguration<AppDbContext>>();
                services.AddDbContext<AppDbContext>(options => options.UseSqlite(database));
            });
        });

        using var client = factory.CreateClient();
        var response = await client.PostAsJsonAsync("/api/auth/register", new
        {
            email = "test@example.test",
            password = "SecurePassword123!"
        });
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    private static async Task RegisterAndLogin(HttpClient client, string email)
    {
        var register = await Post(client, "/api/auth/register", new
        {
            email,
            password = "SecurePassword123!"
        });
        Assert.Equal(HttpStatusCode.OK, register.StatusCode);

        var login = await Post(client, "/api/auth/login", new
        {
            email,
            password = "SecurePassword123!"
        });
        Assert.Equal(HttpStatusCode.NoContent, login.StatusCode);
    }

    private static async Task<HttpResponseMessage> Post(HttpClient client, string url, object body)
    {
        var csrfResponse = await client.GetFromJsonAsync<JsonElement>("/api/auth/csrf");
        var token = csrfResponse.GetProperty("token").GetString()!;

        using var request = new HttpRequestMessage(HttpMethod.Post, url)
        {
            Content = JsonContent.Create(body)
        };
        request.Headers.Add("X-CSRF-TOKEN", token);
        return await client.SendAsync(request);
    }
}
