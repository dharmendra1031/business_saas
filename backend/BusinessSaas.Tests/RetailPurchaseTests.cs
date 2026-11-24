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

        var businessId = await BusinessFixtures.SeedAsync(factory.Services, owner,
            "Purchase Shop", "purchase-shop-20261113");
        var otherBusinessId = await BusinessFixtures.SeedAsync(factory.Services, other,
            "Another Purchase Shop", "purchase-other-20261113");

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
            locationId, supplierId, requestKey = "purchase-access-check-20261117",
            lines = new[] { new { productId, quantity = 100m, unitCost = 80m } }
        };
        Assert.Equal(HttpStatusCode.BadRequest,
            (await Send(owner, orders, new
            {
                locationId, supplierId,
                lines = new[] { new { productId, quantity = 100m, unitCost = 80m } }
            })).StatusCode);

        Assert.Equal(HttpStatusCode.NotFound,
            (await Send(other, orders, valid)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound,
            (await Send(cashier, orders, valid)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound,
            (await Send(owner, orders, new
            {
                locationId, supplierId = otherSupplierId,
                requestKey = "purchase-foreign-supplier-20261117",
                lines = new[] { new { productId, quantity = 100m, unitCost = 80m } }
            })).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest,
            (await Send(owner, orders, new
            {
                locationId, supplierId,
                lines = new[] { new { productId, quantity = -1m, unitCost = 80m } }
            })).StatusCode);

        var keyedOrder = new
        {
            locationId, supplierId, requestKey = "purchase-order-retry-20261116",
            lines = new[] { new { productId, quantity = 100m, unitCost = 80m } }
        };
        var orderId = await CreateId(manager, orders, keyedOrder);
        var retryOrder = await Send(manager, orders, keyedOrder);
        Assert.Equal(HttpStatusCode.OK, retryOrder.StatusCode);
        Assert.Equal(orderId, (await retryOrder.Content.ReadFromJsonAsync<JsonElement>())
            .GetProperty("id").GetInt64());
        Assert.Equal(HttpStatusCode.Conflict,
            (await Send(manager, orders, new
            {
                locationId, supplierId, requestKey = keyedOrder.requestKey,
                lines = new[] { new { productId, quantity = 99m, unitCost = 80m } }
            })).StatusCode);

        var orderPath = orders + "/" + orderId;

        Assert.Equal(HttpStatusCode.NotFound, (await other.GetAsync(orderPath)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound,
            (await Send(manager, orderPath + "/approve", new { })).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict,
            (await Send(manager, orderPath + "/receivings",
                new
                {
                    requestKey = "purchase-before-approval-20261117",
                    lines = new[] { new { purchaseOrderLineId = 1, quantity = 40m } }
                })).StatusCode);

        var before = await manager.GetFromJsonAsync<JsonElement>(orderPath);
        var orderLineId = before.GetProperty("lines")[0].GetProperty("id").GetInt64();
        Assert.Equal("Draft", before.GetProperty("status").GetString());
        Assert.Equal("PCS", before.GetProperty("lines")[0].GetProperty("unitCode").GetString());

        Assert.Equal(HttpStatusCode.OK,
            (await Send(owner, orderPath + "/approve", new { })).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict,
            (await Send(owner, orderPath + "/approve", new { })).StatusCode);

        var receipts = orderPath + "/receivings";
        Assert.Equal(HttpStatusCode.BadRequest,
            (await Send(manager, receipts, new
            {
                lines = new[] { new { purchaseOrderLineId = orderLineId, quantity = 40m } }
            })).StatusCode);
        var keyedReceipt40 = new
        {
            requestKey = "purchase-receiving-retry-20261116",
            lines = new[] { new { purchaseOrderLineId = orderLineId, quantity = 40m } }
        };
        var receipt40 = await CreateId(manager, receipts, keyedReceipt40);
        Assert.Equal(HttpStatusCode.NotFound,
            (await Send(other, receipts, keyedReceipt40)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound,
            (await Send(cashier, receipts, keyedReceipt40)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound,
            (await Send(other, receipts + "/" + receipt40 + "/post", new { })).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound,
            (await Send(cashier, receipts + "/" + receipt40 + "/post", new { })).StatusCode);
        var retryReceiving = await Send(manager, receipts, keyedReceipt40);
        Assert.Equal(HttpStatusCode.OK, retryReceiving.StatusCode);
        Assert.Equal(receipt40, (await retryReceiving.Content.ReadFromJsonAsync<JsonElement>())
            .GetProperty("id").GetInt64());
        Assert.Equal(HttpStatusCode.Conflict,
            (await Send(manager, receipts, new
            {
                requestKey = keyedReceipt40.requestKey,
                lines = new[] { new { purchaseOrderLineId = orderLineId, quantity = 41m } }
            })).StatusCode);

        var beforePost = await owner.GetFromJsonAsync<JsonElement>(orderPath);
        var draftLine = beforePost.GetProperty("receivings")[0].GetProperty("lines")[0];
        Assert.Equal(orderLineId, draftLine.GetProperty("purchaseOrderLineId").GetInt64());
        Assert.Equal(40m, draftLine.GetProperty("quantity").GetDecimal());
        Assert.Equal(0m, beforePost.GetProperty("lines")[0]
            .GetProperty("receivedQuantity").GetDecimal());

        Assert.Equal(HttpStatusCode.OK,
            (await Send(manager, receipts + "/" + receipt40 + "/post", new { })).StatusCode);
        Assert.Equal(HttpStatusCode.OK,
            (await Send(owner, receipts + "/" + receipt40 + "/post", new { })).StatusCode);
        Assert.Equal(HttpStatusCode.OK,
            (await Send(manager, receipts, keyedReceipt40)).StatusCode);

        var partial = await owner.GetFromJsonAsync<JsonElement>(orderPath);
        Assert.Equal("Approved", partial.GetProperty("status").GetString());
        Assert.Equal(40m, partial.GetProperty("lines")[0].GetProperty("receivedQuantity").GetDecimal());
        Assert.Equal(60m, partial.GetProperty("lines")[0].GetProperty("remainingQuantity").GetDecimal());

        Assert.Equal(HttpStatusCode.Conflict,
            (await Send(owner, receipts,
                new
                {
                    requestKey = "purchase-over-receiving-20261117",
                    lines = new[] { new { purchaseOrderLineId = orderLineId, quantity = 70m } }
                })).StatusCode);

        var receipt60 = await CreateId(owner, receipts,
            new
            {
                requestKey = "purchase-final-receiving-20261117",
                lines = new[] { new { purchaseOrderLineId = orderLineId, quantity = 60m } }
            });
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
                new
                {
                    requestKey = "purchase-after-close-20261117",
                    lines = new[] { new { purchaseOrderLineId = orderLineId, quantity = 1m } }
                })).StatusCode);

        var stockPage = await owner.GetFromJsonAsync<JsonElement>(
            $"/api/businesses/{businessId}/stock/locations/{locationId}");
        Assert.Equal(100m, stockPage.GetProperty("items")[0].GetProperty("quantity").GetDecimal());
        Assert.Equal("PCS", stockPage.GetProperty("items")[0].GetProperty("unitCode").GetString());

        // The inventory screen must show the same two posted receivings as the
        // database ledger, without exposing them across business or cashier roles.
        var movementsUrl =
            $"/api/businesses/{businessId}/stock/locations/{locationId}/movements?productId={productId}";
        var postedMovements = await owner.GetFromJsonAsync<JsonElement>(movementsUrl);
        Assert.Equal(2, postedMovements.GetProperty("total").GetInt32());
        var movementRows = postedMovements.GetProperty("items");
        Assert.Equal(2, movementRows.GetArrayLength());
        Assert.All(movementRows.EnumerateArray(), row =>
            Assert.Equal("PCS", row.GetProperty("unitCode").GetString()));
        Assert.All(movementRows.EnumerateArray(), row =>
        {
            Assert.Equal("Receiving", row.GetProperty("sourceType").GetString());
            Assert.True(row.GetProperty("sourceId").GetInt64() > 0);
        });
        Assert.Equal(60m, movementRows[0].GetProperty("quantityChange").GetDecimal());
        Assert.Equal(100m, movementRows[0].GetProperty("balanceAfter").GetDecimal());
        Assert.Equal(40m, movementRows[1].GetProperty("quantityChange").GetDecimal());
        Assert.Equal(40m, movementRows[1].GetProperty("balanceAfter").GetDecimal());
        Assert.Equal(HttpStatusCode.OK, (await manager.GetAsync(movementsUrl)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await cashier.GetAsync(movementsUrl)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await other.GetAsync(movementsUrl)).StatusCode);

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

        var sales = $"/api/businesses/{businessId}/cash-sales";
        var cashSale = new { locationId, productId, quantity = 3, expectedUnitPrice = 150m,
            tendered = 500m, requestKey = "cash-receipt-retry-001" };
        Assert.Equal(HttpStatusCode.NotFound, (await Send(other, sales, cashSale)).StatusCode);
        var lookup = await cashier.GetFromJsonAsync<JsonElement>($"{sales}/products?locationId={locationId}&search=RICE");
        Assert.Equal(productId, lookup[0].GetProperty("id").GetInt64());
        var postedSale = await Send(cashier, sales, cashSale);
        Assert.Equal(HttpStatusCode.OK, postedSale.StatusCode);
        var receipt = await postedSale.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(450m, receipt.GetProperty("total").GetDecimal());
        var receiptByIdPath = $"{sales}/receipts/{receipt.GetProperty("id").GetInt64()}?locationId={locationId}";
        var savedReceipt = await cashier.GetFromJsonAsync<JsonElement>(receiptByIdPath);
        Assert.Equal(receipt.GetProperty("id").GetInt64(), savedReceipt.GetProperty("id").GetInt64());
        Assert.Equal(HttpStatusCode.NotFound, (await owner.GetAsync(receiptByIdPath)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await other.GetAsync(receiptByIdPath)).StatusCode);
        var historyPath = $"{sales}/receipts?locationId={locationId}";
        var cashierHistory = await cashier.GetFromJsonAsync<JsonElement>(historyPath);
        Assert.Equal(receipt.GetProperty("id").GetInt64(),
            cashierHistory.GetProperty("items")[0].GetProperty("id").GetInt64());
        Assert.False(cashierHistory.GetProperty("hasMore").GetBoolean());
        var ownerHistory = await owner.GetFromJsonAsync<JsonElement>(historyPath);
        Assert.Empty(ownerHistory.GetProperty("items").EnumerateArray());
        Assert.Equal(HttpStatusCode.NotFound, (await other.GetAsync(historyPath)).StatusCode);
        var businessHistoryPath = $"{sales}/business-receipts?locationId={locationId}";
        var businessHistory = await owner.GetFromJsonAsync<JsonElement>(
            $"{businessHistoryPath}&receiptId={receipt.GetProperty("id").GetInt64()}");
        Assert.Equal(receipt.GetProperty("id").GetInt64(),
            businessHistory.GetProperty("items")[0].GetProperty("id").GetInt64());
        Assert.Equal(HttpStatusCode.NotFound, (await cashier.GetAsync(businessHistoryPath)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await other.GetAsync(businessHistoryPath)).StatusCode);
        var businessReceiptPath = $"{sales}/business-receipts/{receipt.GetProperty("id").GetInt64()}?locationId={locationId}";
        var businessReceipt = await owner.GetFromJsonAsync<JsonElement>(businessReceiptPath);
        Assert.Equal(450m, businessReceipt.GetProperty("total").GetDecimal());
        Assert.Equal(DateTimeKind.Utc, businessReceipt.GetProperty("postedAtUtc").GetDateTime().Kind);
        var localSaleDate = businessReceipt.GetProperty("postedAtUtc").GetDateTime().AddMinutes(330).Date;
        var datedHistory = await owner.GetFromJsonAsync<JsonElement>(
            $"{businessHistoryPath}&postedDate={localSaleDate:yyyy-MM-dd}");
        Assert.Single(datedHistory.GetProperty("items").EnumerateArray());
        var previousDate = await owner.GetFromJsonAsync<JsonElement>(
            $"{businessHistoryPath}&postedDate={localSaleDate.AddDays(-1):yyyy-MM-dd}");
        Assert.Empty(previousDate.GetProperty("items").EnumerateArray());
        var reportPath = $"{sales}/daily-report?locationId={locationId}&date={localSaleDate:yyyy-MM-dd}";
        var report = await owner.GetFromJsonAsync<JsonElement>(reportPath);
        Assert.Equal(1, report.GetProperty("totals")[0].GetProperty("receiptCount").GetInt32());
        Assert.Equal(450m, report.GetProperty("totals")[0].GetProperty("salesTotal").GetDecimal());
        Assert.Equal(HttpStatusCode.NotFound, (await cashier.GetAsync(reportPath)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await other.GetAsync(reportPath)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await cashier.GetAsync(businessReceiptPath)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await other.GetAsync(businessReceiptPath)).StatusCode);
        var replay = await Send(cashier, sales, cashSale);
        Assert.Equal(HttpStatusCode.OK, replay.StatusCode);
        Assert.Equal(receipt.GetProperty("id").GetInt64(),
            (await replay.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetInt64());
        Assert.Equal(HttpStatusCode.Conflict, (await Send(cashier, sales,
            cashSale with { quantity = 2 })).StatusCode);
        var insufficientStock = await Send(cashier, sales,
            cashSale with { requestKey = "cash-out-of-stock-001", quantity = 98, tendered = 15000m });
        Assert.Equal(HttpStatusCode.BadRequest, insufficientStock.StatusCode);
        var stockReview = await insufficientStock.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("CASH_SALE_BASKET_CHANGED", stockReview.GetProperty("code").GetString());
        Assert.Equal(productId, stockReview.GetProperty("productId").GetInt64());
        // A replayed receipt does not create a second cash movement.
        var afterSaleMovements = await owner.GetFromJsonAsync<JsonElement>(movementsUrl);
        Assert.Equal(3, afterSaleMovements.GetProperty("total").GetInt32());
        var cashMovement = afterSaleMovements.GetProperty("items")[0];
        Assert.Equal("CashSaleLine", cashMovement.GetProperty("sourceType").GetString());
        Assert.True(cashMovement.GetProperty("sourceId").GetInt64() > 0);
        Assert.Equal(-3m, cashMovement.GetProperty("quantityChange").GetDecimal());
        Assert.Equal(97m, cashMovement.GetProperty("balanceAfter").GetDecimal());

        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            Assert.Single(await db.RetailCashSales.Where(x => x.BusinessId == businessId).ToListAsync());
            Assert.Equal(97m, await db.RetailStockBalances.Where(x => x.BusinessId == businessId &&
                x.LocationId == locationId && x.ProductId == productId).Select(x => x.Quantity).SingleAsync());
            Assert.Equal(1, await db.RetailStockMovements.CountAsync(x =>
                x.BusinessId == businessId && x.SourceType == "CashSaleLine"));
        }

        var oilId = await CreateId(owner, $"/api/businesses/{businessId}/products", new
        {
            name = "Cooking oil", sku = "OIL-01", categoryId, unitId, salePrice = 40m, taxClassification = ""
        });
        var basket = new { locationId, requestKey = "cash-two-products-001", tendered = 400m,
            lines = new[] { new { productId, quantity = 2, expectedUnitPrice = 150m },
                            new { productId = oilId, quantity = 1, expectedUnitPrice = 40m } } };
        Assert.Equal(HttpStatusCode.BadRequest, (await Send(cashier, sales, basket)).StatusCode);
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            Assert.Equal(97m, await db.RetailStockBalances.Where(x => x.BusinessId == businessId &&
                x.ProductId == productId).Select(x => x.Quantity).SingleAsync());
            Assert.False(await db.RetailCashSales.AnyAsync(x => x.RequestKey == basket.requestKey));
        }
        var opening = await Send(owner, $"/api/businesses/{businessId}/stock/locations/{locationId}/opening",
            new { productId = oilId, quantity = 10m });
        opening.EnsureSuccessStatusCode();
        var basketResult = await Send(cashier, sales, basket);
        basketResult.EnsureSuccessStatusCode();
        var basketReceipt = await basketResult.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(340m, basketReceipt.GetProperty("total").GetDecimal());
        Assert.Equal(2, basketReceipt.GetProperty("lines").GetArrayLength());
        (await Send(cashier, sales, basket)).EnsureSuccessStatusCode();
        var last = await cashier.GetFromJsonAsync<JsonElement>($"{sales}/latest?locationId={locationId}");
        Assert.Equal(basketReceipt.GetProperty("id").GetInt64(), last.GetProperty("id").GetInt64());
        Assert.Equal(HttpStatusCode.NotFound, (await manager.GetAsync($"{sales}/latest?locationId={locationId}")).StatusCode);
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            Assert.Equal(95m, await db.RetailStockBalances.Where(x => x.ProductId == productId)
                .Select(x => x.Quantity).SingleAsync());
            Assert.Equal(9m, await db.RetailStockBalances.Where(x => x.ProductId == oilId)
                .Select(x => x.Quantity).SingleAsync());
        }

        // Fractional base-unit POS sale uses the same 3-decimal stock ledger precision as receiving.
        var fractionSale = new { locationId, productId, quantity = 0.125m,
            expectedUnitPrice = 150m, tendered = 20m, requestKey = "cash-fractional-20261123" };
        var fractionResponse = await Send(cashier, sales, fractionSale);
        Assert.Equal(HttpStatusCode.OK, fractionResponse.StatusCode);
        var fractionReceipt = await fractionResponse.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(18.75m, fractionReceipt.GetProperty("total").GetDecimal());
        Assert.Equal(0.125m, fractionReceipt.GetProperty("lines")[0].GetProperty("quantity").GetDecimal());
        var replayFraction = await Send(cashier, sales, fractionSale);
        Assert.Equal(HttpStatusCode.OK, replayFraction.StatusCode);
        Assert.Equal(fractionReceipt.GetProperty("id").GetInt64(),
            (await replayFraction.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetInt64());
        Assert.Equal(HttpStatusCode.BadRequest, (await Send(cashier, sales,
            fractionSale with { requestKey = "cash-overprecision-20261123", quantity = 0.0001m })).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await Send(cashier, sales,
            fractionSale with { quantity = 0.250m })).StatusCode);
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            Assert.Equal(94.875m, await db.RetailStockBalances.Where(x =>
                x.BusinessId == businessId && x.ProductId == productId)
                .Select(x => x.Quantity).SingleAsync());
            Assert.Equal(0.125m, await db.Set<RetailCashSaleLine>().Where(x =>
                x.SaleId == fractionReceipt.GetProperty("id").GetInt64())
                .Select(x => x.Quantity).SingleAsync());
        }

        // Verify exact base-unit flow for weighted goods: PO -> receiving -> ledger ->
        // fractional POS -> receipt -> stock, with no implicit BOX/PCS conversion.
        var kgUnitId = await CreateId(owner, $"/api/businesses/{businessId}/units",
            new { code = "KG", name = "Kilogram" });
        var kgProductId = await CreateId(owner, $"/api/businesses/{businessId}/products", new {
            name = "Loose grain", sku = "GRAIN-KG-01", categoryId, unitId = kgUnitId,
            salePrice = 89.99m, taxClassification = ""
        });
        var kgOrderId = await CreateId(manager, orders, new {
            locationId, supplierId, requestKey = "kg-base-unit-purchase-20261123",
            lines = new[] { new { productId = kgProductId, quantity = 0.625m, unitCost = 42m } }
        });
        var kgOrderPath = orders + "/" + kgOrderId;
        var kgDraft = await owner.GetFromJsonAsync<JsonElement>(kgOrderPath);
        Assert.Equal("KG", kgDraft.GetProperty("lines")[0].GetProperty("unitCode").GetString());
        var kgOrderLineId = kgDraft.GetProperty("lines")[0].GetProperty("id").GetInt64();
        Assert.Equal(HttpStatusCode.OK, (await Send(owner, kgOrderPath + "/approve", new { })).StatusCode);
        var kgReceivingId = await CreateId(manager, kgOrderPath + "/receivings", new {
            requestKey = "kg-base-unit-receiving-20261123",
            lines = new[] { new { purchaseOrderLineId = kgOrderLineId, quantity = 0.625m } }
        });
        Assert.Equal(HttpStatusCode.OK, (await Send(manager,
            kgOrderPath + "/receivings/" + kgReceivingId + "/post", new { })).StatusCode);
        var kgStockPath = $"/api/businesses/{businessId}/stock/locations/{locationId}";
        var kgStock = await owner.GetFromJsonAsync<JsonElement>(kgStockPath + "?search=GRAIN-KG-01");
        Assert.Equal(0.625m, kgStock.GetProperty("items")[0].GetProperty("quantity").GetDecimal());
        Assert.Equal("KG", kgStock.GetProperty("items")[0].GetProperty("unitCode").GetString());

        var kgSale = new {
            locationId, requestKey = "kg-base-unit-cash-sale-20261123",
            lines = new[] { new { productId = kgProductId, quantity = 0.125m,
                expectedUnitPrice = 89.99m } },
            tendered = 12m
        };
        var kgCheckout = await Send(cashier, sales, kgSale);
        Assert.Equal(HttpStatusCode.OK, kgCheckout.StatusCode);
        var kgReceipt = await kgCheckout.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(11.25m, kgReceipt.GetProperty("total").GetDecimal());
        Assert.Equal(0.125m, kgReceipt.GetProperty("lines")[0].GetProperty("quantity").GetDecimal());
        Assert.Equal(HttpStatusCode.OK, (await Send(cashier, sales, kgSale)).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await Send(cashier, sales, new {
            locationId, requestKey = "kg-base-unit-oversell-20261123",
            lines = new[] { new { productId = kgProductId, quantity = 0.501m,
                expectedUnitPrice = 89.99m } },
            tendered = 100m
        })).StatusCode);
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            Assert.Equal(0.500m, await db.RetailStockBalances.AsNoTracking()
                .Where(x => x.BusinessId == businessId && x.LocationId == locationId &&
                    x.ProductId == kgProductId)
                .Select(x => x.Quantity).SingleAsync());
            var kgMovements = await db.RetailStockMovements.AsNoTracking()
                .Where(x => x.BusinessId == businessId && x.ProductId == kgProductId)
                .OrderBy(x => x.Id).Select(x => x.QuantityChange).ToListAsync();
            Assert.Equal(new[] { 0.625m, -0.125m }, kgMovements);
        }

        // A fractional product must not reduce stock when the charged line
        // would round down to zero, even alongside other billable items.
        var tinyId = await CreateId(owner, $"/api/businesses/{businessId}/products", new {
            name = "Fractional tiny price", sku = "TINY-FRAC-01",
            categoryId, unitId = kgUnitId, salePrice = 0.01m, taxClassification = ""
        });
        Assert.Equal(HttpStatusCode.Created, (await Send(owner,
            $"/api/businesses/{businessId}/stock/locations/{locationId}/opening",
            new { productId = tinyId, quantity = 1m })).StatusCode);

        var zeroRequest = new {
            locationId, requestKey = "cash-zero-value-20261124", tendered = 1m,
            lines = new[] { new { productId = tinyId, quantity = 0.001m,
                expectedUnitPrice = 0.01m } }
        };
        var zeroResponse = await Send(cashier, sales, zeroRequest);
        Assert.Equal(HttpStatusCode.BadRequest, zeroResponse.StatusCode);
        var zeroError = await zeroResponse.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("CASH_SALE_ZERO_VALUE_LINE", zeroError.GetProperty("code").GetString());
        Assert.Equal(tinyId, zeroError.GetProperty("productId").GetInt64());

        var mixedZeroRequest = new {
            locationId, requestKey = "cash-mixed-zero-value-20261124", tendered = 20m,
            lines = new[] {
                new { productId = kgProductId, quantity = 0.125m, expectedUnitPrice = 89.99m },
                new { productId = tinyId, quantity = 0.001m, expectedUnitPrice = 0.01m }
            }
        };
        var mixedZeroResponse = await Send(cashier, sales, mixedZeroRequest);
        Assert.Equal(HttpStatusCode.BadRequest, mixedZeroResponse.StatusCode);
        Assert.Equal("CASH_SALE_ZERO_VALUE_LINE",
            (await mixedZeroResponse.Content.ReadFromJsonAsync<JsonElement>())
                .GetProperty("code").GetString());
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            Assert.Equal(0.500m, await db.RetailStockBalances.AsNoTracking()
                .Where(x => x.BusinessId == businessId && x.ProductId == kgProductId)
                .Select(x => x.Quantity).SingleAsync());
            Assert.Equal(1m, await db.RetailStockBalances.AsNoTracking()
                .Where(x => x.BusinessId == businessId && x.ProductId == tinyId)
                .Select(x => x.Quantity).SingleAsync());
            Assert.False(await db.RetailCashSales.AsNoTracking()
                .AnyAsync(x => x.RequestKey == zeroRequest.requestKey ||
                    x.RequestKey == mixedZeroRequest.requestKey));
            Assert.False(await db.RetailStockMovements.AsNoTracking()
                .AnyAsync(x => x.ProductId == tinyId && x.SourceType == "CashSaleLine"));
        }

        var tinyValid = new {
            locationId, requestKey = "cash-smallest-cent-20261124", tendered = 0.01m,
            lines = new[] { new { productId = tinyId, quantity = 0.500m,
                expectedUnitPrice = 0.01m } }
        };
        var tinyPaid = await Send(cashier, sales, tinyValid);
        Assert.Equal(HttpStatusCode.OK, tinyPaid.StatusCode);
        Assert.Equal(0.01m, (await tinyPaid.Content.ReadFromJsonAsync<JsonElement>())
            .GetProperty("total").GetDecimal());
        Assert.Equal(HttpStatusCode.OK, (await Send(cashier, sales, tinyValid)).StatusCode);
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            Assert.Equal(0.500m, await db.RetailStockBalances.AsNoTracking()
                .Where(x => x.BusinessId == businessId && x.ProductId == tinyId)
                .Select(x => x.Quantity).SingleAsync());
            Assert.Equal(1, await db.RetailStockMovements.AsNoTracking()
                .CountAsync(x => x.BusinessId == businessId && x.ProductId == tinyId &&
                    x.SourceType == "CashSaleLine"));
        }

        // Owner-only persisted payment settings: no false credit enablement, no
        // new cash sale while disabled, and historical/idempotent receipt preserved.
        var settingsPath = $"/api/businesses/{businessId}/billing-settings";
        Assert.Equal(HttpStatusCode.NotFound, (await manager.GetAsync(settingsPath)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await cashier.GetAsync(settingsPath)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await other.GetAsync(settingsPath)).StatusCode);
        var originalSettings = await owner.GetFromJsonAsync<JsonElement>(settingsPath);
        Assert.True(originalSettings.GetProperty("cashSalesEnabled").GetBoolean());
        Assert.False(originalSettings.GetProperty("creditSalesEnabled").GetBoolean());
        Assert.False(originalSettings.GetProperty("creditAvailable").GetBoolean());
        Assert.Equal(0, originalSettings.GetProperty("revision").GetInt32());
        Assert.Equal(HttpStatusCode.NotFound, (await Put(manager, settingsPath,
            new { cashSalesEnabled = false, creditSalesEnabled = false,
                expectedRevision = 0, reason = "Manager may not change billing" })).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await Put(other, settingsPath,
            new { cashSalesEnabled = false, creditSalesEnabled = false,
                expectedRevision = 0, reason = "Cross business change blocked" })).StatusCode);

        var prematureCredit = await Put(owner, settingsPath, new {
            cashSalesEnabled = true, creditSalesEnabled = true, expectedRevision = 0,
            reason = "Customer credit needs ledger first"
        });
        Assert.Equal(HttpStatusCode.Conflict, prematureCredit.StatusCode);
        Assert.Equal("CREDIT_NOT_RELEASED",
            (await prematureCredit.Content.ReadFromJsonAsync<JsonElement>())
                .GetProperty("code").GetString());
        var cashOff = await Put(owner, settingsPath, new {
            cashSalesEnabled = false, creditSalesEnabled = false, expectedRevision = 0,
            reason = "Pause new cash sales until register review"
        });
        Assert.Equal(HttpStatusCode.OK, cashOff.StatusCode);
        Assert.Equal(1, (await cashOff.Content.ReadFromJsonAsync<JsonElement>())
            .GetProperty("settings").GetProperty("revision").GetInt32());
        Assert.Equal(HttpStatusCode.Conflict, (await Put(owner, settingsPath,
            new { cashSalesEnabled = true, creditSalesEnabled = false,
                expectedRevision = 0, reason = "Old settings edit must not overwrite" })).StatusCode);
        var disabledDashboard = await cashier.GetFromJsonAsync<JsonElement>(
            $"/api/businesses/{businessId}/dashboard");
        Assert.False(disabledDashboard.GetProperty("cashPaymentEnabled").GetBoolean());
        var deniedSale = await Send(cashier, sales,
            cashSale with { requestKey = "cash-disabled-by-owner-20261124" });
        Assert.Equal(HttpStatusCode.Conflict, deniedSale.StatusCode);
        Assert.Equal("CASH_SALES_DISABLED",
            (await deniedSale.Content.ReadFromJsonAsync<JsonElement>())
                .GetProperty("code").GetString());
        Assert.Equal(HttpStatusCode.OK, (await Send(cashier, sales, cashSale)).StatusCode);
        Assert.Equal(HttpStatusCode.OK,
            (await cashier.GetAsync($"{sales}/receipt?locationId={locationId}&requestKey={cashSale.requestKey}")).StatusCode);
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            Assert.False(await db.RetailCashSales.AsNoTracking().AnyAsync(x =>
                x.BusinessId == businessId && x.RequestKey == "cash-disabled-by-owner-20261124"));
            Assert.Equal(1, await db.PlatformAuditEvents.AsNoTracking().CountAsync(x =>
                x.TargetType == "Business" && x.TargetId == businessId &&
                x.Action == "Business.BillingSettingsChanged"));
        }
        var cashOn = await Put(owner, settingsPath, new {
            cashSalesEnabled = true, creditSalesEnabled = false, expectedRevision = 1,
            reason = "Resume cash payment at the cashier register"
        });
        Assert.Equal(HttpStatusCode.OK, cashOn.StatusCode);
        Assert.True((await cashier.GetFromJsonAsync<JsonElement>(
            $"/api/businesses/{businessId}/dashboard"))
            .GetProperty("cashPaymentEnabled").GetBoolean());

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
        Assert.Equal(HttpStatusCode.Forbidden, (await Send(cashier, sales,
            cashSale with { requestKey = "cash-expired-trial-001" })).StatusCode);
        var receiptPath = $"{sales}/receipt?locationId={locationId}&requestKey={cashSale.requestKey}";
        Assert.Equal(HttpStatusCode.OK, (await cashier.GetAsync(receiptPath)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await manager.GetAsync(receiptPath)).StatusCode);
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

    private static async Task<HttpResponseMessage> Put(HttpClient client,
        string path, object body)
    {
        var token = await client.GetFromJsonAsync<JsonElement>("/api/auth/csrf");
        using var request = new HttpRequestMessage(HttpMethod.Put, path)
        {
            Content = JsonContent.Create(body)
        };
        request.Headers.Add("X-CSRF-TOKEN", token.GetProperty("token").GetString()!);
        return await client.SendAsync(request);
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
