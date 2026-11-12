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

public sealed class RetailStockTests
{
    [Fact]
    public async Task Opening_posts_once_and_preserves_tenant_location_and_ledger()
    {
        using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();

        using var factory = new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment("Development");
            builder.ConfigureServices(services =>
            {
                services.RemoveAll<DbContextOptions<AppDbContext>>();
                services.RemoveAll<IDbContextOptionsConfiguration<AppDbContext>>();
                services.AddDbContext<AppDbContext>(options => options.UseSqlite(connection));
            });
        });

        using var owner = factory.CreateClient();
        using var other = factory.CreateClient();
        using var manager = factory.CreateClient();
        using var cashier = factory.CreateClient();

        Assert.Equal(HttpStatusCode.Unauthorized,
            (await owner.GetAsync("/api/businesses/1/stock/locations/1")).StatusCode);

        await Register(owner, "stock-owner@example.test");
        await Register(other, "stock-other@example.test");
        await Register(manager, "stock-manager@example.test");
        await Register(cashier, "stock-cashier@example.test");

        var businessId = await CreateBusiness(owner, "Stock Shop", "stock-owner-20261112");
        var otherBusinessId = await CreateBusiness(other, "Other Stock Shop", "stock-other-20261112");

        var category = await CreateId(owner, $"/api/businesses/{businessId}/categories",
            new { name = "Beverages" });
        var unit = await CreateId(owner, $"/api/businesses/{businessId}/units",
            new { code = "PCS", name = "Pieces" });
        var product = await CreateId(owner, $"/api/businesses/{businessId}/products",
            new { name = "Water bottle", sku = "STOCK-100", categoryId = category, unitId = unit,
                salePrice = 20m, taxClassification = "", isActive = true });
        var foreignCategory = await CreateId(other, $"/api/businesses/{otherBusinessId}/categories",
            new { name = "Foreign Category" });
        var foreignUnit = await CreateId(other, $"/api/businesses/{otherBusinessId}/units",
            new { code = "EA", name = "Each" });
        var foreignProduct = await CreateId(other, $"/api/businesses/{otherBusinessId}/products",
            new { name = "Foreign product", sku = "OTHER-100", categoryId = foreignCategory,
                unitId = foreignUnit, salePrice = 1m, taxClassification = "" });

        long locationId;
        long secondLocationId;
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            locationId = await db.BusinessLocations.Where(x => x.BusinessId == businessId)
                .Select(x => x.Id).SingleAsync();

            var second = new BusinessLocation
            {
                BusinessId = businessId, Code = "BR-2", Name = "Second Branch", IsPrimary = false
            };
            db.BusinessLocations.Add(second);
            var managerId = await db.Users.Where(x => x.Email == "stock-manager@example.test")
                .Select(x => x.Id).SingleAsync();
            var cashierId = await db.Users.Where(x => x.Email == "stock-cashier@example.test")
                .Select(x => x.Id).SingleAsync();
            db.BusinessUsers.AddRange(
                new BusinessUser { BusinessId = businessId, UserId = managerId, RoleCode = "Manager" },
                new BusinessUser { BusinessId = businessId, UserId = cashierId, RoleCode = "Cashier" });
            await db.SaveChangesAsync();
            secondLocationId = second.Id;
        }

        var path = $"/api/businesses/{businessId}/stock/locations/{locationId}";
        var opening = path + "/opening";

        Assert.Equal(HttpStatusCode.NotFound, (await other.GetAsync(path)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await cashier.GetAsync(path)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound,
            (await manager.GetAsync($"/api/businesses/{businessId}/stock/locations/{secondLocationId}")).StatusCode);
        Assert.Equal(HttpStatusCode.OK,
            (await owner.GetAsync($"/api/businesses/{businessId}/stock/locations/{secondLocationId}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound,
            (await Send(other, HttpMethod.Post, opening, new { productId = product, quantity = 5m })).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound,
            (await Send(owner, HttpMethod.Post, opening, new { productId = foreignProduct, quantity = 5m })).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound,
            (await Send(cashier, HttpMethod.Post, opening, new { productId = product, quantity = 5m })).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest,
            (await Send(owner, HttpMethod.Post, opening, new { productId = product, quantity = -1m })).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest,
            (await Send(owner, HttpMethod.Post, opening, new { productId = product, quantity = 3.1234m })).StatusCode);

        var posted = await Send(manager, HttpMethod.Post, opening,
            new { productId = product, quantity = 12.125m });
        Assert.Equal(HttpStatusCode.Created, posted.StatusCode);
        var movement = await posted.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(12.125m, movement.GetProperty("balanceAfter").GetDecimal());

        Assert.Equal(HttpStatusCode.Conflict,
            (await Send(owner, HttpMethod.Post, opening, new { productId = product, quantity = 99m })).StatusCode);

        var stock = await owner.GetFromJsonAsync<JsonElement>(path + "?page=1&search=stock-100");
        Assert.Equal(1, stock.GetProperty("total").GetInt32());
        Assert.True(stock.GetProperty("items")[0].GetProperty("hasStockRecord").GetBoolean());
        Assert.Equal(12.125m, stock.GetProperty("items")[0].GetProperty("quantity").GetDecimal());

        var history = await owner.GetFromJsonAsync<JsonElement>(
            path + $"/movements?productId={product}&page=1");
        Assert.Equal(1, history.GetProperty("total").GetInt32());
        Assert.Equal("Opening", history.GetProperty("items")[0].GetProperty("sourceType").GetString());
        Assert.Equal(12.125m, history.GetProperty("items")[0].GetProperty("quantityChange").GetDecimal());

        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            Assert.Equal(1, await db.RetailStockBalances.CountAsync(x =>
                x.BusinessId == businessId && x.ProductId == product));
            Assert.Equal(1, await db.RetailStockMovements.CountAsync(x =>
                x.BusinessId == businessId && x.ProductId == product));
        }

        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var subscription = await db.BusinessSubscriptions.SingleAsync(x => x.BusinessId == businessId);
            subscription.TrialEndsAtUtc = DateTime.UtcNow.AddMinutes(-1);
            await db.SaveChangesAsync();
        }
        Assert.Equal(HttpStatusCode.Forbidden,
            (await Send(owner, HttpMethod.Post,
                $"/api/businesses/{businessId}/stock/locations/{secondLocationId}/opening",
                new { productId = product, quantity = 1m })).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await owner.GetAsync(path)).StatusCode);
    }

    private static async Task Register(HttpClient client, string email)
    {
        Assert.Equal(HttpStatusCode.OK,
            (await Send(client, HttpMethod.Post, "/api/auth/register",
                new { email, password = "TestPassword123!" })).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent,
            (await Send(client, HttpMethod.Post, "/api/auth/login",
                new { email, password = "TestPassword123!" })).StatusCode);
    }

    private static async Task<long> CreateBusiness(HttpClient client, string name, string key) =>
        await CreateId(client, "/api/businesses", new
        {
            name, businessTypeId = 1, countryCode = "IN", currencyCode = "INR",
            timeZoneId = "Asia/Kolkata", requestKey = key
        });

    private static async Task<long> CreateId(HttpClient client, string path, object body)
    {
        var response = await Send(client, HttpMethod.Post, path, body);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetInt64();
    }

    private static async Task<HttpResponseMessage> Send(HttpClient client,
        HttpMethod method, string path, object body)
    {
        var token = await client.GetFromJsonAsync<JsonElement>("/api/auth/csrf");
        using var request = new HttpRequestMessage(method, path)
        {
            Content = JsonContent.Create(body)
        };
        request.Headers.Add("X-CSRF-TOKEN", token.GetProperty("token").GetString()!);
        return await client.SendAsync(request);
    }
}
