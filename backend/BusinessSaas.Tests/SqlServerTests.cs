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

public sealed class SqlServerTests(Xunit.Abstractions.ITestOutputHelper output)
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
            Assert.Contains("20261125170000_RetailReadIndexes", await db.Database.GetAppliedMigrationsAsync());
            Assert.Contains("20261125180000_RetailExchangeLink", await db.Database.GetAppliedMigrationsAsync());
            Assert.Contains("20261126190000_RetailCashRegisterSessions", await db.Database.GetAppliedMigrationsAsync());
            Assert.Contains("20261127090000_RetailCashRegisterPostingLinks", await db.Database.GetAppliedMigrationsAsync());
            Assert.Contains("20261127110000_RetailCashRegisterMovements", await db.Database.GetAppliedMigrationsAsync());
            Assert.Contains("20261128100000_GlobalProducts", await db.Database.GetAppliedMigrationsAsync());
            Assert.Contains("20261128110000_GlobalProductIdentityIndex", await db.Database.GetAppliedMigrationsAsync());
            Assert.Contains("20261128120000_GlobalProductAuditRevision", await db.Database.GetAppliedMigrationsAsync());
            Assert.Contains("20261128130000_RetailGlobalProductLink", await db.Database.GetAppliedMigrationsAsync());
            Assert.Equal(1, await db.Database.SqlQueryRaw<int>(
                "SELECT COUNT(*) AS [Value] FROM sys.columns WHERE object_id = OBJECT_ID('RetailProducts') " +
                "AND name = 'GlobalProductId' AND is_nullable = 1").SingleAsync());
            Assert.Equal(1, await db.Database.SqlQueryRaw<int>(
                "SELECT COUNT(*) AS [Value] FROM sys.indexes WHERE object_id = OBJECT_ID('RetailProducts') " +
                "AND name = 'IX_RetailProducts_BusinessId_GlobalProductId' AND is_unique = 1 AND has_filter = 1").SingleAsync());
            Assert.Equal(1, await db.Database.SqlQueryRaw<int>(
                "SELECT COUNT(*) AS [Value] FROM sys.foreign_keys WHERE parent_object_id = OBJECT_ID('RetailProducts') " +
                "AND referenced_object_id = OBJECT_ID('GlobalProducts') AND name = 'FK_RetailProducts_GlobalProducts_GlobalProductId' " +
                "AND delete_referential_action = 0").SingleAsync());
            Assert.Equal(1, await db.Database.SqlQueryRaw<int>(
                "SELECT COUNT(*) AS [Value] FROM sys.indexes WHERE object_id = OBJECT_ID('GlobalProducts') " +
                "AND name = 'IX_GlobalProducts_NameKey_BrandKey_PackSizeKey_Unit' AND is_unique = 1 AND has_filter = 1").SingleAsync());
            Assert.Equal(1, await db.Database.SqlQueryRaw<int>(
                "SELECT COUNT(*) AS [Value] FROM sys.indexes WHERE object_id = OBJECT_ID('RetailCashReturns') " +
                "AND name = 'IX_RetailCashReturns_ReplacementSaleId' AND is_unique = 1 AND has_filter = 1").SingleAsync());
            Assert.Equal(1, await db.Database.SqlQueryRaw<int>(
                "SELECT COUNT(*) AS [Value] FROM sys.foreign_keys WHERE parent_object_id = OBJECT_ID('RetailCashReturns') " +
                "AND name = 'FK_RetailCashReturns_RetailCashSales_ReplacementSaleId' AND is_disabled = 0").SingleAsync());
            Assert.Equal(1, await db.Database.SqlQueryRaw<int>(
                "SELECT COUNT(*) AS [Value] FROM sys.check_constraints WHERE parent_object_id = OBJECT_ID('RetailCashReturns') " +
                "AND name = 'CK_RetailCashReturns_ExchangeLink' AND is_disabled = 0 AND is_not_trusted = 0").SingleAsync());
            var indexes = await db.Database.SqlQueryRaw<string>(
                "SELECT name AS [Value] FROM sys.indexes WHERE name IN " +
                "('IX_RetailProducts_BusinessId_Name_Id', 'IX_RetailCashSales_BusinessId_PostedAtUtc_Id', " +
                "'IX_RetailCashReturns_BusinessId_PostedAtUtc_Id')").ToListAsync();
            Assert.Equal(3, indexes.Count);
            Assert.True(await db.Database.CanConnectAsync());
            Assert.True(await db.BusinessTypes.AnyAsync(x => x.Code == "RETAIL" && x.IsSupported));
        }

        // Independent SQL Server contexts race on the same barcode-less identity.
        var duplicateName = "CATALOGUE-RACE-" + Guid.NewGuid().ToString("N");
        async Task<bool> AddCatalogueProduct(string brand, string pack)
        {
            using var scope = factory.Services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            db.GlobalProducts.Add(new GlobalProduct
            {
                Name = duplicateName, NameKey = duplicateName, Brand = brand,
                PackSize = pack, Unit = "PCS", CreatedAtUtc = DateTime.UtcNow
            });
            try { await db.SaveChangesAsync(); return true; }
            catch (DbUpdateException ex) when (ex.InnerException is SqlException { Number: 2601 or 2627 })
            { return false; }
        }
        var catalogueRace = await Task.WhenAll(
            AddCatalogueProduct("Example Brand", "70g"), AddCatalogueProduct("EXAMPLE BRAND", "70G"));
        Assert.Single(catalogueRace.Where(x => x));
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            Assert.Equal(1, await db.GlobalProducts.CountAsync(x => x.NameKey == duplicateName));
            // Different package sizes must remain separate identities.
            Assert.True(await AddCatalogueProduct("Example Brand", "140g"));
        }

        await RegisterAndLogin(owner, "sql-owner@example.test");
        await RegisterAndLogin(stranger, "sql-stranger@example.test");

        using var admin = factory.CreateClient();
        await BusinessFlowTests.SignInProvisioningAdmin(factory.Services, admin,
            "sql-platform-admin@example.test", "sql-owner@example.test");

        const string catalogue = "/api/platform/global-products";
        object CatalogueItem(string barcode, string name = "QA Catalogue item", string pack = "70g",
            string unit = "PCS", bool active = true, int? revision = null) =>
            new { name, barcode, brand = "QA Brand", category = "QA Grocery", packSize = pack,
                unit, isActive = active, expectedRevision = revision };
        var created = await Post(admin, catalogue, CatalogueItem("036000291452"));
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        var createdBody = await created.Content.ReadFromJsonAsync<JsonElement>();
        var globalId = createdBody.GetProperty("id").GetInt64();
        var revision = createdBody.GetProperty("revision").GetInt32();
        Assert.Equal(1, revision);
        foreach (var barcode in new[] { "036000291452", "0036000291452", "00036000291452" })
        {
            var page = await admin.GetFromJsonAsync<JsonElement>(catalogue + "?barcode=" + barcode);
            Assert.Equal(globalId, page.GetProperty("items")[0].GetProperty("id").GetInt64());
        }
        Assert.Equal(HttpStatusCode.Conflict,
            (await Post(admin, catalogue, CatalogueItem("0036000291452"))).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest,
            (await Post(admin, catalogue, CatalogueItem("036000291453"))).StatusCode);
        Assert.Equal(HttpStatusCode.Created,
            (await Post(admin, catalogue, CatalogueItem("96385074", "QA GTIN8 item", unit: "BOX"))).StatusCode);
        var gtin8 = await admin.GetFromJsonAsync<JsonElement>(catalogue + "?barcode=00000096385074");
        Assert.Single(gtin8.GetProperty("items").EnumerateArray());
        var url = catalogue + "/" + globalId;
        Assert.Equal(HttpStatusCode.Conflict, (await Put(admin, url,
            CatalogueItem("036000291452", pack: "140g", revision: revision))).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await Put(admin, url,
            CatalogueItem("036000291452", unit: "KG", revision: revision))).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await Put(admin, url,
            CatalogueItem("96385074", revision: revision))).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await Put(owner, url,
            CatalogueItem("036000291452", revision: revision))).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await Put(admin, url,
            CatalogueItem("0036000291452", name: "QA Catalogue corrected", active: false, revision: revision))).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await Put(admin, url,
            CatalogueItem("036000291452", name: "Stale change", revision: revision))).StatusCode);
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var stored = await db.GlobalProducts.SingleAsync(x => x.Id == globalId);
            Assert.Equal(2, stored.Revision);
            Assert.False(stored.IsActive);
            Assert.Equal("QA Catalogue corrected", stored.Name);
            var audit = await db.PlatformAuditEvents.Where(x => x.TargetType == "GlobalProduct" &&
                x.TargetId == globalId).OrderBy(x => x.Id).ToListAsync();
            Assert.Equal(2, audit.Count);
            Assert.Equal("GlobalProduct.Created", audit[0].Action);
            Assert.Null(audit[0].PreviousValue);
            Assert.Equal("GlobalProduct.Updated", audit[1].Action);
            Assert.Contains("QA Catalogue item", audit[1].PreviousValue);
            Assert.Contains("QA Catalogue corrected", audit[1].NewValue);
            Assert.True(audit[1].ActorUserId > 0);
        }

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


        var activeGlobalId = gtin8.GetProperty("items")[0].GetProperty("id").GetInt64();
        var adoptUrl = $"/api/businesses/{ids[0]}/global-products/{activeGlobalId}/adopt";
        var adoption = new { salePrice = 15m, expectedRevision = 1 };
        Assert.Equal(HttpStatusCode.NotFound, (await Post(stranger, adoptUrl, adoption)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await Post(owner,
            $"/api/businesses/{ids[0]}/global-products/{globalId}/adopt", adoption)).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await Post(owner, adoptUrl,
            new { salePrice = 15m, expectedRevision = 0 })).StatusCode);
        var adopted = await Post(owner, adoptUrl, adoption);
        Assert.Equal(HttpStatusCode.Created, adopted.StatusCode);
        var localId = (await adopted.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetInt64();
        var repeated = await Post(owner, adoptUrl, new { salePrice = 99m, expectedRevision = 1 });
        Assert.Equal(HttpStatusCode.OK, repeated.StatusCode);
        Assert.Equal(localId, (await repeated.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetInt64());
        var lookup = await owner.GetFromJsonAsync<JsonElement>($"/api/businesses/{ids[0]}/products?code=96385074");
        Assert.Equal(localId, lookup.GetProperty("items")[0].GetProperty("id").GetInt64());
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var linked = await db.RetailProducts.SingleAsync(x => x.BusinessId == ids[0] && x.GlobalProductId == activeGlobalId);
            Assert.Equal(15m, linked.SalePrice);
            Assert.False(await db.RetailStockMovements.AnyAsync(x => x.ProductId == localId));
            Assert.False(await db.RetailStockBalances.AnyAsync(x => x.ProductId == localId));
        }

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
            builder.UseSetting("RetailExchanges:PostingEnabled", "true");
            builder.UseSetting("RetailCashRegister:OpeningEnabled", "true");
            builder.UseSetting("RetailCashRegister:LinkingEnabled", "true");
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
        using (var withoutRegister = await Post(cashier, prefix + "/cash-sales", saleRequest))
        {
            Assert.Equal(HttpStatusCode.Conflict, withoutRegister.StatusCode);
            Assert.Equal("REGISTER_REQUIRED", (await withoutRegister.Content.ReadFromJsonAsync<JsonElement>())
                .GetProperty("code").GetString());
        }
        async Task<long> OpenRegister(HttpClient client, string key)
        {
            using var response = await Post(client, prefix + "/cash-register/open",
                new { locationId, requestKey = key, openingCash = 100m });
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            return (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetInt64();
        }
        var cashierRegisterId = await OpenRegister(cashier, "sql-retail-flow-cashier-register");
        var ownerRegisterId = await OpenRegister(owner, "sql-retail-flow-owner-register");
        var saleResponse = await Post(cashier, prefix + "/cash-sales", saleRequest);
        Assert.Equal(HttpStatusCode.OK, saleResponse.StatusCode);
        var saleTiming = Assert.Single(saleResponse.Headers.GetValues("Server-Timing"));
        foreach (var phase in new[] { "sale-validate", "sale-save", "sale-stock", "sale-commit" })
            Assert.Contains(phase + ";dur=", saleTiming);
        Console.WriteLine("SQL synthetic cash checkout: " + saleTiming);
        var sale = await saleResponse.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(30m, sale.GetProperty("total").GetDecimal());
        Assert.Equal("SQL Retail Flow", sale.GetProperty("businessName").GetString());
        string RegisterSummary(long id) => prefix + "/cash-register/" + id + "/summary?locationId=" + locationId;
        var firstSummary = await owner.GetFromJsonAsync<JsonElement>(RegisterSummary(cashierRegisterId));
        Assert.Equal(100m, firstSummary.GetProperty("openingCash").GetDecimal());
        Assert.Equal(30m, firstSummary.GetProperty("cashSales").GetDecimal());
        Assert.Equal(130m, firstSummary.GetProperty("expectedCash").GetDecimal());
        Assert.Equal(HttpStatusCode.NotFound, (await cashier.GetAsync(RegisterSummary(cashierRegisterId))).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await stranger.GetAsync(RegisterSummary(cashierRegisterId))).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await owner.GetAsync(RegisterSummary(long.MaxValue))).StatusCode);
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
        async Task<(int Sales, int Returns, int Movements, decimal SalesTotal, decimal ReturnsTotal)> ExchangeBaseline()
        {
            using var scope = factory.Services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var sales = db.RetailCashSales.Where(x => x.BusinessId == businessId);
            var returns = db.RetailCashReturns.Where(x => x.BusinessId == businessId);
            return (await sales.CountAsync(), await returns.CountAsync(),
                await db.RetailStockMovements.CountAsync(x => x.BusinessId == businessId),
                await sales.SumAsync(x => x.Total), await returns.SumAsync(x => x.Total));
        }

        var beforeExchange = await ExchangeBaseline();
        var exchangePath = returnPath.Replace("/returns", "/exchange-estimate");
        var exchange = new {
            locationId,
            returns = new[] { new { lineId, quantity = 0.250m, restock = true } },
            replacements = new[] { new { productId = unavailableId, quantity = 1m, expectedUnitPrice = 10m } }
        };
        Assert.Equal(HttpStatusCode.NotFound, (await Post(cashier, exchangePath, exchange)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await Post(stranger, exchangePath, exchange)).StatusCode);
        using var exchangeResponse = await Post(owner, exchangePath, exchange);
        Assert.Equal(HttpStatusCode.OK, exchangeResponse.StatusCode);
        var quote = await exchangeResponse.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(20m, quote.GetProperty("returnTotal").GetDecimal());
        Assert.Equal(10m, quote.GetProperty("replacementTotal").GetDecimal());
        Assert.Equal(-10m, quote.GetProperty("difference").GetDecimal());
        Assert.Equal("Refund", quote.GetProperty("settlement").GetString());
        Assert.True(quote.GetProperty("canPost").GetBoolean());
        foreach (var (qty, difference, settlement) in new[] { (2m, 0m, "Even"), (1.5m, -5m, "Refund") })
        {
            using var otherQuote = await Post(owner, exchangePath, exchange with {
                replacements = new[] { new { productId = unavailableId, quantity = qty, expectedUnitPrice = 10m } }
            });
            Assert.Equal(HttpStatusCode.OK, otherQuote.StatusCode);
            var body = await otherQuote.Content.ReadFromJsonAsync<JsonElement>();
            Assert.Equal(difference, body.GetProperty("difference").GetDecimal());
            Assert.Equal(settlement, body.GetProperty("settlement").GetString());
        }
        using var collectQuote = await Post(owner, exchangePath, exchange with {
            returns = new[] { new { lineId, quantity = 0.125m, restock = true } },
            replacements = new[] { new { productId = unavailableId, quantity = 2m, expectedUnitPrice = 10m } }
        });
        Assert.Equal(HttpStatusCode.OK, collectQuote.StatusCode);
        var collectBody = await collectQuote.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(10m, collectBody.GetProperty("difference").GetDecimal());
        Assert.Equal("Collect", collectBody.GetProperty("settlement").GetString());
        Assert.Equal(HttpStatusCode.Conflict, (await Post(owner, exchangePath, exchange with {
            replacements = new[] { new { productId = unavailableId, quantity = 3m, expectedUnitPrice = 10m } }
        })).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await Post(owner, exchangePath, exchange with {
            replacements = new[] { new { productId = unavailableId, quantity = 1m, expectedUnitPrice = 11m } }
        })).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await Post(owner, exchangePath, exchange with {
            returns = new[] { new { lineId, quantity = 0.375m, restock = true } }
        })).StatusCode);
        // The same product can use returned sellable stock, but damaged goods
        // must not increase the replacement quantity available to the customer.
        var sameProduct = exchange with {
            replacements = new[] { new { productId, quantity = 1m, expectedUnitPrice = 80m } }
        };
        using var sameProductQuote = await Post(owner, exchangePath, sameProduct);
        Assert.Equal(HttpStatusCode.OK, sameProductQuote.StatusCode);
        var sameProductBody = await sameProductQuote.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(20m, sameProductBody.GetProperty("returnTotal").GetDecimal());
        Assert.Equal(80m, sameProductBody.GetProperty("replacementTotal").GetDecimal());
        Assert.Equal(60m, sameProductBody.GetProperty("difference").GetDecimal());
        Assert.True(sameProductBody.GetProperty("canPost").GetBoolean());
        Assert.Equal(HttpStatusCode.Conflict, (await Post(owner, exchangePath, sameProduct with {
            returns = new[] { new { lineId, quantity = 0.250m, restock = false } }
        })).StatusCode);
        Assert.Equal(beforeExchange, await ExchangeBaseline());
        using (var exchangeScope = factory.Services.CreateScope())
        {
            var exchangeDb = exchangeScope.ServiceProvider.GetRequiredService<AppDbContext>();
            Assert.Equal(1, await exchangeDb.RetailCashReturns.CountAsync(x => x.SaleId == sale.GetProperty("id").GetInt64()));
            Assert.Equal(2m, await exchangeDb.RetailStockBalances.Where(x => x.BusinessId == businessId &&
                x.LocationId == locationId && x.ProductId == unavailableId).Select(x => x.Quantity).SingleAsync());
            Assert.Equal(0.750m, await exchangeDb.RetailStockBalances.Where(x => x.BusinessId == businessId &&
                x.LocationId == locationId && x.ProductId == productId).Select(x => x.Quantity).SingleAsync());
        }
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
            Assert.False(await db.RetailCashReturns.AnyAsync(x => x.SaleId == sale.GetProperty("id").GetInt64() &&
                (x.ReplacementSaleId != null || x.NetCashSettlement != null)));
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
        var backdatedSaleId = sale.GetProperty("id").GetInt64();
        DateTime originalPostedAtUtc;
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            originalPostedAtUtc = await db.RetailCashSales.Where(x => x.Id == backdatedSaleId)
                .Select(x => x.PostedAtUtc).SingleAsync();
            await db.RetailCashSales.Where(x => x.Id == backdatedSaleId)
                .ExecuteUpdateAsync(set => set.SetProperty(x => x.PostedAtUtc, earlierUtc));
        }
        var dailyPath = prefix + "/cash-sales/daily-report?locationId=" + locationId + "&date=";
        try
        {
            var earlierDate = DateOnly.FromDateTime(TimeZoneInfo.ConvertTimeFromUtc(earlierUtc, zone));
            var earlierReport = await owner.GetFromJsonAsync<JsonElement>(dailyPath + earlierDate.ToString("yyyy-MM-dd"));
            var earlierTotal = earlierReport.GetProperty("totals")[0];
            Assert.Equal(30m, earlierTotal.GetProperty("salesTotal").GetDecimal());
            Assert.Equal(0m, earlierTotal.GetProperty("refundTotal").GetDecimal());
            Assert.Equal(30m, earlierTotal.GetProperty("netSales").GetDecimal());
            Assert.Equal(30m, earlierTotal.GetProperty("cashCollected").GetDecimal());
            Assert.Equal(0m, earlierTotal.GetProperty("cashRefunded").GetDecimal());
            var dailyReport = await owner.GetFromJsonAsync<JsonElement>(dailyPath + reportDate.ToString("yyyy-MM-dd"));
            var todayTotal = dailyReport.GetProperty("totals")[0];
            Assert.True(todayTotal.GetProperty("refundTotal").GetDecimal() >= 10m);
            Assert.Equal(todayTotal.GetProperty("salesTotal").GetDecimal() - todayTotal.GetProperty("refundTotal").GetDecimal(),
                todayTotal.GetProperty("netSales").GetDecimal());
            Assert.Equal(HttpStatusCode.NotFound, (await cashier.GetAsync(dailyPath + reportDate.ToString("yyyy-MM-dd"))).StatusCode);
            using var invalidSummary = await owner.GetAsync(RegisterSummary(cashierRegisterId));
            Assert.Equal(HttpStatusCode.Conflict, invalidSummary.StatusCode);
            Assert.Equal("REGISTER_RECONCILIATION_REQUIRED",
                (await invalidSummary.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("code").GetString());
        }
        finally
        {
            // Restore the synthetic timestamp before testing register reconciliation.
            using var scope = factory.Services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            await db.RetailCashSales.Where(x => x.Id == backdatedSaleId)
                .ExecuteUpdateAsync(set => set.SetProperty(x => x.PostedAtUtc, originalPostedAtUtc));
        }

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
        var filteredPackRegister = packSalePath + "/business-receipts?locationId=" + locationId + "&receiptId=" + packSaleId;
        var partialPackRegister = await owner.GetFromJsonAsync<JsonElement>(filteredPackRegister + "&status=Partial%20return");
        Assert.Equal(1, partialPackRegister.GetProperty("total").GetInt32());
        Assert.Equal(packSaleId, partialPackRegister.GetProperty("items")[0].GetProperty("id").GetInt64());
        Assert.Equal("Partial return", partialPackRegister.GetProperty("items")[0].GetProperty("status").GetString());
        var allPackRegister = await owner.GetFromJsonAsync<JsonElement>(filteredPackRegister + "&status=All");
        Assert.Equal(1, allPackRegister.GetProperty("total").GetInt32());
        Assert.Equal(partialPackRegister.GetProperty("totals").GetRawText(),
            allPackRegister.GetProperty("totals").GetRawText());
        Assert.Equal(220m, allPackRegister.GetProperty("totals")[0].GetProperty("cash").GetDecimal());
        Assert.Equal("Partial return", allPackRegister.GetProperty("items")[0].GetProperty("status").GetString());
        var postedPackRegister = await owner.GetFromJsonAsync<JsonElement>(filteredPackRegister + "&status=Posted");
        Assert.Equal(0, postedPackRegister.GetProperty("total").GetInt32());
        Assert.Empty(postedPackRegister.GetProperty("items").EnumerateArray());
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
        Assert.Equal(OperatingSystem.IsWindows(), creditInvoice.GetProperty("printAvailable").GetBoolean());
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
        var creditPdfPath = creditPath + "/business-invoices/" + creditId + "/pdf?locationId=" + locationId;
        Assert.Equal(HttpStatusCode.NotFound, (await cashier.GetAsync(creditPdfPath)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await stranger.GetAsync(creditPdfPath)).StatusCode);
        using (var creditPdf = await owner.GetAsync(creditPdfPath))
        {
            Assert.Equal(OperatingSystem.IsWindows() ? HttpStatusCode.OK : HttpStatusCode.ServiceUnavailable, creditPdf.StatusCode);
            if (OperatingSystem.IsWindows()) Assert.Equal("application/pdf", creditPdf.Content.Headers.ContentType?.MediaType);
        }
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

        // Owner report and cashier checkout may overlap on separate SQL connections.
        // A retriable SQL conflict is allowed, but no partial or duplicate posting is.
        var reportRaceProduct = await Created(prefix + "/products", new
        {
            name = "SQL Report Race Product", sku = "SQL-REPORT-RACE",
            categoryId, brandId, unitId, salePrice = 15m, taxClassification = ""
        });
        using (var opening = await Post(owner, stockPath + "/opening",
            new { productId = reportRaceProduct, quantity = 2m }))
            Assert.Equal(HttpStatusCode.Created, opening.StatusCode);
        var allCashRegister = prefix + "/cash-sales/business-receipts?locationId=0&payment=Cash&status=All";
        var beforeRace = await owner.GetFromJsonAsync<JsonElement>(allCashRegister);
        var beforeCount = beforeRace.GetProperty("total").GetInt32();
        var beforeTotal = beforeRace.GetProperty("totals").EnumerateArray()
            .Sum(x => x.GetProperty("cash").GetDecimal());
        var reportRaceSale = new
        {
            locationId, requestKey = "sql-report-checkout-race-20261125", tendered = 15m,
            lines = new[] { new { productId = reportRaceProduct, quantity = 1m, expectedUnitPrice = 15m } }
        };
        var reportRace = await Task.WhenAll(owner.GetAsync(allCashRegister),
            Post(cashier, prefix + "/cash-sales", reportRaceSale));
        using var concurrentReport = reportRace[0];
        using var concurrentCheckout = reportRace[1];
        Assert.True(concurrentReport.StatusCode is HttpStatusCode.OK or HttpStatusCode.Conflict,
            await concurrentReport.Content.ReadAsStringAsync());
        Assert.True(concurrentCheckout.StatusCode is HttpStatusCode.OK or HttpStatusCode.Conflict,
            await concurrentCheckout.Content.ReadAsStringAsync());
        if (concurrentReport.StatusCode == HttpStatusCode.OK)
        {
            var observed = await concurrentReport.Content.ReadFromJsonAsync<JsonElement>();
            var observedCount = observed.GetProperty("total").GetInt32();
            Assert.InRange(observedCount, beforeCount, beforeCount + 1);
            Assert.Equal(beforeTotal + (observedCount - beforeCount) * 15m,
                observed.GetProperty("totals").EnumerateArray().Sum(x => x.GetProperty("cash").GetDecimal()));
            Assert.Equal(Math.Min(observedCount, 10), observed.GetProperty("items").GetArrayLength());
        }
        // Replay the same key after both requests finish, whether checkout won or conflicted.
        using var reportRaceReplay = await Post(cashier, prefix + "/cash-sales", reportRaceSale);
        Assert.Equal(HttpStatusCode.OK, reportRaceReplay.StatusCode);
        var reportRaceId = (await reportRaceReplay.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetInt64();
        var afterRace = await owner.GetFromJsonAsync<JsonElement>(allCashRegister);
        Assert.Equal(beforeCount + 1, afterRace.GetProperty("total").GetInt32());
        Assert.Equal(beforeTotal + 15m,
            afterRace.GetProperty("totals").EnumerateArray().Sum(x => x.GetProperty("cash").GetDecimal()));
        Assert.Equal(reportRaceId, afterRace.GetProperty("items")[0].GetProperty("id").GetInt64());
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            Assert.Equal(1, await db.RetailCashSales.CountAsync(x => x.BusinessId == businessId &&
                x.RequestKey == reportRaceSale.requestKey));
            Assert.Equal(1m, await db.RetailStockBalances.Where(x => x.BusinessId == businessId &&
                x.LocationId == locationId && x.ProductId == reportRaceProduct).Select(x => x.Quantity).SingleAsync());
            Assert.Equal(1, await db.RetailStockMovements.CountAsync(x => x.BusinessId == businessId &&
                x.ProductId == reportRaceProduct && x.SourceType == "CashSaleLine"));

            // Reconcile every product/location in this isolated business, not just
            // the individual sale and return examples above.
            var balances = await db.RetailStockBalances.AsNoTracking()
                .Where(x => x.BusinessId == businessId)
                .Select(x => new { x.LocationId, x.ProductId, x.Quantity })
                .ToListAsync();
            var movements = await db.RetailStockMovements.AsNoTracking()
                .Where(x => x.BusinessId == businessId)
                .GroupBy(x => new { x.LocationId, x.ProductId })
                .Select(g => new { g.Key.LocationId, g.Key.ProductId, Quantity = g.Sum(x => x.QuantityChange) })
                .ToListAsync();
            Assert.NotEmpty(balances);
            Assert.Equal(balances.OrderBy(x => x.LocationId).ThenBy(x => x.ProductId).ToArray(),
                movements.OrderBy(x => x.LocationId).ThenBy(x => x.ProductId).ToArray());
            Assert.All(balances, x => Assert.True(x.Quantity >= 0m));

            // Registers expose gross invoice totals and posted refunds separately.
            // Compare unfiltered, paginated API summaries to all persisted records.
            var invoiceRegister = await owner.GetFromJsonAsync<JsonElement>(
                prefix + "/cash-sales/business-receipts?locationId=0&payment=All&status=All");
            var refundRegister = await owner.GetFromJsonAsync<JsonElement>(
                prefix + "/cash-sales/business-returns?locationId=0");
            var invoices = await db.RetailCashSales.AsNoTracking()
                .Where(x => x.BusinessId == businessId).Include(x => x.Lines).ToListAsync();
            var refunds = await db.RetailCashReturns.AsNoTracking()
                .Where(x => x.BusinessId == businessId).Include(x => x.Lines).ToListAsync();
            Assert.Equal(invoices.Count, invoiceRegister.GetProperty("total").GetInt32());
            Assert.Equal(refunds.Count, refundRegister.GetProperty("total").GetInt32());
            Assert.All(invoices, x => Assert.Equal(x.Total, x.Lines.Sum(l => l.LineTotal)));
            Assert.All(refunds, x => Assert.Equal(x.Total, x.Lines.Sum(l => l.Amount)));
            var invoiceTotals = invoiceRegister.GetProperty("totals").EnumerateArray()
                .ToDictionary(x => x.GetProperty("currencyCode").GetString()!);
            Assert.Equal(invoices.Select(x => x.CurrencyCode).Distinct().Count(), invoiceTotals.Count);
            foreach (var group in invoices.GroupBy(x => x.CurrencyCode))
            {
                var total = invoiceTotals[group.Key];
                Assert.Equal(group.Sum(x => x.Total), total.GetProperty("total").GetDecimal());
                Assert.Equal(group.Where(x => x.CreditDueDate == null).Sum(x => x.Total),
                    total.GetProperty("cash").GetDecimal());
                Assert.Equal(group.Where(x => x.CreditDueDate != null).Sum(x => x.Total),
                    total.GetProperty("credit").GetDecimal());
            }
            var refundTotals = refundRegister.GetProperty("totals").EnumerateArray()
                .ToDictionary(x => x.GetProperty("currencyCode").GetString()!);
            var currencyBySale = invoices.ToDictionary(x => x.Id, x => x.CurrencyCode);
            var refundsByCurrency = refunds.GroupBy(x => currencyBySale[x.SaleId]).ToList();
            Assert.Equal(refundsByCurrency.Count, refundTotals.Count);
            foreach (var group in refundsByCurrency)
            {
                Assert.Equal(group.Sum(x => x.Total), refundTotals[group.Key].GetProperty("refundTotal").GetDecimal());
                Assert.Equal(group.Count(), refundTotals[group.Key].GetProperty("returnCount").GetInt32());
            }
        }

        // A same-product exchange restores the sold unit and then issues two units
        // in one transaction; only the price difference is physically collected.
        var exchangeOriginal = await owner.GetFromJsonAsync<JsonElement>(
            prefix + "/cash-sales/business-receipts/" + reportRaceId + "?locationId=" + locationId);
        var exchangeLineId = exchangeOriginal.GetProperty("lines")[0].GetProperty("id").GetInt64();
        var postingPath = prefix + "/cash-sales/business-receipts/" + reportRaceId + "/exchanges";
        var exchangePost = new {
            locationId, requestKey = "sql-atomic-exchange-20261125", reason = "Customer exchanged product",
            expectedSettlement = 15m, settlementConfirmed = true,
            returns = new[] { new { lineId = exchangeLineId, quantity = 1m, restock = true } },
            replacements = new[] { new { productId = reportRaceProduct, quantity = 2m, expectedUnitPrice = 15m } }
        };
        var beforePosting = await ExchangeBaseline();
        Assert.Equal(HttpStatusCode.NotFound, (await Post(cashier, postingPath, exchangePost)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await Post(stranger, postingPath, exchangePost)).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await Post(owner, postingPath, exchangePost with {
            expectedSettlement = 14m
        })).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await Post(owner, postingPath, exchangePost with {
            returns = new[] { new { lineId = exchangeLineId, quantity = 1m, restock = false } }
        })).StatusCode);
        Assert.Equal(beforePosting, await ExchangeBaseline());
        using var postedExchange = await Post(owner, postingPath, exchangePost);
        Assert.Equal(HttpStatusCode.OK, postedExchange.StatusCode);
        var exchangeReceipt = await postedExchange.Content.ReadFromJsonAsync<JsonElement>();
        var replacementId = exchangeReceipt.GetProperty("replacementSaleId").GetInt64();
        Assert.Equal(15m, exchangeReceipt.GetProperty("difference").GetDecimal());
        Assert.Equal(15m, exchangeReceipt.GetProperty("returnTotal").GetDecimal());
        Assert.Equal(30m, exchangeReceipt.GetProperty("replacementTotal").GetDecimal());
        using var exchangeReplay = await Post(owner, postingPath, exchangePost);
        Assert.Equal(HttpStatusCode.OK, exchangeReplay.StatusCode);
        Assert.Equal(replacementId, (await exchangeReplay.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("replacementSaleId").GetInt64());
        Assert.Equal(HttpStatusCode.Conflict, (await Post(owner, postingPath, exchangePost with {
            reason = "Different exchange reason"
        })).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await Post(owner, postingPath, exchangePost with {
            requestKey = "sql-atomic-exchange-excess-20261125"
        })).StatusCode);
        var recoveryPath = postingPath + "?locationId=" + locationId + "&requestKey=" + exchangePost.requestKey;
        Assert.Equal(HttpStatusCode.NotFound, (await stranger.GetAsync(recoveryPath)).StatusCode);
        Assert.Equal(replacementId, (await owner.GetFromJsonAsync<JsonElement>(recoveryPath)).GetProperty("replacementSaleId").GetInt64());
        var replacementPath = prefix + "/cash-sales/business-receipts/" + replacementId;
        var replacementReceipt = await owner.GetFromJsonAsync<JsonElement>(replacementPath + "?locationId=" + locationId);
        Assert.Equal(15m, replacementReceipt.GetProperty("tendered").GetDecimal());
        Assert.Equal(15m, replacementReceipt.GetProperty("exchange").GetProperty("difference").GetDecimal());
        Assert.Equal(OperatingSystem.IsWindows(), replacementReceipt.GetProperty("printAvailable").GetBoolean());
        using var exchangePdf = await owner.GetAsync(replacementPath + "/pdf?locationId=" + locationId);
        Assert.Equal(OperatingSystem.IsWindows() ? HttpStatusCode.OK : HttpStatusCode.ServiceUnavailable, exchangePdf.StatusCode);
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            Assert.Equal(0m, await db.RetailStockBalances.Where(x => x.BusinessId == businessId &&
                x.LocationId == locationId && x.ProductId == reportRaceProduct).Select(x => x.Quantity).SingleAsync());
            var linked = await db.RetailCashReturns.Include(x => x.Lines).SingleAsync(x =>
                x.BusinessId == businessId && x.RequestKey == exchangePost.requestKey);
            Assert.Equal(replacementId, linked.ReplacementSaleId);
            Assert.Equal(15m, linked.NetCashSettlement);
            Assert.Single(linked.Lines);
        }
        Assert.Equal((beforePosting.Sales + 1, beforePosting.Returns + 1, beforePosting.Movements + 2,
            beforePosting.SalesTotal + 30m, beforePosting.ReturnsTotal + 15m), await ExchangeBaseline());

        var exchangeDate = DateOnly.FromDateTime(TimeZoneInfo.ConvertTimeFromUtc(
            replacementReceipt.GetProperty("postedAtUtc").GetDateTime().ToUniversalTime(), zone));
        var exchangeReport = await owner.GetFromJsonAsync<JsonElement>(dailyPath + exchangeDate.ToString("yyyy-MM-dd"));
        var exchangeTotals = exchangeReport.GetProperty("totals")[0];
        Assert.Equal(exchangeTotals.GetProperty("salesTotal").GetDecimal() - 15m,
            exchangeTotals.GetProperty("cashCollected").GetDecimal());
        Assert.Equal(exchangeTotals.GetProperty("refundTotal").GetDecimal() - 15m,
            exchangeTotals.GetProperty("cashRefunded").GetDecimal());
        Assert.Equal(exchangeTotals.GetProperty("netSales").GetDecimal(),
            exchangeTotals.GetProperty("cashCollected").GetDecimal() - exchangeTotals.GetProperty("cashRefunded").GetDecimal());

        // Run competing exchange requests on independent authenticated clients.
        using var secondOwner = factory.CreateClient();
        Assert.Equal(HttpStatusCode.NoContent, (await Post(secondOwner, "/api/auth/login",
            new { email = "sql-retail-flow-owner@example.test", password = "TestPassword123!" })).StatusCode);

        foreach (var (label, price, sameKey) in new[] { ("refund", 10m, false), ("even", 15m, true) })
        {
            var soldProduct = await Created(prefix + "/products", new
            {
                name = "Exchange original " + label, sku = "EX-ORIGINAL-" + label,
                categoryId, brandId, unitId, salePrice = 15m, taxClassification = ""
            });
            var newProduct = await Created(prefix + "/products", new
            {
                name = "Exchange replacement " + label, sku = "EX-REPLACE-" + label,
                categoryId, brandId, unitId, salePrice = price, taxClassification = ""
            });
            foreach (var exchangeProductId in new[] { soldProduct, newProduct })
                Assert.Equal(HttpStatusCode.Created, (await Post(owner, stockPath + "/opening",
                    new { productId = exchangeProductId, quantity = 1m })).StatusCode);
            using var originalResponse = await Post(cashier, prefix + "/cash-sales", new
            {
                locationId, requestKey = "sql-exchange-original-" + label, tendered = 15m,
                lines = new[] { new { productId = soldProduct, quantity = 1m, expectedUnitPrice = 15m } }
            });
            Assert.Equal(HttpStatusCode.OK, originalResponse.StatusCode);
            var raceOriginalReceipt = await originalResponse.Content.ReadFromJsonAsync<JsonElement>();
            var originalId = raceOriginalReceipt.GetProperty("id").GetInt64();
            var originalLineId = raceOriginalReceipt.GetProperty("lines")[0].GetProperty("id").GetInt64();
            var path = prefix + "/cash-sales/business-receipts/" + originalId + "/exchanges";
            var raceExchangeRequest = new
            {
                locationId, requestKey = "sql-exchange-race-" + label, reason = "Concurrent exchange regression",
                expectedSettlement = price - 15m, settlementConfirmed = true,
                returns = new[] { new { lineId = originalLineId, quantity = 1m, restock = true } },
                replacements = new[] { new { productId = newProduct, quantity = 1m, expectedUnitPrice = price } }
            };
            var raceCompetingRequest = sameKey ? raceExchangeRequest : raceExchangeRequest with { requestKey = raceExchangeRequest.requestKey + "-other" };
            var before = await ExchangeBaseline();
            var reportDay = DateOnly.FromDateTime(TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, zone));
            var reportPath = dailyPath + reportDay.ToString("yyyy-MM-dd");
            var reportBefore = (await owner.GetFromJsonAsync<JsonElement>(reportPath)).GetProperty("totals")[0];

            var results = await Task.WhenAll(Post(owner, path, raceExchangeRequest), Post(secondOwner, path, raceCompetingRequest));
            using var first = results[0];
            using var second = results[1];
            foreach (var response in results)
                Assert.True(response.StatusCode is HttpStatusCode.OK or HttpStatusCode.Conflict,
                    await response.Content.ReadAsStringAsync());
            if (!sameKey)
                Assert.True(results.Count(x => x.StatusCode == HttpStatusCode.OK) <= 1);
            var winner = second.StatusCode == HttpStatusCode.OK ? raceCompetingRequest : raceExchangeRequest;
            using var raceExchangeReplay = await Post(owner, path, winner);
            Assert.Equal(HttpStatusCode.OK, raceExchangeReplay.StatusCode);
            var posted = await raceExchangeReplay.Content.ReadFromJsonAsync<JsonElement>();
            var linkedSaleId = posted.GetProperty("replacementSaleId").GetInt64();
            foreach (var response in results.Where(x => x.StatusCode == HttpStatusCode.OK))
                Assert.Equal(linkedSaleId, (await response.Content.ReadFromJsonAsync<JsonElement>())
                    .GetProperty("replacementSaleId").GetInt64());
            Assert.Equal(price - 15m, posted.GetProperty("difference").GetDecimal());
            Assert.Equal(sameKey ? "Even" : "Refund", posted.GetProperty("settlement").GetString());
            Assert.Equal(HttpStatusCode.Conflict, (await Post(owner, path,
                winner with { requestKey = winner.requestKey + "-new" })).StatusCode);
            Assert.Equal((before.Sales + 1, before.Returns + 1, before.Movements + 2,
                before.SalesTotal + price, before.ReturnsTotal + 15m), await ExchangeBaseline());

            using (var scope = factory.Services.CreateScope())
            {
                var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
                var linked = await db.RetailCashReturns.SingleAsync(x =>
                    x.BusinessId == businessId && x.SaleId == originalId);
                Assert.Equal(linkedSaleId, linked.ReplacementSaleId);
                Assert.Equal(price - 15m, linked.NetCashSettlement);
                Assert.Equal(0m, await db.RetailCashSales.Where(x => x.Id == linkedSaleId &&
                    x.BusinessId == businessId).Select(x => x.Tendered).SingleAsync());
                foreach (var (exchangeProductId, quantity) in new[] { (soldProduct, 1m), (newProduct, 0m) })
                {
                    Assert.Equal(quantity, await db.RetailStockBalances.Where(x =>
                        x.BusinessId == businessId && x.LocationId == locationId && x.ProductId == exchangeProductId)
                        .Select(x => x.Quantity).SingleAsync());
                    Assert.Equal(quantity, await db.RetailStockMovements.Where(x =>
                        x.BusinessId == businessId && x.LocationId == locationId && x.ProductId == exchangeProductId)
                        .SumAsync(x => x.QuantityChange));
                }
            }
            var reportAfter = (await owner.GetFromJsonAsync<JsonElement>(reportPath)).GetProperty("totals")[0];
            Assert.Equal(reportBefore.GetProperty("salesTotal").GetDecimal() + price,
                reportAfter.GetProperty("salesTotal").GetDecimal());
            Assert.Equal(reportBefore.GetProperty("refundTotal").GetDecimal() + 15m,
                reportAfter.GetProperty("refundTotal").GetDecimal());
            Assert.Equal(reportBefore.GetProperty("cashCollected").GetDecimal(),
                reportAfter.GetProperty("cashCollected").GetDecimal());
            Assert.Equal(reportBefore.GetProperty("cashRefunded").GetDecimal() + 15m - price,
                reportAfter.GetProperty("cashRefunded").GetDecimal());
        }

        // Every newly posted cash document belongs to the operator who actually
        // settled it. Unpaid credit invoices never count as cash receipts.
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var cashDocuments = await db.RetailCashSales.AsNoTracking().Where(x => x.BusinessId == businessId &&
                x.CreditDueDate == null).ToListAsync();
            var returns = await db.RetailCashReturns.AsNoTracking().Where(x => x.BusinessId == businessId).ToListAsync();
            var replacementIds = returns.Where(x => x.ReplacementSaleId.HasValue)
                .Select(x => x.ReplacementSaleId!.Value).ToHashSet();
            Assert.NotEmpty(cashDocuments);
            Assert.NotEmpty(returns);
            Assert.All(cashDocuments, x => Assert.Equal(replacementIds.Contains(x.Id) ? ownerRegisterId : cashierRegisterId,
                x.RegisterSessionId));
            Assert.All(returns, x => Assert.Equal(ownerRegisterId, x.RegisterSessionId));
            var payments = await db.RetailCreditPayments.AsNoTracking().Where(x => x.BusinessId == businessId).ToListAsync();
            Assert.NotEmpty(payments);
            Assert.All(payments, x => Assert.Equal(ownerRegisterId, x.RegisterSessionId));
            Assert.False(await db.RetailCashSales.AnyAsync(x => x.BusinessId == businessId &&
                x.CreditDueDate != null && x.RegisterSessionId != null));

            var ownerSummary = await owner.GetFromJsonAsync<JsonElement>(RegisterSummary(ownerRegisterId));
            var refundTotal = returns.Where(x => x.ReplacementSaleId == null).Sum(x => x.Total);
            var exchangeTotal = returns.Where(x => x.ReplacementSaleId != null).Sum(x => x.NetCashSettlement!.Value);
            var collections = payments.Sum(x => x.Amount);
            Assert.Equal(0m, ownerSummary.GetProperty("cashSales").GetDecimal());
            Assert.Equal(refundTotal, ownerSummary.GetProperty("cashRefunds").GetDecimal());
            Assert.Equal(exchangeTotal, ownerSummary.GetProperty("netExchangeSettlement").GetDecimal());
            Assert.Equal(collections, ownerSummary.GetProperty("creditCollections").GetDecimal());
            Assert.Equal(100m - refundTotal + exchangeTotal + collections,
                ownerSummary.GetProperty("expectedCash").GetDecimal());

            var cashierSummary = await owner.GetFromJsonAsync<JsonElement>(RegisterSummary(cashierRegisterId));
            var cashierCash = cashDocuments.Where(x => !replacementIds.Contains(x.Id)).Sum(x => x.Total);
            Assert.Equal(cashierCash, cashierSummary.GetProperty("cashSales").GetDecimal());
            Assert.Equal(100m + cashierCash, cashierSummary.GetProperty("expectedCash").GetDecimal());
        }
    }

    [Fact]
    [Trait("Database", "SqlServer")]
    public async Task Corrupt_cross_business_document_lines_are_rejected()
    {
        var connectionString = Environment.GetEnvironmentVariable("SQLSERVER_TEST_CONNECTION");
        if (string.IsNullOrWhiteSpace(connectionString)) return;

        using var factory = new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment("Development");
            builder.UseSetting("RetailReturns:PostingEnabled", "true");
            builder.ConfigureServices(services =>
            {
                services.RemoveAll<DbContextOptions<AppDbContext>>();
                services.RemoveAll<IDbContextOptionsConfiguration<AppDbContext>>();
                services.AddDbContext<AppDbContext>(options => options.UseSqlServer(connectionString));
            });
        });
        using var owner = factory.CreateClient();
        using var other = factory.CreateClient();
        await RegisterAndLogin(owner, "sql-receiving-isolation-owner@example.test");
        await RegisterAndLogin(other, "sql-receiving-isolation-other@example.test");
        var businessId = await BusinessFixtures.SeedAsync(factory.Services, owner,
            "Receiving Isolation", "sql-receiving-isolation-owner");
        var otherId = await BusinessFixtures.SeedAsync(factory.Services, other,
            "Foreign Receiving", "sql-receiving-isolation-other");

        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        async Task<RetailPurchaseOrderLine> CreateOrderLine(long id, string email)
        {
            var supplier = new RetailSupplier { BusinessId = id, Name = "Supplier", NameKey = "SUPPLIER" };
            var product = new RetailProduct
            {
                BusinessId = id, Name = "Item", Sku = "ISOLATION-ITEM", SalePrice = 10m,
                CategoryId = await db.RetailCategories.Where(x => x.BusinessId == id).Select(x => x.Id).SingleAsync(),
                UnitId = await db.RetailUnits.Where(x => x.BusinessId == id).Select(x => x.Id).SingleAsync()
            };
            db.AddRange(supplier, product);
            await db.SaveChangesAsync();
            var order = new RetailPurchaseOrder
            {
                BusinessId = id, SupplierId = supplier.Id, Status = "Approved",
                LocationId = await db.BusinessLocations.Where(x => x.BusinessId == id).Select(x => x.Id).SingleAsync(),
                CreatedByUserId = await db.Users.Where(x => x.Email == email).Select(x => x.Id).SingleAsync()
            };
            db.RetailPurchaseOrders.Add(order);
            await db.SaveChangesAsync();
            var line = new RetailPurchaseOrderLine
            {
                PurchaseOrderId = order.Id, ProductId = product.Id, OrderedQuantity = 5m, UnitCost = 2m
            };
            db.RetailPurchaseOrderLines.Add(line);
            await db.SaveChangesAsync();
            return line;
        }

        var ownLine = await CreateOrderLine(businessId, "sql-receiving-isolation-owner@example.test");
        var foreignLine = await CreateOrderLine(otherId, "sql-receiving-isolation-other@example.test");
        var receipt = new RetailReceiving
        {
            BusinessId = businessId, PurchaseOrderId = ownLine.PurchaseOrderId,
            CreatedByUserId = await db.Users.Where(x => x.Email == "sql-receiving-isolation-owner@example.test")
                .Select(x => x.Id).SingleAsync()
        };
        db.RetailReceivings.Add(receipt);
        await db.SaveChangesAsync();
        var line = new RetailReceivingLine
        {
            ReceivingId = receipt.Id, PurchaseOrderLineId = ownLine.Id, Quantity = 1m
        };
        db.RetailReceivingLines.Add(line);
        await db.SaveChangesAsync();

        var path = $"/api/businesses/{businessId}/purchase-orders/{ownLine.PurchaseOrderId}";
        using var valid = await owner.GetAsync(path);
        Assert.Equal(HttpStatusCode.OK, valid.StatusCode);
        using var denied = await other.GetAsync(path);
        Assert.Equal(HttpStatusCode.NotFound, denied.StatusCode);

        // Simulate pre-existing corrupt data allowed by independent foreign keys.
        line.PurchaseOrderLineId = foreignLine.Id;
        await db.SaveChangesAsync();
        try
        {
            using var invalid = await owner.GetAsync(path);
            Assert.Equal(HttpStatusCode.Conflict, invalid.StatusCode);
            using var post = await Post(owner, path + $"/receivings/{receipt.Id}/post", new { });
            Assert.Equal(HttpStatusCode.Conflict, post.StatusCode);
            Assert.False(await db.RetailStockMovements.AnyAsync(x =>
                x.BusinessId == businessId && x.SourceType == "Receiving" && x.SourceId == line.Id));
        }
        finally
        {
            line.PurchaseOrderLineId = ownLine.Id;
            await db.SaveChangesAsync();
        }

        async Task<RetailCashSale> CreateSale(long tenant, long productId)
        {
            var sale = new RetailCashSale
            {
                BusinessId = tenant,
                LocationId = await db.BusinessLocations.Where(x => x.BusinessId == tenant).Select(x => x.Id).SingleAsync(),
                CashierId = await db.BusinessUsers.Where(x => x.BusinessId == tenant).Select(x => x.UserId).SingleAsync(),
                CurrencyCode = await db.Businesses.Where(x => x.Id == tenant).Select(x => x.CurrencyCode).SingleAsync(),
                RequestKey = "sql-isolation-sale", PostedAtUtc = DateTime.UtcNow, Total = 10m, Tendered = 10m,
                Lines = [new RetailCashSaleLine
                {
                    ProductId = productId, ProductName = "Item", Quantity = 1m, UnitPrice = 10m, LineTotal = 10m
                }]
            };
            db.RetailCashSales.Add(sale);
            await db.SaveChangesAsync();
            return sale;
        }
        var ownSale = await CreateSale(businessId, ownLine.ProductId);
        var foreignSale = await CreateSale(otherId, foreignLine.ProductId);
        var refund = new RetailCashReturn
        {
            BusinessId = businessId, SaleId = ownSale.Id, ActorUserId = ownSale.CashierId,
            RequestKey = "sql-isolation-refund", Reason = "Test refund", Total = 5m, PostedAtUtc = DateTime.UtcNow,
            Lines = [new RetailCashReturnLine
            {
                SaleLineId = ownSale.Lines.Single().Id, Quantity = 0.5m, Amount = 5m
            }]
        };
        db.RetailCashReturns.Add(refund);
        await db.SaveChangesAsync();
        var salePath = $"/api/businesses/{businessId}/cash-sales/business-receipts/{ownSale.Id}";
        var locationQuery = $"?locationId={ownSale.LocationId}";
        using var validHistory = await owner.GetAsync(salePath + "/returns" + locationQuery);
        Assert.Equal(HttpStatusCode.OK, validHistory.StatusCode);
        var refundLine = refund.Lines.Single();
        refundLine.SaleLineId = foreignSale.Lines.Single().Id;
        await db.SaveChangesAsync();
        try
        {
            using var badDetail = await owner.GetAsync(salePath + locationQuery);
            Assert.Equal(HttpStatusCode.Conflict, badDetail.StatusCode);
            using var badHistory = await owner.GetAsync(salePath + "/returns" + locationQuery);
            Assert.Equal(HttpStatusCode.Conflict, badHistory.StatusCode);
            var register = $"/api/businesses/{businessId}/cash-sales";
            Assert.Equal(HttpStatusCode.Conflict, (await owner.GetAsync(register + "/business-returns?locationId=0")).StatusCode);
            Assert.Equal(HttpStatusCode.Conflict, (await owner.GetAsync(register + "/business-receipts?locationId=0")).StatusCode);
            using var estimate = await Post(owner, salePath + "/return-estimate", new
            {
                locationId = ownSale.LocationId,
                lines = new[] { new { lineId = ownSale.Lines.Single().Id, quantity = 0.5m } }
            });
            Assert.Equal(HttpStatusCode.Conflict, estimate.StatusCode);
            using var postRefund = await Post(owner, salePath + "/returns", new
            {
                locationId = ownSale.LocationId, requestKey = "sql-isolation-new-refund",
                reason = "Test refund", expectedRefund = 5m, cashRefundConfirmed = true,
                lines = new[] { new { lineId = ownSale.Lines.Single().Id, quantity = 0.5m, restock = false } }
            });
            Assert.Equal(HttpStatusCode.Conflict, postRefund.StatusCode);
            Assert.Equal(1, await db.RetailCashReturns.CountAsync(x => x.SaleId == ownSale.Id));
        }
        finally
        {
            refundLine.SaleLineId = ownSale.Lines.Single().Id;
            await db.SaveChangesAsync();
        }

        var foreignCustomer = new RetailCustomer
        {
            BusinessId = otherId, Name = "Foreign customer", NameKey = "FOREIGN CUSTOMER"
        };
        db.RetailCustomers.Add(foreignCustomer);
        await db.SaveChangesAsync();
        // A credit document with a foreign customer must not authorize collection.
        ownSale.CustomerId = foreignCustomer.Id;
        ownSale.CreditDueDate = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(7));
        ownSale.Tendered = 0m;
        await db.SaveChangesAsync();
        try
        {
            var paymentPath = $"/api/businesses/{businessId}/customers/{foreignCustomer.Id}/credit-invoices/{ownSale.Id}/payments";
            Assert.Equal(HttpStatusCode.NotFound, (await owner.GetAsync(paymentPath)).StatusCode);
            using var collection = await Post(owner, paymentPath, new
            {
                requestKey = "sql-isolation-credit-collection", amount = 5m,
                expectedOutstanding = 10m, cashReceivedConfirmed = true
            });
            Assert.Equal(HttpStatusCode.NotFound, collection.StatusCode);
            Assert.False(await db.RetailCreditPayments.AnyAsync(x => x.SaleId == ownSale.Id));
        }
        finally
        {
            ownSale.CustomerId = null;
            ownSale.CreditDueDate = null;
            ownSale.Tendered = 10m;
            await db.SaveChangesAsync();
        }
    }

    [Theory]
    [InlineData(5000)]
    [InlineData(50000)]
    [Trait("Database", "SqlServer")]
    public async Task Large_catalog_reads_preserve_order_pagination_and_tenant_isolation(int count)
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
        using var other = factory.CreateClient();
        await RegisterAndLogin(owner, $"sql-catalog-scale-{count}-owner@example.test");
        await RegisterAndLogin(other, $"sql-catalog-scale-{count}-other@example.test");
        var id = await BusinessFixtures.SeedAsync(factory.Services, owner, "Scale Catalog", $"sql-catalog-scale-{count}-20261125");
        var otherId = await BusinessFixtures.SeedAsync(factory.Services, other, "Other Catalog", $"sql-catalog-scale-{count}-other-20261125");
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var categoryId = await db.RetailCategories.Where(x => x.BusinessId == id).Select(x => x.Id).SingleAsync();
            var unitId = await db.RetailUnits.Where(x => x.BusinessId == id).Select(x => x.Id).SingleAsync();
            var otherCategory = await db.RetailCategories.Where(x => x.BusinessId == otherId).Select(x => x.Id).SingleAsync();
            var otherUnit = await db.RetailUnits.Where(x => x.BusinessId == otherId).Select(x => x.Id).SingleAsync();
            // Synthetic test data only; no sales or stock are posted.
            for (var offset = 0; offset < count; offset += 500)
            {
                db.RetailProducts.AddRange(Enumerable.Range(offset + 1, Math.Min(500, count - offset))
                    .Select(n => new RetailProduct
                    {
                        BusinessId = id, CategoryId = categoryId, UnitId = unitId,
                        Name = $"Catalog item {n:D5}", Sku = $"SCALE-{n:D5}",
                        Barcode = $"CAT-{n:D5}", SalePrice = 10m
                    }));
                await db.SaveChangesAsync();
                db.ChangeTracker.Clear();
            }
            db.RetailProducts.Add(new RetailProduct
            {
                BusinessId = otherId, CategoryId = otherCategory, UnitId = otherUnit,
                Name = "Foreign item", Sku = $"SCALE-{count:D5}", Barcode = $"CAT-{count:D5}", SalePrice = 20m
            });
            await db.SaveChangesAsync();
        }
        var path = $"/api/businesses/{id}/products";
        async Task<JsonElement> Read(string label, string filter)
        {
            var started = System.Diagnostics.Stopwatch.StartNew();
            var result = await owner.GetFromJsonAsync<JsonElement>(path + filter);
            output.WriteLine($"{count} products | {label}: {started.Elapsed.TotalMilliseconds:F1} ms");
            return result;
        }

        var first = await Read("Catalog first page", "?page=1");
        Assert.Equal(count, first.GetProperty("total").GetInt32());
        Assert.Equal(20, first.GetProperty("items").GetArrayLength());
        Assert.Equal("SCALE-00001", first.GetProperty("items")[0].GetProperty("sku").GetString());
        var last = await Read("Catalog last page", $"?page={count / 20}");
        Assert.Equal($"SCALE-{count - 19:D5}", last.GetProperty("items")[0].GetProperty("sku").GetString());
        Assert.Equal($"SCALE-{count:D5}", last.GetProperty("items")[19].GetProperty("sku").GetString());
        var beyond = await Read("Catalog beyond last page", $"?page={count / 20 + 1}");
        Assert.Empty(beyond.GetProperty("items").EnumerateArray());
        var exact = await Read("Catalog exact SKU", $"?code=SCALE-{count:D5}&status=active");
        Assert.Equal(1, exact.GetProperty("total").GetInt32());
        Assert.Equal(10m, exact.GetProperty("items")[0].GetProperty("salePrice").GetDecimal());
        var barcode = await Read("Catalog exact barcode", $"/lookup?barcode=CAT-{count:D5}");
        Assert.Equal($"SCALE-{count:D5}", barcode.GetProperty("sku").GetString());
        var substring = await Read("Catalog substring", $"?search=item%20{count:D5}");
        Assert.Equal(1, substring.GetProperty("total").GetInt32());
        Assert.Equal($"SCALE-{count:D5}", substring.GetProperty("items")[0].GetProperty("sku").GetString());
        var empty = await Read("Catalog unmatched search", "?search=NO-SUCH-SCALE-ITEM");
        Assert.Equal(0, empty.GetProperty("total").GetInt32());
        Assert.Empty(empty.GetProperty("items").EnumerateArray());
        Assert.Equal(HttpStatusCode.NotFound, (await other.GetAsync(path)).StatusCode);
        var foreign = await other.GetFromJsonAsync<JsonElement>($"/api/businesses/{otherId}/products?code=SCALE-{count:D5}");
        Assert.Equal(1, foreign.GetProperty("total").GetInt32());
        Assert.Equal(20m, foreign.GetProperty("items")[0].GetProperty("salePrice").GetDecimal());
    }

    [Theory]
    [InlineData(10000)]
    [InlineData(100000)]
    [Trait("Database", "SqlServer")]
    public async Task Large_invoice_register_preserves_totals_dates_and_return_status(int count)
    {
        var connectionString = Environment.GetEnvironmentVariable("SQLSERVER_TEST_CONNECTION");
        if (string.IsNullOrWhiteSpace(connectionString)) return;

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
        await RegisterAndLogin(owner, $"sql-invoice-scale-{count}-owner@example.test");
        await RegisterAndLogin(stranger, $"sql-invoice-scale-{count}-stranger@example.test");
        var id = await BusinessFixtures.SeedAsync(factory.Services, owner, $"Scale Invoices {count}", $"sql-invoice-scale-{count}-20261125");
        var firstDay = new DateTime(2026, 11, 1, 6, 0, 0, DateTimeKind.Utc);
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var actor = await db.BusinessUsers.Where(x => x.BusinessId == id).Select(x => x.UserId).SingleAsync();
            var location = await db.BusinessLocations.Where(x => x.BusinessId == id).Select(x => x.Id).SingleAsync();
            var category = await db.RetailCategories.Where(x => x.BusinessId == id).Select(x => x.Id).SingleAsync();
            var unit = await db.RetailUnits.Where(x => x.BusinessId == id).Select(x => x.Id).SingleAsync();
            var product = new RetailProduct
            {
                BusinessId = id, CategoryId = category, UnitId = unit,
                Name = "Scale invoice product", Sku = "SCALE-INVOICE", SalePrice = 10m
            };
            db.RetailProducts.Add(product);
            await db.SaveChangesAsync();
            var productId = product.Id;
            // Read-path fixture only: historical synthetic documents, no inventory or ledger writes.
            for (var offset = 0; offset < count; offset += 500)
            {
                db.RetailCashSales.AddRange(Enumerable.Range(offset, 500).Select(n => new RetailCashSale
                {
                    BusinessId = id, LocationId = location, CashierId = actor,
                    RequestKey = $"scale-invoice-{n:D5}", CurrencyCode = "INR",
                    Total = 10m, Tendered = 10m,
                    PostedAtUtc = firstDay.AddDays(n < count / 2 ? 0 : 1),
                    Lines = [new RetailCashSaleLine
                    {
                        ProductId = productId, ProductName = "Scale invoice product",
                        Quantity = 1m, UnitPrice = 10m, LineTotal = 10m
                    }]
                }));
                await db.SaveChangesAsync();
                db.ChangeTracker.Clear();
            }
            var originals = await db.RetailCashSales.AsNoTracking()
                .Where(x => x.BusinessId == id).OrderBy(x => x.Id).Take(2)
                .Include(x => x.Lines).ToListAsync();
            for (var i = 0; i < originals.Count; i++)
            {
                var quantity = i == 0 ? 0.5m : 1m;
                db.RetailCashReturns.Add(new RetailCashReturn
                {
                    BusinessId = id, SaleId = originals[i].Id, ActorUserId = actor,
                    RequestKey = $"scale-refund-{i}", Reason = "Synthetic report fixture",
                    PostedAtUtc = firstDay.AddDays(2), Total = quantity * 10m,
                    Lines = [new RetailCashReturnLine
                    {
                        SaleLineId = originals[i].Lines.Single().Id,
                        Quantity = quantity, Amount = quantity * 10m
                    }]
                });
            }
            await db.SaveChangesAsync();
        }

        var path = $"/api/businesses/{id}/cash-sales/business-receipts?locationId=0&payment=All";
        async Task<JsonElement> Read(string label, string filter)
        {
            var started = System.Diagnostics.Stopwatch.StartNew();
            var result = await owner.GetFromJsonAsync<JsonElement>(path + filter);
            output.WriteLine($"{count} invoices | {label}: {started.Elapsed.TotalMilliseconds:F1} ms");
            return result;
        }
        var first = await Read("All invoices first page", "&page=1");
        Assert.Equal(count, first.GetProperty("total").GetInt32());
        Assert.Equal(count * 10m, first.GetProperty("totals")[0].GetProperty("cash").GetDecimal());
        Assert.Equal(0m, first.GetProperty("totals")[0].GetProperty("credit").GetDecimal());
        Assert.Equal(10, first.GetProperty("items").GetArrayLength());
        var last = await Read("All invoices last page", $"&page={count / 10}");
        Assert.Equal(count, last.GetProperty("total").GetInt32());
        Assert.Equal(10, last.GetProperty("items").GetArrayLength());
        Assert.True(first.GetProperty("items")[0].GetProperty("id").GetInt64() >
            last.GetProperty("items")[0].GetProperty("id").GetInt64());
        Assert.False(last.GetProperty("hasMore").GetBoolean());
        Assert.Equal(first.GetProperty("totals").GetRawText(), last.GetProperty("totals").GetRawText());
        var date = await Read("One day invoices", "&fromDate=2026-11-01&toDate=2026-11-01");
        Assert.Equal(count / 2, date.GetProperty("total").GetInt32());
        Assert.Equal(count / 2 * 10m, date.GetProperty("totals")[0].GetProperty("cash").GetDecimal());
        var partial = await Read("Partial return filter", "&status=Partial%20return");
        Assert.Equal(1, partial.GetProperty("total").GetInt32());
        Assert.Equal("Partial return", partial.GetProperty("items")[0].GetProperty("status").GetString());
        var returned = await Read("Returned filter", "&status=Returned");
        Assert.Equal(1, returned.GetProperty("total").GetInt32());
        Assert.Equal("Returned", returned.GetProperty("items")[0].GetProperty("status").GetString());
        var posted = await Read("Posted filter", "&status=Posted");
        Assert.Equal(count - 2, posted.GetProperty("total").GetInt32());
        Assert.Equal(HttpStatusCode.NotFound, (await stranger.GetAsync(path)).StatusCode);
    }

    [Fact]
    [Trait("Database", "SqlServer")]
    public async Task Large_customer_outstanding_report_preserves_aging_and_pagination()
    {
        var connectionString = Environment.GetEnvironmentVariable("SQLSERVER_TEST_CONNECTION");
        if (string.IsNullOrWhiteSpace(connectionString)) return;

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
        await RegisterAndLogin(owner, "sql-credit-scale-owner@example.test");
        await RegisterAndLogin(stranger, "sql-credit-scale-stranger@example.test");
        var id = await BusinessFixtures.SeedAsync(factory.Services, owner, "Scale Credit", "sql-credit-scale-20261125");

        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var actor = await db.BusinessUsers.Where(x => x.BusinessId == id).Select(x => x.UserId).SingleAsync();
            var location = await db.BusinessLocations.Where(x => x.BusinessId == id).Select(x => x.Id).SingleAsync();
            var timeZone = await db.Businesses.Where(x => x.Id == id).Select(x => x.TimeZoneId).SingleAsync();
            var today = DateOnly.FromDateTime(TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow,
                TimeZoneInfo.FindSystemTimeZoneById(timeZone)));
            var dueDates = new[] { today.AddDays(10), today.AddDays(-15), today.AddDays(-45),
                today.AddDays(-75), today.AddDays(-105) };
            var category = await db.RetailCategories.Where(x => x.BusinessId == id).Select(x => x.Id).SingleAsync();
            var unit = await db.RetailUnits.Where(x => x.BusinessId == id).Select(x => x.Id).SingleAsync();
            var product = new RetailProduct
            {
                BusinessId = id, CategoryId = category, UnitId = unit,
                Name = "Scale credit product", Sku = "SCALE-CREDIT", SalePrice = 10m
            };
            var customers = Enumerable.Range(0, 100).Select(n => new RetailCustomer
            {
                BusinessId = id, Name = $"Scale Customer {n:D3}", NameKey = $"SCALE CUSTOMER {n:D3}",
                IsActive = n != 0
            }).ToArray();
            db.RetailProducts.Add(product);
            db.RetailCustomers.AddRange(customers);
            await db.SaveChangesAsync();
            var productId = product.Id;
            var postedAt = DateTime.UtcNow.AddDays(-120);

            // Historical read-path fixtures, not a checkout or collection throughput test.
            for (var offset = 0; offset < customers.Length; offset += 20)
            {
                var sales = customers.Skip(offset).Take(20).SelectMany(customer =>
                    Enumerable.Range(0, 10).Select(n => new RetailCashSale
                    {
                        BusinessId = id, LocationId = location, CashierId = actor,
                        CustomerId = customer.Id, CustomerName = customer.Name,
                        RequestKey = $"credit-scale-{customer.Id}-{n}", CurrencyCode = "INR",
                        CreditDueDate = dueDates[n % 5], Total = 10m, Tendered = 0m,
                        PostedAtUtc = postedAt,
                        Lines = [new RetailCashSaleLine
                        {
                            ProductId = productId, ProductName = product.Name,
                            Quantity = 1m, UnitPrice = 10m, LineTotal = 10m
                        }]
                    })).ToArray();
                db.RetailCashSales.AddRange(sales);
                await db.SaveChangesAsync();
                for (var i = 0; i < sales.Length; i++)
                {
                    var sale = sales[i];
                    // Five partially paid and five settled invoices per customer.
                    var amounts = i % 10 < 5 ? new[] { 1m, 3m } : new[] { 10m };
                    db.RetailCreditPayments.AddRange(amounts.Select((amount, n) => new RetailCreditPayment
                    {
                        BusinessId = id, SaleId = sale.Id, ActorUserId = actor,
                        Amount = amount, RequestKey = $"credit-scale-payment-{sale.Id}-{n}",
                        PostedAtUtc = postedAt.AddDays(1)
                    }));
                }
                await db.SaveChangesAsync();
                db.ChangeTracker.Clear();
            }
        }

        var path = $"/api/businesses/{id}/customers/outstanding-report";
        async Task<JsonElement> Read(string label, string filter)
        {
            var started = System.Diagnostics.Stopwatch.StartNew();
            var result = await owner.GetFromJsonAsync<JsonElement>(path + filter);
            output.WriteLine($"{label}: {started.Elapsed.TotalMilliseconds:F1} ms");
            return result;
        }

        var first = await Read("Credit outstanding first page", "?page=1");
        var totals = first.GetProperty("totals")[0];
        Assert.Equal(3000m, totals.GetProperty("outstanding").GetDecimal());
        Assert.Equal(600m, totals.GetProperty("notDue").GetDecimal());
        Assert.Equal(2400m, totals.GetProperty("overdue").GetDecimal());
        foreach (var bucket in new[] { "days1To30", "days31To60", "days61To90", "daysOver90" })
            Assert.Equal(600m, totals.GetProperty(bucket).GetDecimal());
        Assert.Equal(500, totals.GetProperty("invoiceCount").GetInt32());
        Assert.Equal(20, first.GetProperty("items").GetArrayLength());
        Assert.True(first.GetProperty("hasMore").GetBoolean());

        var last = await Read("Credit outstanding last page", "?page=5");
        Assert.Equal(20, last.GetProperty("items").GetArrayLength());
        Assert.False(last.GetProperty("hasMore").GetBoolean());
        Assert.Equal(first.GetProperty("totals").GetRawText(), last.GetProperty("totals").GetRawText());
        Assert.True(first.GetProperty("items")[19].GetProperty("customerId").GetInt64() <
            last.GetProperty("items")[0].GetProperty("customerId").GetInt64());

        var filtered = await Read("Credit customer search", "?search=Scale%20Customer%20000");
        var item = Assert.Single(filtered.GetProperty("items").EnumerateArray());
        Assert.False(item.GetProperty("isActive").GetBoolean());
        Assert.Equal(30m, item.GetProperty("outstanding").GetDecimal());
        Assert.Equal(5, item.GetProperty("invoiceCount").GetInt32());
        Assert.Equal(30m, filtered.GetProperty("totals")[0].GetProperty("outstanding").GetDecimal());

        var overdue = await Read("Credit overdue only", "?overdueOnly=true");
        Assert.Equal(2400m, overdue.GetProperty("totals")[0].GetProperty("outstanding").GetDecimal());
        Assert.Equal(0m, overdue.GetProperty("totals")[0].GetProperty("notDue").GetDecimal());
        Assert.Equal(400, overdue.GetProperty("totals")[0].GetProperty("invoiceCount").GetInt32());

        var empty = await Read("Credit unmatched search", "?search=NO-SUCH-SCALE-CUSTOMER");
        Assert.Empty(empty.GetProperty("items").EnumerateArray());
        Assert.Empty(empty.GetProperty("totals").EnumerateArray());
        Assert.Equal(HttpStatusCode.NotFound, (await stranger.GetAsync(path)).StatusCode);
    }

    [Fact]
    [Trait("Database", "SqlServer")]
    public async Task Cash_register_opening_is_scoped_idempotent_and_concurrent()
    {
        var connectionString = Environment.GetEnvironmentVariable("SQLSERVER_TEST_CONNECTION");
        if (string.IsNullOrWhiteSpace(connectionString)) return;

        using var factory = new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment("Development");
            builder.UseSetting("RetailCashRegister:OpeningEnabled", "true");
            builder.UseSetting("RetailCashRegister:LinkingEnabled", "true");
            builder.UseSetting("RetailCashRegister:MovementsEnabled", "true");
            builder.ConfigureServices(services =>
            {
                services.RemoveAll<DbContextOptions<AppDbContext>>();
                services.RemoveAll<IDbContextOptionsConfiguration<AppDbContext>>();
                services.AddDbContext<AppDbContext>(options => options.UseSqlServer(connectionString));
            });
        });
        using var owner = factory.CreateClient();
        using var secondOwner = factory.CreateClient();
        using var cashier = factory.CreateClient();
        using var manager = factory.CreateClient();
        using var stranger = factory.CreateClient();
        await RegisterAndLogin(owner, "sql-register-owner@example.test");
        await RegisterAndLogin(cashier, "sql-register-cashier@example.test");
        await RegisterAndLogin(manager, "sql-register-manager@example.test");
        await RegisterAndLogin(stranger, "sql-register-stranger@example.test");
        Assert.Equal(HttpStatusCode.NoContent, (await Post(secondOwner, "/api/auth/login",
            new { email = "sql-register-owner@example.test", password = "TestPassword123!" })).StatusCode);
        var businessId = await BusinessFixtures.SeedAsync(factory.Services, owner, "Register Test", "sql-register-test");
        var otherBusiness = await BusinessFixtures.SeedAsync(factory.Services, stranger, "Foreign Register", "sql-register-foreign");
        long locationId, foreignLocationId;
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            locationId = await db.BusinessLocations.Where(x => x.BusinessId == businessId).Select(x => x.Id).SingleAsync();
            foreignLocationId = await db.BusinessLocations.Where(x => x.BusinessId == otherBusiness).Select(x => x.Id).SingleAsync();
            foreach (var (email, role) in new[] { ("sql-register-cashier@example.test", "Cashier"),
                ("sql-register-manager@example.test", "Manager") })
                db.BusinessUsers.Add(new BusinessUser { BusinessId = businessId, RoleCode = role,
                    UserId = await db.Users.Where(x => x.Email == email).Select(x => x.Id).SingleAsync() });
            await db.SaveChangesAsync();
        }
        var path = $"/api/businesses/{businessId}/cash-register";
        var current = path + "/current?locationId=" + locationId;
        var opening = new { locationId, openingCash = 2000m, requestKey = "sql-register-opening-owner" };
        Assert.True((await owner.GetFromJsonAsync<JsonElement>(current)).GetProperty("canOpen").GetBoolean());
        Assert.Equal(HttpStatusCode.NotFound, (await stranger.GetAsync(current)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await manager.GetAsync(current)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await Post(owner, path + "/open",
            opening with { locationId = foreignLocationId })).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await Post(owner, path + "/open",
            opening with { openingCash = -1m })).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await Post(owner, path + "/open",
            opening with { openingCash = 0.001m })).StatusCode);
        var competing = opening with { requestKey = "sql-register-opening-competing" };
        var results = await Task.WhenAll(Post(owner, path + "/open", opening), Post(secondOwner, path + "/open", competing));
        using var first = results[0];
        using var second = results[1];
        foreach (var response in results)
            Assert.True(response.StatusCode is HttpStatusCode.OK or HttpStatusCode.Conflict,
                await response.Content.ReadAsStringAsync());
        Assert.True(results.Count(x => x.StatusCode == HttpStatusCode.OK) <= 1);
        var winner = second.StatusCode == HttpStatusCode.OK ? competing : opening;
        using var replay = await Post(owner, path + "/open", winner);
        Assert.Equal(HttpStatusCode.OK, replay.StatusCode);
        var opened = await replay.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(2000m, opened.GetProperty("openingCash").GetDecimal());
        Assert.Equal("INR", opened.GetProperty("currencyCode").GetString());
        Assert.Equal(HttpStatusCode.Conflict, (await Post(owner, path + "/open",
            winner with { openingCash = 3000m })).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await Post(owner, path + "/open",
            winner with { requestKey = "sql-register-opening-extra" })).StatusCode);
        var currentOwner = await owner.GetFromJsonAsync<JsonElement>(current);
        Assert.False(currentOwner.GetProperty("canOpen").GetBoolean());
        Assert.Equal(opened.GetProperty("id").GetInt64(), currentOwner.GetProperty("session").GetProperty("id").GetInt64());
        Assert.Equal(JsonValueKind.Null, (await cashier.GetFromJsonAsync<JsonElement>(current)).GetProperty("session").ValueKind);
        Assert.Equal(HttpStatusCode.Conflict, (await Post(cashier, path + "/open", winner)).StatusCode);
        using var cashierOpen = await Post(cashier, path + "/open",
            opening with { requestKey = "sql-register-opening-cashier", openingCash = 0m });
        Assert.Equal(HttpStatusCode.OK, cashierOpen.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await Post(manager, path + "/open", opening)).StatusCode);

        var registerId = opened.GetProperty("id").GetInt64();
        var movementPath = path + "/" + registerId + "/movements";
        var movement = new { locationId, isCashIn = true, amount = 500m, reason = "Add change float",
            requestKey = "sql-register-movement-cash-in" };
        Assert.Equal(HttpStatusCode.NotFound, (await Post(cashier, movementPath, movement)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await Post(stranger, movementPath, movement)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await Post(manager, movementPath, movement)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await Post(owner, movementPath,
            movement with { locationId = foreignLocationId })).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await Post(owner, movementPath, movement with { amount = 0m })).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await Post(owner, movementPath, movement with { amount = 0.001m })).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await Post(owner, movementPath, movement with { reason = "" })).StatusCode);
        var cashInResponses = await Task.WhenAll(Post(owner, movementPath, movement), Post(secondOwner, movementPath, movement));
        foreach (var response in cashInResponses)
        {
            using (response)
                Assert.True(response.StatusCode is HttpStatusCode.OK or HttpStatusCode.Conflict,
                    await response.Content.ReadAsStringAsync());
        }
        using var movementReplay = await Post(owner, movementPath, movement);
        Assert.Equal(HttpStatusCode.OK, movementReplay.StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await Post(owner, movementPath, movement with { amount = 600m })).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await Post(owner, movementPath, movement with {
            isCashIn = false, amount = 125m, reason = "Cash transfer out", requestKey = "sql-register-movement-cash-out"
        })).StatusCode);
        var summary = await owner.GetFromJsonAsync<JsonElement>(path + "/" + registerId + "/summary?locationId=" + locationId);
        Assert.Equal(500m, summary.GetProperty("cashIn").GetDecimal());
        Assert.Equal(125m, summary.GetProperty("cashOut").GetDecimal());
        Assert.Equal(2375m, summary.GetProperty("expectedCash").GetDecimal());

        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            Assert.Equal(2, await db.RetailCashRegisterSessions.CountAsync(x => x.BusinessId == businessId));
            Assert.Equal(2, await db.RetailCashRegisterSessions.Where(x => x.BusinessId == businessId)
                .Select(x => x.ActorUserId).Distinct().CountAsync());
            Assert.False(await db.RetailCashSales.AnyAsync(x => x.BusinessId == businessId));
            Assert.Equal(2, await db.RetailCashRegisterMovements.CountAsync(x => x.RegisterSessionId == registerId));
        }
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
