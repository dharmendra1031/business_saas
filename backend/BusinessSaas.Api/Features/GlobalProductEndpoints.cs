using System.Data;
using System.Security.Claims;
using System.Text.Json;
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

        platform.MapPost("", (ProductRequest request, ClaimsPrincipal user, IConfiguration configuration,
            AppDbContext db, CancellationToken ct) => PlatformOwner.IsOwner(user, configuration)
                ? Save(db, null, request, long.Parse(user.FindFirstValue(ClaimTypes.NameIdentifier)!), ct) : Task.FromResult<IResult>(Results.Forbid()));

        platform.MapPut("/{id:long}", (long id, ProductRequest request, ClaimsPrincipal user,
            IConfiguration configuration, AppDbContext db, CancellationToken ct) =>
            PlatformOwner.IsOwner(user, configuration)
                ? Save(db, id, request, long.Parse(user.FindFirstValue(ClaimTypes.NameIdentifier)!), ct) : Task.FromResult<IResult>(Results.Forbid()));

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
        app.MapPost("/api/businesses/{businessId:long}/global-products/{globalId:long}/adopt", Adopt).RequireAuthorization();
    }

    private static async Task<IResult> Adopt(long businessId, long globalId, AdoptionRequest request,
        ClaimsPrincipal user, AppDbContext db, CancellationToken ct)
    {
        await using var transaction = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, ct);
        try
        {
            var denied = await RetailMasterEndpoints.CheckAccess(db, user, businessId, true, ct);
            if (denied is not null) return denied;
            var existing = await db.RetailProducts.AsNoTracking().SingleOrDefaultAsync(x =>
                x.BusinessId == businessId && x.GlobalProductId == globalId, ct);
            if (existing is not null)
                return Results.Ok(new { existing.Id, existing.IsActive, existing.SalePrice });
            if (request.SalePrice is null or <= 0 or > 9999999999.99m ||
                decimal.Round(request.SalePrice.Value, 2) != request.SalePrice.Value)
                return Results.BadRequest(new { message = "Enter a positive selling price with at most two decimals." });
            var global = await db.GlobalProducts.AsNoTracking().SingleOrDefaultAsync(x => x.Id == globalId && x.IsActive, ct);
            if (global is null) return Results.NotFound();
            if (request.ExpectedRevision != global.Revision)
                return Results.Conflict(new { message = "Catalogue product changed. Search again." });
            if (global.Unit.Length is < 1 or > 12 || !global.Unit.All(ch => char.IsAsciiLetterOrDigit(ch) || ch == '-'))
                return Results.Conflict(new { message = "Catalogue stock unit needs review." });
            var codes = BarcodeForms(global.Barcode);
            var matches = await db.RetailProducts.Where(x => x.BusinessId == businessId &&
                (x.Barcode != null && codes.Contains(x.Barcode) || codes.Contains(x.Sku)))
                .Take(2).ToListAsync(ct);
            if (matches.Count != 0)
            {
                var local = matches[0];
                if (!request.LinkExisting || matches.Count != 1 || local.GlobalProductId != null || !local.IsActive ||
                    local.Barcode == null || !codes.Contains(local.Barcode) ||
                    !await db.RetailUnits.AnyAsync(x => x.Id == local.UnitId && x.BusinessId == businessId &&
                        x.IsActive && x.Code == global.Unit, ct))
                    return Results.Conflict(new { message = "Local code already exists. Review barcode and unit before linking." });
                local.GlobalProductId = globalId;
                await db.SaveChangesAsync(ct);
                await transaction.CommitAsync(ct);
                return Results.Ok(new { local.Id, local.IsActive, local.SalePrice });
            }
            if (global.Barcode is null && await db.RetailProducts.AnyAsync(x => x.BusinessId == businessId &&
                x.Name.ToUpper() == global.NameKey, ct))
                return Results.Conflict(new { message = "A local product has the same name. Review the identity first." });
            var unit = await db.RetailUnits.SingleOrDefaultAsync(x => x.BusinessId == businessId && x.Code == global.Unit, ct);
            if (unit is null)
            {
                var unitName = global.Unit.Length == 1 ? global.Unit + " UNIT" : global.Unit;
                if (await db.RetailUnits.AnyAsync(x => x.BusinessId == businessId && x.NameKey == unitName, ct))
                    return Results.Conflict(new { message = "Local unit name conflicts with the catalogue unit." });
                unit = new RetailUnit { BusinessId = businessId, Code = global.Unit, Name = unitName, NameKey = unitName };
                db.RetailUnits.Add(unit);
            }
            if (!unit.IsActive) return Results.Conflict(new { message = "Activate the matching local unit first." });
            var categoryName = global.Category.Length >= 2 ? global.Category : "General";
            var categoryKey = categoryName.ToUpperInvariant();
            var category = await db.RetailCategories.SingleOrDefaultAsync(x => x.BusinessId == businessId && x.NameKey == categoryKey, ct);
            if (category is null)
            {
                category = new RetailCategory { BusinessId = businessId, Name = categoryName, NameKey = categoryKey };
                db.RetailCategories.Add(category);
            }
            if (!category.IsActive) return Results.Conflict(new { message = "Activate the matching local category first." });
            RetailBrand? brand = null;
            if (global.Brand.Length >= 2)
            {
                var key = global.Brand.ToUpperInvariant();
                brand = await db.RetailBrands.SingleOrDefaultAsync(x => x.BusinessId == businessId && x.NameKey == key, ct);
                if (brand is null)
                {
                    brand = new RetailBrand { BusinessId = businessId, Name = global.Brand, NameKey = key };
                    db.RetailBrands.Add(brand);
                }
                if (!brand.IsActive) return Results.Conflict(new { message = "Activate the matching local brand first." });
            }
            await db.SaveChangesAsync(ct);
            var product = new RetailProduct
            {
                BusinessId = businessId, GlobalProductId = globalId, Name = global.Name, Barcode = global.Barcode,
                Sku = "AUTO-" + Guid.NewGuid().ToString("N"), UnitId = unit.Id, CategoryId = category.Id,
                BrandId = brand?.Id, SalePrice = request.SalePrice.Value
            };
            db.RetailProducts.Add(product);
            await db.SaveChangesAsync(ct);
            product.Sku = $"AUTO-{product.Id}";
            await db.SaveChangesAsync(ct);
            await transaction.CommitAsync(ct);
            return Results.Created($"/api/businesses/{businessId}/products/{product.Id}",
                new { product.Id, product.IsActive, product.SalePrice });
        }
        catch (Exception error) when (StockPosting.IsSqlConflict(error) ||
            error is DbUpdateException update && RetailModule.IsDuplicateKey(update))
        {
            return Results.Conflict(new { message = "Product or masters are being updated. Retry the same catalogue selection." });
        }
    }

    internal static string[] BarcodeForms(string? value)
    {
        var canonical = CanonicalBarcode(value);
        return canonical is null ? [] : new[] { 8, 12, 13, 14 }.Where(length =>
            canonical[..(14 - length)].All(ch => ch == '0'))
            .Select(length => canonical[(14 - length)..]).ToArray();
    }

    private sealed record AdoptionRequest(decimal? SalePrice, int ExpectedRevision, bool LinkExisting = false);

    private static async Task<IResult> Save(AppDbContext db, long? id, ProductRequest request, long actorId, CancellationToken ct)
    {
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

        var product = id.HasValue
            ? await db.GlobalProducts.SingleOrDefaultAsync(x => x.Id == id.Value, ct)
            : new GlobalProduct { CreatedAtUtc = DateTime.UtcNow };
        if (product is null) return Results.NotFound();
        if (id.HasValue && (request.IsActive is null || request.ExpectedRevision is null))
            return Results.BadRequest(new { message = "Select the product status and reload its revision." });
        if (id.HasValue && product.Revision != request.ExpectedRevision)
            return Results.Conflict(new { message = "Product changed elsewhere. Reload before editing." });
        // Freeze pack identity before tenant adoption exists; corrections require an explicit future workflow.
        if (id.HasValue && (product.Barcode != barcode ||
            !string.Equals(product.PackSize, packSize, StringComparison.OrdinalIgnoreCase) || product.Unit != unit))
            return Results.Conflict(new { message = "Barcode, pack size and stock unit are fixed. Create a distinct product or request an audited correction." });
        var previous = id.HasValue ? AuditValue(product) : null;
        var others = db.GlobalProducts.Where(x => x.Id != product.Id);
        if (barcode != null && await others.AnyAsync(x => x.Barcode == barcode, ct))
            return Results.Conflict(new { message = "This manufacturer barcode already exists." });
        if (barcode is null && await others.AnyAsync(x => x.Barcode == null && x.NameKey == key &&
            EF.Property<string>(x, "BrandKey") == brand.ToUpperInvariant() &&
            EF.Property<string>(x, "PackSizeKey") == packSize.ToUpperInvariant() && x.Unit == unit, ct))
            return Results.Conflict(new { message = "A matching product already exists. Review the catalogue." });

        product.Name = name;
        product.NameKey = key;
        product.Barcode = barcode;
        product.Brand = brand;
        product.Category = category;
        product.PackSize = packSize;
        product.Unit = unit;
        product.IsActive = request.IsActive ?? true;
        var current = AuditValue(product);
        if (id.HasValue && previous == current)
            return Results.Ok(new { product.Id, product.Revision });
        product.Revision++;
        if (!id.HasValue) db.GlobalProducts.Add(product);
        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        try
        {
            await db.SaveChangesAsync(ct);
            db.PlatformAuditEvents.Add(new PlatformAuditEvent
            {
                ActorUserId = actorId,
                Action = id.HasValue ? "GlobalProduct.Updated" : "GlobalProduct.Created",
                TargetType = "GlobalProduct", TargetId = product.Id,
                PreviousValue = previous, NewValue = current
            });
            await db.SaveChangesAsync(ct);
            await transaction.CommitAsync(ct);
        }
        catch (DbUpdateConcurrencyException)
        {
            return Results.Conflict(new { message = "Product changed elsewhere. Reload before editing." });
        }
        catch (DbUpdateException error) when (RetailModule.IsDuplicateKey(error))
        {
            return Results.Conflict(new { message = "A matching product or manufacturer barcode already exists. Review the catalogue." });
        }
        return id.HasValue ? Results.Ok(new { product.Id, product.Revision }) :
            Results.Created($"/api/platform/global-products?afterId={product.Id - 1}", new { product.Id, product.Revision });
    }

    private static string AuditValue(GlobalProduct product) => JsonSerializer.Serialize(new
    {
        product.Name, product.Barcode, product.Brand, product.Category,
        product.PackSize, product.Unit, product.IsActive
    });

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
            .Select(x => new { x.Id, x.Name, x.Barcode, x.Brand, x.Category, x.PackSize, x.Unit, x.IsActive, x.Revision })
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
        string? Category, string? PackSize, string? Unit, bool? IsActive = null, int? ExpectedRevision = null);
}
