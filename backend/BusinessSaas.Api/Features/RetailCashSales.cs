using System.Data;
using System.Security.Claims;
using BusinessSaas.Api.Data;
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
        group.MapGet("/receipts", ReceiptHistory);
        group.MapGet("/business-receipts", BusinessReceiptHistory);
        group.MapGet("/business-receipts/{receiptId:long}", BusinessReceiptById);
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

    private static object Receipt(RetailCashSale sale)
    {
        var lines = sale.Lines.OrderBy(x => x.Id).ToArray();
        // Older single-item checkout clients read these four fields from the receipt header.
        var first = lines.First();
        return new { sale.Id, sale.BusinessId, sale.LocationId, sale.CashierId, sale.RequestKey,
            sale.Tendered, sale.Total, sale.CurrencyCode, sale.CustomerId, sale.CustomerName,
            PostedAtUtc = DateTime.SpecifyKind(sale.PostedAtUtc, DateTimeKind.Utc),
            first.ProductId, first.ProductName, first.UnitPrice, first.Quantity, Lines = lines };
    }

    private static async Task<IResult> BusinessReceiptById(long businessId, long locationId, long receiptId,
        ClaimsPrincipal user, AppDbContext db)
    {
        if (!long.TryParse(user.FindFirstValue(ClaimTypes.NameIdentifier), out var actor) ||
            await Shop(db, businessId, actor, locationId, ownerOnly: true) is null) return Results.NotFound();
        var sale = await db.RetailCashSales.AsNoTracking().Include(x => x.Lines)
            .SingleOrDefaultAsync(x => x.Id == receiptId && x.BusinessId == businessId && x.LocationId == locationId);
        return sale is null ? Results.NotFound() : Results.Ok(Receipt(sale));
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
        var totals = await db.RetailCashSales.AsNoTracking()
            .Where(x => x.BusinessId == businessId && x.LocationId == locationId &&
                x.PostedAtUtc >= start && x.PostedAtUtc < end)
            .GroupBy(x => x.CurrencyCode)
            .OrderBy(x => x.Key)
            .Select(x => new { CurrencyCode = x.Key, ReceiptCount = x.Count(), SalesTotal = x.Sum(s => s.Total) })
            .ToListAsync();
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
        ClaimsPrincipal user, AppDbContext db)
    {
        if (!long.TryParse(user.FindFirstValue(ClaimTypes.NameIdentifier), out var actor) ||
            await Shop(db, businessId, actor, locationId) is null) return Results.NotFound();
        var term = search?.Trim().ToUpperInvariant() ?? "";
        if (term.Length > 150) return Results.BadRequest();
        var products = await db.RetailProducts.AsNoTracking()
            .Where(x => x.BusinessId == businessId && x.IsActive && x.SalePrice > 0 &&
                x.TaxClassification == "" && (x.Name.ToUpper().Contains(term) ||
                x.Sku.Contains(term) || x.Barcode == term))
            .OrderByDescending(x => x.Barcode == term || x.Sku == term)
            .ThenBy(x => x.Name).ThenBy(x => x.Id).Take(30)
            .Select(x => new { x.Id, x.Name, x.Sku, x.Barcode, x.SalePrice,
                UnitCode = db.RetailUnits.Where(unit =>
                    unit.Id == x.UnitId && unit.BusinessId == businessId)
                    .Select(unit => unit.Code).FirstOrDefault(),
                Available = db.RetailStockBalances
                .Where(b => b.BusinessId == businessId && b.LocationId == locationId && b.ProductId == x.Id)
                .Select(b => (decimal?)b.Quantity).FirstOrDefault() ?? 0 }).ToListAsync();
        return Results.Ok(products);
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

    public sealed record Item(long ProductId, decimal Quantity, decimal ExpectedUnitPrice);

    private static async Task<IResult> Post(long businessId, CashRequest request,
        ClaimsPrincipal user, AppDbContext db)
    {
        if (!long.TryParse(user.FindFirstValue(ClaimTypes.NameIdentifier), out var actor))
            return Results.NotFound();
        var items = request.Lines ?? [new Item(request.ProductId, request.Quantity, request.ExpectedUnitPrice)];
        if (request.CustomerId is <= 0 || items.Count is < 1 or > 100 || items.Any(x => x is null || x.ProductId <= 0 || x.Quantity is < 0.001m or > 1000000m ||
            decimal.Round(x.Quantity, 3) != x.Quantity) ||
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
                    .Select(x => new Item(x.ProductId, x.Quantity, x.UnitPrice));
                if (previous.LocationId != request.LocationId || previous.Tendered != request.Tendered ||
                    previous.CustomerId != request.CustomerId ||
                    !recorded.SequenceEqual(items.OrderBy(x => x.ProductId)))
                    return Results.Conflict(new { message = "This receipt key belongs to a different sale." });
                // Never hide a committed receipt or double-post when settings changed mid-retry.
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
            var ids = items.Select(x => x.ProductId).ToArray();
            var products = await db.RetailProducts.AsNoTracking()
                .Where(x => x.BusinessId == businessId && x.IsActive && ids.Contains(x.Id))
                .ToDictionaryAsync(x => x.Id);
            var locationName = await db.BusinessLocations.AsNoTracking()
                .Where(x => x.BusinessId == businessId && x.Id == request.LocationId)
                .Select(x => x.Name).SingleAsync();
            var unitIds = products.Values.Select(x => x.UnitId).Distinct().ToArray();
            var units = await db.RetailUnits.AsNoTracking()
                .Where(x => x.BusinessId == businessId && unitIds.Contains(x.Id))
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
                    product.SalePrice <= 0 || product.SalePrice != item.ExpectedUnitPrice ||
                    !units.TryGetValue(product.UnitId, out var unitCode))
                    return Results.BadRequest(new { code = "CASH_SALE_BASKET_CHANGED", item.ProductId,
                        message = "Product or price changed. Remove and select the affected item again." });
                var lineTotal = decimal.Round(product.SalePrice * item.Quantity, 2,
                    MidpointRounding.AwayFromZero);
                // Never post stock for a line whose billable amount rounds to zero.
                if (lineTotal == 0m)
                    return Results.BadRequest(new { code = "CASH_SALE_ZERO_VALUE_LINE", item.ProductId,
                        message = "This line rounds to zero. Increase quantity or remove the product." });
                sale.Lines.Add(new RetailCashSaleLine { ProductId = product.Id, ProductName = product.Name,
                    Quantity = item.Quantity, UnitPrice = product.SalePrice,
                    ProductSku = product.Sku, UnitCode = unitCode, LineTotal = lineTotal });
                sale.Total += lineTotal;
            }
            if (request.Tendered < sale.Total)
                return Results.BadRequest(new { message = "Cash received is less than the basket total." });
            db.RetailCashSales.Add(sale);
            await db.SaveChangesAsync();
            foreach (var line in sale.Lines.OrderBy(x => x.ProductId))
            {
                if (await StockPosting.ApplyAsync(db, businessId, sale.LocationId, line.ProductId,
                        -line.Quantity, "CashSaleLine", line.Id, actor) is null)
                    return Results.BadRequest(new { code = "CASH_SALE_BASKET_CHANGED", line.ProductId,
                        message = $"Insufficient stock for {line.ProductName} at this location. Remove it and select it again if stock is available." });
            }
            await tx.CommitAsync();
            return Results.Ok(Receipt(sale));
        }
        catch (Exception ex) when (StockPosting.IsSqlConflict(ex))
        {
            return Results.Conflict(new { message = "Stock is being updated. Retry this same sale." });
        }
    }
}
