using System.Data;
using System.Security.Claims;
using BusinessSaas.Api.Data;
using Microsoft.EntityFrameworkCore;

namespace BusinessSaas.Api.Features;

// A return is a compensating stock document for one original receiving line.
// It does not alter the PO, receiving, supplier payable, or cash drawer.
internal static class RetailPurchaseReturns
{
    private static IQueryable<ReceiptLine> Lines(AppDbContext db, long businessId, long orderId, long receivingId) =>
        from receipt in db.RetailReceivings.AsNoTracking()
        join order in db.RetailPurchaseOrders.AsNoTracking() on receipt.PurchaseOrderId equals order.Id
        join line in db.RetailReceivingLines.AsNoTracking() on receipt.Id equals line.ReceivingId
        join ordered in db.RetailPurchaseOrderLines.AsNoTracking() on line.PurchaseOrderLineId equals ordered.Id
        join product in db.RetailProducts.AsNoTracking() on ordered.ProductId equals product.Id
        join unit in db.RetailUnits.AsNoTracking() on product.UnitId equals unit.Id
        join supplier in db.RetailSuppliers.AsNoTracking() on order.SupplierId equals supplier.Id
        where receipt.Id == receivingId && receipt.BusinessId == businessId && receipt.Status == "Posted" &&
              order.Id == orderId && order.BusinessId == businessId && ordered.PurchaseOrderId == orderId &&
              product.BusinessId == businessId && unit.BusinessId == businessId && supplier.BusinessId == businessId
        select new ReceiptLine
        {
            Id = line.Id,
            LocationId = order.LocationId,
            ProductId = product.Id,
            Name = product.Name,
            Quantity = line.Quantity,
            Factor = ordered.ConversionFactor ?? 1m,
            UnitCode = ordered.SelectedUnitCode ?? unit.Code
        };

    internal static async Task<IResult> Read(long businessId, long orderId, long receivingId,
        ClaimsPrincipal user, AppDbContext db, CancellationToken ct)
    {
        var order = await db.RetailPurchaseOrders.AsNoTracking().SingleOrDefaultAsync(x =>
            x.BusinessId == businessId && x.Id == orderId, ct);
        if (order is null) return Results.NotFound();
        var denied = await RetailMasterEndpoints.CheckAccess(db, user, businessId, false, ct, order.LocationId, ownerOnly: true);
        if (denied is not null) return denied;
        var lines = await Lines(db, businessId, orderId, receivingId).OrderBy(x => x.Id).ToListAsync(ct);
        if (lines.Count == 0) return Results.NotFound();
        if (lines.Count != await db.RetailReceivingLines.CountAsync(x => x.ReceivingId == receivingId, ct))
            return Results.Conflict(new { message = "Receiving lines require reconciliation." });
        var lineIds = lines.Select(x => x.Id).ToArray();
        var returns = db.RetailPurchaseReturns.AsNoTracking().Where(x =>
            x.BusinessId == businessId && lineIds.Contains(x.ReceivingLineId));
        var totals = await returns.GroupBy(x => x.ReceivingLineId).Select(x => new
            { Id = x.Key, Quantity = x.Sum(y => y.Quantity) }).ToDictionaryAsync(x => x.Id, x => x.Quantity, ct);
        var history = await returns.OrderByDescending(x => x.Id).Take(50)
            .Select(x => new { x.Id, x.ReceivingLineId, x.Quantity, x.Reason, x.PostedAtUtc }).ToListAsync(ct);
        return Results.Ok(new
        {
            lines = lines.Select(x => new
            {
                x.Id, x.Name, x.Quantity, x.UnitCode, x.Factor,
                returned = totals.GetValueOrDefault(x.Id),
                remaining = x.Quantity - totals.GetValueOrDefault(x.Id)
            }),
            history
        });
    }

