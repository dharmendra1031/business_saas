using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using BusinessSaas.Api.Data;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Xunit;

namespace BusinessSaas.Tests;

public sealed class SqlServerTests
{
    [Fact]
    [Trait("Database", "SqlServer")]
    public async Task Migration_and_concurrent_business_creation_preserve_tenant_isolation()
    {
        var connectionString = Environment.GetEnvironmentVariable("SQLSERVER_TEST_CONNECTION");
        if (string.IsNullOrWhiteSpace(connectionString))
            return;

        using var factory = new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment("Development");
            builder.ConfigureServices(services =>
            {
                services.RemoveAll<DbContextOptions<AppDbContext>>();
                services.RemoveAll<IDbContextOptionsConfiguration<AppDbContext>>();
                services.AddDbContext<AppDbContext>(options => options.UseSqlServer(connectionString));
            });
        });

        using var owner = factory.CreateClient();
        using var stranger = factory.CreateClient();

        Assert.Equal(HttpStatusCode.OK, (await owner.GetAsync("/api/health")).StatusCode);

        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            Assert.Contains(await db.Database.GetAppliedMigrationsAsync(),
                migration => migration.EndsWith("_InitialCreate"));
            Assert.Contains(await db.Database.GetAppliedMigrationsAsync(),
                migration => migration.EndsWith("_AddRetailCategoriesAndUnits"));
            Assert.Contains(await db.Database.GetAppliedMigrationsAsync(),
                migration => migration.EndsWith("_AddRetailProducts"));
            Assert.True(await db.Database.CanConnectAsync());
            Assert.True(await db.BusinessTypes.AnyAsync(x => x.Code == "RETAIL" && x.IsSupported));
        }

        await RegisterAndLogin(owner, "sql-owner@example.test");
        await RegisterAndLogin(stranger, "sql-stranger@example.test");

        var creation = new
        {
            name = "SQL Retail Shop",
            businessTypeId = 1,
            countryCode = "IN",
            currencyCode = "INR",
            timeZoneId = "Asia/Kolkata",
            requestKey = "same-request-20261109"
        };

        var results = await Task.WhenAll(
            Post(owner, "/api/businesses", creation),
            Post(owner, "/api/businesses", creation));

        foreach (var result in results)
            Assert.True(result.StatusCode is HttpStatusCode.Created or HttpStatusCode.OK,
                await result.Content.ReadAsStringAsync());

        var ids = await Task.WhenAll(results.Select(async response =>
            (await response.Content.ReadFromJsonAsync<JsonElement>())
                .GetProperty("id").GetInt64()));

        Assert.True(ids[0] > 0);
        Assert.Equal(ids[0], ids[1]);

        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            Assert.Equal(1, await db.Businesses.CountAsync(x => x.Id == ids[0]));
            Assert.Equal(1, await db.BusinessUsers.CountAsync(x => x.BusinessId == ids[0]));
            Assert.Equal(1, await db.BusinessLocations.CountAsync(x => x.BusinessId == ids[0]));
        }

        Assert.Equal(HttpStatusCode.OK,
            (await owner.GetAsync($"/api/businesses/{ids[0]}/dashboard")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound,
            (await stranger.GetAsync($"/api/businesses/{ids[0]}/dashboard")).StatusCode);

        var changedPayload = await Post(owner, "/api/businesses", new
        {
            name = "Not The Same Business",
            businessTypeId = 1,
            countryCode = "IN",
            currencyCode = "INR",
            timeZoneId = "Asia/Kolkata",
            requestKey = "same-request-20261109"
        });
        Assert.Equal(HttpStatusCode.Conflict, changedPayload.StatusCode);
    }

    private static async Task RegisterAndLogin(HttpClient client, string email)
    {
        Assert.Equal(HttpStatusCode.OK, (await Post(client, "/api/auth/register",
            new { email, password = "TestPassword123!" })).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await Post(client, "/api/auth/login",
            new { email, password = "TestPassword123!" })).StatusCode);
    }

    private static async Task<HttpResponseMessage> Post(HttpClient client, string path, object body)
    {
        var tokenResponse = await client.GetFromJsonAsync<JsonElement>("/api/auth/csrf");
        var token = tokenResponse.GetProperty("token").GetString()!;

        using var request = new HttpRequestMessage(HttpMethod.Post, path)
        {
            Content = JsonContent.Create(body)
        };
        request.Headers.Add("X-CSRF-TOKEN", token);
        return await client.SendAsync(request);
    }
}
