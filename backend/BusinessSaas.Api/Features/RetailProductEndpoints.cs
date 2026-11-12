using System.Security.Claims;
using BusinessSaas.Api.Data;
using Microsoft.Data.SqlClient;
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
            long? categoryId, int? page, ClaimsPrincipal user, AppDbContext db) =>
        {
            var denied = await RetailMasterEndpoints.CheckAccess(db, user, businessId, false);
            if (denied is not null) return denied;

            var currentPage = page ?? 1;
            if (currentPage is < 1 or > 100000 || categoryId is <= 0)
                return Results.BadRequest(new { message = "Invalid page or category." });

            var term = search?.Trim().ToUpperInvariant() ?? "";
            if (term.Length > 150 || status is not (null or "" or "all" or "active" or "inactive"))
                return Results.BadRequest(new { message = "Invalid search or status filter." });

            var query = Products(db, businessId);
            if (term.Length > 0)
                query = query.Where(x => x.Name.ToUpper().Contains(term) ||
                    x.Sku.Contains(term) || x.Barcode != null && x.Barcode.Contains(term));
            if (categoryId is > 0) query = query.Where(x => x.CategoryId == categoryId);
            if (status == "active") query = query.Where(x => x.IsActive);
            if (status == "inactive") query = query.Where(x => !x.IsActive);

            var total = await query.CountAsync();
            var items = await query.OrderBy(x => x.Name).ThenBy(x => x.Id)
                .Skip((currentPage - 1) * PageSize).Take(PageSize).ToListAsync();

            return Results.Ok(new { items, total, page = currentPage, pageSize = PageSize });
        });

        group.MapGet("/lookup", async (long businessId, string? barcode,
            ClaimsPrincipal user, AppDbContext db) =>
        {
            var denied = await RetailMasterEndpoints.CheckAccess(db, user, businessId, false);
            if (denied is not null) return denied;

            var code = barcode?.Trim().ToUpperInvariant() ?? "";
            if (!ValidBarcode(code))
                return Results.BadRequest(new { message = "Enter a valid barcode." });

            var product = await Products(db, businessId)
                .SingleOrDefaultAsync(x => x.Barcode == code && x.IsActive);
            return product is null ? Results.NotFound() : Results.Ok(product);
        });

        group.MapGet("/{productId:long}", async (long businessId, long productId,
            ClaimsPrincipal user, AppDbContext db) =>
        {
            var denied = await RetailMasterEndpoints.CheckAccess(db, user, businessId, false);
            if (denied is not null) return denied;

            var product = await Products(db, businessId)
                .SingleOrDefaultAsync(x => x.Id == productId);
            return product is null ? Results.NotFound() : Results.Ok(product);
        });

        group.MapPost("", async (long businessId, ProductRequest request,
            ClaimsPrincipal user, AppDbContext db) =>
        {
            var denied = await RetailMasterEndpoints.CheckAccess(db, user, businessId, true);
            if (denied is not null) return denied;

            var error = Validate(request);
            if (error is not null) return Results.BadRequest(new { message = error });

            var sku = request.Sku!.Trim().ToUpperInvariant();
            var barcode = NormalizeBarcode(request.Barcode);
            if (await db.RetailProducts.AnyAsync(x => x.BusinessId == businessId &&
                (x.Sku == sku || barcode != null && x.Barcode == barcode)))
                return Results.Conflict(new { message = "SKU or barcode is already used in this business." });

            if (!await ValidReferences(db, businessId, request.CategoryId, request.UnitId, true))
                return Results.BadRequest(new { message = "Select an active category and unit from this business." });

            var product = new RetailProduct
            {
                BusinessId = businessId,
                Name = request.Name!.Trim(),
                Sku = sku,
                Barcode = barcode,
                CategoryId = request.CategoryId,
                UnitId = request.UnitId,
                SalePrice = request.SalePrice,
                TaxClassification = NormalizeCode(request.TaxClassification),
                IsActive = request.IsActive ?? true
            };

            db.RetailProducts.Add(product);
            try { await db.SaveChangesAsync(); }
            catch (DbUpdateException exception) when (IsDuplicate(exception))
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

            var product = await db.RetailProducts.SingleOrDefaultAsync(
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

            if (!await ValidReferences(db, businessId, request.CategoryId, request.UnitId,
                request.IsActive.Value))
                return Results.BadRequest(new { message = "Select a valid category and unit from this business. Active products need active masters." });

            product.Name = request.Name!.Trim();
            product.Sku = sku;
            product.Barcode = barcode;
            product.CategoryId = request.CategoryId;
            product.UnitId = request.UnitId;
            product.SalePrice = request.SalePrice;
            product.TaxClassification = NormalizeCode(request.TaxClassification);
            product.IsActive = request.IsActive.Value;

            try { await db.SaveChangesAsync(); }
            catch (DbUpdateException exception) when (IsDuplicate(exception))
            {
                return Results.Conflict(new { message = "SKU or barcode is already used in this business." });
            }

            return Results.Ok(new { product.Id });
        });
    }

    private static IQueryable<ProductItem> Products(AppDbContext db, long businessId) =>
        from product in db.RetailProducts.AsNoTracking()
        join category in db.RetailCategories.AsNoTracking()
            on product.CategoryId equals category.Id
        join unit in db.RetailUnits.AsNoTracking()
            on product.UnitId equals unit.Id
        where product.BusinessId == businessId &&
              category.BusinessId == businessId && unit.BusinessId == businessId
        select new ProductItem(product.Id, product.Name, product.Sku, product.Barcode,
            product.CategoryId, category.Name, product.UnitId, unit.Code,
            product.SalePrice, product.TaxClassification, product.IsActive);

    private static async Task<bool> ValidReferences(AppDbContext db, long businessId,
        long categoryId, long unitId, bool requireActive)
    {
        if (categoryId <= 0 || unitId <= 0) return false;

        var category = await db.RetailCategories.AnyAsync(x =>
            x.Id == categoryId && x.BusinessId == businessId && (!requireActive || x.IsActive));
        if (!category) return false;

        return await db.RetailUnits.AnyAsync(x =>
            x.Id == unitId && x.BusinessId == businessId && (!requireActive || x.IsActive));
    }

    private static string? Validate(ProductRequest request)
    {
        var name = request.Name?.Trim() ?? "";
        var sku = NormalizeCode(request.Sku);
        var barcode = NormalizeBarcode(request.Barcode);
        var tax = NormalizeCode(request.TaxClassification);

        if (name.Length is < 2 or > 150) return "Product name must be 2–150 characters.";
        if (sku.Length is < 2 or > 50 || !sku.All(ch =>
            char.IsAsciiLetterOrDigit(ch) || ch is '-' or '_' or '.' or '/'))
            return "SKU must contain 2–50 letters, numbers, dots, hyphens, underscores or slashes.";
        if (request.Barcode is not null && request.Barcode.Trim().Length > 0 && barcode is null)
            return "Barcode must contain 3–64 non-space characters.";
        if (request.SalePrice is < 0 or > 9999999999.99m ||
            decimal.Round(request.SalePrice, 2) != request.SalePrice)
            return "Sale price must be non-negative with at most two decimal places.";
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

    private static bool IsDuplicate(DbUpdateException exception) =>
        exception.InnerException is SqlException { Number: 2601 or 2627 };

    private sealed record ProductRequest(string? Name, string? Sku, string? Barcode,
        long CategoryId, long UnitId, decimal SalePrice, string? TaxClassification, bool? IsActive);

    private sealed record ProductItem(long Id, string Name, string Sku, string? Barcode,
        long CategoryId, string CategoryName, long UnitId, string UnitCode,
        decimal SalePrice, string TaxClassification, bool IsActive);
}
