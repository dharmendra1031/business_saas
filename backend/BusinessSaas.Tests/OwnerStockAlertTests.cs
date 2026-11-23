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

public sealed class OwnerStockAlertTests
{
    [Fact]
    public async Task Reorder_alerts_are_configured_location_specific_and_tenant_scoped()
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
        using var cashier = factory.CreateClient();
        await Login(owner, "alerts-owner@example.test");
        await Login(other, "alerts-other@example.test");
        await Login(cashier, "alerts-cashier@example.test");

        var id = await BusinessFixtures.SeedAsync(factory.Services, owner, "Alerts Store", "alerts-store-owner-20261123");
        var foreignId = await BusinessFixtures.SeedAsync(factory.Services, other, "Foreign Store", "alerts-store-other-20261123");
        long location;
        long secondLocation;
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            location = await db.BusinessLocations.Where(x => x.BusinessId == id)
                .Select(x => x.Id).SingleAsync();
            var second = new BusinessLocation
            {
                BusinessId = id, Name = "Second Branch", Code = "BR2", IsPrimary = false
            };
            db.BusinessLocations.Add(second);
            var cashierId = await db.Users.Where(x => x.Email == "alerts-cashier@example.test")
                .Select(x => x.Id).SingleAsync();
            db.BusinessUsers.Add(new BusinessUser { BusinessId = id, UserId = cashierId, RoleCode = "Cashier" });
            await db.SaveChangesAsync();
            secondLocation = second.Id;
        }

        var options = await owner.GetFromJsonAsync<JsonElement>($"/api/businesses/{id}/products/options");
        var category = options.GetProperty("categories")[0].GetProperty("id").GetInt64();
        var unit = options.GetProperty("units")[0].GetProperty("id").GetInt64();
        var basePath = $"/api/businesses/{id}/products";
        object Product(string sku, decimal? threshold, bool isActive = true) => new
        {
            name = "Sample item " + sku, sku, categoryId = category, unitId = unit,
            barcode = (string?)null, salePrice = 25m, taxClassification = "",
            reorderLevel = threshold, isActive
        };
        var initial = await owner.GetFromJsonAsync<JsonElement>($"/api/businesses/{id}/dashboard");
        Assert.Equal(JsonValueKind.Null, initial.GetProperty("stockAlerts").ValueKind);

        Assert.Equal(HttpStatusCode.BadRequest,
            (await Send(owner, HttpMethod.Post, basePath, Product("ALERT-INVALID", -1m))).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest,
            (await Send(owner, HttpMethod.Post, basePath, Product("ALERT-PREC", 1.1234m))).StatusCode);
        var monitoredProduct = await CreateId(owner, basePath, Product("ALERT-MAIN", 5m));
        var unopenedId = await CreateId(owner, basePath, Product("ALERT-UNOPENED", 0m));
        await CreateId(owner, basePath, Product("ALERT-NOTHRESHOLD", null));

        var opening = $"/api/businesses/{id}/stock/locations/";
        (await Send(owner, HttpMethod.Post, opening + location + "/opening",
            new { productId = monitoredProduct, quantity = 3m })).EnsureSuccessStatusCode();
        (await Send(owner, HttpMethod.Post, opening + secondLocation + "/opening",
            new { productId = monitoredProduct, quantity = 0m })).EnsureSuccessStatusCode();

        var otherOptions = await other.GetFromJsonAsync<JsonElement>($"/api/businesses/{foreignId}/products/options");
        var otherCategory = otherOptions.GetProperty("categories")[0].GetProperty("id").GetInt64();
        var otherUnit = otherOptions.GetProperty("units")[0].GetProperty("id").GetInt64();
        var foreignProduct = await CreateId(other, $"/api/businesses/{foreignId}/products", new
        {
            name = "Other stock", sku = "FOREIGN-ALERT", categoryId = otherCategory, unitId = otherUnit,
            salePrice = 10m, taxClassification = "", reorderLevel = 5m
        });
        long foreignLocation;
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            foreignLocation = await db.BusinessLocations.Where(x => x.BusinessId == foreignId)
                .Select(x => x.Id).SingleAsync();
        }
        (await Send(other, HttpMethod.Post,
            $"/api/businesses/{foreignId}/stock/locations/{foreignLocation}/opening",
            new { productId = foreignProduct, quantity = 0m })).EnsureSuccessStatusCode();

        var response = await owner.GetAsync($"/api/businesses/{id}/dashboard");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var result = (await response.Content.ReadFromJsonAsync<JsonElement>());
        var alerts = result.GetProperty("stockAlerts");
        Assert.Equal(2, alerts.GetProperty("count").GetInt32());
        Assert.Equal(1, alerts.GetProperty("outOfStockCount").GetInt32());
        var items = alerts.GetProperty("items").EnumerateArray().ToArray();
        Assert.Equal(2, items.Length);
        Assert.Equal(0m, items[0].GetProperty("quantity").GetDecimal());
        Assert.All(items, item => Assert.Equal(monitoredProduct, item.GetProperty("productId").GetInt64()));
        Assert.Contains(items, item => item.GetProperty("locationId").GetInt64() == secondLocation);
        Assert.DoesNotContain(items, item => item.GetProperty("locationId").GetInt64() == foreignLocation);

        // The Inventory filter has the same server-defined rules as the Owner dashboard.
        var primaryStockPath = $"/api/businesses/{id}/stock/locations/{location}";
        var secondStockPath = $"/api/businesses/{id}/stock/locations/{secondLocation}";
        var filteredPrimary = await owner.GetFromJsonAsync<JsonElement>(
            primaryStockPath + "?alertsOnly=true&page=1");
        Assert.Equal(1, filteredPrimary.GetProperty("total").GetInt32());
        Assert.Equal(monitoredProduct,
            filteredPrimary.GetProperty("items")[0].GetProperty("id").GetInt64());
        Assert.Equal(3m,
            filteredPrimary.GetProperty("items")[0].GetProperty("quantity").GetDecimal());
        Assert.Equal(5m,
            filteredPrimary.GetProperty("items")[0].GetProperty("reorderLevel").GetDecimal());

        var filteredSecond = await owner.GetFromJsonAsync<JsonElement>(
            secondStockPath + "?alertsOnly=true&page=1");
        Assert.Equal(1, filteredSecond.GetProperty("total").GetInt32());
        Assert.Equal(0m,
            filteredSecond.GetProperty("items")[0].GetProperty("quantity").GetDecimal());
        var unfiltered = await owner.GetFromJsonAsync<JsonElement>(primaryStockPath + "?page=1");
        Assert.Equal(3, unfiltered.GetProperty("total").GetInt32());
        Assert.Equal(HttpStatusCode.NotFound,
            (await other.GetAsync(primaryStockPath + "?alertsOnly=true")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound,
            (await cashier.GetAsync(primaryStockPath + "?alertsOnly=true")).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest,
            (await owner.GetAsync(primaryStockPath + "?alertsOnly=true&page=0")).StatusCode);

        var cashierData = await cashier.GetFromJsonAsync<JsonElement>($"/api/businesses/{id}/dashboard");
        Assert.Equal(JsonValueKind.Null, cashierData.GetProperty("stockAlerts").ValueKind);
        Assert.Equal(HttpStatusCode.NotFound,
            (await other.GetAsync($"/api/businesses/{id}/dashboard")).StatusCode);

        Assert.Equal(HttpStatusCode.BadRequest, (await Send(owner, HttpMethod.Put,
            basePath + "/" + monitoredProduct, Product("ALERT-MAIN", -0.5m))).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await Send(owner, HttpMethod.Put,
            basePath + "/" + monitoredProduct, Product("ALERT-MAIN", 5m, isActive: false))).StatusCode);
        result = await owner.GetFromJsonAsync<JsonElement>($"/api/businesses/{id}/dashboard");
        Assert.Equal(0, result.GetProperty("stockAlerts").GetProperty("count").GetInt32());
        filteredPrimary = await owner.GetFromJsonAsync<JsonElement>(primaryStockPath + "?alertsOnly=true");
        Assert.Equal(0, filteredPrimary.GetProperty("total").GetInt32());
        Assert.Equal(HttpStatusCode.OK, (await Send(owner, HttpMethod.Put,
            basePath + "/" + monitoredProduct, Product("ALERT-MAIN", 5m))).StatusCode);
        result = await owner.GetFromJsonAsync<JsonElement>($"/api/businesses/{id}/dashboard");
        Assert.Equal(2, result.GetProperty("stockAlerts").GetProperty("count").GetInt32());

        var updated = await Send(owner, HttpMethod.Put,
            basePath + "/" + monitoredProduct, Product("ALERT-MAIN", null));
        Assert.Equal(HttpStatusCode.OK, updated.StatusCode);
        result = await owner.GetFromJsonAsync<JsonElement>($"/api/businesses/{id}/dashboard");
        Assert.Equal(0, result.GetProperty("stockAlerts").GetProperty("count").GetInt32());

        var stored = await owner.GetFromJsonAsync<JsonElement>(basePath + "/" + monitoredProduct);
        Assert.Equal(JsonValueKind.Null, stored.GetProperty("reorderLevel").ValueKind);
        Assert.Equal(HttpStatusCode.OK, (await Send(owner, HttpMethod.Put,
            basePath + "/" + unopenedId, Product("ALERT-UNOPENED", null))).StatusCode);
        result = await owner.GetFromJsonAsync<JsonElement>($"/api/businesses/{id}/dashboard");
        Assert.Equal(JsonValueKind.Null, result.GetProperty("stockAlerts").ValueKind);
    }

    private static async Task Login(HttpClient client, string email)
    {
        Assert.Equal(HttpStatusCode.OK, (await Send(client, HttpMethod.Post, "/api/auth/register",
            new { email, password = "TestPassword123!" })).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await Send(client, HttpMethod.Post, "/api/auth/login",
            new { email, password = "TestPassword123!" })).StatusCode);
    }

    private static async Task<long> CreateId(HttpClient client, string path, object data)
    {
        var result = await Send(client, HttpMethod.Post, path, data);
        Assert.Equal(HttpStatusCode.Created, result.StatusCode);
        return (await result.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetInt64();
    }

    private static async Task<HttpResponseMessage> Send(HttpClient client, HttpMethod method,
        string path, object data)
    {
        var csrf = await client.GetFromJsonAsync<JsonElement>("/api/auth/csrf");
        using var request = new HttpRequestMessage(method, path) { Content = JsonContent.Create(data) };
        request.Headers.Add("X-CSRF-TOKEN", csrf.GetProperty("token").GetString()!);
        return await client.SendAsync(request);
    }
}
