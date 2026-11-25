using System.Security.Claims;
using BusinessSaas.Api.Data;
using Microsoft.EntityFrameworkCore;

namespace BusinessSaas.Api.Features;

public static class RetailMasterEndpoints
{
    private const int PageSize = 20;

    public static void MapRetailMasterEndpoints(this WebApplication app)
    {
        var categories = app.MapGroup("/api/businesses/{businessId:long}/categories")
            .RequireAuthorization();

        categories.MapGet("", async (long businessId, string? search, int? page,
            int? level, long? parentId, ClaimsPrincipal user, AppDbContext db) =>
        {
            var denied = await CheckAccess(db, user, businessId, false);
            if (denied is not null) return denied;
            var currentPage = page ?? 1;
            if (currentPage is < 1 or > 100000 || level is < 1 or > 3 || parentId is <= 0)
                return Results.BadRequest(new { message = "Invalid category page, level or parent." });

            var term = search?.Trim().ToUpperInvariant() ?? "";
            if (term.Length > 80) return Results.BadRequest(new { message = "Search is too long." });

            var query = db.RetailCategories.AsNoTracking().Where(x => x.BusinessId == businessId);
            if (level == 1) query = query.Where(x => x.ParentId == null);
            if (level == 2) query = query.Where(x => x.ParentId != null &&
                db.RetailCategories.Any(parent => parent.Id == x.ParentId &&
                    parent.BusinessId == businessId && parent.ParentId == null));
            if (level == 3) query = query.Where(x => x.ParentId != null &&
                db.RetailCategories.Any(parent => parent.Id == x.ParentId &&
                    parent.BusinessId == businessId && parent.ParentId != null));
            if (parentId.HasValue) query = query.Where(x => x.ParentId == parentId.Value);
            if (term.Length > 0) query = query.Where(x => x.NameKey.Contains(term));

            var total = await query.CountAsync();
            var items = await query.OrderBy(x => x.NameKey).ThenBy(x => x.Id)
                .Skip((currentPage - 1) * PageSize).Take(PageSize)
                .Select(x => new { x.Id, x.Name, x.ParentId, x.IsActive }).ToListAsync();

            return Results.Ok(new { items, total, page = currentPage, pageSize = PageSize });
        });

        categories.MapPost("", async (long businessId, CategoryRequest request,
            ClaimsPrincipal user, AppDbContext db) =>
        {
            var denied = await CheckAccess(db, user, businessId, true);
            if (denied is not null) return denied;

            var name = request.Name?.Trim() ?? "";
            if (name.Length is < 2 or > 80)
                return Results.BadRequest(new { message = "Category name must be 2–80 characters." });

            var key = name.ToUpperInvariant();
            if (await db.RetailCategories.AnyAsync(x => x.BusinessId == businessId && x.NameKey == key))
                return Results.Conflict(new { message = "Category already exists." });

            var parentError = await ValidateParent(db, businessId, request.ParentId, null);
            if (parentError is not null) return Results.BadRequest(new { message = parentError });
            var category = new RetailCategory
            {
                BusinessId = businessId, Name = name, NameKey = key, ParentId = request.ParentId
            };
            db.RetailCategories.Add(category);
            try { await db.SaveChangesAsync(); }
            catch (DbUpdateException error) when (RetailModule.IsDuplicateKey(error))
            {
                return Results.Conflict(new { message = "Category already exists." });
            }

            return Results.Created($"/api/businesses/{businessId}/categories/{category.Id}",
                new { category.Id, category.Name, category.ParentId, category.IsActive });
        });

        categories.MapPut("/{categoryId:long}", async (long businessId, long categoryId,
            CategoryRequest request, ClaimsPrincipal user, AppDbContext db) =>
        {
            var denied = await CheckAccess(db, user, businessId, true);
            if (denied is not null) return denied;

            var name = request.Name?.Trim() ?? "";
            if (name.Length is < 2 or > 80 || request.IsActive is null)
                return Results.BadRequest(new { message = "Enter a valid name and status." });

            var category = await db.RetailCategories.SingleOrDefaultAsync(
                x => x.Id == categoryId && x.BusinessId == businessId);
            if (category is null) return Results.NotFound();

            var key = name.ToUpperInvariant();
            if (await db.RetailCategories.AnyAsync(x =>
                x.BusinessId == businessId && x.Id != categoryId && x.NameKey == key))
                return Results.Conflict(new { message = "Category already exists." });

            var parentError = await ValidateParent(db, businessId, request.ParentId, categoryId);
            if (parentError is not null) return Results.BadRequest(new { message = parentError });
            if (!request.IsActive.Value && await db.RetailCategories.AsNoTracking().AnyAsync(x =>
                    x.BusinessId == businessId && x.ParentId == categoryId && x.IsActive))
                return Results.Conflict(new { message =
                    "Deactivate or move active subcategories before deactivating their parent." });
            category.Name = name;
            category.NameKey = key;
            category.ParentId = request.ParentId;
            category.IsActive = request.IsActive.Value;
            try { await db.SaveChangesAsync(); }
            catch (DbUpdateException error) when (RetailModule.IsDuplicateKey(error))
            {
                return Results.Conflict(new { message = "Category already exists." });
            }
            return Results.Ok(new { category.Id, category.Name, category.ParentId, category.IsActive });
        });

        var units = app.MapGroup("/api/businesses/{businessId:long}/units")
            .RequireAuthorization();

        units.MapGet("", async (long businessId, string? search, int? page,
            ClaimsPrincipal user, AppDbContext db) =>
        {
            var denied = await CheckAccess(db, user, businessId, false);
            if (denied is not null) return denied;
            var currentPage = page ?? 1;
            if (currentPage is < 1 or > 100000) return Results.BadRequest(new { message = "Invalid page." });

            var term = search?.Trim().ToUpperInvariant() ?? "";
            if (term.Length > 60) return Results.BadRequest(new { message = "Search is too long." });

            var query = db.RetailUnits.AsNoTracking().Where(x => x.BusinessId == businessId);
            if (term.Length > 0)
                query = query.Where(x => x.NameKey.Contains(term) || x.Code.Contains(term));

            var total = await query.CountAsync();
            var items = await query.OrderBy(x => x.NameKey).ThenBy(x => x.Id)
                .Skip((currentPage - 1) * PageSize).Take(PageSize)
                .Select(x => new { x.Id, x.Code, x.Name, x.IsActive }).ToListAsync();

            return Results.Ok(new { items, total, page = currentPage, pageSize = PageSize });
        });

        units.MapPost("", async (long businessId, UnitRequest request,
            ClaimsPrincipal user, AppDbContext db) =>
        {
            var denied = await CheckAccess(db, user, businessId, true);
            if (denied is not null) return denied;

            var name = request.Name?.Trim() ?? "";
            var code = request.Code?.Trim().ToUpperInvariant() ?? "";
            if (!ValidUnit(name, code))
                return Results.BadRequest(new { message = "Enter a unit name (2–60) and code (1–12 letters, numbers or hyphens)." });

            var key = name.ToUpperInvariant();
            if (await db.RetailUnits.AnyAsync(x =>
                x.BusinessId == businessId && (x.Code == code || x.NameKey == key)))
                return Results.Conflict(new { message = "Unit code or name already exists." });

            var unit = new RetailUnit
            {
                BusinessId = businessId, Code = code, Name = name, NameKey = key
            };
            db.RetailUnits.Add(unit);
            try { await db.SaveChangesAsync(); }
            catch (DbUpdateException error) when (RetailModule.IsDuplicateKey(error))
            {
                return Results.Conflict(new { message = "Unit code or name already exists." });
            }
            return Results.Created($"/api/businesses/{businessId}/units/{unit.Id}",
                new { unit.Id, unit.Code, unit.Name, unit.IsActive });
        });

        units.MapPut("/{unitId:long}", async (long businessId, long unitId,
            UnitRequest request, ClaimsPrincipal user, AppDbContext db) =>
        {
            var denied = await CheckAccess(db, user, businessId, true);
            if (denied is not null) return denied;

            var name = request.Name?.Trim() ?? "";
            var code = request.Code?.Trim().ToUpperInvariant() ?? "";
            if (!ValidUnit(name, code) || request.IsActive is null)
                return Results.BadRequest(new { message = "Enter a valid unit name, code and status." });

            var unit = await db.RetailUnits.SingleOrDefaultAsync(
                x => x.Id == unitId && x.BusinessId == businessId);
            if (unit is null) return Results.NotFound();

            var key = name.ToUpperInvariant();
            if (await db.RetailUnits.AnyAsync(x => x.BusinessId == businessId &&
                x.Id != unitId && (x.Code == code || x.NameKey == key)))
                return Results.Conflict(new { message = "Unit code or name already exists." });

            if (unit.Code != code &&
                (await db.RetailProducts.AsNoTracking().AnyAsync(x => x.BusinessId == businessId && x.UnitId == unitId) ||
                 await db.RetailProductUnits.AsNoTracking().AnyAsync(x => x.UnitId == unitId)))
                return Results.Conflict(new { message =
                    "Unit code is already assigned to products and cannot change. Name and status can still be edited." });

            unit.Name = name;
            unit.NameKey = key;
            unit.Code = code;
            unit.IsActive = request.IsActive.Value;
            try { await db.SaveChangesAsync(); }
            catch (DbUpdateException error) when (RetailModule.IsDuplicateKey(error))
            {
                return Results.Conflict(new { message = "Unit code or name already exists." });
            }
            return Results.Ok(new { unit.Id, unit.Code, unit.Name, unit.IsActive });
        });

        var brands = app.MapGroup("/api/businesses/{businessId:long}/brands")
            .RequireAuthorization();

        brands.MapGet("", async (long businessId, string? search, int? page,
            ClaimsPrincipal user, AppDbContext db) =>
        {
            var denied = await CheckAccess(db, user, businessId, false);
            if (denied is not null) return denied;
            var currentPage = page ?? 1;
            if (currentPage is < 1 or > 100000)
                return Results.BadRequest(new { message = "Invalid page." });
            var term = search?.Trim().ToUpperInvariant() ?? "";
            if (term.Length > 80) return Results.BadRequest(new { message = "Search is too long." });
            var query = db.RetailBrands.AsNoTracking().Where(x => x.BusinessId == businessId);
            if (term.Length > 0) query = query.Where(x => x.NameKey.Contains(term));
            var total = await query.CountAsync();
            var items = await query.OrderBy(x => x.NameKey).ThenBy(x => x.Id)
                .Skip((currentPage - 1) * PageSize).Take(PageSize)
                .Select(x => new { x.Id, x.Name, x.IsActive }).ToListAsync();
            return Results.Ok(new { items, total, page = currentPage, pageSize = PageSize });
        });

        brands.MapPost("", async (long businessId, BrandRequest request,
            ClaimsPrincipal user, AppDbContext db) =>
        {
            var denied = await CheckAccess(db, user, businessId, true);
            if (denied is not null) return denied;
            var name = request.Name?.Trim() ?? "";
            if (name.Length is < 2 or > 80)
                return Results.BadRequest(new { message = "Brand name must be 2–80 characters." });
            var key = name.ToUpperInvariant();
            if (await db.RetailBrands.AnyAsync(x => x.BusinessId == businessId && x.NameKey == key))
                return Results.Conflict(new { message = "Brand already exists." });
            var brand = new RetailBrand { BusinessId = businessId, Name = name, NameKey = key };
            db.RetailBrands.Add(brand);
            try { await db.SaveChangesAsync(); }
            catch (DbUpdateException error) when (RetailModule.IsDuplicateKey(error))
            {
                return Results.Conflict(new { message = "Brand already exists." });
            }
            return Results.Created($"/api/businesses/{businessId}/brands/{brand.Id}",
                new { brand.Id, brand.Name, brand.IsActive });
        });

        brands.MapPut("/{brandId:long}", async (long businessId, long brandId,
            BrandRequest request, ClaimsPrincipal user, AppDbContext db) =>
        {
            var denied = await CheckAccess(db, user, businessId, true);
            if (denied is not null) return denied;
            var name = request.Name?.Trim() ?? "";
            if (name.Length is < 2 or > 80 || request.IsActive is null)
                return Results.BadRequest(new { message = "Enter a valid brand name and status." });
            var brand = await db.RetailBrands.SingleOrDefaultAsync(x =>
                x.Id == brandId && x.BusinessId == businessId);
            if (brand is null) return Results.NotFound();
            var key = name.ToUpperInvariant();
            if (await db.RetailBrands.AnyAsync(x => x.BusinessId == businessId &&
                x.Id != brandId && x.NameKey == key))
                return Results.Conflict(new { message = "Brand already exists." });
            brand.Name = name;
            brand.NameKey = key;
            brand.IsActive = request.IsActive.Value;
            try { await db.SaveChangesAsync(); }
            catch (DbUpdateException error) when (RetailModule.IsDuplicateKey(error))
            {
                return Results.Conflict(new { message = "Brand already exists." });
            }
            return Results.Ok(new { brand.Id, brand.Name, brand.IsActive });
        });
    }

