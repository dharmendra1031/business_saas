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
        var sale = await db.RetailCashSales.AsNoTracking().SingleOrDefaultAsync(x =>
            x.BusinessId == businessId && x.CashierId == actor && x.LocationId == locationId &&
            x.RequestKey == requestKey);
        return sale is null ? Results.NotFound() : Results.Ok(sale);
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
            .Select(x => new { x.Id, x.Name, x.Sku, x.SalePrice }).ToListAsync();
        return Results.Ok(products);
    }

    public sealed record CashRequest(long LocationId, long ProductId, int Quantity,
        decimal ExpectedUnitPrice, decimal Tendered, string RequestKey);

    private static async Task<IResult> Post(long businessId, CashRequest request,
        ClaimsPrincipal user, AppDbContext db)
    {
        if (!long.TryParse(user.FindFirstValue(ClaimTypes.NameIdentifier), out var actor))
            return Results.NotFound();
        if (string.IsNullOrWhiteSpace(request.RequestKey) || request.RequestKey.Length is < 12 or > 80 ||
            request.Quantity is < 1 or > 1000000 || request.Tendered is <= 0 or > 9999999999999999.99m ||
            decimal.Round(request.Tendered, 2) != request.Tendered)
            return Results.BadRequest(new { message = "Check the quantity and cash received." });

        await using var tx = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable);
        try
        {
            var shop = await Shop(db, businessId, actor, request.LocationId);
            if (shop is null) return Results.NotFound();
            var previous = await db.RetailCashSales.AsNoTracking().SingleOrDefaultAsync(x =>
                x.BusinessId == businessId && x.CashierId == actor && x.RequestKey == request.RequestKey);
            if (previous is not null)
            {
                if (previous.ProductId != request.ProductId || previous.LocationId != request.LocationId ||
                    previous.Quantity != request.Quantity || previous.UnitPrice != request.ExpectedUnitPrice ||
                    previous.Tendered != request.Tendered)
                    return Results.Conflict(new { message = "This receipt key belongs to a different sale." });
                return Results.Ok(previous);
            }

            var sub = await db.BusinessSubscriptions.AsNoTracking().SingleOrDefaultAsync(x => x.BusinessId == businessId);
            if (sub is null || !SubscriptionAccess.CanOperate(sub, DateTime.UtcNow))
                return Results.StatusCode(StatusCodes.Status403Forbidden);
            var product = await db.RetailProducts.AsNoTracking().SingleOrDefaultAsync(x =>
                x.Id == request.ProductId && x.BusinessId == businessId && x.IsActive);
            if (product is null || product.TaxClassification != "" || product.SalePrice <= 0 ||
                product.SalePrice != request.ExpectedUnitPrice)
                return Results.BadRequest(new { message = "Product or price changed. Select the product again." });
            var total = product.SalePrice * request.Quantity;
            if (request.Tendered < total)
                return Results.BadRequest(new { message = "Cash received is less than the total." });

            var sale = new RetailCashSale {
                BusinessId = businessId, CashierId = actor, LocationId = request.LocationId,
                ProductId = product.Id, ProductName = product.Name, UnitPrice = product.SalePrice,
                Quantity = request.Quantity, Total = total, Tendered = request.Tendered,
                CurrencyCode = shop.CurrencyCode, PostedAtUtc = DateTime.UtcNow, RequestKey = request.RequestKey
            };
            db.RetailCashSales.Add(sale);
            await db.SaveChangesAsync();
            if (await StockPosting.ApplyAsync(db, businessId, sale.LocationId, product.Id,
                    -sale.Quantity, "CashSale", sale.Id, actor) is null)
                return Results.BadRequest(new { message = "Insufficient stock at this location." });
            await tx.CommitAsync();
            return Results.Ok(sale);
        }
        catch (Exception ex) when (StockPosting.IsSqlConflict(ex))
        {
            return Results.Conflict(new { message = "Stock is being updated. Retry this same sale." });
        }
    }
}
