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

public sealed class RetailPurchaseTests
{
    [Fact]
    public async Task Partial_receiving_posts_once_and_rejects_over_receiving_and_other_business()
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
        await Register(owner, "purchase-owner@example.test");
        await Register(other, "purchase-other@example.test");
        await Register(manager, "purchase-manager@example.test");
        await Register(cashier, "purchase-cashier@example.test");

        var businessId = await CreateId(owner, "/api/businesses", new
        {
            name = "Purchase Shop", businessTypeId = 1, countryCode = "IN",
            currencyCode = "INR", timeZoneId = "Asia/Kolkata",
            requestKey = "purchase-shop-20261113"
        });
        var otherBusinessId = await CreateId(other, "/api/businesses", new
        {
            name = "Another Purchase Shop", businessTypeId = 1, countryCode = "IN",
            currencyCode = "INR", timeZoneId = "Asia/Kolkata",
            requestKey = "purchase-other-20261113"
        });

        long locationId;
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            locationId = await db.BusinessLocations.Where(x => x.BusinessId == businessId)
                .Select(x => x.Id).SingleAsync();
            var managerId = await db.Users.Where(x => x.Email == "purchase-manager@example.test")
                .Select(x => x.Id).SingleAsync();
            var cashierId = await db.Users.Where(x => x.Email == "purchase-cashier@example.test")
                .Select(x => x.Id).SingleAsync();
            db.BusinessUsers.AddRange(
                new BusinessUser { BusinessId = businessId, UserId = managerId, RoleCode = "Manager" },
                new BusinessUser { BusinessId = businessId, UserId = cashierId, RoleCode = "Cashier" });
            await db.SaveChangesAsync();
        }

        var categoryId = await CreateId(owner, $"/api/businesses/{businessId}/categories",
            new { name = "Food" });
        var unitId = await CreateId(owner, $"/api/businesses/{businessId}/units",
            new { code = "PCS", name = "Pieces" });
        var productId = await CreateId(owner, $"/api/businesses/{businessId}/products", new
        {
            name = "Rice bag", sku = "RICE-01", categoryId, unitId,
            salePrice = 150m, taxClassification = ""
        });
        var supplierId = await CreateId(owner, $"/api/businesses/{businessId}/suppliers",
            new { name = "Wholesaler", phone = "9999999999", email = "" });
        var otherSupplierId = await CreateId(other,
            $"/api/businesses/{otherBusinessId}/suppliers",
            new { name = "Other Supplier", phone = "9999999998", email = "" });

        var orders = $"/api/businesses/{businessId}/purchase-orders";
        var valid = new
        {
            locationId, supplierId,
            lines = new[] { new { productId, quantity = 100m, unitCost = 80m } }
        };

        Assert.Equal(HttpStatusCode.NotFound,
            (await Send(other, orders, valid)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound,
            (await Send(cashier, orders, valid)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound,
            (await Send(owner, orders, new
            {
                locationId, supplierId = otherSupplierId,
                lines = new[] { new { productId, quantity = 100m, unitCost = 80m } }
            })).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest,
            (await Send(owner, orders, new
            {
                locationId, supplierId,
                lines = new[] { new { productId, quantity = -1m, unitCost = 80m } }
            })).StatusCode);

        var orderId = await CreateId(manager, orders, valid);
        var orderPath = orders + "/" + orderId;

        Assert.Equal(HttpStatusCode.NotFound, (await other.GetAsync(orderPath)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound,
            (await Send(manager, orderPath + "/approve", new { })).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict,
            (await Send(manager, orderPath + "/receivings",
                new { lines = new[] { new { purchaseOrderLineId = 1, quantity = 40m } } })).StatusCode);

        var before = await manager.GetFromJsonAsync<JsonElement>(orderPath);
        var orderLineId = before.GetProperty("lines")[0].GetProperty("id").GetInt64();
        Assert.Equal("Draft", before.GetProperty("status").GetString());

        Assert.Equal(HttpStatusCode.OK,
            (await Send(owner, orderPath + "/approve", new { })).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict,
            (await Send(owner, orderPath + "/approve", new { })).StatusCode);

        var receipts = orderPath + "/receivings";
        var receipt40 = await CreateId(manager, receipts,
            new { lines = new[] { new { purchaseOrderLineId = orderLineId, quantity = 40m } } });

        var beforePost = await owner.GetFromJsonAsync<JsonElement>(orderPath);
        Assert.Equal(0m, beforePost.GetProperty("lines")[0]
            .GetProperty("receivedQuantity").GetDecimal());

        Assert.Equal(HttpStatusCode.OK,
            (await Send(manager, receipts + "/" + receipt40 + "/post", new { })).StatusCode);
        Assert.Equal(HttpStatusCode.OK,
            (await Send(owner, receipts + "/" + receipt40 + "/post", new { })).StatusCode);

        var partial = await owner.GetFromJsonAsync<JsonElement>(orderPath);
        Assert.Equal("Approved", partial.GetProperty("status").GetString());
        Assert.Equal(40m, partial.GetProperty("lines")[0].GetProperty("receivedQuantity").GetDecimal());
        Assert.Equal(60m, partial.GetProperty("lines")[0].GetProperty("remainingQuantity").GetDecimal());

        Assert.Equal(HttpStatusCode.Conflict,
            (await Send(owner, receipts,
                new { lines = new[] { new { purchaseOrderLineId = orderLineId, quantity = 70m } } })).StatusCode);

        var receipt60 = await CreateId(owner, receipts,
            new { lines = new[] { new { purchaseOrderLineId = orderLineId, quantity = 60m } } });
        Assert.Equal(HttpStatusCode.OK,
            (await Send(owner, receipts + "/" + receipt60 + "/post", new { })).StatusCode);
        Assert.Equal(HttpStatusCode.OK,
            (await Send(owner, receipts + "/" + receipt60 + "/post", new { })).StatusCode);

        var completed = await owner.GetFromJsonAsync<JsonElement>(orderPath);
        Assert.Equal("Closed", completed.GetProperty("status").GetString());
        Assert.Equal(100m, completed.GetProperty("lines")[0].GetProperty("receivedQuantity").GetDecimal());
        Assert.Equal(0m, completed.GetProperty("lines")[0].GetProperty("remainingQuantity").GetDecimal());

        Assert.Equal(HttpStatusCode.Conflict,
            (await Send(owner, receipts,
                new { lines = new[] { new { purchaseOrderLineId = orderLineId, quantity = 1m } } })).StatusCode);

        var stockPage = await owner.GetFromJsonAsync<JsonElement>(
            $"/api/businesses/{businessId}/stock/locations/{locationId}");
        Assert.Equal(100m, stockPage.GetProperty("items")[0].GetProperty("quantity").GetDecimal());

        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            Assert.Equal(2, await db.RetailStockMovements.CountAsync(x =>
                x.BusinessId == businessId && x.ProductId == productId &&
                x.SourceType == "Receiving"));
            Assert.Equal(2, await db.RetailReceivings.CountAsync(x =>
                x.BusinessId == businessId && x.Status == "Posted"));
            Assert.Equal(100m, await db.RetailStockBalances
                .Where(x => x.BusinessId == businessId && x.ProductId == productId)
                .Select(x => x.Quantity).SingleAsync());
        }

        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var subscription = await db.BusinessSubscriptions.SingleAsync(x => x.BusinessId == businessId);
            subscription.TrialEndsAtUtc = DateTime.UtcNow.AddMinutes(-2);
            await db.SaveChangesAsync();
        }
        Assert.Equal(HttpStatusCode.Forbidden,
            (await Send(owner, orders, valid)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await owner.GetAsync(orders)).StatusCode);
    }

    private static async Task Register(HttpClient client, string email)
    {
        Assert.Equal(HttpStatusCode.OK,
            (await Send(client, "/api/auth/register",
                new { email, password = "TestPassword123!" })).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent,
            (await Send(client, "/api/auth/login",
                new { email, password = "TestPassword123!" })).StatusCode);
    }

    private static async Task<long> CreateId(HttpClient client, string path, object body)
    {
        var response = await Send(client, path, body);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var result = await response.Content.ReadFromJsonAsync<JsonElement>();
        return result.GetProperty("id").GetInt64();
    }

    private static async Task<HttpResponseMessage> Send(HttpClient client,
        string path, object body)
    {
        var token = await client.GetFromJsonAsync<JsonElement>("/api/auth/csrf");
        using var request = new HttpRequestMessage(HttpMethod.Post, path)
        {
            Content = JsonContent.Create(body)
        };
        request.Headers.Add("X-CSRF-TOKEN", token.GetProperty("token").GetString()!);
        return await client.SendAsync(request);
    }
}
