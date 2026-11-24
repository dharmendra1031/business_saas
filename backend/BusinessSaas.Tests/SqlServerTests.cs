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
        Assert.Equal(HttpStatusCode.OK, (await owner.GetAsync("/api/ready")).StatusCode);

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
            Assert.Contains(await db.Database.GetAppliedMigrationsAsync(),
                migration => migration.EndsWith("_AddStockAdjustmentReason"));
            Assert.Contains(await db.Database.GetAppliedMigrationsAsync(),
                migration => migration.EndsWith("_AddPurchaseDraftRequestKeys"));
            Assert.Contains(await db.Database.GetAppliedMigrationsAsync(),
                migration => migration.EndsWith("_AddPlatformAuditEvents"));
            Assert.Contains(await db.Database.GetAppliedMigrationsAsync(),
                migration => migration.EndsWith("_AddPlatformAuditChangeValues"));
            Assert.Contains(await db.Database.GetAppliedMigrationsAsync(),
                migration => migration.EndsWith("_AddPlanDraftsAndSubscriptionSuspension"));
            Assert.Contains(await db.Database.GetAppliedMigrationsAsync(),
                migration => migration.EndsWith("_AddPublishedPlanVersions"));
            Assert.Contains(await db.Database.GetAppliedMigrationsAsync(),
                migration => migration.EndsWith("_AddSubscriptionBillingDrafts"));
            Assert.Contains(await db.Database.GetAppliedMigrationsAsync(),
                migration => migration.EndsWith("_AddBillingDraftPeriods"));
            Assert.Contains(await db.Database.GetAppliedMigrationsAsync(),
                migration => migration.EndsWith("_AddSubscriptionPaymentRequests"));
            Assert.Contains(await db.Database.GetAppliedMigrationsAsync(),
                migration => migration.EndsWith("_AddRazorpayPaymentsAndPaidValidity"));
            Assert.Contains(await db.Database.GetAppliedMigrationsAsync(),
                migration => migration.EndsWith("_AddVerifiedSubscriptionRefunds"));
            Assert.Contains(await db.Database.GetAppliedMigrationsAsync(),
                name => name.EndsWith("_AddBillingTaxDocumentDrafts"));
            Assert.Contains("20261118210000_RetailCashBasket", await db.Database.GetAppliedMigrationsAsync());
            Assert.True(await db.Database.CanConnectAsync());
            Assert.True(await db.BusinessTypes.AnyAsync(x => x.Code == "RETAIL" && x.IsSupported));
        }

        await RegisterAndLogin(owner, "sql-owner@example.test");
        await RegisterAndLogin(stranger, "sql-stranger@example.test");

        using var admin = factory.CreateClient();
        await BusinessFlowTests.SignInProvisioningAdmin(factory.Services, admin,
            "sql-platform-admin@example.test", "sql-owner@example.test");

        var creation = new
        {
            name = "SQL Retail Shop",
            businessTypeId = 1,
            countryCode = "IN",
            currencyCode = "INR",
            timeZoneId = "Asia/Kolkata",
            ownerEmail = "sql-owner@example.test",
            locationName = "Main Shop",
            planCode = "STARTER",
            requestKey = "same-request-20261109"
        };

        var results = await Task.WhenAll(
            Post(admin, "/api/platform/businesses", creation),
            Post(admin, "/api/platform/businesses", creation));

        foreach (var result in results)
            Assert.True(result.StatusCode is HttpStatusCode.Created or HttpStatusCode.OK,
                await result.Content.ReadAsStringAsync());

        var ids = await Task.WhenAll(results.Select(async response =>
            (await response.Content.ReadFromJsonAsync<JsonElement>())
                .GetProperty("id").GetInt64()));

        Assert.True(ids[0] > 0);
        Assert.Equal(ids[0], ids[1]);
        var directory = await admin.GetFromJsonAsync<JsonElement>(
            "/api/platform/businesses?search=SQL&ownerEmail=SQL-OWNER&businessTypeId=1&planCode=STARTER&status=Trial&pageSize=1");
        Assert.Equal(1, directory.GetProperty("total").GetInt32());
        Assert.Equal(ids[0], directory.GetProperty("items")[0].GetProperty("id").GetInt64());

        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            Assert.Equal(1, await db.Businesses.CountAsync(x => x.Id == ids[0]));
            Assert.Equal(1, await db.BusinessUsers.CountAsync(x => x.BusinessId == ids[0]));
            Assert.Equal(1, await db.BusinessLocations.CountAsync(x => x.BusinessId == ids[0]));
            Assert.Equal(1, await db.BusinessSubscriptions.CountAsync(x => x.BusinessId == ids[0]));
            Assert.Equal(1, await db.PlatformAuditEvents.CountAsync(x =>
                x.TargetId == ids[0] && x.Action == "Business.Provisioned"));
        }

        Assert.Equal(HttpStatusCode.OK,
            (await owner.GetAsync($"/api/businesses/{ids[0]}/dashboard")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound,
            (await stranger.GetAsync($"/api/businesses/{ids[0]}/dashboard")).StatusCode);

        var changedPayload = await Post(admin, "/api/platform/businesses", new
        {
            name = "Not The Same Business",
            businessTypeId = 1,
            countryCode = "IN",
            currencyCode = "INR",
            timeZoneId = "Asia/Kolkata",
            ownerEmail = "sql-owner@example.test",
            locationName = "Main Shop",
            planCode = "STARTER",
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

        var orderRequest = new
        {
            locationId, supplierId,
            requestKey = "sql-purchase-order-20261117",
            lines = new[] { new { productId, quantity = 100m, unitCost = 1m } }
        };
        var orderResponses = await Task.WhenAll(
            Post(owner, ordersPath, orderRequest),
            Post(owner, ordersPath, orderRequest));
        var orderIds = new List<long>();
        foreach (var response in orderResponses)
        {
            Assert.True(response.StatusCode is HttpStatusCode.Created or HttpStatusCode.OK,
                await response.Content.ReadAsStringAsync());
            orderIds.Add((await response.Content.ReadFromJsonAsync<JsonElement>())
                .GetProperty("id").GetInt64());
        }
        Assert.Equal(orderIds[0], orderIds[1]);
        var orderId = orderIds[0];
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            Assert.Equal(1, await db.RetailPurchaseOrders.CountAsync(x =>
                x.BusinessId == ids[0] && x.Id == orderId));
        }
        var orderPath = ordersPath + "/" + orderId;
        Assert.Equal(HttpStatusCode.OK,
            (await Post(owner, orderPath + "/approve", new { })).StatusCode);

        var orderData = await owner.GetFromJsonAsync<JsonElement>(orderPath);
        var purchaseLineId = orderData.GetProperty("lines")[0].GetProperty("id").GetInt64();

        async Task<long> DraftReceipt(string requestKey)
        {
            var response = await Post(owner, orderPath + "/receivings", new
            {
                requestKey,
                lines = new[] { new { purchaseOrderLineId = purchaseLineId, quantity = 70m } }
            });
            Assert.Equal(HttpStatusCode.Created, response.StatusCode);
            return (await response.Content.ReadFromJsonAsync<JsonElement>())
                .GetProperty("id").GetInt64();
        }

        var receiptA = await DraftReceipt("sql-receipt-first-20261117");
        var receiptB = await DraftReceipt("sql-receipt-second-20261117");
        var csrf = await owner.GetFromJsonAsync<JsonElement>("/api/auth/csrf");
        var csrfToken = csrf.GetProperty("token").GetString()!;
        async Task<HttpResponseMessage> PostReceipt(long receiptId)
        {
            using var request = new HttpRequestMessage(HttpMethod.Post,
                orderPath + "/receivings/" + receiptId + "/post")
            {
                Content = JsonContent.Create(new { })
            };
            request.Headers.Add("X-CSRF-TOKEN", csrfToken);
            return await owner.SendAsync(request);
        }

        var competingReceipts = await Task.WhenAll(
            PostReceipt(receiptA), PostReceipt(receiptB));
        Assert.Equal(1, competingReceipts.Count(x => x.StatusCode == HttpStatusCode.OK));
        var rejected = competingReceipts.Single(x => x.StatusCode != HttpStatusCode.OK);
        Assert.True(rejected.StatusCode == HttpStatusCode.Conflict,
            $"Expected 409; got {(int)rejected.StatusCode}: {await rejected.Content.ReadAsStringAsync()}");

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


    [Fact]
    [Trait("Database", "SqlServer")]
    public async Task Classified_product_flows_from_purchase_to_stock_and_cash_sale()
    {
        var connectionString = Environment.GetEnvironmentVariable("SQLSERVER_TEST_CONNECTION");
        if (string.IsNullOrWhiteSpace(connectionString)) return;

        using var factory = new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment("Development"); // Cash posting is not released outside Development yet.
            builder.ConfigureServices(services =>
            {
                services.RemoveAll<DbContextOptions<AppDbContext>>();
                services.RemoveAll<IDbContextOptionsConfiguration<AppDbContext>>();
                services.AddDbContext<AppDbContext>(options => options.UseSqlServer(connectionString));
            });
        });
        using var owner = factory.CreateClient();
        using var cashier = factory.CreateClient();
        using var stranger = factory.CreateClient();
        await RegisterAndLogin(owner, "sql-retail-flow-owner@example.test");
        await RegisterAndLogin(cashier, "sql-retail-flow-cashier@example.test");
        await RegisterAndLogin(stranger, "sql-retail-flow-stranger@example.test");

        var businessId = await BusinessFixtures.SeedAsync(factory.Services, owner,
            "SQL Retail Flow", "sql-retail-flow-20261124");
        long locationId;
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            locationId = await db.BusinessLocations.Where(x => x.BusinessId == businessId)
                .Select(x => x.Id).SingleAsync();
            var cashierId = await db.Users.Where(x => x.Email == "sql-retail-flow-cashier@example.test")
                .Select(x => x.Id).SingleAsync();
            db.BusinessUsers.Add(new BusinessUser
            {
                BusinessId = businessId, UserId = cashierId, RoleCode = "Cashier"
            });
            await db.SaveChangesAsync();
        }

        async Task<long> Created(string path, object body)
        {
            using var response = await Post(owner, path, body);
            Assert.Equal(HttpStatusCode.Created, response.StatusCode);
            return (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetInt64();
        }

        var prefix = $"/api/businesses/{businessId}";
        var groupId = await Created(prefix + "/categories", new { name = "SQL Garments" });
        var subgroupId = await Created(prefix + "/categories",
            new { name = "SQL Menswear", parentId = groupId });
        var categoryId = await Created(prefix + "/categories",
            new { name = "SQL Shirts", parentId = subgroupId });
        var brandId = await Created(prefix + "/brands", new { name = "SQL Test Brand" });
        var unitId = await Created(prefix + "/units",
            new { name = "Test Pieces", code = "SQPCS" });
        var productId = await Created(prefix + "/products", new
        {
            name = "SQL Blue Shirt", sku = "SQL-BLUE-SHIRT", categoryId, brandId, unitId,
            salePrice = 80m, taxClassification = ""
        });

        var byGroup = await owner.GetFromJsonAsync<JsonElement>(
            prefix + "/products?categoryId=" + groupId);
        Assert.Contains(byGroup.GetProperty("items").EnumerateArray(),
            x => x.GetProperty("id").GetInt64() == productId);
        var product = await owner.GetFromJsonAsync<JsonElement>(prefix + "/products/" + productId);
        Assert.Equal(brandId, product.GetProperty("brandId").GetInt64());
        Assert.Equal(categoryId, product.GetProperty("categoryId").GetInt64());

        var supplierId = await Created(prefix + "/suppliers",
            new { name = "SQL Retail Supplier", phone = "9999999981", email = "" });
        var orderId = await Created(prefix + "/purchase-orders", new
        {
            locationId, supplierId, requestKey = "sql-flow-purchase-20261124",
            lines = new[] { new { productId, quantity = 1.250m, unitCost = 40m } }
        });
        var orderPath = prefix + "/purchase-orders/" + orderId;
        Assert.Equal(HttpStatusCode.OK,
            (await Post(owner, orderPath + "/approve", new { })).StatusCode);
        var order = await owner.GetFromJsonAsync<JsonElement>(orderPath);
        var orderLineId = order.GetProperty("lines")[0].GetProperty("id").GetInt64();
        var receivingId = await Created(orderPath + "/receivings", new
        {
            requestKey = "sql-flow-receiving-20261124",
            lines = new[] { new { purchaseOrderLineId = orderLineId, quantity = 1.250m } }
        });
        var postPath = orderPath + "/receivings/" + receivingId + "/post";
        Assert.Equal(HttpStatusCode.OK, (await Post(owner, postPath, new { })).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await Post(owner, postPath, new { })).StatusCode);

        var stockPath = prefix + "/stock/locations/" + locationId;
        var stock = await owner.GetFromJsonAsync<JsonElement>(stockPath + "?search=SQL-BLUE-SHIRT");
        Assert.Equal(1.250m, stock.GetProperty("items")[0].GetProperty("quantity").GetDecimal());
        Assert.Equal("SQPCS", stock.GetProperty("items")[0].GetProperty("unitCode").GetString());
        Assert.Equal(HttpStatusCode.NotFound, (await stranger.GetAsync(stockPath)).StatusCode);

        var cashierCatalog = await cashier.GetFromJsonAsync<JsonElement>(
            prefix + "/cash-sales/products?locationId=" + locationId + "&search=SQL-BLUE-SHIRT");
        Assert.Equal(productId, cashierCatalog[0].GetProperty("id").GetInt64());
        var saleRequest = new
        {
            locationId, requestKey = "sql-flow-cash-sale-20261124", tendered = 35m,
            lines = new[] { new { productId, quantity = 0.375m, expectedUnitPrice = 80m } }
        };
        var saleResponse = await Post(cashier, prefix + "/cash-sales", saleRequest);
        Assert.Equal(HttpStatusCode.OK, saleResponse.StatusCode);
        var sale = await saleResponse.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(30m, sale.GetProperty("total").GetDecimal());
        var saleRetry = await Post(cashier, prefix + "/cash-sales", saleRequest);
        Assert.Equal(HttpStatusCode.OK, saleRetry.StatusCode);
        Assert.Equal(sale.GetProperty("id").GetInt64(),
            (await saleRetry.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetInt64());
        Assert.Equal(HttpStatusCode.NotFound,
            (await stranger.GetAsync(prefix + "/cash-sales/receipts?locationId=" + locationId)).StatusCode);

        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            Assert.Equal(0.875m, await db.RetailStockBalances.AsNoTracking()
                .Where(x => x.BusinessId == businessId && x.LocationId == locationId &&
                    x.ProductId == productId)
                .Select(x => x.Quantity).SingleAsync());
            var movements = await db.RetailStockMovements.AsNoTracking()
                .Where(x => x.BusinessId == businessId && x.LocationId == locationId &&
                    x.ProductId == productId)
                .OrderBy(x => x.Id).Select(x => x.QuantityChange).ToListAsync();
            Assert.Equal(new[] { 1.250m, -0.375m }, movements);
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
