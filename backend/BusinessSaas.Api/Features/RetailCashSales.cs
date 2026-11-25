using System.Data;
using System.Diagnostics;
using System.Security.Claims;
using BusinessSaas.Api.Data;
using BusinessSaas.Api.Reports;
using Microsoft.EntityFrameworkCore;

namespace BusinessSaas.Api.Features;

public static class RetailCashSales
{
    public static void MapRetailCashSales(this WebApplication app)
    {
        var group = app.MapGroup("/api/businesses/{businessId:long}/cash-sales").RequireAuthorization();
        group.MapGet("/products", Search);
        group.MapGet("/customers", SearchCustomers);
        group.MapGet("/receipt", FindReceipt);
        group.MapGet("/latest", Latest);
        group.MapGet("/receipts/{receiptId:long}", ReceiptById);
        group.MapGet("/receipts/{receiptId:long}/pdf", ReceiptPdf);
        group.MapGet("/receipts", ReceiptHistory);
        group.MapGet("/business-receipts", BusinessReceiptHistory);
        group.MapGet("/business-receipts/{receiptId:long}", BusinessReceiptById);
        group.MapPost("/business-receipts/{receiptId:long}/return-estimate", EstimateReturn);
        group.MapGet("/business-receipts/{receiptId:long}/returns", ReturnHistory);
        group.MapPost("/business-receipts/{receiptId:long}/returns", PostReturn);
        group.MapGet("/business-receipts/{receiptId:long}/pdf", BusinessReceiptPdf);
        group.MapGet("/daily-report", DailyReport);
        group.MapPost("", Post);
    }

    private static async Task<Business?> Shop(AppDbContext db, long businessId, long actor, long locationId,
        bool ownerOnly = false)
    {
        return await (from b in db.Businesses.AsNoTracking()
                      join t in db.BusinessTypes on b.BusinessTypeId equals t.Id
                      join m in db.BusinessUsers on b.Id equals m.BusinessId
                      join loc in db.BusinessLocations on b.Id equals loc.BusinessId
                      where b.Id == businessId && t.Code == "RETAIL" && m.UserId == actor &&
                          loc.Id == locationId && (m.RoleCode == "Owner" ||
                          (!ownerOnly && loc.IsPrimary && (m.RoleCode == "Cashier" || m.RoleCode == "Manager")))
                      select b).SingleOrDefaultAsync();
    }

    private static async Task<IResult> FindReceipt(long businessId, long locationId, string requestKey,
        ClaimsPrincipal user, AppDbContext db)
    {
        if (!long.TryParse(user.FindFirstValue(ClaimTypes.NameIdentifier), out var actor) ||
            await Shop(db, businessId, actor, locationId) is null) return Results.NotFound();
        var sale = await db.RetailCashSales.AsNoTracking().Include(x => x.Lines).SingleOrDefaultAsync(x =>
            x.BusinessId == businessId && x.CashierId == actor && x.LocationId == locationId &&
            x.RequestKey == requestKey);
        return sale is null ? Results.NotFound() : Results.Ok(Receipt(sale));
    }

    private static async Task<IResult> Latest(long businessId, long locationId, ClaimsPrincipal user, AppDbContext db)
    {
        if (!long.TryParse(user.FindFirstValue(ClaimTypes.NameIdentifier), out var actor) ||
            await Shop(db, businessId, actor, locationId) is null) return Results.NotFound();
        var sale = await db.RetailCashSales.AsNoTracking().Include(x => x.Lines)
            .Where(x => x.BusinessId == businessId && x.CashierId == actor && x.LocationId == locationId)
            .OrderByDescending(x => x.Id).FirstOrDefaultAsync();
        return sale is null ? Results.NotFound() : Results.Ok(Receipt(sale));
    }

    private static async Task<IResult> ReceiptById(long businessId, long locationId, long receiptId,
        ClaimsPrincipal user, AppDbContext db)
    {
        if (!long.TryParse(user.FindFirstValue(ClaimTypes.NameIdentifier), out var actor) ||
            await Shop(db, businessId, actor, locationId) is null) return Results.NotFound();
        var sale = await db.RetailCashSales.AsNoTracking().Include(x => x.Lines)
            .SingleOrDefaultAsync(x => x.Id == receiptId && x.BusinessId == businessId &&
                x.CashierId == actor && x.LocationId == locationId);
        return sale is null ? Results.NotFound() : Results.Ok(Receipt(sale));
    }

