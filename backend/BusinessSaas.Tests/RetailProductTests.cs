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

public sealed class RetailProductTests
{
    [Fact]
    public async Task Products_validate_codes_price_masters_and_tenant_access()
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
        using var manager = factory.CreateClient();
        using var cashier = factory.CreateClient();
        using var other = factory.CreateClient();

        Assert.Equal(HttpStatusCode.Unauthorized,
            (await owner.GetAsync("/api/businesses/1/products")).StatusCode);

        await Register(owner, "product-owner@example.test");
        await Register(manager, "product-manager@example.test");
        await Register(cashier, "product-cashier@example.test");
        await Register(other, "product-other@example.test");

        var businessId = await CreateBusiness(owner, "Product Retail A", "product-retail-a-20261112");
        var otherBusinessId = await CreateBusiness(other, "Product Retail B", "product-retail-b-20261112");

        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var managerId = await db.Users.Where(x => x.Email == "product-manager@example.test")
                .Select(x => x.Id).SingleAsync();
            var cashierId = await db.Users.Where(x => x.Email == "product-cashier@example.test")
                .Select(x => x.Id).SingleAsync();
            db.BusinessUsers.AddRange(
                new BusinessUser { BusinessId = businessId, UserId = managerId, RoleCode = "Manager" },
                new BusinessUser { BusinessId = businessId, UserId = cashierId, RoleCode = "Cashier" });
            await db.SaveChangesAsync();
        }

        var categoryId = await CreateId(owner, $"/api/businesses/{businessId}/categories",
            new { name = "Stationery" });
        var unitId = await CreateId(owner, $"/api/businesses/{businessId}/units",
            new { code = "PCS", name = "Pieces" });
        var foreignCategory = await CreateId(other,
            $"/api/businesses/{otherBusinessId}/categories", new { name = "Other Category" });
        var foreignUnit = await CreateId(other,
            $"/api/businesses/{otherBusinessId}/units", new { code = "EA", name = "Each" });
        var path = $"/api/businesses/{businessId}/products";

        object Product(string sku = "PEN-100", string? barcode = "890000010001",
            long? category = null, long? unit = null, decimal price = 12.50m,
            bool? isActive = null) => new
        {
            name = "Blue pen", sku, barcode,
            categoryId = category ?? categoryId, unitId = unit ?? unitId,
            salePrice = price, taxClassification = "EXEMPT", isActive
        };

        Assert.Equal(HttpStatusCode.BadRequest,
            (await Post(owner, path, Product(price: 12.345m))).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest,
            (await Post(owner, path, Product(price: -1m))).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest,
            (await Post(owner, path, Product(sku: "bad sku"))).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest,
            (await Post(owner, path, Product(category: foreignCategory))).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest,
            (await Post(owner, path, Product(unit: foreignUnit))).StatusCode);

        var first = await Post(owner, path, Product());
        Assert.Equal(HttpStatusCode.Created, first.StatusCode);
        var productId = (await first.Content.ReadFromJsonAsync<JsonElement>())
            .GetProperty("id").GetInt64();
        Assert.True(productId > 0);

        Assert.Equal(HttpStatusCode.Conflict,
            (await Post(owner, path, Product(sku: "pen-100", barcode: "890000010002"))).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict,
            (await Post(owner, path, Product(sku: "PEN-200", barcode: "890000010001"))).StatusCode);

        var lookupResponse = await owner.GetAsync(path + "/lookup?barcode=890000010001");
        Assert.True(lookupResponse.IsSuccessStatusCode, await lookupResponse.Content.ReadAsStringAsync());
        var found = await lookupResponse.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(productId, found.GetProperty("id").GetInt64());
        Assert.Equal("EXEMPT", found.GetProperty("taxClassification").GetString());
        Assert.Equal(12.50m, found.GetProperty("salePrice").GetDecimal());

        var filtered = await owner.GetFromJsonAsync<JsonElement>(
            path + "?page=1&search=pen-100&status=active");
        Assert.Equal(1, filtered.GetProperty("total").GetInt32());
        Assert.Equal(productId, filtered.GetProperty("items")[0].GetProperty("id").GetInt64());
        Assert.Equal(HttpStatusCode.BadRequest, (await owner.GetAsync(path + "?page=0")).StatusCode);

        Assert.Equal(HttpStatusCode.OK, (await manager.GetAsync(path + "/options")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await cashier.GetAsync(path)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await other.GetAsync(path)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await other.GetAsync(path + "/" + productId)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await Put(other, path + "/" + productId, Product())).StatusCode);

        var changed = await Put(manager, path + "/" + productId,
            Product(sku: "PEN-101", barcode: "890000010001", price: 14.25m, isActive: false));
        Assert.Equal(HttpStatusCode.OK, changed.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound,
            (await owner.GetAsync(path + "/lookup?barcode=890000010001")).StatusCode);

        var inactive = await owner.GetFromJsonAsync<JsonElement>(
            path + "?page=1&status=inactive");
        Assert.Equal(1, inactive.GetProperty("total").GetInt32());
        Assert.False(inactive.GetProperty("items")[0].GetProperty("isActive").GetBoolean());

        Assert.Equal(HttpStatusCode.Created,
            (await Post(other, $"/api/businesses/{otherBusinessId}/products",
                Product(category: foreignCategory, unit: foreignUnit))).StatusCode);

        var second = await Post(owner, path, Product(sku: "PEN-202", barcode: null));
        Assert.Equal(HttpStatusCode.Created, second.StatusCode);
        var third = await Post(owner, path, Product(sku: "PEN-203", barcode: null));
        Assert.Equal(HttpStatusCode.Created, third.StatusCode);

        // Deactivated masters cannot be selected for active new products.
        Assert.Equal(HttpStatusCode.OK,
            (await Put(owner, $"/api/businesses/{businessId}/categories/{categoryId}",
                new { name = "Stationery", isActive = false })).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest,
            (await Post(owner, path, Product(sku: "PEN-300", barcode: null))).StatusCode);

        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var subscription = await db.BusinessSubscriptions.SingleAsync(x => x.BusinessId == businessId);
            subscription.TrialEndsAtUtc = DateTime.UtcNow.AddMinutes(-1);
            await db.SaveChangesAsync();
        }

        Assert.Equal(HttpStatusCode.Forbidden,
            (await Post(owner, path, Product(sku: "PEN-400", barcode: null))).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden,
            (await Put(manager, path + "/" + productId,
                Product(sku: "PEN-101", isActive: true))).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await owner.GetAsync(path)).StatusCode);
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

    private static async Task<long> CreateBusiness(HttpClient client, string name, string key) =>
        await CreateId(client, "/api/businesses", new
        {
            name, businessTypeId = 1, countryCode = "IN", currencyCode = "INR",
            timeZoneId = "Asia/Kolkata", requestKey = key
        });

    private static async Task<long> CreateId(HttpClient client, string url, object request)
    {
        var response = await Post(client, url, request);
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