    private static bool ValidUnit(string name, string code) =>
        name.Length is >= 2 and <= 60 &&
        code.Length is >= 1 and <= 12 &&
        code.All(ch => char.IsAsciiLetterOrDigit(ch) || ch == '-');


    // Flat categories remain valid. Nested categories support three levels at most.
    private static async Task<string?> ValidateParent(AppDbContext db, long businessId,
        long? parentId, long? categoryId)
    {
        if (parentId is null && categoryId is null) return null;
        if (parentId is <= 0) return "Select a valid parent category.";
        var nodes = await db.RetailCategories.AsNoTracking()
            .Where(x => x.BusinessId == businessId)
            .Select(x => new { x.Id, x.ParentId, x.IsActive }).ToListAsync();
        var byId = nodes.ToDictionary(x => x.Id);

        var depth = 0;
        var seen = new HashSet<long>();
        for (var current = parentId; current is not null;)
        {
            if (!byId.TryGetValue(current.Value, out var ancestor) || !ancestor.IsActive)
                return "Choose an active parent category from this business.";
            if (current == categoryId || !seen.Add(current.Value))
                return "A category cannot be its own parent or descendant.";
            depth++;
            if (depth > 2) return "Only Group, Subgroup and Category levels are supported.";
            current = ancestor.ParentId;
        }

        if (categoryId is not null)
        {
            foreach (var node in nodes)
            {
                var below = 0;
                var current = node.ParentId;
                while (current is not null && below <= 3)
                {
                    below++;
                    if (current == categoryId && depth + 1 + below > 3)
                        return "Move child categories first; hierarchy supports only three levels.";
                    if (!byId.TryGetValue(current.Value, out var ancestor)) break;
                    current = ancestor.ParentId;
                }
            }
        }
        return null;
    }