    private static object Receipt(RetailCashSale sale, IReadOnlyList<RetailCashReturnLine>? returns = null)
    {
        var lines = sale.Lines.OrderBy(x => x.Id).ToArray();
        // Older single-item checkout clients read these four fields from the receipt header.
        var first = lines.First();
        return new { sale.Id, sale.BusinessId, sale.LocationId, sale.CashierId, sale.RequestKey,
            sale.Tendered, sale.Total, sale.CurrencyCode, sale.CustomerId, sale.CustomerName,
            sale.BusinessName, sale.LocationName, sale.TimeZoneId, printAvailable = OperatingSystem.IsWindows(),
            PostedAtUtc = DateTime.SpecifyKind(sale.PostedAtUtc, DateTimeKind.Utc),
            first.ProductId, first.ProductName, first.UnitPrice, first.Quantity,
            Lines = lines.Select(line => new { line.Id, line.SaleId, line.ProductId,
                line.ProductName, line.ProductSku, line.UnitCode, line.Quantity, line.UnitPrice,
                line.SelectedUnitId, line.ConversionFactor, line.BaseUnitCode,
                BaseQuantity = line.Quantity * (line.ConversionFactor ?? 1m),
                LineTotal = OriginalAmount(line),
                ReturnedQuantity = returns?.Where(x => x.SaleLineId == line.Id).Sum(x => x.Quantity),
                RemainingQuantity = line.Quantity - returns?.Where(x => x.SaleLineId == line.Id).Sum(x => x.Quantity),
                ReturnedAmount = returns?.Where(x => x.SaleLineId == line.Id).Sum(x => x.Amount),
                RemainingAmount = OriginalAmount(line) - returns?.Where(x => x.SaleLineId == line.Id).Sum(x => x.Amount) }) };
    }

    private static Task<IResult> ReceiptPdf(long businessId, long locationId, long receiptId,
        ClaimsPrincipal user, AppDbContext db, HttpContext context, ILoggerFactory loggerFactory) =>
        PrintPdf(businessId, locationId, receiptId, user, db, context, loggerFactory, ownerOnly: false);

    private static Task<IResult> BusinessReceiptPdf(long businessId, long locationId, long receiptId,
        ClaimsPrincipal user, AppDbContext db, HttpContext context, ILoggerFactory loggerFactory) =>
        PrintPdf(businessId, locationId, receiptId, user, db, context, loggerFactory, ownerOnly: true);

    private static async Task<IResult> PrintPdf(long businessId, long locationId, long receiptId,
        ClaimsPrincipal user, AppDbContext db, HttpContext context, ILoggerFactory loggerFactory, bool ownerOnly)
    {
        var started = Stopwatch.GetTimestamp();
        if (!long.TryParse(user.FindFirstValue(ClaimTypes.NameIdentifier), out var actor) ||
            await Shop(db, businessId, actor, locationId, ownerOnly) is not { } business)
            return Results.NotFound();

        // Posted receipts are immutable. Split the collection load so the receipt's
        // historical logo and header are not transferred again for every line.
        var sale = await db.RetailCashSales.AsNoTracking().AsSplitQuery().Include(x => x.Lines)
            .SingleOrDefaultAsync(x => x.Id == receiptId && x.BusinessId == businessId &&
                x.LocationId == locationId && (ownerOnly || x.CashierId == actor), context.RequestAborted);
        var loaded = Stopwatch.GetTimestamp();
        if (sale is null) return Results.NotFound();
        if (!OperatingSystem.IsWindows())
            return Results.Problem("RDLC PDF printing requires a Windows report host.", statusCode: 503);
        try
        {
            var data = RetailReceiptReportData.FromCashSale(sale, business,
                TimeZoneInfo.FindSystemTimeZoneById(business.TimeZoneId));
            var mapped = Stopwatch.GetTimestamp();
            var pdf = RetailReceiptRenderer.Render(data, business.ReceiptPaperSize);
            var rendered = Stopwatch.GetTimestamp();
            // Durations only: no customer data, prices or report content in diagnostics.
            context.Response.Headers["Server-Timing"] = FormattableString.Invariant(
                $"receipt-db;dur={Stopwatch.GetElapsedTime(started, loaded).TotalMilliseconds:F1}, receipt-map;dur={Stopwatch.GetElapsedTime(loaded, mapped).TotalMilliseconds:F1}, receipt-render;dur={Stopwatch.GetElapsedTime(mapped, rendered).TotalMilliseconds:F1}");
            context.Response.Headers.CacheControl = "no-store";
            context.Response.Headers.ContentDisposition = $"inline; filename=receipt-{sale.Id}.pdf";
            return Results.File(pdf, "application/pdf");
        }
        catch (Exception error)
        {
            loggerFactory.CreateLogger("RetailReceiptRenderer").LogError(error, "RDLC render failed for business {BusinessId}, receipt {ReceiptId}", businessId, receiptId);
            return Results.Problem("Unable to render this posted receipt. Retry printing; do not record another sale.", statusCode: 500);
        }
    }

    private static async Task<IResult> BusinessReceiptById(long businessId, long locationId, long receiptId,
        ClaimsPrincipal user, AppDbContext db)
    {
        if (!long.TryParse(user.FindFirstValue(ClaimTypes.NameIdentifier), out var actor) ||
            await Shop(db, businessId, actor, locationId, ownerOnly: true) is null) return Results.NotFound();
        var sale = await db.RetailCashSales.AsNoTracking().Include(x => x.Lines)
            .SingleOrDefaultAsync(x => x.Id == receiptId && x.BusinessId == businessId && x.LocationId == locationId);
        if (sale is null) return Results.NotFound();
        return Results.Ok(Receipt(sale, await ReturnedLines(db, businessId, receiptId)));
    }

    private static Task<List<RetailCashReturnLine>> ReturnedLines(AppDbContext db, long businessId, long receiptId) =>
        db.RetailCashReturns.AsNoTracking()
            .Where(x => x.BusinessId == businessId && x.SaleId == receiptId)
            .SelectMany(x => x.Lines).ToListAsync();

