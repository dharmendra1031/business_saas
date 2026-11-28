using System.Data;
using System.Security.Claims;
using BusinessSaas.Api.Data;
using Microsoft.EntityFrameworkCore;

namespace BusinessSaas.Api.Features;

internal static class RetailSupplierAccounts
{
    private static IQueryable<RetailSupplierDocument> Documents(AppDbContext db, long businessId) =>
        from d in db.RetailSupplierDocuments.AsNoTracking()
        join r in db.RetailReceivings.AsNoTracking() on d.ReceivingId equals r.Id
        join o in db.RetailPurchaseOrders.AsNoTracking() on r.PurchaseOrderId equals o.Id
        join s in db.RetailSuppliers.AsNoTracking() on d.SupplierId equals s.Id
        where d.BusinessId == businessId && r.BusinessId == businessId && r.Status == "Posted" &&
              o.BusinessId == businessId && o.SupplierId == d.SupplierId && s.BusinessId == businessId
        select d;

    private static IQueryable<RetailPurchaseReturn> Returns(AppDbContext db, long businessId, long orderId, long receivingId) =>
        from r in db.RetailPurchaseReturns.AsNoTracking()
        join l in db.RetailReceivingLines.AsNoTracking() on r.ReceivingLineId equals l.Id
        join o in db.RetailPurchaseOrderLines.AsNoTracking() on l.PurchaseOrderLineId equals o.Id
        join p in db.RetailProducts.AsNoTracking() on o.ProductId equals p.Id
        where r.BusinessId == businessId && l.ReceivingId == receivingId &&
              o.PurchaseOrderId == orderId && p.BusinessId == businessId
        select r;

    private static Task<RetailPurchaseOrder?> Order(AppDbContext db, long businessId, long orderId, long receivingId, CancellationToken ct) =>
        (from o in db.RetailPurchaseOrders.AsNoTracking()
         join r in db.RetailReceivings.AsNoTracking() on o.Id equals r.PurchaseOrderId
         join s in db.RetailSuppliers.AsNoTracking() on o.SupplierId equals s.Id
         where o.BusinessId == businessId && o.Id == orderId && r.Id == receivingId &&
               r.BusinessId == businessId && r.Status == "Posted" && s.BusinessId == businessId
         select o).SingleOrDefaultAsync(ct);

    private static Task<decimal> Total(IQueryable<RetailSupplierDocument> query, string kind, CancellationToken ct) =>
        query.Where(x => x.Kind == kind).SumAsync(x => x.Amount, ct);

    private sealed record Row(long Id, long ReceivingId, string Kind, decimal Amount, string CurrencyCode,
        string Reference, long? PurchaseReturnId, string RequestKey, DateTime PostedAtUtc);

    private static Task<List<Row>> Page(IQueryable<RetailSupplierDocument> query, int page, CancellationToken ct) =>
        query.OrderByDescending(x => x.Id).Skip((page - 1) * 20).Take(21)
            .Select(x => new Row(x.Id, x.ReceivingId, x.Kind, x.Amount, x.CurrencyCode, x.Reference,
                x.PurchaseReturnId, x.RequestKey, x.PostedAtUtc)).ToListAsync(ct);

