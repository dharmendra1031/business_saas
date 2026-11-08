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
        Assert.Equal(HttpStatusCode.Created, first.StatusCode);
        var firstId = (await first.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetInt64();
        Assert.True(firstId > 0);

        var retry = await Post(owner, "/api/businesses", creation);
        Assert.Equal(HttpStatusCode.OK, retry.StatusCode);
        var sameId = (await retry.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetInt64();
        Assert.Equal(firstId, sameId);

        var owned = await owner.GetFromJsonAsync<JsonElement>("/api/businesses");
        Assert.Equal(1, owned.GetArrayLength());

        var second = await Post(stranger, "/api/businesses", new
        {
            name = "Another Shop",
            businessTypeId = 1,
            countryCode = "IN",
            currencyCode = "INR",
            timeZoneId = "Asia/Kolkata",
            requestKey = "request-other-20261109"
        });
        Assert.Equal(HttpStatusCode.Created, second.StatusCode);
        var secondId = (await second.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetInt64();
        Assert.NotEqual(firstId, secondId);

        var ownerDashboard = await owner.GetAsync($"/api/businesses/{firstId}/dashboard");
        Assert.Equal(HttpStatusCode.OK, ownerDashboard.StatusCode);

        Assert.Equal(HttpStatusCode.NotFound,
            (await stranger.GetAsync($"/api/businesses/{firstId}/dashboard")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound,
            (await owner.GetAsync($"/api/businesses/{secondId}/dashboard")).StatusCode);

        var strangerList = await stranger.GetFromJsonAsync<JsonElement>("/api/businesses");
        Assert.Equal(1, strangerList.GetArrayLength());
        Assert.Equal(secondId, strangerList[0].GetProperty("id").GetInt64());
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
