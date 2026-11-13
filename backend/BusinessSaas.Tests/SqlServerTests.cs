using System.Data;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using BusinessSaas.Api.Data;
using BusinessSaas.Api.Features;
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
            Assert.Contains(await db.Database.GetAppliedMigrationsAsync(),
                migration => migration.EndsWith("_AddRetailContacts"));
            Assert.Contains(await db.Database.GetAppliedMigrationsAsync(),
                migration => migration.EndsWith("_AddRetailStockLedger"));
            Assert.Contains(await db.Database.GetAppliedMigrationsAsync(),
                migration => migration.EndsWith("_AddRetailStockAdjustments"));
            Assert.Contains(await db.Database.GetAppliedMigrationsAsync(),
                migration => migration.EndsWith("_AddRetailPurchaseReceiving"));
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

        long locationId;
        long productId;
        long actorId;
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            locationId = await db.BusinessLocations.Where(x => x.BusinessId == ids[0])
                .Select(x => x.Id).SingleAsync();
            actorId = await db.BusinessUsers.Where(x => x.BusinessId == ids[0])
                .Select(x => x.UserId).SingleAsync();

            var category = new RetailCategory
            {
                BusinessId = ids[0], Name = "Concurrency", NameKey = "CONCURRENCY"
            };
            var unit = new RetailUnit
            {
                BusinessId = ids[0], Name = "Pieces", NameKey = "PIECES", Code = "PCS"
            };
            db.RetailCategories.Add(category);
            db.RetailUnits.Add(unit);
            await db.SaveChangesAsync();

            var product = new RetailProduct
            {
                BusinessId = ids[0],
                CategoryId = category.Id,
                UnitId = unit.Id,
                Name = "Concurrent stock product",
                Sku = "CONCURRENT-1",
                TaxClassification = "NONE",
                SalePrice = 1m
            };
            db.RetailProducts.Add(product);
            await db.SaveChangesAsync();
            productId = product.Id;
            db.RetailStockBalances.Add(new RetailStockBalance
            {
                BusinessId = ids[0],
                LocationId = locationId,
                ProductId = productId,
                Quantity = 90m
            });
            await db.SaveChangesAsync();
        }

        async Task<bool> PostMovement(long sourceId, decimal quantity)
        {
            await using var scope = factory.Services.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            await using var transaction =
                await db.Database.BeginTransactionAsync(IsolationLevel.ReadCommitted);
            var balance = await StockPosting.ApplyAsync(db, ids[0], locationId,
                productId, quantity, "Sale", sourceId, actorId);
            if (balance is null)
                return false;
            await transaction.CommitAsync();
            return true;
        }

        // Two independent database connections try to sell 70 from an available 90.
        // The guarded SQL UPDATE must let exactly one through.
        var sales = await Task.WhenAll(PostMovement(1001, -70m), PostMovement(1002, -70m));
        Assert.Equal(1, sales.Count(x => x));
        Assert.False(await PostMovement(1001, -70m));
        Assert.False(await PostMovement(1003, -30m));
        Assert.True(await PostMovement(1004, -20m));
        Assert.False(await PostMovement(1005, -1m));

        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            Assert.Equal(0m, await db.RetailStockBalances
                .Where(x => x.BusinessId == ids[0] && x.LocationId == locationId &&
                    x.ProductId == productId)
                .Select(x => x.Quantity).SingleAsync());
            var movements = await db.RetailStockMovements.AsNoTracking()
                .Where(x => x.BusinessId == ids[0] && x.LocationId == locationId &&
                    x.ProductId == productId).ToListAsync();
            Assert.Equal(2, movements.Count);
            Assert.Equal(-90m, movements.Sum(x => x.QuantityChange));
            Assert.Equal(0m, movements.OrderByDescending(x => x.Id)
                .First().BalanceAfter);
        }

        var ordersPath = $"/api/businesses/{ids[0]}/purchase-orders";
        var supplier = await Post(owner, $"/api/businesses/{ids[0]}/suppliers",
            new { name = "SQL Purchase Supplier", phone = "9999999988", email = "" });
        Assert.Equal(HttpStatusCode.Created, supplier.StatusCode);
        var supplierId = (await supplier.Content.ReadFromJsonAsync<JsonElement>())
            .GetProperty("id").GetInt64();

        var orderResponse = await Post(owner, ordersPath, new
        {
            locationId, supplierId,
            lines = new[] { new { productId, quantity = 100m, unitCost = 1m } }
        });
        Assert.Equal(HttpStatusCode.Created, orderResponse.StatusCode);
        var orderId = (await orderResponse.Content.ReadFromJsonAsync<JsonElement>())
            .GetProperty("id").GetInt64();
        var orderPath = ordersPath + "/" + orderId;
        Assert.Equal(HttpStatusCode.OK,
            (await Post(owner, orderPath + "/approve", new { })).StatusCode);

        var orderData = await owner.GetFromJsonAsync<JsonElement>(orderPath);
        var purchaseLineId = orderData.GetProperty("lines")[0].GetProperty("id").GetInt64();

        async Task<long> DraftReceipt()
        {
            var response = await Post(owner, orderPath + "/receivings", new
            {
                lines = new[] { new { purchaseOrderLineId = purchaseLineId, quantity = 70m } }
            });
            Assert.Equal(HttpStatusCode.Created, response.StatusCode);
            return (await response.Content.ReadFromJsonAsync<JsonElement>())
                .GetProperty("id").GetInt64();
        }

        var receiptA = await DraftReceipt();
        var receiptB = await DraftReceipt();
        var competingReceipts = await Task.WhenAll(
            Post(owner, orderPath + "/receivings/" + receiptA + "/post", new { }),
            Post(owner, orderPath + "/receivings/" + receiptB + "/post", new { }));
        Assert.Equal(1, competingReceipts.Count(x => x.StatusCode == HttpStatusCode.OK));
        Assert.Equal(1, competingReceipts.Count(x => x.StatusCode == HttpStatusCode.Conflict));

        var finalOrder = await owner.GetFromJsonAsync<JsonElement>(orderPath);
        Assert.Equal(70m, finalOrder.GetProperty("lines")[0]
            .GetProperty("receivedQuantity").GetDecimal());
        Assert.Equal(30m, finalOrder.GetProperty("lines")[0]
            .GetProperty("remainingQuantity").GetDecimal());

        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            Assert.Equal(70m, await db.RetailStockBalances
                .Where(x => x.BusinessId == ids[0] && x.LocationId == locationId &&
                    x.ProductId == productId).Select(x => x.Quantity).SingleAsync());
            Assert.Equal(1, await db.RetailStockMovements.CountAsync(x =>
                x.BusinessId == ids[0] && x.ProductId == productId &&
                x.SourceType == "Receiving"));
        }
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
