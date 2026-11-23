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
        group.MapGet("/receipt", FindReceipt);
        group.MapGet("/latest", Latest);
        group.MapPost("", Post);
    }

    private static async Task<Business?> Shop(AppDbContext db, long businessId, long actor, long locationId)
    {
        return await (from b in db.Businesses.AsNoTracking()
                      join t in db.BusinessTypes on b.BusinessTypeId equals t.Id
                      join m in db.BusinessUsers on b.Id equals m.BusinessId
                      join loc in db.BusinessLocations on b.Id equals loc.BusinessId
                      where b.Id == businessId && t.Code == "RETAIL" && m.UserId == actor &&
                          loc.Id == locationId && (m.RoleCode == "Owner" ||
                          (loc.IsPrimary && (m.RoleCode == "Cashier" || m.RoleCode == "Manager")))
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

    private static object Receipt(RetailCashSale sale)
    {
        var lines = sale.Lines.OrderBy(x => x.Id).ToArray();
        // Older single-item checkout clients read these four fields from the receipt header.
        var first = lines.First();
        return new { sale.Id, sale.BusinessId, sale.LocationId, sale.CashierId, sale.RequestKey,
            sale.Tendered, sale.Total, sale.CurrencyCode, sale.PostedAtUtc,
            first.ProductId, first.ProductName, first.UnitPrice, first.Quantity, Lines = lines };
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

    public sealed record CashRequest(long LocationId, decimal Tendered, string RequestKey,
        List<Item>? Lines = null, long ProductId = 0, decimal Quantity = 0, decimal ExpectedUnitPrice = 0);

    public sealed record Item(long ProductId, decimal Quantity, decimal ExpectedUnitPrice);

    private static async Task<IResult> Post(long businessId, CashRequest request,
        ClaimsPrincipal user, AppDbContext db)
    {
        if (!long.TryParse(user.FindFirstValue(ClaimTypes.NameIdentifier), out var actor))
            return Results.NotFound();
        var items = request.Lines ?? [new Item(request.ProductId, request.Quantity, request.ExpectedUnitPrice)];
        if (items.Count is < 1 or > 100 || items.Any(x => x is null || x.ProductId <= 0 || x.Quantity is < 0.001m or > 1000000m ||
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
                    !recorded.SequenceEqual(items.OrderBy(x => x.ProductId)))
                    return Results.Conflict(new { message = "This receipt key belongs to a different sale." });
                return Results.Ok(Receipt(previous));
            }

            var sub = await db.BusinessSubscriptions.AsNoTracking().SingleOrDefaultAsync(x => x.BusinessId == businessId);
            if (sub is null || !SubscriptionAccess.CanOperate(sub, DateTime.UtcNow))
                return Results.StatusCode(StatusCodes.Status403Forbidden);
            var ids = items.Select(x => x.ProductId).ToArray();
            var products = await db.RetailProducts.AsNoTracking()
                .Where(x => x.BusinessId == businessId && x.IsActive && ids.Contains(x.Id))
                .ToDictionaryAsync(x => x.Id);
            var sale = new RetailCashSale {
                BusinessId = businessId, CashierId = actor, LocationId = request.LocationId,
                Tendered = request.Tendered, CurrencyCode = shop.CurrencyCode,
                PostedAtUtc = DateTime.UtcNow, RequestKey = request.RequestKey
            };
            foreach (var item in items)
            {
                if (!products.TryGetValue(item.ProductId, out var product) || product.TaxClassification != "" ||
                    product.SalePrice <= 0 || product.SalePrice != item.ExpectedUnitPrice)
                    return Results.BadRequest(new { message = "Product or price changed. Remove and select the affected item again." });
                sale.Lines.Add(new RetailCashSaleLine { ProductId = product.Id, ProductName = product.Name,
                    Quantity = item.Quantity, UnitPrice = product.SalePrice });
                sale.Total += decimal.Round(product.SalePrice * item.Quantity, 2,
                    MidpointRounding.AwayFromZero);
            }
            if (request.Tendered < sale.Total)
                return Results.BadRequest(new { message = "Cash received is less than the basket total." });
            db.RetailCashSales.Add(sale);
            await db.SaveChangesAsync();
            foreach (var line in sale.Lines.OrderBy(x => x.ProductId))
            {
                if (await StockPosting.ApplyAsync(db, businessId, sale.LocationId, line.ProductId,
                        -line.Quantity, "CashSaleLine", line.Id, actor) is null)
                    return Results.BadRequest(new { message = $"Insufficient stock for {line.ProductName} at this location." });
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
