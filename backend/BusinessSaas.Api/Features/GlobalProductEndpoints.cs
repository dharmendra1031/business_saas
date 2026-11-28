using System.Security.Claims;
using BusinessSaas.Api.Data;
using Microsoft.EntityFrameworkCore;

namespace BusinessSaas.Api.Features;

public static class GlobalProductEndpoints
{
    public static void MapGlobalProductEndpoints(this WebApplication app)
    {
        var platform = app.MapGroup("/api/platform/global-products").RequireAuthorization();
        platform.MapGet("", async (string? search, string? barcode, long? afterId, ClaimsPrincipal user,
            IConfiguration configuration, AppDbContext db, CancellationToken ct) =>
        {
            if (!PlatformOwner.IsOwner(user, configuration)) return Results.Forbid();
            return await Search(db.GlobalProducts.AsNoTracking(), search, barcode, afterId, ct);
        });

        platform.MapPost("", async (ProductRequest request, ClaimsPrincipal user, IConfiguration configuration,
            AppDbContext db, CancellationToken ct) =>
        {
            if (!PlatformOwner.IsOwner(user, configuration)) return Results.Forbid();
            var name = Clean(request.Name);
            var brand = Clean(request.Brand);
            var category = Clean(request.Category);
            var packSize = Clean(request.PackSize);
            var unit = Clean(request.Unit).ToUpperInvariant();
            var key = name.ToUpperInvariant();
            var barcode = CanonicalBarcode(request.Barcode);
            if (name.Length is < 2 or > 150 || key.Length > 150 || brand.Length > 80 ||
                category.Length > 80 || packSize.Length > 40 || unit.Length is < 1 or > 20 ||
                (!string.IsNullOrWhiteSpace(request.Barcode) && barcode is null))
                return Results.BadRequest(new { message = "Enter a valid product name, unit and optional GTIN." });
            if (barcode != null && await db.GlobalProducts.AnyAsync(x => x.Barcode == barcode, ct))
                return Results.Conflict(new { message = "This manufacturer barcode already exists." });

            var product = new GlobalProduct
            {
                Name = name, NameKey = key, Barcode = barcode, Brand = brand, Category = category,
                PackSize = packSize, Unit = unit, CreatedAtUtc = DateTime.UtcNow
            };
            db.GlobalProducts.Add(product);
            try { await db.SaveChangesAsync(ct); }
            catch (DbUpdateException error) when (RetailModule.IsDuplicateKey(error))
            {
                return Results.Conflict(new { message = "This manufacturer barcode already exists." });
            }
            return Results.Created($"/api/platform/global-products?afterId={product.Id - 1}", new { product.Id });
        });

        app.MapGet("/api/businesses/{businessId:long}/global-products", async (long businessId,
            string? search, string? barcode, long? afterId, ClaimsPrincipal user, AppDbContext db,
            CancellationToken ct) =>
        {
            var denied = await RetailMasterEndpoints.CheckAccess(db, user, businessId, false, ct);
            if (denied is not null) return denied;
            if (string.IsNullOrWhiteSpace(search) && string.IsNullOrWhiteSpace(barcode))
                return Results.BadRequest(new { message = "Enter a product name or barcode." });
            return await Search(db.GlobalProducts.AsNoTracking().Where(x => x.IsActive), search, barcode, afterId, ct);
        }).RequireAuthorization();
    }

    private static async Task<IResult> Search(IQueryable<GlobalProduct> query, string? search,
        string? barcode, long? afterId, CancellationToken ct)
    {
        var term = Clean(search).ToUpperInvariant();
        var code = CanonicalBarcode(barcode);
        if (term.Length > 150 || afterId is < 0 ||
            (!string.IsNullOrWhiteSpace(barcode) && code is null))
            return Results.BadRequest(new { message = "Invalid catalogue search." });
        if (code != null) query = query.Where(x => x.Barcode == code);
        if (term.Length > 0) query = query.Where(x => x.NameKey.StartsWith(term));
        if (afterId.HasValue) query = query.Where(x => x.Id > afterId.Value);
        var items = await query.OrderBy(x => x.Id).Take(21)
            .Select(x => new { x.Id, x.Name, x.Barcode, x.Brand, x.Category, x.PackSize, x.Unit, x.IsActive })
            .ToListAsync(ct);
        var hasMore = items.Count > 20;
        if (hasMore) items.RemoveAt(20);
        return Results.Ok(new { items, nextAfterId = hasMore ? items[^1].Id : (long?)null });
    }

    private static string Clean(string? value) =>
        string.Join(" ", (value ?? "").Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));

    // GTIN-8, UPC-A, EAN-13 and GTIN-14 share a canonical 14-digit identity.
    private static string? CanonicalBarcode(string? value)
    {
        var code = value?.Trim() ?? "";
        if (code.Length is not (8 or 12 or 13 or 14) || code.Any(c => !char.IsAsciiDigit(c)) ||
            code.All(c => c == '0')) return null;
        var sum = 0;
        for (var i = code.Length - 2; i >= 0; i--)
            sum += (code[i] - '0') * ((code.Length - 2 - i) % 2 == 0 ? 3 : 1);
        return (10 - sum % 10) % 10 == code[^1] - '0' ? code.PadLeft(14, '0') : null;
    }

    private sealed record ProductRequest(string? Name, string? Barcode, string? Brand,
        string? Category, string? PackSize, string? Unit);
}