    public sealed record ReturnEstimateLine(long LineId, decimal Quantity);
    public sealed record ReturnEstimateRequest(long LocationId, List<ReturnEstimateLine>? Lines);

    // Read-only estimate of the original sale; not a refund authorization or posting.
    private static async Task<IResult> EstimateReturn(long businessId, long receiptId,
        ReturnEstimateRequest request, ClaimsPrincipal user, AppDbContext db, IConfiguration config)
    {
        if (!long.TryParse(user.FindFirstValue(ClaimTypes.NameIdentifier), out var actor) ||
            await Shop(db, businessId, actor, request.LocationId, ownerOnly: true) is null)
            return Results.NotFound();

        var sale = await db.RetailCashSales.AsNoTracking().Include(x => x.Lines)
            .SingleOrDefaultAsync(x => x.Id == receiptId && x.BusinessId == businessId &&
                x.LocationId == request.LocationId);
        if (sale is null) return Results.NotFound();
        var requested = request.Lines;
        if (requested is null || requested.Count is < 1 or > 100 ||
            requested.Any(x => x is null || x.LineId <= 0 || x.Quantity <= 0 ||
                decimal.Round(x.Quantity, 3) != x.Quantity) ||
            requested.Select(x => x.LineId).Distinct().Count() != requested.Count)
            return Results.BadRequest(new { message = "Choose each return line once and enter a positive quantity with up to three decimals." });

        var original = sale.Lines.ToDictionary(x => x.Id);
        var earlier = await ReturnedLines(db, businessId, receiptId);
        if (sale.Lines.Count == 0 || sale.Lines.Any(x => x.Quantity <= 0 || OriginalAmount(x) < 0) ||
            sale.Lines.Sum(OriginalAmount) != sale.Total)
            return Results.Conflict(new { message = "Original receipt amounts require reconciliation." });

        var estimates = new List<object>();
        decimal total = 0;
        foreach (var item in requested)
        {
            if (!original.TryGetValue(item.LineId, out var line) || item.Quantity > line.Quantity)
                return Results.BadRequest(new { message = "Return quantity cannot exceed the original sold quantity." });
            if (AllocateReturn(line, earlier, item.Quantity) is not decimal amount)
                return Results.Conflict(new { message = "Return exceeds the remaining balance or requires reconciliation." });
            estimates.Add(new { item.LineId, item.Quantity, Amount = amount });
            total += amount;
        }
        return Results.Ok(new { receiptId, sale.CurrencyCode, total, lines = estimates, canPost = config.GetValue<bool>("RetailReturns:PostingEnabled") });
    }

    private static async Task<IResult> ReturnHistory(long businessId, long locationId, long receiptId,
        ClaimsPrincipal user, AppDbContext db, int page = 1, string? requestKey = null)
    {
        if (!long.TryParse(user.FindFirstValue(ClaimTypes.NameIdentifier), out var actor) ||
            await Shop(db, businessId, actor, locationId, ownerOnly: true) is null ||
            !await db.RetailCashSales.AnyAsync(x => x.Id == receiptId && x.BusinessId == businessId &&
                x.LocationId == locationId))
            return Results.NotFound();
        if (page is < 1 or > 100000 || requestKey is { Length: > 80 })
            return Results.BadRequest(new { message = "Invalid return history filter." });
        const int pageSize = 10;
        var query = db.RetailCashReturns.AsNoTracking().Where(x => x.BusinessId == businessId && x.SaleId == receiptId);
        if (requestKey is not null) query = query.Where(x => x.RequestKey == requestKey);
        var records = await query.OrderByDescending(x => x.Id).Skip((page - 1) * pageSize)
            .Take(pageSize + 1).Include(x => x.Lines).ToListAsync();
        return Results.Ok(new { items = records.Take(pageSize).Select(x => new {
            x.Id, x.SaleId, x.RequestKey, x.Total, x.Reason, x.ActorUserId,
            PostedAtUtc = DateTime.SpecifyKind(x.PostedAtUtc, DateTimeKind.Utc),
            Lines = x.Lines.OrderBy(line => line.Id).Select(line => new {
                line.SaleLineId, line.Quantity, line.Amount, line.Restock
            })
        }), hasMore = records.Count > pageSize });
    }

    public sealed record ReturnItem(long LineId, decimal Quantity, bool Restock);
    public sealed record ReturnRequest(long LocationId, string RequestKey, string Reason,
        decimal ExpectedRefund, bool CashRefundConfirmed, List<ReturnItem>? Lines);

    private static decimal OriginalAmount(RetailCashSaleLine line) => line.LineTotal ??
        decimal.Round(line.Quantity * line.UnitPrice, 2, MidpointRounding.AwayFromZero);

