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

public sealed class RetailMasterTests
{
    [Fact]
    public async Task Category_and_unit_masters_are_scoped_validated_and_subscription_gated()
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
        using var other = factory.CreateClient();
        using var manager = factory.CreateClient();
        using var cashier = factory.CreateClient();

        Assert.Equal(HttpStatusCode.Unauthorized,
            (await owner.GetAsync("/api/businesses/1/categories?page=1")).StatusCode);

        await Register(owner, "retail-owner@example.test");
        await Register(other, "retail-other@example.test");
        await Register(manager, "retail-manager@example.test");
        await Register(cashier, "retail-cashier@example.test");

        var firstId = await CreateBusiness(owner, "First Retail", "retail-first-20261112");
        var secondId = await CreateBusiness(other, "Other Retail", "retail-other-20261112");
        var categories = $"/api/businesses/{firstId}/categories";
        var units = $"/api/businesses/{firstId}/units";

        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var managerId = await db.Users.Where(x => x.Email == "retail-manager@example.test")
                .Select(x => x.Id).SingleAsync();
            var cashierId = await db.Users.Where(x => x.Email == "retail-cashier@example.test")
                .Select(x => x.Id).SingleAsync();
            db.BusinessUsers.AddRange(
                new BusinessUser { BusinessId = firstId, UserId = managerId, RoleCode = "Manager" },
                new BusinessUser { BusinessId = firstId, UserId = cashierId, RoleCode = "Cashier" });
            await db.SaveChangesAsync();
        }

        var category = await Post(owner, categories, new { name = "Hardware" });
        Assert.Equal(HttpStatusCode.Created, category.StatusCode);
        var categoryId = (await category.Content.ReadFromJsonAsync<JsonElement>())
            .GetProperty("id").GetInt64();
        Assert.True(categoryId > 0);

        Assert.Equal(HttpStatusCode.Conflict,
            (await Post(owner, categories, new { name = " hardware " })).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest,
            (await Post(owner, categories, new { name = " " })).StatusCode);

        var unit = await Post(manager, units, new { code = "pcs", name = "Pieces" });
        Assert.Equal(HttpStatusCode.Created, unit.StatusCode);
        var unitId = (await unit.Content.ReadFromJsonAsync<JsonElement>())
            .GetProperty("id").GetInt64();
        Assert.True(unitId > 0);

        Assert.Equal(HttpStatusCode.Conflict,
            (await Post(owner, units, new { code = "PCS", name = "Another Name" })).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict,
            (await Post(owner, units, new { code = "EA", name = "pieces" })).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest,
            (await Post(owner, units, new { code = "BAD CODE", name = "Bad Unit" })).StatusCode);

        var filtered = await owner.GetFromJsonAsync<JsonElement>(categories + "?page=1&search=hard");
        Assert.Equal(1, filtered.GetProperty("total").GetInt32());
        Assert.Equal(categoryId, filtered.GetProperty("items")[0].GetProperty("id").GetInt64());

        Assert.Equal(HttpStatusCode.OK, (await manager.GetAsync(units + "?page=1")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await cashier.GetAsync(categories + "?page=1")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await other.GetAsync(categories + "?page=1")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound,
            (await Put(other, categories + "/" + categoryId,
                new { name = "Stolen", isActive = false })).StatusCode);

        var changed = await Put(manager, units + "/" + unitId,
            new { code = "EA", name = "Each", isActive = false });
        Assert.Equal(HttpStatusCode.OK, changed.StatusCode);

        var readUnit = await owner.GetFromJsonAsync<JsonElement>(units + "?page=1");
        Assert.False(readUnit.GetProperty("items")[0].GetProperty("isActive").GetBoolean());
        Assert.Equal("EA", readUnit.GetProperty("items")[0].GetProperty("code").GetString());

        Assert.Equal(HttpStatusCode.NotFound,
            (await Put(manager, categories + "/9999999",
                new { name = "Unknown", isActive = true })).StatusCode);

        Assert.Equal(HttpStatusCode.Created,
            (await Post(other, $"/api/businesses/{secondId}/categories",
                new { name = "hardware" })).StatusCode);

        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            Assert.Equal(1, await db.RetailCategories.CountAsync(x => x.BusinessId == firstId));
            Assert.Equal(1, await db.RetailCategories.CountAsync(x => x.BusinessId == secondId));
            var subscription = await db.BusinessSubscriptions.SingleAsync(x => x.BusinessId == firstId);
            subscription.TrialEndsAtUtc = DateTime.UtcNow.AddMinutes(-1);
            await db.SaveChangesAsync();
        }

        Assert.Equal(HttpStatusCode.Forbidden,
            (await Post(owner, categories, new { name = "After Trial" })).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden,
            (await Put(manager, units + "/" + unitId,
                new { code = "EA", name = "Each", isActive = true })).StatusCode);
        Assert.Equal(HttpStatusCode.OK,
            (await owner.GetAsync(categories + "?page=1")).StatusCode);
    }

    private static async Task Register(HttpClient client, string email)
    {
        Assert.Equal(HttpStatusCode.OK,
            (await Post(client, "/api/auth/register",
                new { email, password = "TestPassword123!" })).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent,
            (await Post(client, "/api/auth/login",
                new { email, password = "TestPassword123!" })).StatusCode);
    }

    private static async Task<long> CreateBusiness(HttpClient client, string name, string key)
    {
        var response = await Post(client, "/api/businesses", new
        {
            name, businessTypeId = 1, countryCode = "IN", currencyCode = "INR",
            timeZoneId = "Asia/Kolkata", requestKey = key
        });
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<JsonElement>())
            .GetProperty("id").GetInt64();
    }

    private static Task<HttpResponseMessage> Post(HttpClient client, string url, object body) =>
        Send(client, HttpMethod.Post, url, body);

    private static Task<HttpResponseMessage> Put(HttpClient client, string url, object body) =>
        Send(client, HttpMethod.Put, url, body);

    private static async Task<HttpResponseMessage> Send(HttpClient client,
        HttpMethod method, string url, object body)
    {
        var token = await client.GetFromJsonAsync<JsonElement>("/api/auth/csrf");
        using var request = new HttpRequestMessage(method, url)
        {
            Content = JsonContent.Create(body)
        };
        request.Headers.Add("X-CSRF-TOKEN", token.GetProperty("token").GetString()!);
        return await client.SendAsync(request);
    }
}
