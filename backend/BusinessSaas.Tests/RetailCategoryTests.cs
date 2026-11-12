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

public sealed class RetailCategoryTests
{
    [Fact]
    public async Task Categories_validate_names_and_enforce_business_roles_and_trial()
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
        using var otherOwner = factory.CreateClient();
        using var cashier = factory.CreateClient();

        Assert.Equal(HttpStatusCode.Unauthorized,
            (await cashier.GetAsync("/api/businesses/1/categories")).StatusCode);

        await RegisterAndLogin(owner, "category-owner@example.test");
        await RegisterAndLogin(otherOwner, "category-other@example.test");
        await RegisterAndLogin(cashier, "category-cashier@example.test");

        var firstId = await CreateBusiness(owner, "Category First Shop", "category-first-20261112");
        var secondId = await CreateBusiness(otherOwner, "Category Other Shop", "category-other-20261112");
        var firstPath = $"/api/businesses/{firstId}/categories";
        var secondPath = $"/api/businesses/{secondId}/categories";

        var invalid = await Send(owner, HttpMethod.Post, firstPath, new { name = " " });
        Assert.Equal(HttpStatusCode.BadRequest, invalid.StatusCode);

        var created = await Send(owner, HttpMethod.Post, firstPath, new { name = "  Drinks  " });
        Assert.Equal(HttpStatusCode.OK, created.StatusCode);
        var category = await created.Content.ReadFromJsonAsync<JsonElement>();
        var categoryId = category.GetProperty("id").GetInt64();
        Assert.True(categoryId > 0);
        Assert.Equal("Drinks", category.GetProperty("name").GetString());

        var duplicate = await Send(owner, HttpMethod.Post, firstPath, new { name = "drINKS" });
        Assert.Equal(HttpStatusCode.Conflict, duplicate.StatusCode);

        Assert.Equal(HttpStatusCode.NotFound,
            (await otherOwner.GetAsync(firstPath)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound,
            (await Send(otherOwner, HttpMethod.Put, $"{firstPath}/{categoryId}",
                new { name = "Changed" })).StatusCode);

        Assert.Equal(HttpStatusCode.OK,
            (await Send(otherOwner, HttpMethod.Post, secondPath, new { name = "Drinks" })).StatusCode);

        var updated = await Send(owner, HttpMethod.Put, $"{firstPath}/{categoryId}",
            new { name = "Snacks" });
        Assert.Equal(HttpStatusCode.OK, updated.StatusCode);

        var list = await owner.GetFromJsonAsync<JsonElement>(firstPath);
        Assert.Single(list.EnumerateArray());
        Assert.Equal("Snacks", list[0].GetProperty("name").GetString());

        var assign = await Send(owner, HttpMethod.Post, $"/api/businesses/{firstId}/staff",
            new { email = "category-cashier@example.test", roleCode = "Cashier" });
        Assert.Equal(HttpStatusCode.OK, assign.StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await cashier.GetAsync(firstPath)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden,
            (await Send(cashier, HttpMethod.Post, firstPath, new { name = "Unavailable" })).StatusCode);

        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var subscription = await db.BusinessSubscriptions.SingleAsync(x => x.BusinessId == firstId);
            subscription.TrialEndsAtUtc = DateTime.UtcNow.AddMinutes(-1);
            await db.SaveChangesAsync();
        }

        Assert.Equal(HttpStatusCode.Forbidden,
            (await Send(owner, HttpMethod.Post, firstPath, new { name = "Expired" })).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await owner.GetAsync(firstPath)).StatusCode);
    }

    private static async Task RegisterAndLogin(HttpClient client, string email)
    {
        Assert.Equal(HttpStatusCode.OK,
            (await Send(client, HttpMethod.Post, "/api/auth/register",
                new { email, password = "SafeTestPassword123!" })).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent,
            (await Send(client, HttpMethod.Post, "/api/auth/login",
                new { email, password = "SafeTestPassword123!" })).StatusCode);
    }

    private static async Task<long> CreateBusiness(HttpClient client, string name, string requestKey)
    {
        var response = await Send(client, HttpMethod.Post, "/api/businesses", new
        {
            name, businessTypeId = 1, countryCode = "IN", currencyCode = "INR",
            timeZoneId = "Asia/Kolkata", requestKey
        });
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetInt64();
    }

    private static async Task<HttpResponseMessage> Send(HttpClient client, HttpMethod method,
        string path, object body)
    {
        var csrf = await client.GetFromJsonAsync<JsonElement>("/api/auth/csrf");
        using var request = new HttpRequestMessage(method, path)
        {
            Content = JsonContent.Create(body)
        };
        request.Headers.Add("X-CSRF-TOKEN", csrf.GetProperty("token").GetString());
        return await client.SendAsync(request);
    }
}
