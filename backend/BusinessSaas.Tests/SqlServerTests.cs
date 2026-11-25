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
            builder.UseSetting("RetailReturns:PostingEnabled", "true");
            builder.UseSetting("RetailSales:AlternateUnitsEnabled", "true");
            builder.UseSetting("RetailCredit:PostingEnabled", "true");
            builder.UseSetting("RetailCredit:CollectionsEnabled", "true");
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
            name = "SQL Blue Shirt", sku = "SQL-BLUE-SHIRT", barcode = "890000010001", categoryId, brandId, unitId,
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
        var exactPath = prefix + "/cash-sales/products?locationId=" + locationId + "&exact=true&search=";
        var exactSku = await cashier.GetFromJsonAsync<JsonElement>(exactPath + "sql-blue-shirt");
        Assert.Equal(1, exactSku.GetArrayLength());
        Assert.Equal(productId, exactSku[0].GetProperty("id").GetInt64());
        Assert.Equal(1.250m, exactSku[0].GetProperty("available").GetDecimal());
        Assert.Equal("SQPCS", exactSku[0].GetProperty("unitCode").GetString());
        var exactBarcode = await cashier.GetFromJsonAsync<JsonElement>(exactPath + "890000010001");
        Assert.Equal(1, exactBarcode.GetArrayLength());
        Assert.Equal(productId, exactBarcode[0].GetProperty("id").GetInt64());
        Assert.Equal(0, (await cashier.GetFromJsonAsync<JsonElement>(exactPath + "SQL-BLUE")).GetArrayLength());
        Assert.Equal(HttpStatusCode.BadRequest, (await cashier.GetAsync(exactPath)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await stranger.GetAsync(exactPath + "890000010001")).StatusCode);
        var ambiguousId = await Created(prefix + "/products", new
        {
            name = "SQL Code Collision", sku = "890000010001",
            categoryId, brandId, unitId, salePrice = 10m, taxClassification = ""
        });
        var ambiguous = await cashier.GetFromJsonAsync<JsonElement>(exactPath + "890000010001");
        Assert.Equal(2, ambiguous.GetArrayLength());
        Assert.Contains(ambiguous.EnumerateArray(), x => x.GetProperty("id").GetInt64() == productId);
        Assert.Contains(ambiguous.EnumerateArray(), x => x.GetProperty("id").GetInt64() == ambiguousId);
        var receiptSettingsPath = prefix + "/receipt-settings";
        var initialReceiptSettings = await owner.GetFromJsonAsync<JsonElement>(receiptSettingsPath);
        var logo = Convert.FromBase64String("iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mP8/x8AAwMCAO+j2ZsAAAAASUVORK5CYII=");
        var receiptSettings = new {
            paperSize = "A4", showTime = true, showUnitPrice = true,
            showCashReceived = true, showChange = true, showFooter = true,
            footerText = "Thank you", expectedRevision = initialReceiptSettings.GetProperty("revision").GetInt32(),
            reason = "Verify receipt logo preferences", logo, showLogo = true, printCopies = 2, autoOpen = true,
            legalName = "", address = "", phone = "", email = ""
        };
        Assert.Equal(HttpStatusCode.NotFound, (await cashier.GetAsync(receiptSettingsPath)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await Put(stranger, receiptSettingsPath, receiptSettings)).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await Put(owner, receiptSettingsPath, receiptSettings with { printCopies = 6 })).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await Put(owner, receiptSettingsPath, receiptSettings with { logo = new byte[50] })).StatusCode);
        using var settingsSaved = await Put(owner, receiptSettingsPath, receiptSettings);
        Assert.Equal(HttpStatusCode.OK, settingsSaved.StatusCode);
        var settingsBody = await settingsSaved.Content.ReadFromJsonAsync<JsonElement>();
        Assert.False(settingsBody.GetProperty("unchanged").GetBoolean());
        Assert.Equal(2, settingsBody.GetProperty("settings").GetProperty("printCopies").GetInt32());
        Assert.Equal(HttpStatusCode.Conflict, (await Put(owner, receiptSettingsPath, receiptSettings)).StatusCode);
        var savedReceiptSettings = await owner.GetFromJsonAsync<JsonElement>(receiptSettingsPath);
        Assert.Equal(2, savedReceiptSettings.GetProperty("printCopies").GetInt32());
        Assert.True(savedReceiptSettings.GetProperty("autoOpen").GetBoolean());
        Assert.Equal(Convert.ToBase64String(logo), savedReceiptSettings.GetProperty("logo").GetString());

        using var unchangedSettings = await Put(owner, receiptSettingsPath, receiptSettings with {
            expectedRevision = savedReceiptSettings.GetProperty("revision").GetInt32()
        });
        Assert.Equal(HttpStatusCode.OK, unchangedSettings.StatusCode);
        var unchangedBody = await unchangedSettings.Content.ReadFromJsonAsync<JsonElement>();
        Assert.True(unchangedBody.GetProperty("unchanged").GetBoolean());
        Assert.Equal(savedReceiptSettings.GetProperty("revision").GetInt32(),
            unchangedBody.GetProperty("settings").GetProperty("revision").GetInt32());

        var saleRequest = new
        {
            locationId, requestKey = "sql-flow-cash-sale-20261124", tendered = 35m,
            lines = new[] { new { productId, quantity = 0.375m, expectedUnitPrice = 80m } }
        };
        var saleResponse = await Post(cashier, prefix + "/cash-sales", saleRequest);
        Assert.Equal(HttpStatusCode.OK, saleResponse.StatusCode);
        var saleTiming = Assert.Single(saleResponse.Headers.GetValues("Server-Timing"));
        foreach (var phase in new[] { "sale-validate", "sale-save", "sale-stock", "sale-commit" })
            Assert.Contains(phase + ";dur=", saleTiming);
        Console.WriteLine("SQL synthetic cash checkout: " + saleTiming);
        var sale = await saleResponse.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(30m, sale.GetProperty("total").GetDecimal());
        Assert.Equal("SQL Retail Flow", sale.GetProperty("businessName").GetString());
        var originalLine = sale.GetProperty("lines")[0];
        Assert.Equal(0.375m, originalLine.GetProperty("quantity").GetDecimal());
        Assert.Equal(30m, originalLine.GetProperty("lineTotal").GetDecimal());
        Assert.Equal("SQL-BLUE-SHIRT", originalLine.GetProperty("productSku").GetString());
        Assert.Equal("SQPCS", originalLine.GetProperty("unitCode").GetString());
        var originalReceiptPath = prefix + "/cash-sales/business-receipts/" +
            sale.GetProperty("id").GetInt64() + "?locationId=" + locationId;
        var originalReceipt = await owner.GetFromJsonAsync<JsonElement>(originalReceiptPath);
        Assert.Equal(30m, originalReceipt.GetProperty("lines")[0].GetProperty("lineTotal").GetDecimal());
        Assert.False(string.IsNullOrWhiteSpace(originalReceipt.GetProperty("locationName").GetString()));
        Assert.Equal(HttpStatusCode.NotFound, (await cashier.GetAsync(originalReceiptPath)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await stranger.GetAsync(originalReceiptPath)).StatusCode);
        var estimatePath = prefix + "/cash-sales/business-receipts/" +
            sale.GetProperty("id").GetInt64() + "/return-estimate";
        var lineId = originalLine.GetProperty("id").GetInt64();
        object Estimate(decimal quantity) => new { locationId, lines = new[] { new { lineId, quantity } } };
        using var partialEstimate = await Post(owner, estimatePath, Estimate(0.125m));
        Assert.Equal(HttpStatusCode.OK, partialEstimate.StatusCode);
        var partialBody = await partialEstimate.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(10m, partialBody.GetProperty("total").GetDecimal());
        Assert.True(partialBody.GetProperty("canPost").GetBoolean());
        using var fullEstimate = await Post(owner, estimatePath, Estimate(0.375m));
        Assert.Equal(HttpStatusCode.OK, fullEstimate.StatusCode);
        Assert.Equal(30m, (await fullEstimate.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("total").GetDecimal());
        Assert.Equal(HttpStatusCode.BadRequest, (await Post(owner, estimatePath, Estimate(0.376m))).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await Post(owner, estimatePath, Estimate(0.0001m))).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await Post(owner, estimatePath, new { locationId,
            lines = new[] { new { lineId, quantity = 0.125m }, new { lineId, quantity = 0.125m } } })).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await Post(cashier, estimatePath, Estimate(0.125m))).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await Post(stranger, estimatePath, Estimate(0.125m))).StatusCode);
        Assert.False(string.IsNullOrWhiteSpace(sale.GetProperty("timeZoneId").GetString()));
        var saleRetry = await Post(cashier, prefix + "/cash-sales", saleRequest);
        Assert.Equal(HttpStatusCode.OK, saleRetry.StatusCode);
        Assert.StartsWith("sale-replay;dur=", Assert.Single(saleRetry.Headers.GetValues("Server-Timing")));
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

        Assert.Equal(HttpStatusCode.OK, (await Put(owner, receiptSettingsPath, receiptSettings with {
            expectedRevision = savedReceiptSettings.GetProperty("revision").GetInt32(),
            logo = Array.Empty<byte>(), showLogo = false
        })).StatusCode);
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var historical = await db.RetailCashSales.AsNoTracking().SingleAsync(x =>
                x.Id == sale.GetProperty("id").GetInt64() && x.BusinessId == businessId);
            Assert.Equal(logo, historical.MerchantLogo);
            Assert.Null(await db.Businesses.Where(x => x.Id == businessId).Select(x => x.ReceiptLogo).SingleAsync());
        }

        // SQL Server rollback: a later out-of-stock line must not commit an earlier
        // line's deduction, a receipt, or any cash-sale movement.
        var unavailableId = await Created(prefix + "/products", new
        {
            name = "SQL Unstocked Item", sku = "SQL-UNSTOCKED-ITEM",
            categoryId, brandId, unitId, salePrice = 10m, taxClassification = ""
        });
        var atomicBasket = new
        {
            locationId, requestKey = "sql-flow-atomic-basket-20261124", tendered = 100m,
            lines = new[] {
                new { productId, quantity = 0.250m, expectedUnitPrice = 80m },
                new { productId = unavailableId, quantity = 1m, expectedUnitPrice = 10m }
            }
        };
        using (var rejected = await Post(cashier, prefix + "/cash-sales", atomicBasket))
        {
            Assert.Equal(HttpStatusCode.BadRequest, rejected.StatusCode);
            var error = await rejected.Content.ReadFromJsonAsync<JsonElement>();
            Assert.Equal("CASH_SALE_BASKET_CHANGED", error.GetProperty("code").GetString());
            Assert.Equal(unavailableId, error.GetProperty("productId").GetInt64());
        }
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            Assert.Equal(0.875m, await db.RetailStockBalances.AsNoTracking()
                .Where(x => x.BusinessId == businessId && x.LocationId == locationId &&
                    x.ProductId == productId).Select(x => x.Quantity).SingleAsync());
            Assert.False(await db.RetailCashSales.AsNoTracking()
                .AnyAsync(x => x.BusinessId == businessId &&
                    x.RequestKey == atomicBasket.requestKey));
            Assert.Single(await db.RetailStockMovements.AsNoTracking()
                .Where(x => x.BusinessId == businessId && x.ProductId == productId &&
                    x.SourceType == "CashSaleLine").ToListAsync());
        }

        Assert.Equal(HttpStatusCode.Created,
            (await Post(owner, stockPath + "/opening",
                new { productId = unavailableId, quantity = 3m })).StatusCode);
        using var recoveredBasket = await Post(cashier, prefix + "/cash-sales", atomicBasket);
        Assert.Equal(HttpStatusCode.OK, recoveredBasket.StatusCode);
        var recoveredSale = await recoveredBasket.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(30m, recoveredSale.GetProperty("total").GetDecimal());
        using var repeatedBasket = await Post(cashier, prefix + "/cash-sales", atomicBasket);
        Assert.Equal(HttpStatusCode.OK, repeatedBasket.StatusCode);
        Assert.Equal(recoveredSale.GetProperty("id").GetInt64(),
            (await repeatedBasket.Content.ReadFromJsonAsync<JsonElement>())
                .GetProperty("id").GetInt64());

        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            Assert.Equal(0.625m, await db.RetailStockBalances.AsNoTracking()
                .Where(x => x.BusinessId == businessId && x.LocationId == locationId &&
                    x.ProductId == productId).Select(x => x.Quantity).SingleAsync());
            Assert.Equal(2m, await db.RetailStockBalances.AsNoTracking()
                .Where(x => x.BusinessId == businessId && x.LocationId == locationId &&
                    x.ProductId == unavailableId).Select(x => x.Quantity).SingleAsync());
            Assert.Equal(1, await db.RetailCashSales.AsNoTracking().CountAsync(x =>
                x.BusinessId == businessId && x.RequestKey == atomicBasket.requestKey));
            // One prior sale movement, plus exactly two from the recovered basket.
            Assert.Equal(3, await db.RetailStockMovements.AsNoTracking().CountAsync(x =>
                x.BusinessId == businessId && x.SourceType == "CashSaleLine" &&
                (x.ProductId == productId || x.ProductId == unavailableId)));
        }

        // Two simultaneous cash checkouts must not sell the same final unit twice.
        // This exercises the HTTP cash posting transaction, not just StockPosting directly.
        var lastUnitId = await Created(prefix + "/products", new
        {
            name = "SQL Last Stock Unit", sku = "SQL-LAST-STOCK",
            categoryId, brandId, unitId, salePrice = 20m, taxClassification = ""
        });
        using (var opening = await Post(owner, stockPath + "/opening",
            new { productId = lastUnitId, quantity = 1m }))
            Assert.Equal(HttpStatusCode.Created, opening.StatusCode);

        object RaceRequest(string requestKey) => new
        {
            locationId, requestKey, tendered = 20m,
            lines = new[] { new { productId = lastUnitId,
                quantity = 1m, expectedUnitPrice = 20m } }
        };
        const string firstKey = "sql-cash-race-first-20261124";
        const string secondKey = "sql-cash-race-second-20261124";
        var raceResponses = await Task.WhenAll(
            Post(cashier, prefix + "/cash-sales", RaceRequest(firstKey)),
            Post(cashier, prefix + "/cash-sales", RaceRequest(secondKey)));
        using var firstRace = raceResponses[0];
        using var secondRace = raceResponses[1];
        var successes = raceResponses.Where(r => r.StatusCode == HttpStatusCode.OK).ToArray();
        Assert.Single(successes);
        var raceRejected = raceResponses.Single(r => r.StatusCode != HttpStatusCode.OK);
        Assert.True(raceRejected.StatusCode is HttpStatusCode.BadRequest or HttpStatusCode.Conflict,
            $"Concurrent checkout must reject the oversell; status {(int)raceRejected.StatusCode}: " +
            await raceRejected.Content.ReadAsStringAsync());

        var winningKey = firstRace.StatusCode == HttpStatusCode.OK ? firstKey : secondKey;
        var successfulSale = await successes[0].Content.ReadFromJsonAsync<JsonElement>();
        using var replay = await Post(cashier, prefix + "/cash-sales", RaceRequest(winningKey));
        Assert.Equal(HttpStatusCode.OK, replay.StatusCode);
        Assert.Equal(successfulSale.GetProperty("id").GetInt64(),
            (await replay.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetInt64());

        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            Assert.Equal(0m, await db.RetailStockBalances.AsNoTracking()
                .Where(x => x.BusinessId == businessId && x.LocationId == locationId &&
                    x.ProductId == lastUnitId).Select(x => x.Quantity).SingleAsync());
            Assert.Equal(1, await db.RetailCashSales.AsNoTracking().CountAsync(x =>
                x.BusinessId == businessId && x.Lines.Any(l => l.ProductId == lastUnitId)));
            Assert.Equal(1, await db.RetailStockMovements.AsNoTracking().CountAsync(x =>
                x.BusinessId == businessId && x.ProductId == lastUnitId &&
                x.SourceType == "CashSaleLine"));
        }

        // Two identical requests racing must have one durable outcome. A concurrent
        // HTTP response may be OK or a retriable conflict, but never a second sale.
        var retriedId = await Created(prefix + "/products", new
        {
            name = "SQL Idempotent Checkout", sku = "SQL-RETRY-UNIT",
            categoryId, brandId, unitId, salePrice = 15m, taxClassification = ""
        });
        using (var opening = await Post(owner, stockPath + "/opening",
            new { productId = retriedId, quantity = 2m }))
            Assert.Equal(HttpStatusCode.Created, opening.StatusCode);

        const string sharedKey = "sql-same-key-concurrent-20261124";
        var identicalCheckout = new
        {
            locationId, requestKey = sharedKey, tendered = 15m,
            lines = new[] { new { productId = retriedId,
                quantity = 1m, expectedUnitPrice = 15m } }
        };
        var duplicateRequests = await Task.WhenAll(
            Post(cashier, prefix + "/cash-sales", identicalCheckout),
            Post(cashier, prefix + "/cash-sales", identicalCheckout));
        using var duplicateA = duplicateRequests[0];
        using var duplicateB = duplicateRequests[1];
        Assert.Contains(duplicateRequests, response => response.StatusCode == HttpStatusCode.OK);
        foreach (var response in duplicateRequests)
            Assert.True(response.StatusCode is HttpStatusCode.OK or HttpStatusCode.Conflict,
                $"Same-key checkout returned {(int)response.StatusCode}: " +
                await response.Content.ReadAsStringAsync());

        var outcome = await cashier.GetFromJsonAsync<JsonElement>(
            prefix + "/cash-sales/receipt?locationId=" + locationId +
            "&requestKey=" + sharedKey);
        var saleId = outcome.GetProperty("id").GetInt64();
        Assert.Equal(15m, outcome.GetProperty("total").GetDecimal());
        foreach (var response in duplicateRequests.Where(x => x.StatusCode == HttpStatusCode.OK))
            Assert.Equal(saleId, (await response.Content.ReadFromJsonAsync<JsonElement>())
                .GetProperty("id").GetInt64());
        using var finalReplay = await Post(cashier, prefix + "/cash-sales", identicalCheckout);
        Assert.Equal(HttpStatusCode.OK, finalReplay.StatusCode);
        Assert.Equal(saleId, (await finalReplay.Content.ReadFromJsonAsync<JsonElement>())
            .GetProperty("id").GetInt64());

        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            Assert.Equal(1m, await db.RetailStockBalances.AsNoTracking()
                .Where(x => x.BusinessId == businessId && x.LocationId == locationId &&
                    x.ProductId == retriedId).Select(x => x.Quantity).SingleAsync());
            Assert.Equal(1, await db.RetailCashSales.AsNoTracking().CountAsync(x =>
                x.BusinessId == businessId && x.CashierId != 0 &&
                x.RequestKey == sharedKey));
            Assert.Equal(1, await db.RetailStockMovements.AsNoTracking().CountAsync(x =>
                x.BusinessId == businessId && x.ProductId == retriedId &&
                x.SourceType == "CashSaleLine"));
        }

        // SQL Server migration + Owner policy + idempotent historical retry.
        var billingSettingsPath = prefix + "/billing-settings";
        var defaultSettings = await owner.GetFromJsonAsync<JsonElement>(billingSettingsPath);
        Assert.True(defaultSettings.GetProperty("cashSalesEnabled").GetBoolean());
        Assert.False(defaultSettings.GetProperty("creditAvailable").GetBoolean());
        Assert.Equal(HttpStatusCode.Conflict, (await Put(owner, billingSettingsPath,
            new { cashSalesEnabled = true, creditSalesEnabled = true,
                expectedRevision = 0, reason = "Do not release credit without verified ledger" })).StatusCode);
        using var disabledSettings = await Put(owner, billingSettingsPath, new {
            cashSalesEnabled = false, creditSalesEnabled = false, expectedRevision = 0,
            reason = "Pause cash checkout during register verification"
        });
        Assert.Equal(HttpStatusCode.OK, disabledSettings.StatusCode);
        var deniedKey = new {
            locationId, requestKey = "sql-business-cash-paused-20261124", tendered = 15m,
            lines = new[] { new { productId = retriedId,
                quantity = 1m, expectedUnitPrice = 15m } }
        };
        using var deniedCheckout = await Post(cashier, prefix + "/cash-sales", deniedKey);
        Assert.Equal(HttpStatusCode.Conflict, deniedCheckout.StatusCode);
        Assert.Equal("CASH_SALES_DISABLED", (await deniedCheckout.Content.ReadFromJsonAsync<JsonElement>())
            .GetProperty("code").GetString());
        using var originalReplayAfterDisable = await Post(cashier, prefix + "/cash-sales", identicalCheckout);
        Assert.Equal(HttpStatusCode.OK, originalReplayAfterDisable.StatusCode);
        Assert.Equal(saleId, (await originalReplayAfterDisable.Content.ReadFromJsonAsync<JsonElement>())
            .GetProperty("id").GetInt64());
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            Assert.Equal(1m, await db.RetailStockBalances.AsNoTracking()
                .Where(x => x.BusinessId == businessId && x.LocationId == locationId &&
                    x.ProductId == retriedId).Select(x => x.Quantity).SingleAsync());
            Assert.False(await db.RetailCashSales.AsNoTracking().AnyAsync(x =>
                x.BusinessId == businessId && x.RequestKey == deniedKey.requestKey));
            Assert.Single(await db.PlatformAuditEvents.AsNoTracking()
                .Where(x => x.TargetType == "Business" && x.TargetId == businessId &&
                    x.Action == "Business.BillingSettingsChanged").ToListAsync());
        }
        using var enabledSettings = await Put(owner, billingSettingsPath, new {
            cashSalesEnabled = true, creditSalesEnabled = false, expectedRevision = 1,
            reason = "Resume verified cash checkout after store check"
        });
        Assert.Equal(HttpStatusCode.OK, enabledSettings.StatusCode);
        using var resumedCheckout = await Post(cashier, prefix + "/cash-sales", deniedKey);
        Assert.Equal(HttpStatusCode.OK, resumedCheckout.StatusCode);
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            Assert.Equal(0m, await db.RetailStockBalances.AsNoTracking()
                .Where(x => x.BusinessId == businessId && x.LocationId == locationId &&
                    x.ProductId == retriedId).Select(x => x.Quantity).SingleAsync());
            Assert.Equal(2, await db.PlatformAuditEvents.AsNoTracking().CountAsync(x =>
                x.TargetType == "Business" && x.TargetId == businessId &&
                x.Action == "Business.BillingSettingsChanged"));
        }
        // Return only after all original sale assertions have completed.
        var returnPath = prefix + "/cash-sales/business-receipts/" +
            sale.GetProperty("id").GetInt64() + "/returns";
        var request = new { locationId, requestKey = "sql-return-partial-20261125",
            reason = "Customer returned goods", expectedRefund = 10m, cashRefundConfirmed = true,
            lines = new[] { new { lineId, quantity = 0.125m, restock = true } } };
        Assert.Equal(HttpStatusCode.NotFound, (await Post(cashier, returnPath, request)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await Post(stranger, returnPath, request)).StatusCode);
        using var returned = await Post(owner, returnPath, request);
        Assert.Equal(HttpStatusCode.OK, returned.StatusCode);
        var returnedBody = await returned.Content.ReadFromJsonAsync<JsonElement>();
        using var replayReturn = await Post(owner, returnPath, request);
        Assert.Equal(HttpStatusCode.OK, replayReturn.StatusCode);
        Assert.Equal(returnedBody.GetProperty("id").GetInt64(),
            (await replayReturn.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetInt64());
        Assert.Equal(HttpStatusCode.Conflict, (await Post(owner, returnPath, request with {
            requestKey = "sql-return-excess-20261125", expectedRefund = 30m,
            lines = new[] { new { lineId, quantity = 0.375m, restock = true } }
        })).StatusCode);
        var historyPath = returnPath + "?locationId=" + locationId;
        var history = await owner.GetFromJsonAsync<JsonElement>(historyPath);
        Assert.Equal(returnedBody.GetProperty("id").GetInt64(), history.GetProperty("items")[0].GetProperty("id").GetInt64());
        Assert.False(history.GetProperty("hasMore").GetBoolean());
        var recovered = await owner.GetFromJsonAsync<JsonElement>(historyPath + "&requestKey=" + request.requestKey);
        Assert.Equal(10m, recovered.GetProperty("items")[0].GetProperty("total").GetDecimal());
        Assert.Equal(HttpStatusCode.NotFound, (await cashier.GetAsync(historyPath)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await stranger.GetAsync(historyPath)).StatusCode);
        var returnRegisterPath = prefix + "/cash-sales/business-returns?locationId=0";
        var returnRegister = await owner.GetFromJsonAsync<JsonElement>(returnRegisterPath);
        Assert.Equal(1, returnRegister.GetProperty("total").GetInt32());
        Assert.Equal(10m, returnRegister.GetProperty("totals")[0].GetProperty("refundTotal").GetDecimal());
        Assert.Equal(sale.GetProperty("id").GetInt64(), returnRegister.GetProperty("items")[0].GetProperty("saleId").GetInt64());
        Assert.True(returnRegister.GetProperty("items")[0].GetProperty("lines")[0].GetProperty("restock").GetBoolean());
        Assert.Equal(0.125m, returnRegister.GetProperty("items")[0].GetProperty("lines")[0].GetProperty("quantity").GetDecimal());
        Assert.Equal(HttpStatusCode.NotFound, (await cashier.GetAsync(returnRegisterPath)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await stranger.GetAsync(returnRegisterPath)).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await owner.GetAsync(returnRegisterPath + "&fromDate=2026-12-01&toDate=2026-11-01")).StatusCode);
        var partialDetail = await owner.GetFromJsonAsync<JsonElement>(originalReceiptPath);
        Assert.Equal(0.125m, partialDetail.GetProperty("lines")[0].GetProperty("returnedQuantity").GetDecimal());
        Assert.Equal(0.250m, partialDetail.GetProperty("lines")[0].GetProperty("remainingQuantity").GetDecimal());
        Assert.Equal(20m, partialDetail.GetProperty("lines")[0].GetProperty("remainingAmount").GetDecimal());
        using var remainingEstimate = await Post(owner, estimatePath, Estimate(0.250m));
        Assert.Equal(HttpStatusCode.OK, remainingEstimate.StatusCode);
        Assert.Equal(20m, (await remainingEstimate.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("total").GetDecimal());
        using var remainder = await Post(owner, returnPath, request with {
            requestKey = "sql-return-final-20261125", expectedRefund = 20m,
            lines = new[] { new { lineId, quantity = 0.250m, restock = false } }
        });
        Assert.Equal(HttpStatusCode.OK, remainder.StatusCode);
        var completeRegister = await owner.GetFromJsonAsync<JsonElement>(returnRegisterPath);
        Assert.Equal(2, completeRegister.GetProperty("total").GetInt32());
        Assert.Equal(30m, completeRegister.GetProperty("totals")[0].GetProperty("refundTotal").GetDecimal());
        Assert.False(completeRegister.GetProperty("items")[0].GetProperty("lines")[0].GetProperty("restock").GetBoolean());
        var fullDetail = await owner.GetFromJsonAsync<JsonElement>(originalReceiptPath);
        Assert.Equal(0m, fullDetail.GetProperty("lines")[0].GetProperty("remainingQuantity").GetDecimal());
        Assert.Equal(0m, fullDetail.GetProperty("lines")[0].GetProperty("remainingAmount").GetDecimal());
        Assert.Equal(HttpStatusCode.Conflict, (await Post(owner, estimatePath, Estimate(0.001m))).StatusCode);
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            Assert.Equal(30m, await db.RetailCashReturns.Where(x => x.SaleId == sale.GetProperty("id").GetInt64()).SumAsync(x => x.Total));
            Assert.Equal(2, await db.RetailCashReturns.CountAsync(x => x.SaleId == sale.GetProperty("id").GetInt64()));
            Assert.Equal(0.750m, await db.RetailStockBalances.Where(x => x.BusinessId == businessId &&
                x.LocationId == locationId && x.ProductId == productId).Select(x => x.Quantity).SingleAsync());
            Assert.Single(await db.RetailStockMovements.Where(x => x.BusinessId == businessId &&
                x.ProductId == productId && x.SourceType == "CashReturnLine").ToListAsync());
        }
        // Two competing returns of one original line must produce exactly one compensating transaction.
        var finalSale = await resumedCheckout.Content.ReadFromJsonAsync<JsonElement>();
        var raceSaleId = finalSale.GetProperty("id").GetInt64();
        var raceLineId = finalSale.GetProperty("lines")[0].GetProperty("id").GetInt64();
        var raceReturnPath = prefix + "/cash-sales/business-receipts/" + raceSaleId + "/returns";
        var returnRace = request with { expectedRefund = 15m,
            lines = new[] { new { lineId = raceLineId, quantity = 1m, restock = true } } };
        var returnResponses = await Task.WhenAll(
            Post(owner, raceReturnPath, returnRace with { requestKey = "sql-return-race-a-20261125" }),
            Post(owner, raceReturnPath, returnRace with { requestKey = "sql-return-race-b-20261125" }));
        using var returnA = returnResponses[0];
        using var returnB = returnResponses[1];
        Assert.Single(returnResponses.Where(x => x.StatusCode == HttpStatusCode.OK));
        Assert.Single(returnResponses.Where(x => x.StatusCode == HttpStatusCode.Conflict));
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            Assert.Equal(1, await db.RetailCashReturns.CountAsync(x => x.SaleId == raceSaleId));
            Assert.Equal(1m, await db.RetailStockBalances.Where(x => x.BusinessId == businessId &&
                x.LocationId == locationId && x.ProductId == retriedId).Select(x => x.Quantity).SingleAsync());
            Assert.Single(await db.RetailStockMovements.Where(x => x.BusinessId == businessId &&
                x.ProductId == retriedId && x.SourceType == "CashReturnLine").ToListAsync());
        }
        // Put only the synthetic original sale on an earlier date: refunds must
        // still appear on their own posting date, not the original invoice date.
        var returnUtc = DateTime.SpecifyKind(returnedBody.GetProperty("postedAtUtc").GetDateTime(), DateTimeKind.Utc);
        var zone = TimeZoneInfo.FindSystemTimeZoneById(sale.GetProperty("timeZoneId").GetString()!);
        var reportDate = DateOnly.FromDateTime(TimeZoneInfo.ConvertTimeFromUtc(returnUtc, zone));
        var earlierUtc = returnUtc.AddDays(-2);
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            await db.RetailCashSales.Where(x => x.Id == sale.GetProperty("id").GetInt64())
                .ExecuteUpdateAsync(set => set.SetProperty(x => x.PostedAtUtc, earlierUtc));
        }
        var dailyPath = prefix + "/cash-sales/daily-report?locationId=" + locationId + "&date=";
        var earlierDate = DateOnly.FromDateTime(TimeZoneInfo.ConvertTimeFromUtc(earlierUtc, zone));
        var earlierReport = await owner.GetFromJsonAsync<JsonElement>(dailyPath + earlierDate.ToString("yyyy-MM-dd"));
        var earlierTotal = earlierReport.GetProperty("totals")[0];
        Assert.Equal(30m, earlierTotal.GetProperty("salesTotal").GetDecimal());
        Assert.Equal(0m, earlierTotal.GetProperty("refundTotal").GetDecimal());
        Assert.Equal(30m, earlierTotal.GetProperty("netSales").GetDecimal());
        var dailyReport = await owner.GetFromJsonAsync<JsonElement>(dailyPath + reportDate.ToString("yyyy-MM-dd"));
        var todayTotal = dailyReport.GetProperty("totals")[0];
        Assert.True(todayTotal.GetProperty("refundTotal").GetDecimal() >= 10m);
        Assert.Equal(todayTotal.GetProperty("salesTotal").GetDecimal() - todayTotal.GetProperty("refundTotal").GetDecimal(),
            todayTotal.GetProperty("netSales").GetDecimal());
        Assert.Equal(HttpStatusCode.NotFound, (await cashier.GetAsync(dailyPath + reportDate.ToString("yyyy-MM-dd"))).StatusCode);

        // Converted purchase quantities use the factor recorded on the PO, not a later master edit.
        var boxId = await Created(prefix + "/units", new { name = "SQL Box", code = "SQBOX" });
        var convertedProduct = new { name = "SQL Pack Test", sku = "SQL-PACK-TEST", categoryId, unitId,
            salePrice = 10m, taxClassification = "", isActive = true,
            conversions = new[] { new { unitId = boxId, factor = 12m } } };
        var packProductId = await Created(prefix + "/products", convertedProduct);
        var convertedPurchase = new { locationId, supplierId, requestKey = "sql-converted-purchase-20261125",
            lines = new[] { new { productId = packProductId, quantity = 10m, unitCost = 100m,
                unitId = boxId, expectedFactor = 12m, expectedBaseUnitCode = "SQPCS" } } };
        var purchasePath = prefix + "/purchase-orders";
        Assert.Equal(HttpStatusCode.Conflict, (await Post(owner, purchasePath, convertedPurchase with {
            requestKey = "sql-stale-base-purchase-20261125",
            lines = new[] { new { productId = packProductId, quantity = 10m, unitCost = 100m,
                unitId = boxId, expectedFactor = 12m, expectedBaseUnitCode = "KG" } }
        })).StatusCode);
        var packOrderId = await Created(purchasePath, convertedPurchase);
        var packOrderPath = purchasePath + "/" + packOrderId;
        var packDetail = await owner.GetFromJsonAsync<JsonElement>(packOrderPath);
        var packLine = packDetail.GetProperty("lines")[0];
        var packLineId = packLine.GetProperty("id").GetInt64();
        Assert.Equal("SQBOX", packLine.GetProperty("unitCode").GetString());
        Assert.Equal("SQPCS", packLine.GetProperty("baseUnitCode").GetString());
        Assert.Equal(120m, packLine.GetProperty("baseOrderedQuantity").GetDecimal());
        Assert.Equal(HttpStatusCode.OK, (await Put(owner, prefix + "/products/" + packProductId,
            convertedProduct with { conversions = new[] { new { unitId = boxId, factor = 24m } } })).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await Post(owner, purchasePath, convertedPurchase)).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await Post(owner, purchasePath,
            convertedPurchase with { requestKey = "sql-stale-factor-20261125" })).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await Post(owner, packOrderPath + "/approve", new { })).StatusCode);
        var packReceivePath = packOrderPath + "/receivings";
        var receivePackId = await Created(packReceivePath, new { requestKey = "sql-pack-receive-20261125",
            lines = new[] { new { purchaseOrderLineId = packLineId, quantity = 2.5m } } });
        var postPack = packReceivePath + "/" + receivePackId + "/post";
        Assert.Equal(HttpStatusCode.OK, (await Post(owner, postPack, new { })).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await Post(owner, postPack, new { })).StatusCode);
        var packAfter = (await owner.GetFromJsonAsync<JsonElement>(packOrderPath)).GetProperty("lines")[0];
        Assert.Equal(7.5m, packAfter.GetProperty("remainingQuantity").GetDecimal());
        Assert.Equal(30m, packAfter.GetProperty("baseReceivedQuantity").GetDecimal());
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            Assert.Equal(30m, await db.RetailStockBalances.Where(x => x.BusinessId == businessId &&
                x.LocationId == locationId && x.ProductId == packProductId).Select(x => x.Quantity).SingleAsync());
            Assert.Single(await db.RetailStockMovements.Where(x => x.BusinessId == businessId &&
                x.ProductId == packProductId && x.SourceType == "Receiving").ToListAsync());
        }
        // The master now says 24 PCS per BOX. Sell one BOX from the 30 PCS received.
        var packSalePath = prefix + "/cash-sales";
        var catalogPath = packSalePath + "/products?locationId=" + locationId + "&search=SQL-PACK-TEST&exact=true";
        var automaticQuote = (await cashier.GetFromJsonAsync<JsonElement>(catalogPath))[0].GetProperty("conversions")[0];
        Assert.Equal(240m, automaticQuote.GetProperty("salePrice").GetDecimal());
        Assert.Equal(HttpStatusCode.BadRequest, (await Put(owner, prefix + "/products/" + packProductId,
            new { convertedProduct.name, convertedProduct.sku, categoryId, unitId, salePrice = 10m,
                taxClassification = "", isActive = true, conversions = new[] { new { unitId = boxId, factor = 24m, salePrice = 0m } } })).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await Put(owner, prefix + "/products/" + packProductId,
            new { convertedProduct.name, convertedProduct.sku, categoryId, unitId, salePrice = 10m,
                taxClassification = "", isActive = true, conversions = new[] { new { unitId = boxId, factor = 24m, salePrice = 220m } } })).StatusCode);
        var convertedSale = new { locationId, requestKey = "sql-pack-sale-20261125", tendered = 220m,
            lines = new[] { new { productId = packProductId, quantity = 1m, expectedUnitPrice = 220m,
                unitId = boxId, expectedFactor = 24m, expectedBaseUnitCode = "SQPCS" } } };
        var packCatalog = await cashier.GetFromJsonAsync<JsonElement>(packSalePath +
            "/products?locationId=" + locationId + "&search=SQL-PACK-TEST&exact=true");
        var packQuote = packCatalog[0].GetProperty("conversions")[0];
        Assert.Equal(boxId, packQuote.GetProperty("unitId").GetInt64());
        Assert.Equal(24m, packQuote.GetProperty("factor").GetDecimal());
        Assert.Equal(220m, packQuote.GetProperty("salePrice").GetDecimal());
        Assert.Equal(HttpStatusCode.BadRequest, (await Post(cashier, packSalePath, convertedSale with {
            requestKey = "sql-pack-stale-sale-20261125",
            lines = new[] { new { productId = packProductId, quantity = 1m, expectedUnitPrice = 120m,
                unitId = boxId, expectedFactor = 12m, expectedBaseUnitCode = "SQPCS" } }
        })).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await Post(cashier, packSalePath, convertedSale with {
            requestKey = "sql-pack-oversell-20261125", tendered = 440m,
            lines = new[] { new { productId = packProductId, quantity = 2m, expectedUnitPrice = 220m,
                unitId = boxId, expectedFactor = 24m, expectedBaseUnitCode = "SQPCS" } }
        })).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await Post(cashier, packSalePath, convertedSale with {
            requestKey = "sql-pack-stale-price-20261125", tendered = 240m,
            lines = new[] { new { productId = packProductId, quantity = 1m, expectedUnitPrice = 240m,
                unitId = boxId, expectedFactor = 24m, expectedBaseUnitCode = "SQPCS" } }
        })).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await Post(cashier, packSalePath, convertedSale with {
            requestKey = "sql-stale-base-sale-20261125",
            lines = new[] { new { productId = packProductId, quantity = 1m, expectedUnitPrice = 220m,
                unitId = boxId, expectedFactor = 24m, expectedBaseUnitCode = "KG" } }
        })).StatusCode);
        using var packSaleResponse = await Post(cashier, packSalePath, convertedSale);
        Assert.Equal(HttpStatusCode.OK, packSaleResponse.StatusCode);
        var packSale = await packSaleResponse.Content.ReadFromJsonAsync<JsonElement>();
        var packSaleId = packSale.GetProperty("id").GetInt64();
        var soldPack = packSale.GetProperty("lines")[0];
        var soldPackLineId = soldPack.GetProperty("id").GetInt64();
        Assert.Equal("SQBOX", soldPack.GetProperty("unitCode").GetString());
        Assert.Equal("SQPCS", soldPack.GetProperty("baseUnitCode").GetString());
        Assert.Equal(24m, soldPack.GetProperty("baseQuantity").GetDecimal());
        Assert.Equal(220m, soldPack.GetProperty("lineTotal").GetDecimal());
        Assert.Equal(HttpStatusCode.OK, (await Put(owner, prefix + "/products/" + packProductId,
            convertedProduct)).StatusCode);
        using var replayPack = await Post(cashier, packSalePath, convertedSale);
        Assert.Equal(HttpStatusCode.OK, replayPack.StatusCode);
        Assert.Equal(packSaleId, (await replayPack.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetInt64());
        var packReturnPath = packSalePath + "/business-receipts/" + packSaleId;
        using var packEstimate = await Post(owner, packReturnPath + "/return-estimate",
            new { locationId, lines = new[] { new { lineId = soldPackLineId, quantity = 0.5m } } });
        Assert.Equal(HttpStatusCode.OK, packEstimate.StatusCode);
        Assert.Equal(110m, (await packEstimate.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("total").GetDecimal());
        var packReturn = new { locationId, requestKey = "sql-pack-return-20261125", reason = "Returned half box",
            expectedRefund = 110m, cashRefundConfirmed = true,
            lines = new[] { new { lineId = soldPackLineId, quantity = 0.5m, restock = true } } };
        Assert.Equal(HttpStatusCode.OK, (await Post(owner, packReturnPath + "/returns", packReturn)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await Post(owner, packReturnPath + "/returns", packReturn)).StatusCode);
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            Assert.Equal(18m, await db.RetailStockBalances.Where(x => x.BusinessId == businessId &&
                x.LocationId == locationId && x.ProductId == packProductId).Select(x => x.Quantity).SingleAsync());
            Assert.Equal(-24m, await db.RetailStockMovements.Where(x => x.ProductId == packProductId &&
                x.SourceType == "CashSaleLine").Select(x => x.QuantityChange).SingleAsync());
            Assert.Equal(12m, await db.RetailStockMovements.Where(x => x.ProductId == packProductId &&
                x.SourceType == "CashReturnLine").Select(x => x.QuantityChange).SingleAsync());
        }

        // A valid fractional sale can still have return quantities which would
        // produce a fourth stock decimal. Reject these before any refund posting.
        Assert.Equal(HttpStatusCode.OK, (await Put(owner, prefix + "/products/" + packProductId,
            convertedProduct with { conversions = new[] { new { unitId = boxId, factor = 12.5m } } })).StatusCode);
        var fractionalSale = new { locationId, requestKey = "sql-fraction-pack-sale-20261125", tendered = 125m,
            lines = new[] { new { productId = packProductId, quantity = 1m, expectedUnitPrice = 125m,
                unitId = boxId, expectedFactor = 12.5m } } };
        using var fractionalResponse = await Post(cashier, packSalePath, fractionalSale);
        Assert.Equal(HttpStatusCode.OK, fractionalResponse.StatusCode);
        var fractionReceipt = await fractionalResponse.Content.ReadFromJsonAsync<JsonElement>();
        var fractionLineId = fractionReceipt.GetProperty("lines")[0].GetProperty("id").GetInt64();
        var fractionPath = packSalePath + "/business-receipts/" + fractionReceipt.GetProperty("id").GetInt64();
        Assert.Equal(HttpStatusCode.Conflict, (await Post(owner, fractionPath + "/return-estimate",
            new { locationId, lines = new[] { new { lineId = fractionLineId, quantity = 0.001m } } })).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await Post(owner, fractionPath + "/returns",
            new { locationId, requestKey = "sql-invalid-fraction-return-20261125", reason = "Fractional return",
                expectedRefund = 0.13m, cashRefundConfirmed = true,
                lines = new[] { new { lineId = fractionLineId, quantity = 0.001m, restock = true } } })).StatusCode);
        using var fractionEstimate = await Post(owner, fractionPath + "/return-estimate",
            new { locationId, lines = new[] { new { lineId = fractionLineId, quantity = 0.2m } } });
        Assert.Equal(HttpStatusCode.OK, fractionEstimate.StatusCode);
        Assert.Equal(25m, (await fractionEstimate.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("total").GetDecimal());
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            Assert.Equal(5.5m, await db.RetailStockBalances.Where(x => x.BusinessId == businessId &&
                x.LocationId == locationId && x.ProductId == packProductId).Select(x => x.Quantity).SingleAsync());
            Assert.False(await db.RetailCashReturns.AnyAsync(x => x.BusinessId == businessId &&
                x.RequestKey == "sql-invalid-fraction-return-20261125"));
        }
        // The credit pilot requires both gates; enable the business setting only
        // inside this disposable test database, never through public billing settings.
        var creditCustomerId = await Created(prefix + "/customers", new { name = "SQL Credit Customer", phone = "1234567890", email = "" });
        var creditPath = prefix + "/credit-sales";
        var creditLedgerPath = prefix + "/customers/" + creditCustomerId + "/credit-ledger";
        var creditDate = DateOnly.FromDateTime(TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, zone));
        var creditRequest = new { locationId, customerId = creditCustomerId, requestKey = "sql-credit-pilot-20261125",
            dueDate = creditDate.AddDays(7),
            lines = new[] { new { productId = packProductId, quantity = 2m, expectedUnitPrice = 10m, expectedBaseUnitCode = "SQPCS" } } };
        Assert.Equal(HttpStatusCode.Conflict, (await Post(owner, creditPath, creditRequest)).StatusCode);
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            await db.Businesses.Where(x => x.Id == businessId).ExecuteUpdateAsync(x =>
                x.SetProperty(b => b.CreditSalesEnabled, true));
        }
        Assert.Equal(HttpStatusCode.Conflict, (await Post(owner, creditPath, creditRequest)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await Put(owner, prefix + "/customers/" + creditCustomerId + "/credit-limit",
            new { limit = 25m, expectedLimit = 0m, reason = "Approved test credit limit" })).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await Post(cashier, creditPath, creditRequest)).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await Post(owner, creditPath, creditRequest with { customerId = 0L })).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await Post(owner, creditPath, creditRequest with { dueDate = creditDate.AddDays(-1) })).StatusCode);
        var cashReportBefore = await owner.GetFromJsonAsync<JsonElement>(dailyPath + creditDate.ToString("yyyy-MM-dd"));
        using var creditResponse = await Post(owner, creditPath, creditRequest);
        Assert.Equal(HttpStatusCode.OK, creditResponse.StatusCode);
        var creditInvoice = await creditResponse.Content.ReadFromJsonAsync<JsonElement>();
        var creditId = creditInvoice.GetProperty("id").GetInt64();
        Assert.Equal("Credit", creditInvoice.GetProperty("paymentMethod").GetString());
        Assert.Equal(20m, creditInvoice.GetProperty("outstanding").GetDecimal());
        Assert.Equal(0m, creditInvoice.GetProperty("tendered").GetDecimal());
        Assert.False(creditInvoice.GetProperty("printAvailable").GetBoolean());
        using var creditReplay = await Post(owner, creditPath, creditRequest);
        Assert.Equal(HttpStatusCode.OK, creditReplay.StatusCode);
        Assert.Equal(creditId, (await creditReplay.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetInt64());
        Assert.Equal(HttpStatusCode.Conflict, (await Post(owner, creditPath,
            creditRequest with { requestKey = "sql-credit-over-limit-20261125" })).StatusCode);
        var ledger = await owner.GetFromJsonAsync<JsonElement>(creditLedgerPath);
        Assert.Equal(20m, ledger.GetProperty("totals")[0].GetProperty("outstanding").GetDecimal());
        Assert.Equal(creditId, ledger.GetProperty("items")[0].GetProperty("id").GetInt64());
        Assert.Equal(HttpStatusCode.NotFound, (await cashier.GetAsync(creditLedgerPath)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await stranger.GetAsync(creditLedgerPath)).StatusCode);
        var creditLookup = creditPath + "/receipt?locationId=" + locationId + "&requestKey=" + creditRequest.requestKey;
        var recoveredCredit = await owner.GetFromJsonAsync<JsonElement>(creditLookup);
        Assert.Equal(creditId, recoveredCredit.GetProperty("id").GetInt64());
        Assert.Equal("Credit", recoveredCredit.GetProperty("paymentMethod").GetString());
        Assert.Equal(HttpStatusCode.NotFound, (await cashier.GetAsync(creditLookup)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await stranger.GetAsync(creditLookup)).StatusCode);
        var registerPath = packSalePath + "/business-receipts?locationId=0&payment=All&search=" + creditId;
        var register = await owner.GetFromJsonAsync<JsonElement>(registerPath);
        Assert.Equal(1, register.GetProperty("total").GetInt32());
        Assert.Equal("Credit", register.GetProperty("items")[0].GetProperty("paymentMethod").GetString());
        Assert.Equal("Posted", register.GetProperty("items")[0].GetProperty("status").GetString());
        Assert.Equal(20m, register.GetProperty("totals")[0].GetProperty("credit").GetDecimal());
        Assert.Equal(HttpStatusCode.NotFound, (await cashier.GetAsync(registerPath)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await stranger.GetAsync(registerPath)).StatusCode);
        var creditDetailPath = creditPath + "/business-invoices/" + creditId + "?locationId=" + locationId;
        Assert.Equal(creditId, (await owner.GetFromJsonAsync<JsonElement>(creditDetailPath)).GetProperty("id").GetInt64());
        Assert.Equal(HttpStatusCode.NotFound, (await cashier.GetAsync(creditDetailPath)).StatusCode);
        var creditCashPath = packSalePath + "/business-receipts/" + creditId;
        Assert.Equal(HttpStatusCode.NotFound, (await owner.GetAsync(creditCashPath + "?locationId=" + locationId)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await owner.GetAsync(creditCashPath + "/pdf?locationId=" + locationId)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await Post(owner, creditCashPath + "/return-estimate",
            new { locationId, lines = new[] { new { lineId = creditInvoice.GetProperty("lines")[0].GetProperty("id").GetInt64(), quantity = 1m } } })).StatusCode);
        var cashReportAfter = await owner.GetFromJsonAsync<JsonElement>(dailyPath + creditDate.ToString("yyyy-MM-dd"));
        Assert.Equal(cashReportBefore.GetProperty("totals").GetRawText(), cashReportAfter.GetProperty("totals").GetRawText());
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            Assert.Equal(3.5m, await db.RetailStockBalances.Where(x => x.BusinessId == businessId &&
                x.LocationId == locationId && x.ProductId == packProductId).Select(x => x.Quantity).SingleAsync());
            Assert.Single(await db.RetailCashSales.Where(x => x.BusinessId == businessId && x.CreditDueDate != null).ToListAsync());
            Assert.Single(await db.RetailStockMovements.Where(x => x.BusinessId == businessId &&
                x.ProductId == packProductId && x.SourceType == "CreditSaleLine").ToListAsync());
            await db.Businesses.Where(x => x.Id == businessId).ExecuteUpdateAsync(x =>
                x.SetProperty(b => b.CreditSalesEnabled, false));
        }
        Assert.Equal(HttpStatusCode.OK, (await owner.GetAsync(creditLedgerPath)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await Post(owner, creditPath, creditRequest)).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await Post(owner, creditPath,
            creditRequest with { requestKey = "sql-credit-disabled-20261125" })).StatusCode);

        // Collections stay available for existing debt when new credit is disabled.
        var paymentsPath = prefix + "/customers/" + creditCustomerId + "/credit-invoices/" + creditId + "/payments";
        var payment = new { requestKey = "sql-credit-payment-20261125", amount = 8m,
            expectedOutstanding = 20m, cashReceivedConfirmed = true };
        Assert.Equal(HttpStatusCode.NotFound, (await Post(cashier, paymentsPath, payment)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await Post(stranger, paymentsPath, payment)).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await Post(owner, paymentsPath, payment with { amount = 0m })).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await Post(owner, paymentsPath, payment with { amount = 8.001m })).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await Post(owner, paymentsPath, payment with { cashReceivedConfirmed = false })).StatusCode);
        using var collectionResponse = await Post(owner, paymentsPath, payment);
        Assert.Equal(HttpStatusCode.OK, collectionResponse.StatusCode);
        var collection = await collectionResponse.Content.ReadFromJsonAsync<JsonElement>();
        var collectionId = collection.GetProperty("id").GetInt64();
        using var collectionReplay = await Post(owner, paymentsPath, payment);
        Assert.Equal(HttpStatusCode.OK, collectionReplay.StatusCode);
        Assert.Equal(collectionId, (await collectionReplay.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetInt64());
        Assert.Equal(HttpStatusCode.Conflict, (await Post(owner, paymentsPath, payment with { amount = 9m })).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await Post(owner, paymentsPath, payment with {
            requestKey = "sql-stale-collection-20261125" })).StatusCode);
        var partialLedger = await owner.GetFromJsonAsync<JsonElement>(creditLedgerPath);
        Assert.Equal(8m, partialLedger.GetProperty("totals")[0].GetProperty("paid").GetDecimal());
        Assert.Equal(12m, partialLedger.GetProperty("totals")[0].GetProperty("outstanding").GetDecimal());
        Assert.Equal(12m, partialLedger.GetProperty("items")[0].GetProperty("outstanding").GetDecimal());
        var outstandingPath = prefix + "/customers/outstanding-report";
        var outstandingReport = await owner.GetFromJsonAsync<JsonElement>(outstandingPath);
        var aging = outstandingReport.GetProperty("totals")[0];
        Assert.Equal(12m, aging.GetProperty("outstanding").GetDecimal());
        Assert.Equal(12m, aging.GetProperty("notDue").GetDecimal());
        Assert.Equal(0m, aging.GetProperty("overdue").GetDecimal());
        Assert.Equal(creditCustomerId, outstandingReport.GetProperty("items")[0].GetProperty("customerId").GetInt64());
        Assert.Equal(HttpStatusCode.NotFound, (await cashier.GetAsync(outstandingPath)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await stranger.GetAsync(outstandingPath)).StatusCode);
        var noOverdue = await owner.GetFromJsonAsync<JsonElement>(outstandingPath + "?overdueOnly=true");
        Assert.Empty(noOverdue.GetProperty("items").EnumerateArray());
        // Age a disposable fixture, then restore its due date before invoice replay tests.
        using (var agingScope = factory.Services.CreateScope())
        {
            var agingDb = agingScope.ServiceProvider.GetRequiredService<AppDbContext>();
            await agingDb.RetailCashSales.Where(x => x.Id == creditId).ExecuteUpdateAsync(x =>
                x.SetProperty(sale => sale.CreditDueDate, creditDate.AddDays(-31)));
            var overdueReport = await owner.GetFromJsonAsync<JsonElement>(outstandingPath + "?overdueOnly=true");
            var overdueAging = overdueReport.GetProperty("totals")[0];
            Assert.Equal(12m, overdueAging.GetProperty("overdue").GetDecimal());
            Assert.Equal(12m, overdueAging.GetProperty("days31To60").GetDecimal());
            Assert.Equal(0m, overdueAging.GetProperty("days1To30").GetDecimal());
            await agingDb.RetailCashSales.Where(x => x.Id == creditId).ExecuteUpdateAsync(x =>
                x.SetProperty(sale => sale.CreditDueDate, creditRequest.dueDate));
        }
        var collectionHistory = await owner.GetFromJsonAsync<JsonElement>(paymentsPath + "?requestKey=" + payment.requestKey);
        Assert.Equal(collectionId, collectionHistory.GetProperty("items")[0].GetProperty("id").GetInt64());
        Assert.Equal(HttpStatusCode.NotFound, (await cashier.GetAsync(paymentsPath)).StatusCode);
        // Independent requests competing for the exact remaining balance.
        var finalPayment = payment with { requestKey = "sql-final-collection-a-20261125",
            amount = 12m, expectedOutstanding = 12m };
        var competing = await Task.WhenAll(Post(owner, paymentsPath, finalPayment),
            Post(owner, paymentsPath, finalPayment with { requestKey = "sql-final-collection-b-20261125" }));
        Assert.Single(competing, x => x.StatusCode == HttpStatusCode.OK);
        Assert.Single(competing, x => x.StatusCode == HttpStatusCode.Conflict);
        var settledReport = await owner.GetFromJsonAsync<JsonElement>(outstandingPath);
        Assert.Empty(settledReport.GetProperty("items").EnumerateArray());
        Assert.Empty(settledReport.GetProperty("totals").EnumerateArray());
        var settledLedger = await owner.GetFromJsonAsync<JsonElement>(creditLedgerPath);
        Assert.Equal(20m, settledLedger.GetProperty("totals")[0].GetProperty("paid").GetDecimal());
        Assert.Equal(0m, settledLedger.GetProperty("totals")[0].GetProperty("outstanding").GetDecimal());
        using var settledReplay = await Post(owner, creditPath, creditRequest);
        Assert.Equal(HttpStatusCode.OK, settledReplay.StatusCode);
        Assert.Equal(0m, (await settledReplay.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("outstanding").GetDecimal());
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            Assert.Equal(2, await db.RetailCreditPayments.CountAsync(x => x.BusinessId == businessId && x.SaleId == creditId));
            Assert.Equal(20m, await db.RetailCreditPayments.Where(x => x.SaleId == creditId).SumAsync(x => x.Amount));
            Assert.Equal(20m, await db.RetailCashSales.Where(x => x.Id == creditId).Select(x => x.Total).SingleAsync());
            Assert.Equal(3.5m, await db.RetailStockBalances.Where(x => x.BusinessId == businessId &&
                x.LocationId == locationId && x.ProductId == packProductId).Select(x => x.Quantity).SingleAsync());
        }
        var finalCashReport = await owner.GetFromJsonAsync<JsonElement>(dailyPath + creditDate.ToString("yyyy-MM-dd"));
        Assert.Equal(cashReportBefore.GetProperty("totals").GetRawText(), finalCashReport.GetProperty("totals").GetRawText());
    }

    private static async Task RegisterAndLogin(HttpClient client, string email)
    {
        Assert.Equal(HttpStatusCode.OK, (await Post(client, "/api/auth/register",
            new { email, password = "TestPassword123!" })).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await Post(client, "/api/auth/login",
            new { email, password = "TestPassword123!" })).StatusCode);
    }

    private static async Task<HttpResponseMessage> Put(HttpClient client, string path, object body)
    {
        var tokenResponse = await client.GetFromJsonAsync<JsonElement>("/api/auth/csrf");
        using var request = new HttpRequestMessage(HttpMethod.Put, path)
        {
            Content = JsonContent.Create(body)
        };
        request.Headers.Add("X-CSRF-TOKEN", tokenResponse.GetProperty("token").GetString()!);
        return await client.SendAsync(request);
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