    // Shared cumulative allocation keeps estimate and posting consistent.
    private static decimal? AllocateReturn(RetailCashSaleLine line,
        IReadOnlyList<RetailCashReturnLine> earlier, decimal quantity)
    {
        var returned = earlier.Where(x => x.SaleLineId == line.Id).ToArray();
        var cumulativeQty = returned.Sum(x => x.Quantity) + quantity;
        var originalAmount = OriginalAmount(line);
        if (cumulativeQty > line.Quantity ||
            !StockPosting.ValidBaseQuantity(quantity, line.ConversionFactor ?? 1m)) return null;
        var cumulativeAmount = cumulativeQty == line.Quantity ? originalAmount :
            decimal.Round(originalAmount * (cumulativeQty / line.Quantity), 2, MidpointRounding.AwayFromZero);
        var amount = cumulativeAmount - returned.Sum(x => x.Amount);
        return amount < 0 || cumulativeAmount > originalAmount ? null : amount;
    }

    // Owner-only development pilot. Confirmation records an actual cash refund,
    // not a provider payout or authorization to dispense cash.
    private static async Task<IResult> PostReturn(long businessId, long receiptId, ReturnRequest request,
        ClaimsPrincipal user, AppDbContext db, IConfiguration config)
    {
        if (!long.TryParse(user.FindFirstValue(ClaimTypes.NameIdentifier), out var actor))
            return Results.NotFound();
        var items = request.Lines;
        var reason = request.Reason?.Trim() ?? "";
        if (items is null || items.Count is < 1 or > 100 ||
            items.Any(x => x is null || x.LineId <= 0 || x.Quantity is < 0.001m or > 1000000m ||
                decimal.Round(x.Quantity, 3) != x.Quantity) ||
            items.Select(x => x.LineId).Distinct().Count() != items.Count ||
            string.IsNullOrWhiteSpace(request.RequestKey) || request.RequestKey.Length is < 12 or > 80 ||
            reason.Length is < 5 or > 200 || !request.CashRefundConfirmed ||
            request.ExpectedRefund < 0 || decimal.Round(request.ExpectedRefund, 2) != request.ExpectedRefund)
            return Results.BadRequest(new { message = "Provide unique lines, valid quantities, reason and confirmed cash refund amount." });

        await using var tx = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable);
        try
        {
            if (await Shop(db, businessId, actor, request.LocationId, ownerOnly: true) is null)
                return Results.NotFound();
            var sale = await db.RetailCashSales.AsNoTracking().Include(x => x.Lines)
                .SingleOrDefaultAsync(x => x.Id == receiptId && x.BusinessId == businessId && x.LocationId == request.LocationId);
            if (sale is null) return Results.NotFound();
            var prior = await db.RetailCashReturns.AsNoTracking().Include(x => x.Lines)
                .Where(x => x.BusinessId == businessId && x.RequestKey == request.RequestKey).SingleOrDefaultAsync();
            if (prior is not null)
            {
                if (prior.SaleId != receiptId || prior.Total != request.ExpectedRefund || prior.Reason != reason ||
                    !prior.Lines.Select(x => new ReturnItem(x.SaleLineId, x.Quantity, x.Restock)).OrderBy(x => x.LineId)
                        .SequenceEqual(items.OrderBy(x => x.LineId)))
                    return Results.Conflict(new { message = "Return key belongs to a different transaction." });
                return Results.Ok(prior);
            }
            if (!config.GetValue<bool>("RetailReturns:PostingEnabled"))
                return Results.Conflict(new { message = "Return posting is not enabled on this development host." });
            if (sale.Lines.Count == 0 || sale.Lines.Any(x => x.Quantity <= 0 || OriginalAmount(x) < 0) ||
                sale.Lines.Sum(OriginalAmount) != sale.Total)
                return Results.Conflict(new { message = "Original receipt requires reconciliation." });

            var earlier = await ReturnedLines(db, businessId, receiptId);
            var original = sale.Lines.ToDictionary(x => x.Id);
            var record = new RetailCashReturn { BusinessId = businessId, SaleId = receiptId, ActorUserId = actor,
                Reason = reason, RequestKey = request.RequestKey, PostedAtUtc = DateTime.UtcNow };
            foreach (var item in items)
            {
                if (!original.TryGetValue(item.LineId, out var line))
                    return Results.BadRequest(new { message = "Return line is not on the original receipt." });
                if (AllocateReturn(line, earlier, item.Quantity) is not decimal amount)
                    return Results.Conflict(new { code = "RETURN_REJECTED", message = "Return exceeds the remaining balance or requires reconciliation." });
                record.Lines.Add(new RetailCashReturnLine { SaleLineId = line.Id, Quantity = item.Quantity,
                    Amount = amount, Restock = item.Restock });
                record.Total += amount;
            }
            if (record.Total != request.ExpectedRefund)
                return Results.Conflict(new { code = "RETURN_REJECTED", message = "Refund amount changed. Verify remaining return balance." });

            db.RetailCashReturns.Add(record);
            await db.SaveChangesAsync();
            foreach (var line in record.Lines.Where(x => x.Restock).OrderBy(x => original[x.SaleLineId].ProductId))
            {
                if (await StockPosting.ApplyAsync(db, businessId, sale.LocationId, original[line.SaleLineId].ProductId,
                    line.Quantity * (original[line.SaleLineId].ConversionFactor ?? 1m),
                    "CashReturnLine", line.Id, actor, reason: reason) is null)
                    return Results.Conflict(new { message = "Stock restoration failed; return was not posted." });
            }
            await tx.CommitAsync();
            return Results.Ok(record);
        }
        catch (Exception ex) when (StockPosting.IsSqlConflict(ex))
        {
            return Results.Conflict(new { message = "Concurrent return or stock update. Retry the same request key." });
        }
    }

    private static async Task<IResult> ReceiptHistory(long businessId, long locationId, ClaimsPrincipal user,
        AppDbContext db, int page = 1)
    {
        if (!long.TryParse(user.FindFirstValue(ClaimTypes.NameIdentifier), out var actor) ||
            await Shop(db, businessId, actor, locationId) is null) return Results.NotFound();
        return await ReadHistory(db, businessId, locationId, page, actor, null);
    }

    private static async Task<IResult> BusinessReceiptHistory(long businessId, long locationId,
        ClaimsPrincipal user, AppDbContext db, int page = 1, long? receiptId = null, DateOnly? postedDate = null)
    {
        if (!long.TryParse(user.FindFirstValue(ClaimTypes.NameIdentifier), out var actor) ||
            await Shop(db, businessId, actor, locationId, ownerOnly: true) is not { } business) return Results.NotFound();
        if (postedDate is null) return await ReadHistory(db, businessId, locationId, page, null, receiptId);
        if (postedDate.Value.Year is < 1900 or > 9998)
            return Results.BadRequest(new { message = "Choose a valid receipt date." });
        var zone = TimeZoneInfo.FindSystemTimeZoneById(business.TimeZoneId);
        return await ReadHistory(db, businessId, locationId, page, null, receiptId,
            LocalDateStart(postedDate.Value, zone), LocalDateStart(postedDate.Value.AddDays(1), zone));
    }

    private static DateTime LocalDateStart(DateOnly date, TimeZoneInfo zone)
    {
        var local = date.ToDateTime(TimeOnly.MinValue);
        while (zone.IsInvalidTime(local)) local = local.AddMinutes(1);
        return zone.IsAmbiguousTime(local)
            ? new DateTimeOffset(local, zone.GetAmbiguousTimeOffsets(local).Max()).UtcDateTime
            : TimeZoneInfo.ConvertTimeToUtc(local, zone);
    }

    private static async Task<IResult> DailyReport(long businessId, long locationId, DateOnly date,
        ClaimsPrincipal user, AppDbContext db)
    {
        if (!long.TryParse(user.FindFirstValue(ClaimTypes.NameIdentifier), out var actor) ||
            await Shop(db, businessId, actor, locationId, ownerOnly: true) is not { } business)
            return Results.NotFound();
        if (date.Year is < 1900 or > 9998)
            return Results.BadRequest(new { message = "Choose a valid report date." });

        var zone = TimeZoneInfo.FindSystemTimeZoneById(business.TimeZoneId);
        var start = LocalDateStart(date, zone);
        var end = LocalDateStart(date.AddDays(1), zone);
        var sales = db.RetailCashSales.AsNoTracking()
            .Where(x => x.BusinessId == businessId && x.LocationId == locationId &&
                x.PostedAtUtc >= start && x.PostedAtUtc < end)
            .Select(x => new { x.CurrencyCode, Amount = x.Total, IsReturn = false });
        // Refunds belong to their own posting date, including returns of older sales.
        var refunds = from r in db.RetailCashReturns.AsNoTracking()
                      join sale in db.RetailCashSales.AsNoTracking() on r.SaleId equals sale.Id
                      where r.BusinessId == businessId && sale.BusinessId == businessId &&
                          sale.LocationId == locationId && r.PostedAtUtc >= start && r.PostedAtUtc < end
                      select new { sale.CurrencyCode, Amount = r.Total, IsReturn = true };
        var totals = await sales.Concat(refunds)
            .GroupBy(x => x.CurrencyCode).OrderBy(x => x.Key)
            .Select(x => new {
                CurrencyCode = x.Key,
                ReceiptCount = x.Sum(s => s.IsReturn ? 0 : 1),
                ReturnCount = x.Sum(s => s.IsReturn ? 1 : 0),
                SalesTotal = x.Sum(s => s.IsReturn ? 0m : s.Amount),
                RefundTotal = x.Sum(s => s.IsReturn ? s.Amount : 0m),
                NetSales = x.Sum(s => s.IsReturn ? -s.Amount : s.Amount)
            }).ToListAsync();
        return Results.Ok(new { date, business.TimeZoneId, totals });
    }

    private static async Task<IResult> ReadHistory(AppDbContext db, long businessId, long locationId,
        int page, long? cashierId, long? receiptId, DateTime? start = null, DateTime? end = null)
    {
        if (page is < 1 or > 100000) return Results.BadRequest(new { message = "Choose a valid receipt page." });
        if (receiptId is <= 0) return Results.BadRequest(new { message = "Choose a valid receipt number." });

        const int pageSize = 10;
        var query = db.RetailCashSales.AsNoTracking()
            .Where(x => x.BusinessId == businessId && x.LocationId == locationId);
        if (cashierId.HasValue) query = query.Where(x => x.CashierId == cashierId.Value);
        if (receiptId.HasValue) query = query.Where(x => x.Id == receiptId.Value);
        if (start.HasValue && end.HasValue)
            query = query.Where(x => x.PostedAtUtc >= start.Value && x.PostedAtUtc < end.Value);
        var items = await query
            .OrderByDescending(x => x.Id).Skip((page - 1) * pageSize).Take(pageSize + 1)
            .Select(x => new { x.Id, x.Total, x.CurrencyCode,
                PostedAtUtc = DateTime.SpecifyKind(x.PostedAtUtc, DateTimeKind.Utc) }).ToListAsync();
        return Results.Ok(new { items = items.Take(pageSize), hasMore = items.Count > pageSize });
    }

    private static async Task<IResult> Search(long businessId, long locationId, string? search,
        ClaimsPrincipal user, AppDbContext db, IConfiguration config, CancellationToken cancellationToken, bool exact = false)
    {
        if (!long.TryParse(user.FindFirstValue(ClaimTypes.NameIdentifier), out var actor) ||
            await Shop(db, businessId, actor, locationId) is null) return Results.NotFound();
        var term = search?.Trim().ToUpperInvariant() ?? "";
        if (term.Length > 150 || (exact && term.Length == 0)) return Results.BadRequest();
        var eligible = db.RetailProducts.AsNoTracking()
            .Where(x => x.BusinessId == businessId && x.IsActive && x.SalePrice > 0 &&
                x.TaxClassification == "");
        // Exact scans use the existing business/SKU and business/barcode indexes.
        // Both matches must be returned if a SKU collides with another product's barcode.
        var matches = exact
            ? eligible.Where(x => x.Sku == term || x.Barcode == term)
                .OrderBy(x => x.Id).Take(2)
            : term.Length == 0
                ? eligible.OrderBy(x => x.Name).ThenBy(x => x.Id).Take(30)
                : eligible.Where(x => x.Name.ToUpper().Contains(term) ||
                    x.Sku.Contains(term) || x.Barcode == term)
                    .OrderByDescending(x => x.Barcode == term || x.Sku == term)
                    .ThenBy(x => x.Name).ThenBy(x => x.Id).Take(30);
        var allowAlternates = config.GetValue<bool>("RetailSales:AlternateUnitsEnabled");
        var products = await matches.Select(x => new { x.Id, x.Name, x.Sku, x.Barcode, x.SalePrice,
                UnitCode = db.RetailUnits.Where(unit =>
                    unit.Id == x.UnitId && unit.BusinessId == businessId)
                    .Select(unit => unit.Code).FirstOrDefault(),
                Available = db.RetailStockBalances
                .Where(b => b.BusinessId == businessId && b.LocationId == locationId && b.ProductId == x.Id)
                .Select(b => (decimal?)b.Quantity).FirstOrDefault() ?? 0,
                Conversions = x.Conversions.Where(conversion => allowAlternates && conversion.UnitId != x.UnitId &&
                    db.RetailUnits.Any(unit => unit.Id == conversion.UnitId && unit.BusinessId == businessId && unit.IsActive))
                    .OrderBy(conversion => conversion.UnitId)
                    .Select(conversion => new { conversion.UnitId, conversion.Factor, conversion.SalePrice,
                        UnitCode = db.RetailUnits.Where(unit => unit.Id == conversion.UnitId && unit.BusinessId == businessId)
                            .Select(unit => unit.Code).FirstOrDefault() }).ToList()
            }).ToListAsync(cancellationToken);
        return Results.Ok(products.Select(x => new {
            x.Id, x.Name, x.Sku, x.Barcode, x.SalePrice, x.UnitCode, x.Available,
            Conversions = x.Conversions.Select(unit => new { unit.UnitId, unit.Factor, unit.UnitCode,
                SalePrice = SaleUnitPrice(x.SalePrice, unit.Factor, unit.SalePrice) })
                .Where(unit => unit.SalePrice > 0 && unit.SalePrice <= 9999999999999999.99m)
        }));
    }

    private static async Task<IResult> SearchCustomers(long businessId, long locationId, string? search,
        ClaimsPrincipal user, AppDbContext db)
    {
        if (!long.TryParse(user.FindFirstValue(ClaimTypes.NameIdentifier), out var actor) ||
            await Shop(db, businessId, actor, locationId) is null) return Results.NotFound();
        var term = search?.Trim().ToUpperInvariant() ?? "";
        if (term.Length is < 2 or > 100) return Results.BadRequest();
        return Results.Ok(await db.RetailCustomers.AsNoTracking()
            .Where(x => x.BusinessId == businessId && x.IsActive &&
                (x.NameKey.Contains(term) || x.Phone.Contains(term)))
            .OrderBy(x => x.NameKey).ThenBy(x => x.Id).Take(20)
            .Select(x => new { x.Id, x.Name, x.Phone }).ToListAsync());
    }

    public sealed record CashRequest(long LocationId, decimal Tendered, string RequestKey,
        List<Item>? Lines = null, long ProductId = 0, decimal Quantity = 0, decimal ExpectedUnitPrice = 0, long? CustomerId = null);

    public sealed record Item(long ProductId, decimal Quantity, decimal ExpectedUnitPrice,
        long? UnitId = null, decimal? ExpectedFactor = null);

    private static decimal SaleUnitPrice(decimal basePrice, decimal factor, decimal? overridePrice = null) =>
        overridePrice ?? decimal.Round(basePrice * factor, 2, MidpointRounding.AwayFromZero);

    private static async Task<IResult> Post(long businessId, CashRequest request,
        ClaimsPrincipal user, AppDbContext db, HttpContext context, IConfiguration config)
    {
        var started = Stopwatch.GetTimestamp();
        if (!long.TryParse(user.FindFirstValue(ClaimTypes.NameIdentifier), out var actor))
            return Results.NotFound();
        var items = request.Lines ?? [new Item(request.ProductId, request.Quantity, request.ExpectedUnitPrice)];
        if (request.CustomerId is <= 0 || items.Count is < 1 or > 100 || items.Any(x => x is null || x.ProductId <= 0 || x.Quantity is < 0.001m or > 1000000m ||
            decimal.Round(x.Quantity, 3) != x.Quantity || x.UnitId is <= 0 ||
            (x.UnitId is null) != (x.ExpectedFactor is null) ||
            x.ExpectedFactor is < 0.001m or > 9999999.999m ||
            x.ExpectedFactor.HasValue && decimal.Round(x.ExpectedFactor.Value, 3) != x.ExpectedFactor.Value) ||
            items.Select(x => x.ProductId).Distinct().Count() != items.Count || string.IsNullOrWhiteSpace(request.RequestKey) || request.RequestKey.Length is < 12 or > 80 ||
            request.Tendered is <= 0 or > 9999999999999999.99m ||
            decimal.Round(request.Tendered, 2) != request.Tendered)
            return Results.BadRequest(new { message = "Check the quantity and cash received." });

        await using var tx = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable);
        try
        {
            var shop = await Shop(db, businessId, actor, request.LocationId);
            if (shop is null) return Results.NotFound();
            var previous = await db.RetailCashSales.AsNoTracking().Include(x => x.Lines).SingleOrDefaultAsync(x =>
                x.BusinessId == businessId && x.CashierId == actor && x.RequestKey == request.RequestKey);
            if (previous is not null)
            {
                var recorded = previous.Lines.OrderBy(x => x.ProductId)
                    .Select(x => new Item(x.ProductId, x.Quantity, x.UnitPrice, x.SelectedUnitId, x.ConversionFactor));
                if (previous.LocationId != request.LocationId || previous.Tendered != request.Tendered ||
                    previous.CustomerId != request.CustomerId ||
                    !recorded.SequenceEqual(items.OrderBy(x => x.ProductId)))
                    return Results.Conflict(new { message = "This receipt key belongs to a different sale." });
                // Never hide a committed receipt or double-post when settings changed mid-retry.
                context.Response.Headers["Server-Timing"] = FormattableString.Invariant(
                    $"sale-replay;dur={Stopwatch.GetElapsedTime(started).TotalMilliseconds:F1}");
                return Results.Ok(Receipt(previous));
            }
            if (!shop.CashSalesEnabled)
                return Results.Conflict(new { code = "CASH_SALES_DISABLED",
                    message = "Cash sales are disabled by the business owner." });

            var sub = await db.BusinessSubscriptions.AsNoTracking().SingleOrDefaultAsync(x => x.BusinessId == businessId);
            if (sub is null || !SubscriptionAccess.CanOperate(sub, DateTime.UtcNow))
                return Results.StatusCode(StatusCodes.Status403Forbidden);
            RetailCustomer? customer = null;
            if (request.CustomerId is long customerId)
            {
                customer = await db.RetailCustomers.AsNoTracking().SingleOrDefaultAsync(x =>
                    x.Id == customerId && x.BusinessId == businessId && x.IsActive);
                if (customer is null)
                    return Results.BadRequest(new { code = "CASH_SALE_CUSTOMER_CHANGED",
                        message = "Customer is unavailable. Select an active customer or continue as walk-in." });
            }
            if (items.Any(x => x.UnitId.HasValue) && !config.GetValue<bool>("RetailSales:AlternateUnitsEnabled"))
                return Results.BadRequest(new { code = "CASH_SALE_BASKET_CHANGED",
                    productId = items.First(x => x.UnitId.HasValue).ProductId,
                    message = "Alternate sale units are not enabled on this development host." });
            var ids = items.Select(x => x.ProductId).ToArray();
            var products = await db.RetailProducts.AsNoTracking()
                .Where(x => x.BusinessId == businessId && x.IsActive && ids.Contains(x.Id))
                .Include(x => x.Conversions).ToDictionaryAsync(x => x.Id);
            var locationName = await db.BusinessLocations.AsNoTracking()
                .Where(x => x.BusinessId == businessId && x.Id == request.LocationId)
                .Select(x => x.Name).SingleAsync();
            var unitIds = products.Values.Select(x => x.UnitId).Distinct().ToArray();
            var units = await db.RetailUnits.AsNoTracking()
                .Where(x => x.BusinessId == businessId && unitIds.Contains(x.Id))
                .ToDictionaryAsync(x => x.Id, x => x.Code);
            var alternateIds = items.Where(x => x.UnitId.HasValue).Select(x => x.UnitId!.Value).Distinct().ToArray();
            var alternateUnits = await db.RetailUnits.AsNoTracking()
                .Where(x => x.BusinessId == businessId && x.IsActive && alternateIds.Contains(x.Id))
                .ToDictionaryAsync(x => x.Id, x => x.Code);
            var sale = new RetailCashSale {
                BusinessId = businessId, CashierId = actor, LocationId = request.LocationId,
                CustomerId = customer?.Id, CustomerName = customer?.Name,
                CustomerPhone = customer?.Phone, CustomerAddress = customer?.Address,
                Tendered = request.Tendered, CurrencyCode = shop.CurrencyCode,
                MerchantLogo = shop.ReceiptLogo,
                MerchantLegalName = shop.ReceiptLegalName,
                MerchantAddress = shop.ReceiptAddress,
                MerchantPhone = shop.ReceiptPhone,
                MerchantEmail = shop.ReceiptEmail,
                BusinessName = shop.Name, LocationName = locationName, TimeZoneId = shop.TimeZoneId,
                PostedAtUtc = DateTime.UtcNow, RequestKey = request.RequestKey
            };
            foreach (var item in items)
            {
                if (!products.TryGetValue(item.ProductId, out var product) || product.TaxClassification != "" ||
                    product.SalePrice <= 0 ||
                    !units.TryGetValue(product.UnitId, out var unitCode))
                    return Results.BadRequest(new { code = "CASH_SALE_BASKET_CHANGED", item.ProductId,
                        message = "Product or price changed. Remove and select the affected item again." });
                var baseCode = unitCode;
                decimal? factor = null;
                decimal? overridePrice = null;
                if (item.UnitId is long alternateId)
                {
                    var conversion = product.Conversions.SingleOrDefault(x => x.UnitId == alternateId);
                    if (alternateId == product.UnitId || conversion is null ||
                        conversion.Factor != item.ExpectedFactor ||
                        !alternateUnits.TryGetValue(alternateId, out unitCode))
                        return Results.BadRequest(new { code = "CASH_SALE_BASKET_CHANGED", item.ProductId,
                            message = "Sale unit or conversion changed. Select the affected item again." });
                    factor = conversion.Factor;
                    overridePrice = conversion.SalePrice;
                }
                var unitPrice = SaleUnitPrice(product.SalePrice, factor ?? 1m, overridePrice);
                if (unitPrice <= 0 || unitPrice > 9999999999999999.99m ||
                    unitPrice != item.ExpectedUnitPrice || !StockPosting.ValidBaseQuantity(item.Quantity, factor ?? 1m))
                    return Results.BadRequest(new { code = "CASH_SALE_BASKET_CHANGED", item.ProductId,
                        message = "Price or converted quantity is invalid. Select the affected item again." });
                var lineTotal = decimal.Round(unitPrice * item.Quantity, 2,
                    MidpointRounding.AwayFromZero);
                // Never post stock for a line whose billable amount rounds to zero.
                if (lineTotal == 0m)
                    return Results.BadRequest(new { code = "CASH_SALE_ZERO_VALUE_LINE", item.ProductId,
                        message = "This line rounds to zero. Increase quantity or remove the product." });
                sale.Lines.Add(new RetailCashSaleLine { ProductId = product.Id, ProductName = product.Name,
                    Quantity = item.Quantity, UnitPrice = unitPrice,
                    SelectedUnitId = item.UnitId, ConversionFactor = factor,
                    BaseUnitCode = factor.HasValue ? baseCode : null,
                    ProductSku = product.Sku, UnitCode = unitCode, LineTotal = lineTotal });
                sale.Total += lineTotal;
            }
            if (request.Tendered < sale.Total)
                return Results.BadRequest(new { message = "Cash received is less than the basket total." });
            var validated = Stopwatch.GetTimestamp();
            db.RetailCashSales.Add(sale);
            await db.SaveChangesAsync();
            var saved = Stopwatch.GetTimestamp();
            foreach (var line in sale.Lines.OrderBy(x => x.ProductId))
            {
                if (await StockPosting.ApplyAsync(db, businessId, sale.LocationId, line.ProductId,
                        -line.Quantity * (line.ConversionFactor ?? 1m), "CashSaleLine", line.Id, actor) is null)
                    return Results.BadRequest(new { code = "CASH_SALE_BASKET_CHANGED", line.ProductId,
                        message = $"Insufficient stock for {line.ProductName} at this location. Remove it and select it again if stock is available." });
            }
            var stockPosted = Stopwatch.GetTimestamp();
            await tx.CommitAsync();
            var committed = Stopwatch.GetTimestamp();
            context.Response.Headers["Server-Timing"] = FormattableString.Invariant(
                $"sale-validate;dur={Stopwatch.GetElapsedTime(started, validated).TotalMilliseconds:F1}, sale-save;dur={Stopwatch.GetElapsedTime(validated, saved).TotalMilliseconds:F1}, sale-stock;dur={Stopwatch.GetElapsedTime(saved, stockPosted).TotalMilliseconds:F1}, sale-commit;dur={Stopwatch.GetElapsedTime(stockPosted, committed).TotalMilliseconds:F1}");
            return Results.Ok(Receipt(sale));
        }
        catch (Exception ex) when (StockPosting.IsSqlConflict(ex))
        {
            return Results.Conflict(new { message = "Stock is being updated. Retry this same sale." });
        }
    }
}
