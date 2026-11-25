using System.Data;
using System.Net;
using System.Security.Claims;
using BusinessSaas.Api.Data;
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

        group.MapGet("", async (long businessId, int? page, bool? draftOnly,
            ClaimsPrincipal user, AppDbContext db, CancellationToken cancellationToken) =>
        {
            var role = await RetailMasterEndpoints.ReadRole(db, user, businessId, cancellationToken);
            if (role is null) return Results.NotFound();
            var currentPage = page ?? 1;
            if (currentPage is < 1 or > 100000)
                return Results.BadRequest(new { message = "Invalid page." });

            var query = from order in db.RetailPurchaseOrders.AsNoTracking()
                        join supplier in db.RetailSuppliers.AsNoTracking()
                            on order.SupplierId equals supplier.Id
                        join location in db.BusinessLocations.AsNoTracking()
                            on order.LocationId equals location.Id
                        where order.BusinessId == businessId && (draftOnly != true || order.Status == "Draft") &&
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
            var total = await query.CountAsync(cancellationToken);
            var items = await query.OrderByDescending(x => x.Id)
                .Skip((currentPage - 1) * PageSize).Take(PageSize).ToListAsync(cancellationToken);
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
                    !ValidCost(x.UnitCost) || x.UnitId is <= 0 ||
                    x.ExpectedBaseUnitCode is { Length: < 1 or > 12 } ||
                    (x.UnitId is null) != (x.ExpectedFactor is null) ||
                    x.ExpectedFactor is < 0.001m or > 9999999.999m ||
                    x.ExpectedFactor.HasValue && decimal.Round(x.ExpectedFactor.Value, 3) != x.ExpectedFactor.Value))
                return Results.BadRequest(new { message = "Enter 1–50 distinct products with valid quantities and costs." });

            var requestKey = request.RequestKey?.Trim() ?? "";
            if (requestKey.Length is < 12 or > 80)
                return Results.BadRequest(new { message = "Request key must be 12–80 characters." });

            var userId = UserId(user);
            var previous = await db.RetailPurchaseOrders.AsNoTracking()
                .FirstOrDefaultAsync(x => x.BusinessId == businessId &&
                    x.CreatedByUserId == userId && x.CreationKey == requestKey);
            if (previous is not null)
                return previous.LocationId == request.LocationId &&
                    previous.SupplierId == request.SupplierId &&
                    await SameOrderLines(db, previous.Id, request.Lines)
                    ? Results.Ok(new { previous.Id, previous.Status })
                    : Results.Conflict(new { message = "Request key is already used for a different purchase order." });

            if (!await db.RetailSuppliers.AsNoTracking().AnyAsync(x =>
                x.Id == request.SupplierId && x.BusinessId == businessId && x.IsActive))
                return Results.NotFound();

            await using var transaction = await db.Database.BeginTransactionAsync(
                db.Database.IsSqlServer() ? IsolationLevel.RepeatableRead : IsolationLevel.Serializable);
            try
            {
                var ids = request.Lines.Select(x => x.ProductId).ToArray();
                var products = await db.RetailProducts.AsNoTracking()
                    .Where(x => x.BusinessId == businessId && x.IsActive && ids.Contains(x.Id))
                    .Include(x => x.Conversions).ToDictionaryAsync(x => x.Id);
                if (products.Count != ids.Length)
                    return Results.BadRequest(new { message = "Select active products from this business." });

                var baseUnitIds = products.Values.Select(x => x.UnitId).Distinct().ToArray();
                var baseUnits = await db.RetailUnits.AsNoTracking()
                    .Where(x => x.BusinessId == businessId && baseUnitIds.Contains(x.Id))
                    .ToDictionaryAsync(x => x.Id, x => x.Code);
                var alternateIds = request.Lines.Where(x => x.UnitId.HasValue).Select(x => x.UnitId!.Value).Distinct().ToArray();
                var alternateUnits = await db.RetailUnits.AsNoTracking()
                    .Where(x => x.BusinessId == businessId && x.IsActive && alternateIds.Contains(x.Id))
                    .ToDictionaryAsync(x => x.Id);
                var orderLines = new List<RetailPurchaseOrderLine>();
                foreach (var line in request.Lines)
                {
                    var product = products[line.ProductId];
                    if (!baseUnits.TryGetValue(product.UnitId, out var baseCode) ||
                        line.ExpectedBaseUnitCode is not null && line.ExpectedBaseUnitCode != baseCode)
                        return Results.Conflict(new { message = "Base stock unit changed. Refresh the product and verify its quantity and cost." });
                    decimal? factor = null;
                    string? unitCode = null;
                    if (line.UnitId is long alternateId)
                    {
                        var conversion = product.Conversions.SingleOrDefault(x => x.UnitId == alternateId);
                        if (alternateId == product.UnitId || conversion is null ||
                            !alternateUnits.TryGetValue(alternateId, out var alternate))
                            return Results.BadRequest(new { message = "Select an active alternate unit configured for this product." });
                        if (conversion.Factor != line.ExpectedFactor)
                            return Results.Conflict(new { message = "Unit conversion changed. Refresh the product and verify its quantity and cost." });
                        factor = conversion.Factor;
                        unitCode = alternate.Code;
                    }
                    if (!StockPosting.ValidBaseQuantity(line.Quantity, factor ?? 1m))
                        return Results.BadRequest(new { message = "Converted stock quantity must be positive, within the stock limit and exact to three decimals." });
                    orderLines.Add(new RetailPurchaseOrderLine
                    {
                        ProductId = line.ProductId, OrderedQuantity = line.Quantity, UnitCost = line.UnitCost,
                        SelectedUnitId = line.UnitId, SelectedUnitCode = unitCode, ConversionFactor = factor
                    });
                }

                var order = new RetailPurchaseOrder
                {
                    BusinessId = businessId,
                    LocationId = request.LocationId,
                    SupplierId = request.SupplierId,
                    CreatedByUserId = userId,
                    CreationKey = requestKey
                };
                db.RetailPurchaseOrders.Add(order);
                await db.SaveChangesAsync();
                foreach (var line in orderLines) line.PurchaseOrderId = order.Id;
                db.RetailPurchaseOrderLines.AddRange(orderLines);
                await db.SaveChangesAsync();
                await transaction.CommitAsync();
                return Results.Created($"/api/businesses/{businessId}/purchase-orders/{order.Id}",
                    new { order.Id, order.Status });
            }
            catch (DbUpdateException error) when (RetailModule.IsDuplicateKey(error))
            {
                await transaction.RollbackAsync();
                db.ChangeTracker.Clear();
                var existing = await db.RetailPurchaseOrders.AsNoTracking()
                    .FirstOrDefaultAsync(x => x.BusinessId == businessId &&
                        x.CreatedByUserId == userId && x.CreationKey == requestKey);
                if (existing is not null && existing.LocationId == request.LocationId &&
                    existing.SupplierId == request.SupplierId &&
                    await SameOrderLines(db, existing.Id, request.Lines))
                    return Results.Ok(new { existing.Id, existing.Status });

                return Results.Conflict(new { message = "Purchase order already exists or the request changed." });
            }
            catch (Exception error) when (StockPosting.IsSqlConflict(error))
            {
                return Results.Conflict(new { message = "Product or order is being updated. Retry the same purchase request." });
            }
        });

        group.MapGet("/{orderId:long}", async (long businessId, long orderId,
            ClaimsPrincipal user, AppDbContext db, CancellationToken cancellationToken) =>
        {
            var order = await db.RetailPurchaseOrders.AsNoTracking().SingleOrDefaultAsync(x =>
                x.BusinessId == businessId && x.Id == orderId, cancellationToken);
            if (order is null) return Results.NotFound();

            var denied = await RetailStockEndpoints.CheckLocation(db, user,
                businessId, order.LocationId, false, cancellationToken);
            if (denied is not null) return denied;

            var lines = await (from line in db.RetailPurchaseOrderLines.AsNoTracking()
                               join product in db.RetailProducts.AsNoTracking()
                                   on line.ProductId equals product.Id
                               join unit in db.RetailUnits.AsNoTracking()
                                   on product.UnitId equals unit.Id
                               where line.PurchaseOrderId == orderId &&
                                     product.BusinessId == businessId && unit.BusinessId == businessId
                               orderby line.Id
                               select new
                               {
                                   line.Id, line.ProductId, product.Name, product.Sku,
                                   UnitCode = line.SelectedUnitCode ?? unit.Code,
                                   BaseUnitCode = unit.Code, line.SelectedUnitId,
                                   ConversionFactor = line.ConversionFactor ?? 1m,
                                   line.OrderedQuantity, line.UnitCost
                               }).ToListAsync(cancellationToken);

            var received = await PostedQuantities(db, businessId, orderId, cancellationToken);
            var items = lines.Select(line => new
            {
                line.Id,
                line.ProductId,
                line.Name,
                line.Sku,
                line.UnitCode, line.BaseUnitCode, line.SelectedUnitId, line.ConversionFactor,
                BaseOrderedQuantity = line.OrderedQuantity * line.ConversionFactor,
                BaseReceivedQuantity = received.GetValueOrDefault(line.Id) * line.ConversionFactor,
                line.OrderedQuantity,
                line.UnitCost,
                ReceivedQuantity = received.GetValueOrDefault(line.Id),
                RemainingQuantity = line.OrderedQuantity - received.GetValueOrDefault(line.Id)
            }).ToList();

            var receipts = await db.RetailReceivings.AsNoTracking()
                .Where(x => x.BusinessId == businessId && x.PurchaseOrderId == orderId)
                .OrderByDescending(x => x.Id)
                .Select(x => new { x.Id, x.Status, x.CreatedAtUtc, x.PostedAtUtc,
                    Lines = db.RetailReceivingLines.Where(l => l.ReceivingId == x.Id)
                        .OrderBy(l => l.Id).Select(l => new { l.PurchaseOrderLineId, l.Quantity }).ToList() })
                .ToListAsync(cancellationToken);
            var validLineIds = lines.Select(line => line.Id).ToHashSet();
            if (receipts.Any(receipt => receipt.Lines.Any(line => !validLineIds.Contains(line.PurchaseOrderLineId))))
                return Results.Conflict(new { message = "Receiving lines require reconciliation." });

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
            var denied = await RetailMasterEndpoints.CheckAccess(db, user, businessId, true,
                locationId: order.LocationId, ownerOnly: true);
            if (denied is not null) return denied;

            if (!await db.RetailSuppliers.AsNoTracking().AnyAsync(x =>
                    x.Id == order.SupplierId && x.BusinessId == businessId && x.IsActive))
                return Results.Conflict(new { message = "Supplier is no longer active." });

            var productIds = await db.RetailPurchaseOrderLines.AsNoTracking()
                .Where(x => x.PurchaseOrderId == orderId)
                .Select(x => x.ProductId).ToArrayAsync();
            if (productIds.Length == 0 || await db.RetailProducts.AsNoTracking().CountAsync(x =>
                    x.BusinessId == businessId && x.IsActive && productIds.Contains(x.Id)) != productIds.Length)
                return Results.Conflict(new { message = "An ordered product is no longer active." });

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

            if (request.Lines is null || request.Lines.Count is < 1 or > 50 ||
                request.Lines.Any(x => x.PurchaseOrderLineId <= 0 || !ValidQuantity(x.Quantity)) ||
                request.Lines.Select(x => x.PurchaseOrderLineId).Distinct().Count() != request.Lines.Count)
                return Results.BadRequest(new { message = "Enter distinct PO lines with positive receiving quantities." });

            var requestKey = request.RequestKey?.Trim() ?? "";
            if (requestKey.Length is < 12 or > 80)
                return Results.BadRequest(new { message = "Request key must be 12–80 characters." });

            var userId = UserId(user);
            var previous = await db.RetailReceivings.AsNoTracking()
                .FirstOrDefaultAsync(x => x.BusinessId == businessId &&
                    x.PurchaseOrderId == orderId && x.CreatedByUserId == userId &&
                    x.CreationKey == requestKey);
            if (previous is not null)
                return await SameReceivingLines(db, previous.Id, request.Lines)
                    ? Results.Ok(new { previous.Id, previous.Status })
                    : Results.Conflict(new { message = "Request key is already used for a different receiving." });

            if (order.Status != "Approved")
                return Results.Conflict(new { message = "Only approved, open purchase orders can be received." });

            var lines = await db.RetailPurchaseOrderLines.AsNoTracking()
                .Where(x => x.PurchaseOrderId == orderId).ToDictionaryAsync(x => x.Id);
            var received = await PostedQuantities(db, businessId, orderId);
            foreach (var line in request.Lines)
            {
                if (!lines.TryGetValue(line.PurchaseOrderLineId, out var ordered))
                    return Results.BadRequest(new { message = "Invalid purchase order line." });
                if (!StockPosting.ValidBaseQuantity(line.Quantity, ordered.ConversionFactor ?? 1m))
                    return Results.BadRequest(new { message = "Receiving quantity cannot be represented exactly in the base stock unit." });
                if (line.Quantity > ordered.OrderedQuantity - received.GetValueOrDefault(ordered.Id))
                    return Results.Conflict(new { message = "Receiving exceeds the remaining ordered quantity." });
            }

            await using var transaction = await db.Database.BeginTransactionAsync();
            var receipt = new RetailReceiving
            {
                BusinessId = businessId, PurchaseOrderId = orderId,
                CreatedByUserId = userId, CreationKey = requestKey
            };
            db.RetailReceivings.Add(receipt);
            try
            {
                await db.SaveChangesAsync();
                db.RetailReceivingLines.AddRange(request.Lines.Select(x => new RetailReceivingLine
                {
                    ReceivingId = receipt.Id,
                    PurchaseOrderLineId = x.PurchaseOrderLineId,
                    Quantity = x.Quantity
                }));
                await db.SaveChangesAsync();
                await transaction.CommitAsync();
            }
            catch (DbUpdateException error) when (RetailModule.IsDuplicateKey(error))
            {
                await transaction.RollbackAsync();
                db.ChangeTracker.Clear();
                var existing = await db.RetailReceivings.AsNoTracking()
                    .FirstOrDefaultAsync(x => x.BusinessId == businessId &&
                        x.PurchaseOrderId == orderId && x.CreatedByUserId == userId &&
                        x.CreationKey == requestKey);
                if (existing is not null && await SameReceivingLines(db, existing.Id, request.Lines))
                    return Results.Ok(new { existing.Id, existing.Status });

                return Results.Conflict(new { message = "Receiving already exists or the request changed." });
            }
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
                        !StockPosting.ValidBaseQuantity(line.Quantity, ordered.ConversionFactor ?? 1m) ||
                        line.Quantity > ordered.OrderedQuantity - received.GetValueOrDefault(ordered.Id) ||
                        !await db.RetailProducts.AsNoTracking().AnyAsync(x =>
                            x.Id == ordered.ProductId && x.BusinessId == businessId && x.IsActive))
                        return Results.Conflict(new { message = "Receiving exceeds the remaining order or the product is inactive." });
                }

                foreach (var line in receiptLines.OrderBy(x => allLines[x.PurchaseOrderLineId].ProductId))
                {
                    var ordered = allLines[line.PurchaseOrderLineId];
                    var balance = await StockPosting.ApplyAsync(db, businessId,
                        order.LocationId, ordered.ProductId, line.Quantity * (ordered.ConversionFactor ?? 1m), "Receiving",
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
            catch (Exception error) when (StockPosting.IsSqlConflict(error))
            {
                return Results.Conflict(new { message = "Another receiving updated this order. Refresh and retry." });
            }

        });
    }

    private static async Task<bool> SameOrderLines(AppDbContext db, long orderId, List<OrderLine> lines)
    {
        // A posted order prevents its product's base unit from changing.
        var saved = await (from item in db.RetailPurchaseOrderLines.AsNoTracking()
                           join product in db.RetailProducts on item.ProductId equals product.Id
                           join unit in db.RetailUnits on product.UnitId equals unit.Id
                           where item.PurchaseOrderId == orderId
                           select new { item.ProductId, item.OrderedQuantity, item.UnitCost,
                               item.SelectedUnitId, item.ConversionFactor, BaseUnitCode = unit.Code })
            .ToDictionaryAsync(x => x.ProductId);
        return saved.Count == lines.Count && lines.All(line =>
            saved.TryGetValue(line.ProductId, out var item) &&
            item.OrderedQuantity == line.Quantity && item.UnitCost == line.UnitCost &&
            item.SelectedUnitId == line.UnitId && item.ConversionFactor == line.ExpectedFactor &&
            (line.ExpectedBaseUnitCode is null || item.BaseUnitCode == line.ExpectedBaseUnitCode));
    }

    private static async Task<bool> SameReceivingLines(AppDbContext db, long receivingId, List<ReceivingLine> lines)
    {
        var saved = await db.RetailReceivingLines.AsNoTracking()
            .Where(x => x.ReceivingId == receivingId).ToDictionaryAsync(x => x.PurchaseOrderLineId);
        return saved.Count == lines.Count && lines.All(line =>
            saved.TryGetValue(line.PurchaseOrderLineId, out var item) &&
            item.Quantity == line.Quantity);
    }

    private static async Task<Dictionary<long, decimal>> PostedQuantities(
        AppDbContext db, long businessId, long orderId, CancellationToken cancellationToken = default)
    {
        return await (from line in db.RetailReceivingLines.AsNoTracking()
                      join receipt in db.RetailReceivings.AsNoTracking()
                          on line.ReceivingId equals receipt.Id
                      where receipt.BusinessId == businessId &&
                            receipt.PurchaseOrderId == orderId && receipt.Status == "Posted"
                      group line by line.PurchaseOrderLineId into values
                      select new { LineId = values.Key, Total = values.Sum(x => x.Quantity) })
            .ToDictionaryAsync(x => x.LineId, x => x.Total, cancellationToken);
    }

    private static bool ValidQuantity(decimal value) =>
        value is > 0 and <= MaxQuantity && decimal.Round(value, 3) == value;

    private static bool ValidCost(decimal value) =>
        value is >= 0 and <= MaxCost && decimal.Round(value, 2) == value;

    private static long UserId(ClaimsPrincipal user) =>
        long.Parse(user.FindFirstValue(ClaimTypes.NameIdentifier)!);

    private sealed record OrderLine(long ProductId, decimal Quantity, decimal UnitCost,
        long? UnitId = null, decimal? ExpectedFactor = null, string? ExpectedBaseUnitCode = null);
    private sealed record CreateOrder(long LocationId, long SupplierId, List<OrderLine>? Lines, string? RequestKey);
    private sealed record ReceivingLine(long PurchaseOrderLineId, decimal Quantity);
    private sealed record CreateReceiving(List<ReceivingLine>? Lines, string? RequestKey);
}