    internal static async Task<IResult> ReadReceiving(long businessId, long orderId, long receivingId,
        int? page, string? requestKey, ClaimsPrincipal user, AppDbContext db, IWebHostEnvironment environment, CancellationToken ct)
    {
        if (page is < 1 or > 100000 || requestKey?.Length > 80) return Results.BadRequest();
        var order = await Order(db, businessId, orderId, receivingId, ct);
        if (order is null) return Results.NotFound();
        var denied = await RetailMasterEndpoints.CheckAccess(db, user, businessId, false, ct, order.LocationId, ownerOnly: true);
        if (denied is not null) return denied;
        await using var transaction = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, ct);
        var query = Documents(db, businessId).Where(x => x.ReceivingId == receivingId);
        var invoice = await query.SingleOrDefaultAsync(x => x.Kind == "Invoice", ct);
        var paid = await Total(query, "Payment", ct);
        var credited = await Total(query, "CreditNote", ct);
        var currency = invoice?.CurrencyCode ?? await db.Businesses.Where(x => x.Id == businessId).Select(x => x.CurrencyCode).SingleAsync(ct);
        var returns = await Returns(db, businessId, orderId, receivingId)
            .Where(x => !query.Any(d => d.PurchaseReturnId == x.Id))
            .OrderBy(x => x.Id).Take(100).Select(x => new { x.Id, x.Quantity, x.Reason }).ToListAsync(ct);
        var items = await Page(requestKey is null ? query : query.Where(x => x.RequestKey == requestKey), page ?? 1, ct);
        var canPost = environment.IsDevelopment() &&
            await RetailMasterEndpoints.CheckAccess(db, user, businessId, true, ct, order.LocationId, ownerOnly: true) is null;
        await transaction.CommitAsync(ct);
        return Results.Ok(new { invoiceId = invoice?.Id, currencyCode = currency, billed = invoice?.Amount ?? 0m,
            paid, credited, outstanding = (invoice?.Amount ?? 0m) - paid - credited, returns, canPost,
            items = items.Take(20), hasMore = items.Count > 20 });
    }

    internal static async Task<IResult> ReadLedger(long businessId, long supplierId, int? page,
        ClaimsPrincipal user, AppDbContext db, CancellationToken ct)
    {
        if (page is < 1 or > 100000) return Results.BadRequest();
        var denied = await RetailMasterEndpoints.CheckAccess(db, user, businessId, false, ct, ownerOnly: true);
        if (denied is not null) return denied;
        var supplier = await db.RetailSuppliers.AsNoTracking().SingleOrDefaultAsync(x =>
            x.BusinessId == businessId && x.Id == supplierId, ct);
        if (supplier is null) return Results.NotFound();
        await using var transaction = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, ct);
        var query = Documents(db, businessId).Where(x => x.SupplierId == supplierId);
        var totals = await query.GroupBy(x => x.CurrencyCode).Select(g => new
        {
            currencyCode = g.Key,
            billed = g.Sum(x => x.Kind == "Invoice" ? x.Amount : 0m),
            paid = g.Sum(x => x.Kind == "Payment" ? x.Amount : 0m),
            credited = g.Sum(x => x.Kind == "CreditNote" ? x.Amount : 0m),
            outstanding = g.Sum(x => x.Kind == "Invoice" ? x.Amount : -x.Amount)
        }).ToListAsync(ct);
        var items = await Page(query, page ?? 1, ct);
        await transaction.CommitAsync(ct);
        return Results.Ok(new { supplier.Name, totals, items = items.Take(20), hasMore = items.Count > 20 });
    }

    internal static async Task<IResult> Post(long businessId, long orderId, long receivingId,
        DocumentRequest request, ClaimsPrincipal user, AppDbContext db, IWebHostEnvironment environment, CancellationToken ct)
    {
        var key = request.RequestKey?.Trim() ?? "";
        var reference = request.Reference?.Trim().ToUpperInvariant() ?? "";
        if (request.Kind is not ("Invoice" or "Payment" or "CreditNote") || key.Length is < 16 or > 80 ||
            reference.Length is < 1 or > 120 || request.Amount is <= 0 or > 999999999999.99m ||
            decimal.Round(request.Amount, 2) != request.Amount || !request.Confirmed ||
            (request.Kind == "CreditNote" ? request.PurchaseReturnId is not > 0 : request.PurchaseReturnId is not null))
            return Results.BadRequest(new { message = "Enter a valid document reference, amount and confirmation." });
        await using var transaction = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, ct);
        try
        {
            var order = await Order(db, businessId, orderId, receivingId, ct);
            if (order is null) return Results.NotFound();
            var denied = await RetailMasterEndpoints.CheckAccess(db, user, businessId, false, ct, order.LocationId, ownerOnly: true);
            if (denied is not null) return denied;
            var existing = await Documents(db, businessId).SingleOrDefaultAsync(x => x.RequestKey == key, ct);
            if (existing is not null)
                return existing.ReceivingId == receivingId && existing.Kind == request.Kind && existing.Amount == request.Amount &&
                    existing.Reference == reference && existing.PurchaseReturnId == request.PurchaseReturnId
                    ? Results.Ok(Confirmation(existing))
                    : Results.Conflict(new { message = "Document request key is already in use." });
            if (!environment.IsDevelopment()) return Results.Conflict(new { message = "Supplier financial posting is not released." });
            denied = await RetailMasterEndpoints.CheckAccess(db, user, businessId, true, ct, order.LocationId, ownerOnly: true);
            if (denied is not null) return denied;
            var query = Documents(db, businessId).Where(x => x.ReceivingId == receivingId);
            var invoice = await query.SingleOrDefaultAsync(x => x.Kind == "Invoice", ct);
            var paid = await Total(query, "Payment", ct);
            var credited = await Total(query, "CreditNote", ct);
            var balance = (invoice?.Amount ?? 0m) - paid - credited;
            if (balance != request.ExpectedOutstanding)
                return Results.Conflict(new { message = "Supplier balance changed. Verify before retrying." });
            if (request.Kind == "Invoice" && invoice is not null || request.Kind != "Invoice" && invoice is null)
                return Results.Conflict(new { message = "Exactly one supplier invoice must exist before settlement." });
            if (request.Kind == "Payment" && request.Amount > balance)
                return Results.Conflict(new { message = "Payment exceeds invoice outstanding." });
            if (request.Kind == "CreditNote")
            {
                if (!await Returns(db, businessId, orderId, receivingId).AnyAsync(x => x.Id == request.PurchaseReturnId, ct))
                    return Results.NotFound();
                if (request.Amount > invoice!.Amount - credited ||
                    await query.AnyAsync(x => x.PurchaseReturnId == request.PurchaseReturnId, ct))
                    return Results.Conflict(new { message = "Credit exceeds invoice value or return already credited." });
            }
            if (await db.RetailSupplierDocuments.AnyAsync(x => x.BusinessId == businessId && x.SupplierId == order.SupplierId &&
                x.Kind == request.Kind && x.Reference == reference, ct))
                return Results.Conflict(new { message = "Supplier document reference already recorded." });
            var document = new RetailSupplierDocument
            {
                BusinessId = businessId, SupplierId = order.SupplierId, ReceivingId = receivingId,
                ActorUserId = long.Parse(user.FindFirstValue(ClaimTypes.NameIdentifier)!),
                CurrencyCode = invoice?.CurrencyCode ?? await db.Businesses.Where(x => x.Id == businessId).Select(x => x.CurrencyCode).SingleAsync(ct),
                Kind = request.Kind, Amount = request.Amount, Reference = reference,
                PurchaseReturnId = request.PurchaseReturnId, RequestKey = key
            };
            db.RetailSupplierDocuments.Add(document);
            await db.SaveChangesAsync(ct);
            await transaction.CommitAsync(ct);
            return Results.Ok(Confirmation(document));
        }
        catch (Exception error) when (StockPosting.IsSqlConflict(error) ||
            error is DbUpdateException update && RetailModule.IsDuplicateKey(update))
        {
            return Results.Conflict(new { message = "Supplier document is being updated. Verify the original request." });
        }
    }

    private static object Confirmation(RetailSupplierDocument x) => new
    { x.Id, x.ReceivingId, x.Kind, x.Amount, x.Reference, x.RequestKey, x.PurchaseReturnId };

    internal sealed record DocumentRequest(string Kind, decimal Amount, string? Reference, string? RequestKey,
        long? PurchaseReturnId, decimal ExpectedOutstanding, bool Confirmed);
}