    internal static async Task<IResult> Post(long businessId, long orderId, long receivingId,
        ReturnRequest request, ClaimsPrincipal user, AppDbContext db, CancellationToken ct)
    {
        var key = request.RequestKey?.Trim() ?? "";
        var reason = request.Reason?.Trim() ?? "";
        if (key.Length is < 16 or > 80 || reason.Length is < 3 or > 200 ||
            request.Quantity <= 0 || request.ExpectedReturned < 0 || request.ReceivingLineId <= 0)
            return Results.BadRequest(new { message = "Select an item, valid quantity and return reason." });

        await using var transaction = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, ct);
        try
        {
            var order = await db.RetailPurchaseOrders.AsNoTracking().SingleOrDefaultAsync(x =>
                x.BusinessId == businessId && x.Id == orderId, ct);
            if (order is null) return Results.NotFound();
            var denied = await RetailMasterEndpoints.CheckAccess(db, user, businessId, false, ct, order.LocationId, ownerOnly: true);
            if (denied is not null) return denied;
            var line = await Lines(db, businessId, orderId, receivingId)
                .SingleOrDefaultAsync(x => x.Id == request.ReceivingLineId, ct);
            if (line is null) return Results.NotFound();
            var existing = await db.RetailPurchaseReturns.AsNoTracking().SingleOrDefaultAsync(x =>
                x.BusinessId == businessId && x.RequestKey == key, ct);
            if (existing is not null)
                return existing.ReceivingLineId == line.Id && existing.Quantity == request.Quantity && existing.Reason == reason
                    ? Results.Ok(new { existing.Id, existing.ReceivingLineId, existing.Quantity, existing.RequestKey })
                    : Results.Conflict(new { message = "Return request key already used for another return." });
            denied = await RetailMasterEndpoints.CheckAccess(db, user, businessId, true, ct, order.LocationId, ownerOnly: true);
            if (denied is not null) return denied;
            if (!StockPosting.ValidBaseQuantity(request.Quantity, line.Factor))
                return Results.BadRequest(new { message = "Return quantity must be exact in the base stock unit." });
            var returned = await db.RetailPurchaseReturns.Where(x =>
                x.BusinessId == businessId && x.ReceivingLineId == line.Id).SumAsync(x => x.Quantity, ct);
            if (returned != request.ExpectedReturned || request.Quantity > line.Quantity - returned)
                return Results.Conflict(new { message = "Return exceeds received quantity or the receipt changed. Refresh." });
            var actor = long.Parse(user.FindFirstValue(ClaimTypes.NameIdentifier)!);
            var document = new RetailPurchaseReturn
            {
                BusinessId = businessId, ReceivingLineId = line.Id, ActorUserId = actor,
                Quantity = request.Quantity, RequestKey = key, Reason = reason
            };
            db.RetailPurchaseReturns.Add(document);
            await db.SaveChangesAsync(ct);
            var stock = await StockPosting.ApplyAsync(db, businessId, line.LocationId, line.ProductId,
                -request.Quantity * line.Factor, "PurchaseReturn", document.Id, actor, reason: reason);
            if (stock is null) return Results.Conflict(new { message = "Insufficient available stock or concurrent stock change." });
            await transaction.CommitAsync(ct);
            return Results.Ok(new { document.Id, document.ReceivingLineId, document.Quantity, document.RequestKey });
        }
        catch (Exception error) when (StockPosting.IsSqlConflict(error) ||
            error is DbUpdateException update && RetailModule.IsDuplicateKey(update))
        {
            return Results.Conflict(new { message = "Return is being processed. Retry the same request." });
        }
    }

    internal sealed record ReturnRequest(long ReceivingLineId, decimal Quantity, decimal ExpectedReturned,
        string? RequestKey, string? Reason);
    private sealed class ReceiptLine
    {
        public long Id { get; init; }
        public long LocationId { get; init; }
        public long ProductId { get; init; }
        public string Name { get; init; } = "";
        public decimal Quantity { get; init; }
        public decimal Factor { get; init; }
        public string UnitCode { get; init; } = "";
    }
}
