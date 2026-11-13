using System.Data;
using System.Net;
using System.Security.Claims;
using BusinessSaas.Api.Data;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;

namespace BusinessSaas.Api.Features;

public static class RetailPurchaseEndpoints
{
    private const int PageSize = 20;
    private const decimal MaxQuantity = 9999999.999m;
    private const decimal MaxCost = 99999999.99m;

    public static void MapRetailPurchaseEndpoints(this WebApplication app)
    {
        var group = app.MapGroup("/api/businesses/{businessId:long}/purchase-orders")
            .RequireAuthorization();

        group.MapGet("", async (long businessId, int? page,
            ClaimsPrincipal user, AppDbContext db) =>
        {
            var denied = await RetailMasterEndpoints.CheckAccess(db, user, businessId, false);
            if (denied is not null) return denied;
            var currentPage = page ?? 1;
            if (currentPage is < 1 or > 100000)
                return Results.BadRequest(new { message = "Invalid page." });

            var userId = UserId(user);
            var role = await db.BusinessUsers.AsNoTracking()
                .Where(x => x.BusinessId == businessId && x.UserId == userId)
                .Select(x => x.RoleCode).SingleAsync();

            var query = from order in db.RetailPurchaseOrders.AsNoTracking()
                        join supplier in db.RetailSuppliers.AsNoTracking()
                            on order.SupplierId equals supplier.Id
                        join location in db.BusinessLocations.AsNoTracking()
                            on order.LocationId equals location.Id
                        where order.BusinessId == businessId &&
                              supplier.BusinessId == businessId &&
                              location.BusinessId == businessId &&
                              (role == "Owner" || location.IsPrimary)
                        select new
                        {
                            order.Id,
                            order.LocationId,
                            LocationName = location.Name,
                            order.SupplierId,
                            SupplierName = supplier.Name,
                            order.Status,
                            order.CreatedAtUtc,
                            order.ApprovedAtUtc
                        };
            var total = await query.CountAsync();
            var items = await query.OrderByDescending(x => x.Id)
                .Skip((currentPage - 1) * PageSize).Take(PageSize).ToListAsync();
            return Results.Ok(new { items, total, page = currentPage, pageSize = PageSize });
        });

        group.MapPost("", async (long businessId, CreateOrder request,
            ClaimsPrincipal user, AppDbContext db) =>
        {
            var denied = await RetailStockEndpoints.CheckLocation(db, user,
                businessId, request.LocationId, true);
            if (denied is not null) return denied;

            if (request.SupplierId <= 0 || request.Lines is null ||
                request.Lines.Count is < 1 or > 50 ||
                request.Lines.Select(x => x.ProductId).Distinct().Count() != request.Lines.Count ||
                request.Lines.Any(x => x.ProductId <= 0 || !ValidQuantity(x.Quantity) ||
                    !ValidCost(x.UnitCost)))
                return Results.BadRequest(new { message = "Enter 1–50 distinct products with valid quantities and costs." });

            if (!await db.RetailSuppliers.AsNoTracking().AnyAsync(x =>
                x.Id == request.SupplierId && x.BusinessId == businessId && x.IsActive))
                return Results.NotFound();

            var ids = request.Lines.Select(x => x.ProductId).ToArray();
            if (await db.RetailProducts.AsNoTracking().CountAsync(x =>
                x.BusinessId == businessId && x.IsActive && ids.Contains(x.Id)) != ids.Length)
                return Results.BadRequest(new { message = "Select active products from this business." });

            await using var transaction = await db.Database.BeginTransactionAsync();
            var order = new RetailPurchaseOrder
            {
                BusinessId = businessId,
                LocationId = request.LocationId,
                SupplierId = request.SupplierId,
                CreatedByUserId = UserId(user)
            };
            db.RetailPurchaseOrders.Add(order);
            await db.SaveChangesAsync();
            db.RetailPurchaseOrderLines.AddRange(request.Lines.Select(line =>
                new RetailPurchaseOrderLine
                {
                    PurchaseOrderId = order.Id,
                    ProductId = line.ProductId,
                    OrderedQuantity = line.Quantity,
                    UnitCost = line.UnitCost
                }));
            await db.SaveChangesAsync();
            await transaction.CommitAsync();
            return Results.Created($"/api/businesses/{businessId}/purchase-orders/{order.Id}",
                new { order.Id, order.Status });
        });

        group.MapGet("/{orderId:long}", async (long businessId, long orderId,
            ClaimsPrincipal user, AppDbContext db) =>
        {
            var order = await db.RetailPurchaseOrders.AsNoTracking().SingleOrDefaultAsync(x =>
                x.BusinessId == businessId && x.Id == orderId);
            if (order is null) return Results.NotFound();

            var denied = await RetailStockEndpoints.CheckLocation(db, user,
                businessId, order.LocationId, false);
            if (denied is not null) return denied;

            var lines = await (from line in db.RetailPurchaseOrderLines.AsNoTracking()
                               join product in db.RetailProducts.AsNoTracking()
                                   on line.ProductId equals product.Id
                               where line.PurchaseOrderId == orderId &&
                                     product.BusinessId == businessId
                               orderby line.Id
                               select new
                               {
                                   line.Id, line.ProductId, product.Name, product.Sku,
                                   line.OrderedQuantity, line.UnitCost
                               }).ToListAsync();

            var received = await PostedQuantities(db, businessId, orderId);
            var items = lines.Select(line => new
            {
                line.Id,
                line.ProductId,
                line.Name,
                line.Sku,
                line.OrderedQuantity,
                line.UnitCost,
                ReceivedQuantity = received.GetValueOrDefault(line.Id),
                RemainingQuantity = line.OrderedQuantity - received.GetValueOrDefault(line.Id)
            }).ToList();

            var receipts = await db.RetailReceivings.AsNoTracking()
                .Where(x => x.BusinessId == businessId && x.PurchaseOrderId == orderId)
                .OrderByDescending(x => x.Id)
                .Select(x => new { x.Id, x.Status, x.CreatedAtUtc, x.PostedAtUtc })
                .ToListAsync();
            return Results.Ok(new
            {
                order.Id, order.BusinessId, order.LocationId, order.SupplierId,
                order.Status, order.CreatedAtUtc, order.ApprovedAtUtc,
                Lines = items, Receivings = receipts
            });
        });

        group.MapPost("/{orderId:long}/approve", async (long businessId, long orderId,
            ClaimsPrincipal user, AppDbContext db) =>
        {
            var order = await db.RetailPurchaseOrders.AsNoTracking().SingleOrDefaultAsync(x =>
                x.BusinessId == businessId && x.Id == orderId);
            if (order is null) return Results.NotFound();
            var denied = await CheckOwner(db, user, businessId, order.LocationId);
            if (denied is not null) return denied;

            if (!await db.RetailSuppliers.AsNoTracking().AnyAsync(x =>
                    x.Id == order.SupplierId && x.BusinessId == businessId && x.IsActive))
                return Results.Conflict(new { message = "Supplier is no longer active." });

            var updated = await db.RetailPurchaseOrders
                .Where(x => x.Id == orderId && x.BusinessId == businessId && x.Status == "Draft")
                .ExecuteUpdateAsync(set => set
                    .SetProperty(x => x.Status, "Approved")
                    .SetProperty(x => x.ApprovedByUserId, UserId(user))
                    .SetProperty(x => x.ApprovedAtUtc, DateTime.UtcNow));
            return updated == 1
                ? Results.Ok(new { id = orderId, status = "Approved" })
                : Results.Conflict(new { message = "Only draft orders can be approved." });
        });

        group.MapPost("/{orderId:long}/receivings", async (long businessId, long orderId,
            CreateReceiving request, ClaimsPrincipal user, AppDbContext db) =>
        {
            var order = await db.RetailPurchaseOrders.AsNoTracking().SingleOrDefaultAsync(x =>
                x.Id == orderId && x.BusinessId == businessId);
            if (order is null) return Results.NotFound();
            var denied = await RetailStockEndpoints.CheckLocation(db, user,
                businessId, order.LocationId, true);
            if (denied is not null) return denied;

            if (order.Status != "Approved")
                return Results.Conflict(new { message = "Only approved, open purchase orders can be received." });
            if (request.Lines is null || request.Lines.Count is < 1 or > 50 ||
                request.Lines.Any(x => x.PurchaseOrderLineId <= 0 || !ValidQuantity(x.Quantity)) ||
                request.Lines.Select(x => x.PurchaseOrderLineId).Distinct().Count() != request.Lines.Count)
                return Results.BadRequest(new { message = "Enter distinct PO lines with positive receiving quantities." });

            var lines = await db.RetailPurchaseOrderLines.AsNoTracking()
                .Where(x => x.PurchaseOrderId == orderId).ToDictionaryAsync(x => x.Id);
            var received = await PostedQuantities(db, businessId, orderId);
            foreach (var line in request.Lines)
            {
                if (!lines.TryGetValue(line.PurchaseOrderLineId, out var ordered))
                    return Results.BadRequest(new { message = "Invalid purchase order line." });
                if (line.Quantity > ordered.OrderedQuantity - received.GetValueOrDefault(ordered.Id))
                    return Results.Conflict(new { message = "Receiving exceeds the remaining ordered quantity." });
            }

            await using var transaction = await db.Database.BeginTransactionAsync();
            var receipt = new RetailReceiving
            {
                BusinessId = businessId, PurchaseOrderId = orderId,
                CreatedByUserId = UserId(user)
            };
            db.RetailReceivings.Add(receipt);
            await db.SaveChangesAsync();
            db.RetailReceivingLines.AddRange(request.Lines.Select(x => new RetailReceivingLine
            {
                ReceivingId = receipt.Id,
                PurchaseOrderLineId = x.PurchaseOrderLineId,
                Quantity = x.Quantity
            }));
            await db.SaveChangesAsync();
            await transaction.CommitAsync();
            return Results.Created($"/api/businesses/{businessId}/purchase-orders/{orderId}",
                new { receipt.Id, receipt.Status });
        });

        group.MapPost("/{orderId:long}/receivings/{receivingId:long}/post",
            async (long businessId, long orderId, long receivingId,
                ClaimsPrincipal user, AppDbContext db) =>
        {
            var order = await db.RetailPurchaseOrders.AsNoTracking().SingleOrDefaultAsync(x =>
                x.Id == orderId && x.BusinessId == businessId);
            if (order is null) return Results.NotFound();
            var denied = await RetailStockEndpoints.CheckLocation(db, user,
                businessId, order.LocationId, true);
            if (denied is not null) return denied;

            await using var transaction = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable);
            try
            {
                var receipt = await db.RetailReceivings.AsNoTracking().SingleOrDefaultAsync(x =>
                    x.Id == receivingId && x.BusinessId == businessId &&
                    x.PurchaseOrderId == orderId);
                if (receipt is null) return Results.NotFound();
                if (receipt.Status == "Posted")
                    return Results.Ok(new { receipt.Id, receipt.Status });
                if (receipt.Status != "Draft" || order.Status != "Approved")
                    return Results.Conflict(new { message = "This receiving cannot be posted." });

                var allLines = await db.RetailPurchaseOrderLines.AsNoTracking()
                    .Where(x => x.PurchaseOrderId == orderId).ToDictionaryAsync(x => x.Id);
                var receiptLines = await db.RetailReceivingLines.AsNoTracking()
                    .Where(x => x.ReceivingId == receivingId).OrderBy(x => x.PurchaseOrderLineId)
                    .ToListAsync();
                if (receiptLines.Count == 0 || receiptLines.Any(x => !allLines.ContainsKey(x.PurchaseOrderLineId)))
                    return Results.Conflict(new { message = "Receiving lines are invalid." });

                var received = await PostedQuantities(db, businessId, orderId);
                foreach (var line in receiptLines)
                {
                    var ordered = allLines[line.PurchaseOrderLineId];
                    if (!ValidQuantity(line.Quantity) ||
                        line.Quantity > ordered.OrderedQuantity - received.GetValueOrDefault(ordered.Id) ||
                        !await db.RetailProducts.AsNoTracking().AnyAsync(x =>
                            x.Id == ordered.ProductId && x.BusinessId == businessId && x.IsActive))
                        return Results.Conflict(new { message = "Receiving exceeds the remaining order or the product is inactive." });
                }

                foreach (var line in receiptLines.OrderBy(x => allLines[x.PurchaseOrderLineId].ProductId))
                {
                    var ordered = allLines[line.PurchaseOrderLineId];
                    var balance = await StockPosting.ApplyAsync(db, businessId,
                        order.LocationId, ordered.ProductId, line.Quantity, "Receiving",
                        line.Id, UserId(user));
                    if (balance is null)
                        return Results.Conflict(new { message = "Stock conflict. Refresh and retry." });
                }

                var postedAt = DateTime.UtcNow;
                var updated = await db.RetailReceivings
                    .Where(x => x.Id == receivingId && x.BusinessId == businessId &&
                        x.PurchaseOrderId == orderId && x.Status == "Draft")
                    .ExecuteUpdateAsync(set => set
                        .SetProperty(x => x.Status, "Posted")
                        .SetProperty(x => x.PostedByUserId, UserId(user))
                        .SetProperty(x => x.PostedAtUtc, postedAt));
                if (updated != 1)
                    return Results.Conflict(new { message = "Receiving was already processed." });

                var receivedNow = receiptLines.ToDictionary(x => x.PurchaseOrderLineId, x => x.Quantity);
                var completed = allLines.Values.All(line =>
                    received.GetValueOrDefault(line.Id) + receivedNow.GetValueOrDefault(line.Id)
                        == line.OrderedQuantity);
                if (completed)
                {
                    await db.RetailPurchaseOrders
                        .Where(x => x.Id == orderId && x.BusinessId == businessId && x.Status == "Approved")
                        .ExecuteUpdateAsync(set => set.SetProperty(x => x.Status, "Closed"));
                }

                await transaction.CommitAsync();
                return Results.Ok(new { receipt.Id, status = "Posted", orderClosed = completed });
            }
            catch (SqlException error) when (error.Number is 1205 or 1222 or 3960 or 2601 or 2627)
            {
                return Results.Conflict(new { message = "Another receiving updated this order. Refresh and retry." });
            }
            catch (DbUpdateException error) when (
                error.InnerException is SqlException { Number: 2601 or 2627 or 1205 })
            {
                return Results.Conflict(new { message = "Duplicate or conflicting stock posting." });
            }
        });
    }

    private static async Task<Dictionary<long, decimal>> PostedQuantities(
        AppDbContext db, long businessId, long orderId)
    {
        return await (from line in db.RetailReceivingLines.AsNoTracking()
                      join receipt in db.RetailReceivings.AsNoTracking()
                          on line.ReceivingId equals receipt.Id
                      where receipt.BusinessId == businessId &&
                            receipt.PurchaseOrderId == orderId && receipt.Status == "Posted"
                      group line by line.PurchaseOrderLineId into values
                      select new { LineId = values.Key, Total = values.Sum(x => x.Quantity) })
            .ToDictionaryAsync(x => x.LineId, x => x.Total);
    }

    private static bool ValidQuantity(decimal value) =>
        value is > 0 and <= MaxQuantity && decimal.Round(value, 3) == value;

    private static bool ValidCost(decimal value) =>
        value is >= 0 and <= MaxCost && decimal.Round(value, 2) == value;

    private static long UserId(ClaimsPrincipal user) =>
        long.Parse(user.FindFirstValue(ClaimTypes.NameIdentifier)!);

    private static async Task<IResult?> CheckOwner(AppDbContext db,
        ClaimsPrincipal user, long businessId, long locationId)
    {
        var denied = await RetailStockEndpoints.CheckLocation(db, user, businessId, locationId, true);
        if (denied is not null) return denied;
        return await db.BusinessUsers.AsNoTracking().AnyAsync(x =>
            x.BusinessId == businessId && x.UserId == UserId(user) && x.RoleCode == "Owner")
            ? null : Results.NotFound();
    }

    private sealed record OrderLine(long ProductId, decimal Quantity, decimal UnitCost);
    private sealed record CreateOrder(long LocationId, long SupplierId, List<OrderLine>? Lines);
    private sealed record ReceivingLine(long PurchaseOrderLineId, decimal Quantity);
    private sealed record CreateReceiving(List<ReceivingLine>? Lines);
}