    internal static async Task<IResult?> CheckAccess(AppDbContext db, ClaimsPrincipal user,
        long businessId, bool write, CancellationToken cancellationToken = default, long? locationId = null, bool ownerOnly = false)
    {
        if (!long.TryParse(user.FindFirstValue(ClaimTypes.NameIdentifier), out var userId))
            return Results.NotFound();

        var role = await (from member in db.BusinessUsers.AsNoTracking()
                          join business in db.Businesses.AsNoTracking()
                              on member.BusinessId equals business.Id
                          join type in db.BusinessTypes.AsNoTracking()
                              on business.BusinessTypeId equals type.Id
                          where member.BusinessId == businessId && member.UserId == userId
                              && type.Code == "RETAIL"
                          select member.RoleCode).SingleOrDefaultAsync(cancellationToken);
        if (role is not ("Owner" or "Manager"))
            return Results.NotFound();

        if (write && !await db.BusinessSubscriptions.AsNoTracking().AnyAsync(x =>
                x.BusinessId == businessId &&
                (x.Status == "Active" && x.PaidStartsAtUtc <= DateTime.UtcNow &&
                    x.PaidEndsAtUtc > DateTime.UtcNow && x.PaidPlanVersionId != null ||
                    x.Status == "Trial" && x.TrialEndsAtUtc > DateTime.UtcNow), cancellationToken))
            return Results.Problem("Subscription is not active. Editing is unavailable.",
                statusCode: StatusCodes.Status403Forbidden);

        if (locationId.HasValue && !await db.BusinessLocations.AsNoTracking().AnyAsync(x =>
                x.BusinessId == businessId && x.Id == locationId.Value &&
                (role == "Owner" || x.IsPrimary), cancellationToken))
            return Results.NotFound();

        return ownerOnly && role != "Owner" ? Results.NotFound() : null;
    }

    private sealed record CategoryRequest(string? Name, bool? IsActive, long? ParentId = null);
    private sealed record BrandRequest(string? Name, bool? IsActive);
    private sealed record UnitRequest(string? Code, string? Name, bool? IsActive);
}
