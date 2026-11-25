using System.Data;
using System.Security.Claims;
using BusinessSaas.Api.Data;
using Microsoft.EntityFrameworkCore;

namespace BusinessSaas.Api.Features;

public static class RetailProductEndpoints
{
    private const int PageSize = 20;

    public static void MapRetailProductEndpoints(this WebApplication app)
    {
        var group = app.MapGroup("/api/businesses/{businessId:long}/products")
            .RequireAuthorization();

        group.MapGet("", async (long businessId, string? search, string? status,
            long? categoryId, int? page, long? locationId, string? code, ClaimsPrincipal user, AppDbContext db, CancellationToken cancellationToken) =>
        {
            var denied = locationId is > 0
                ? await RetailStockEndpoints.CheckLocation(db, user, businessId, locationId.Value, false, cancellationToken)
                : await RetailMasterEndpoints.CheckAccess(db, user, businessId, false, cancellationToken);
            if (denied is not null) return denied;
            if (locationId is <= 0) return Results.BadRequest(new { message = "Invalid location." });

            var currentPage = page ?? 1;
            if (currentPage is < 1 or > 100000 || categoryId is <= 0)
                return Results.BadRequest(new { message = "Invalid page or category." });

            var term = search?.Trim().ToUpperInvariant() ?? "";
            var exactCode = code?.Trim().ToUpperInvariant() ?? "";
            if (exactCode.Length > 50 || term.Length > 150 || status is not (null or "" or "all" or "active" or "inactive"))
                return Results.BadRequest(new { message = "Invalid search or status filter." });

            var query = db.RetailProducts.AsNoTracking().Where(x => x.BusinessId == businessId);
            if (exactCode.Length > 0)
                query = query.Where(x => x.Sku == exactCode);
            else if (term.Length > 0)
                query = query.Where(x => x.Name.ToUpper().Contains(term) ||
                    x.Sku.Contains(term) || x.Barcode != null && x.Barcode.Contains(term));
            if (categoryId is > 0)
            {
                var categories = await db.RetailCategories.AsNoTracking()
                    .Where(x => x.BusinessId == businessId)
                    .Select(x => new { x.Id, x.ParentId }).ToListAsync(cancellationToken);
                var allowed = new HashSet<long> { categoryId.Value };
                for (var i = 0; i < 2; i++)
                    foreach (var child in categories.Where(x => x.ParentId is not null &&
                                 allowed.Contains(x.ParentId.Value)))
                        allowed.Add(child.Id);
                query = query.Where(x => allowed.Contains(x.CategoryId));
            }
            if (status == "active") query = query.Where(x => x.IsActive);
            if (status == "inactive") query = query.Where(x => !x.IsActive);

            var total = await query.CountAsync(cancellationToken);
            var pageQuery = query.OrderBy(x => x.Name).ThenBy(x => x.Id)
                .Skip((currentPage - 1) * PageSize).Take(PageSize);
            var items = await ProductDetails(db, pageQuery, businessId, locationId).ToListAsync(cancellationToken);

            return Results.Ok(new { items, total, page = currentPage, pageSize = PageSize });
        });

        group.MapGet("/options", async (long businessId, bool? includeInactiveCategories,
            ClaimsPrincipal user, AppDbContext db, CancellationToken cancellationToken) =>
        {
            var denied = await RetailMasterEndpoints.CheckAccess(db, user, businessId, false, cancellationToken);
            if (denied is not null) return denied;

            var categoryQuery = db.RetailCategories.AsNoTracking()
                .Where(x => x.BusinessId == businessId);
            if (includeInactiveCategories != true)
                categoryQuery = categoryQuery.Where(x => x.IsActive);
            var categories = await categoryQuery.OrderBy(x => x.Name)
                .Select(x => new { x.Id, x.Name, x.ParentId, x.IsActive }).ToListAsync(cancellationToken);
            var units = await db.RetailUnits.AsNoTracking()
                .Where(x => x.BusinessId == businessId && x.IsActive)
                .OrderBy(x => x.Name)
                .Select(x => new { x.Id, x.Name, x.Code }).ToListAsync(cancellationToken);

            var brands = await db.RetailBrands.AsNoTracking()
                .Where(x => x.BusinessId == businessId && x.IsActive)
                .OrderBy(x => x.Name)
                .Select(x => new { x.Id, x.Name }).ToListAsync(cancellationToken);

            return Results.Ok(new { categories, units, brands });
        });

        group.MapGet("/lookup", async (long businessId, string? barcode,
            ClaimsPrincipal user, AppDbContext db, CancellationToken cancellationToken) =>
        {
            var denied = await RetailMasterEndpoints.CheckAccess(db, user, businessId, false, cancellationToken);
            if (denied is not null) return denied;

            var code = barcode?.Trim().ToUpperInvariant() ?? "";
            if (!ValidBarcode(code))
                return Results.BadRequest(new { message = "Enter a valid barcode." });

            var matches = db.RetailProducts.AsNoTracking()
                .Where(x => x.BusinessId == businessId && x.Barcode == code && x.IsActive);
            var product = await ProductDetails(db, matches, businessId).SingleOrDefaultAsync(cancellationToken);
            return product is null ? Results.NotFound() : Results.Ok(product);
        });

        group.MapGet("/{productId:long}", async (long businessId, long productId,
            ClaimsPrincipal user, AppDbContext db, CancellationToken cancellationToken) =>
        {
            var denied = await RetailMasterEndpoints.CheckAccess(db, user, businessId, false, cancellationToken);
            if (denied is not null) return denied;

            var matches = db.RetailProducts.AsNoTracking()
                .Where(x => x.BusinessId == businessId && x.Id == productId);
            var product = await ProductDetails(db, matches, businessId).SingleOrDefaultAsync(cancellationToken);
            return product is null ? Results.NotFound() : Results.Ok(product);
        });

        group.MapPost("", async (long businessId, ProductRequest request,
            ClaimsPrincipal user, AppDbContext db) =>
        {
            var denied = await RetailMasterEndpoints.CheckAccess(db, user, businessId, true);
            if (denied is not null) return denied;

            var autoSku = string.IsNullOrWhiteSpace(request.Sku);
            var error = Validate(request, allowAutoSku: true);
            if (error is not null) return Results.BadRequest(new { message = error });

            var sku = autoSku ? "" : request.Sku!.Trim().ToUpperInvariant();
            var barcode = NormalizeBarcode(request.Barcode);
            if (await db.RetailProducts.AnyAsync(x => x.BusinessId == businessId &&
                (x.Sku == sku || barcode != null && x.Barcode == barcode)))
                return Results.Conflict(new { message = "SKU or barcode is already used in this business." });

            if (!await ValidReferences(db, businessId, request.CategoryId, request.UnitId, request.BrandId, true))
                return Results.BadRequest(new { message = "Select an active category and unit from this business." });

            if (!await ValidConversions(db, businessId, request.UnitId, request.Conversions, []))
                return Results.BadRequest(new { message = "Choose distinct active alternate units, valid conversion factors and positive optional sale prices." });

            var product = new RetailProduct
            {
                BusinessId = businessId,
                Name = request.Name!.Trim(),
                Sku = sku,
                Barcode = barcode,
                CategoryId = request.CategoryId,
                UnitId = request.UnitId,
                BrandId = request.BrandId,
                SalePrice = request.SalePrice,
                ReorderLevel = request.ReorderLevel,
                TaxClassification = NormalizeCode(request.TaxClassification),
                IsActive = request.IsActive ?? true
            };

            ApplyConversions(product, request.Conversions);
            await using var transaction = await db.Database.BeginTransactionAsync();
            db.RetailProducts.Add(product);
            try
            {
                await db.SaveChangesAsync();
                if (autoSku)
                {
                    product.Sku = $"AUTO-{product.Id}";
                    await db.SaveChangesAsync();
                }
                await transaction.CommitAsync();
            }
            catch (DbUpdateException exception) when (RetailModule.IsDuplicateKey(exception))
            {
                return Results.Conflict(new { message = "SKU or barcode is already used in this business." });
            }

            return Results.Created($"/api/businesses/{businessId}/products/{product.Id}",
                new { product.Id });
        });

        group.MapPut("/{productId:long}", async (long businessId, long productId,
            ProductRequest request, ClaimsPrincipal user, AppDbContext db) =>
        {
            var denied = await RetailMasterEndpoints.CheckAccess(db, user, businessId, true);
            if (denied is not null) return denied;

            await using var transaction = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable);
            try
            {
                var product = await db.RetailProducts.Include(x => x.Conversions).SingleOrDefaultAsync(
                    x => x.BusinessId == businessId && x.Id == productId);
                if (product is null) return Results.NotFound();

                var error = Validate(request);
                if (error is not null || request.IsActive is null)
                    return Results.BadRequest(new { message = error ?? "Select a product status." });

                var sku = request.Sku!.Trim().ToUpperInvariant();
                var barcode = NormalizeBarcode(request.Barcode);
                if (await db.RetailProducts.AnyAsync(x => x.BusinessId == businessId &&
                    x.Id != productId && (x.Sku == sku || barcode != null && x.Barcode == barcode)))
                    return Results.Conflict(new { message = "SKU or barcode is already used in this business." });

                // Existing active products may retain inactive historical masters when editing.
                // Newly selected masters (or reactivating a product) must still be active.
                var retainExisting = product.IsActive && request.IsActive.Value;
                if (!await ValidReferences(db, businessId, request.CategoryId, request.UnitId,
                    request.BrandId, request.IsActive.Value,
                    retainExisting ? product.CategoryId : null,
                    retainExisting ? product.UnitId : null,
                    retainExisting ? product.BrandId : null))
                    return Results.BadRequest(new { message = "Select a valid category and unit from this business. Active products need active masters." });

                if (product.UnitId != request.UnitId && product.Conversions.Count > 0)
                    return Results.Conflict(new { message = "Remove alternate units and save before changing the base stock unit." });
                if (!await ValidConversions(db, businessId, request.UnitId, request.Conversions, product.Conversions))
                    return Results.BadRequest(new { message = "Choose distinct active alternate units, valid conversion factors and positive optional sale prices." });

                if (product.UnitId != request.UnitId &&
                    (await db.RetailPurchaseOrderLines.AsNoTracking().AnyAsync(x => x.ProductId == productId) ||
                     await db.RetailStockBalances.AsNoTracking().AnyAsync(x =>
                         x.BusinessId == businessId && x.ProductId == productId) ||
                     await db.RetailStockMovements.AsNoTracking().AnyAsync(x =>
                         x.BusinessId == businessId && x.ProductId == productId) ||
                     await db.RetailStockAdjustments.AsNoTracking().AnyAsync(x =>
                         x.BusinessId == businessId && x.ProductId == productId) ||
                     await db.Set<RetailCashSaleLine>().AsNoTracking().AnyAsync(x => x.ProductId == productId)))
                    return Results.Conflict(new { message =
                        "Stock unit cannot change after a purchase, stock entry, adjustment or sale. Create a new product for a different base unit." });

                product.Name = request.Name!.Trim();
                product.Sku = sku;
                product.Barcode = barcode;
                product.CategoryId = request.CategoryId;
                product.UnitId = request.UnitId;
                product.BrandId = request.BrandId;
                product.SalePrice = request.SalePrice;
                product.ReorderLevel = request.ReorderLevel;
                product.TaxClassification = NormalizeCode(request.TaxClassification);
                product.IsActive = request.IsActive.Value;
                ApplyConversions(product, request.Conversions);

                await db.SaveChangesAsync();
                await transaction.CommitAsync();
                return Results.Ok(new { product.Id });
            }
            catch (DbUpdateException exception) when (RetailModule.IsDuplicateKey(exception))
            {
                return Results.Conflict(new { message = "SKU or barcode is already used in this business." });
            }
            catch (Exception exception) when (StockPosting.IsSqlConflict(exception))
            {
                return Results.Conflict(new { message = "Product or stock is being updated. Refresh and retry." });
            }
        });
    }

    private static IQueryable<ProductItem> ProductDetails(AppDbContext db,
        IQueryable<RetailProduct> source, long businessId, long? locationId = null) =>
        from product in source
        join category in db.RetailCategories.AsNoTracking()
            on product.CategoryId equals category.Id
        join unit in db.RetailUnits.AsNoTracking()
            on product.UnitId equals unit.Id
        where product.BusinessId == businessId &&
              category.BusinessId == businessId && unit.BusinessId == businessId
        select new ProductItem(product.Id, product.Name, product.Sku, product.Barcode,
            product.CategoryId, category.Name, product.UnitId, unit.Code,
            product.SalePrice, product.TaxClassification, product.IsActive, product.ReorderLevel,
            product.BrandId,
            db.RetailBrands.Where(brand => brand.Id == product.BrandId &&
                brand.BusinessId == businessId).Select(brand => brand.Name).FirstOrDefault(),
            product.Conversions.OrderBy(x => x.UnitId)
                .Select(x => new ConversionItem(x.UnitId, x.Factor,
                    db.RetailUnits.Where(u => u.Id == x.UnitId && u.BusinessId == businessId)
                        .Select(u => u.Code).FirstOrDefault(),
                    db.RetailUnits.Any(u => u.Id == x.UnitId && u.BusinessId == businessId && u.IsActive),
                    x.SalePrice)).ToList(),
             locationId.HasValue ? (db.RetailStockBalances.Where(balance =>
                 balance.BusinessId == businessId && balance.LocationId == locationId &&
                 balance.ProductId == product.Id).Select(balance => (decimal?)balance.Quantity).FirstOrDefault() ?? 0m) : null);

    private static async Task<bool> ValidReferences(AppDbContext db, long businessId,
        long categoryId, long unitId, long? brandId, bool requireActive,
        long? retainedCategoryId = null, long? retainedUnitId = null,
        long? retainedBrandId = null)
    {
        if (categoryId <= 0 || unitId <= 0) return false;

        var category = await db.RetailCategories.AnyAsync(x =>
            x.Id == categoryId && x.BusinessId == businessId &&
            (!requireActive || x.IsActive || x.Id == retainedCategoryId));
        if (!category) return false;

        if (!await db.RetailUnits.AnyAsync(x =>
            x.Id == unitId && x.BusinessId == businessId &&
            (!requireActive || x.IsActive || x.Id == retainedUnitId)))
            return false;
        return brandId is null || brandId > 0 && await db.RetailBrands.AnyAsync(x =>
            x.Id == brandId.Value && x.BusinessId == businessId &&
            (!requireActive || x.IsActive || x.Id == retainedBrandId));
    }

    private static async Task<bool> ValidConversions(AppDbContext db, long businessId, long baseUnitId,
        List<ConversionRequest>? requested, List<RetailProductUnit> existing)
    {
        if (requested is null) return true; // Existing clients omit this field.
        if (requested.Count > 10 || requested.Any(x => x is null || x.UnitId <= 0 ||
            x.UnitId == baseUnitId || x.Factor is < 0.001m or > 9999999.999m ||
            decimal.Round(x.Factor, 3) != x.Factor ||
            x.SalePrice is <= 0 or > 9999999999.99m ||
            x.SalePrice.HasValue && decimal.Round(x.SalePrice.Value, 2) != x.SalePrice.Value) ||
            requested.Select(x => x.UnitId).Distinct().Count() != requested.Count) return false;
        var ids = requested.Select(x => x.UnitId).ToArray();
        var units = await db.RetailUnits.AsNoTracking()
            .Where(x => x.BusinessId == businessId && ids.Contains(x.Id))
            .Select(x => new { x.Id, x.IsActive }).ToListAsync();
        return requested.All(x => units.Any(u => u.Id == x.UnitId &&
            (u.IsActive || existing.Any(e => e.UnitId == x.UnitId && e.Factor == x.Factor && e.SalePrice == x.SalePrice))));
    }

    private static void ApplyConversions(RetailProduct product, List<ConversionRequest>? requested)
    {
        if (requested is null) return;
        foreach (var removed in product.Conversions.Where(x => !requested.Any(r => r.UnitId == x.UnitId)).ToArray())
            product.Conversions.Remove(removed);
        foreach (var item in requested)
        {
            var existing = product.Conversions.SingleOrDefault(x => x.UnitId == item.UnitId);
            if (existing is null)
                product.Conversions.Add(new RetailProductUnit { UnitId = item.UnitId, Factor = item.Factor, SalePrice = item.SalePrice });
            else
            {
                existing.Factor = item.Factor;
                existing.SalePrice = item.SalePrice;
            }
        }
    }

    private static string? Validate(ProductRequest request, bool allowAutoSku = false)
    {
        var name = request.Name?.Trim() ?? "";
        var sku = NormalizeCode(request.Sku);
        var barcode = NormalizeBarcode(request.Barcode);
        var tax = NormalizeCode(request.TaxClassification);

        if (name.Length is < 2 or > 150) return "Product name must be 2–150 characters.";
        if ((!allowAutoSku || sku.Length > 0) &&
            (sku.Length is < 2 or > 50 || !sku.All(ch =>
                char.IsAsciiLetterOrDigit(ch) || ch is '-' or '_' or '.' or '/')))
            return "SKU must contain 2–50 letters, numbers, dots, hyphens, underscores or slashes.";
        if (request.Barcode is not null && request.Barcode.Trim().Length > 0 && barcode is null)
            return "Barcode must contain 3–64 non-space characters.";
        if (request.SalePrice is < 0 or > 9999999999.99m ||
            decimal.Round(request.SalePrice, 2) != request.SalePrice)
            return "Sale price must be non-negative with at most two decimal places.";
        if (request.ReorderLevel is < 0m or > 9999999999.999m ||
            request.ReorderLevel is not null &&
            decimal.Round(request.ReorderLevel.Value, 3) != request.ReorderLevel.Value)
            return "Reorder level must be non-negative with at most three decimal places.";
        if (tax.Length > 30 || !tax.All(ch =>
            char.IsAsciiLetterOrDigit(ch) || ch is '-' or '_'))
            return "Tax classification must be a code of at most 30 letters, numbers, hyphens or underscores.";
        if (request.CategoryId <= 0 || request.UnitId <= 0)
            return "Select a category and unit.";
        return null;
    }

    private static string NormalizeCode(string? value) => value?.Trim().ToUpperInvariant() ?? "";

    private static bool ValidBarcode(string value) =>
        value.Length is >= 3 and <= 64 && value.All(ch => ch >= '!' && ch <= '~');

    private static string? NormalizeBarcode(string? value)
    {
        var trimmed = NormalizeCode(value);
        return trimmed.Length == 0 ? null : ValidBarcode(trimmed) ? trimmed : null;
    }

    private sealed record ProductRequest(string? Name, string? Sku, string? Barcode,
        long CategoryId, long UnitId, decimal SalePrice, string? TaxClassification, bool? IsActive,
        decimal? ReorderLevel = null, long? BrandId = null, List<ConversionRequest>? Conversions = null);

    private sealed record ConversionRequest(long UnitId, decimal Factor, decimal? SalePrice = null);

    private sealed record ProductItem(long Id, string Name, string Sku, string? Barcode,
        long CategoryId, string CategoryName, long UnitId, string UnitCode,
        decimal SalePrice, string TaxClassification, bool IsActive, decimal? ReorderLevel,
        long? BrandId, string? BrandName, List<ConversionItem> Conversions, decimal? AvailableStock);

    private sealed record ConversionItem(long UnitId, decimal Factor, string? UnitCode, bool IsActive, decimal? SalePrice);
}
