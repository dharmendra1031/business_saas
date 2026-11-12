using System.Security.Claims;
using BusinessSaas.Api.Data;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;

namespace BusinessSaas.Api.Features;

public static class RetailMasterEndpoints
{
    private const int PageSize = 20;

    public static void MapRetailMasterEndpoints(this WebApplication app)
    {
        var categories = app.MapGroup("/api/businesses/{businessId:long}/categories")
            .RequireAuthorization();

        categories.MapGet("", async (long businessId, string? search, int page,
            ClaimsPrincipal user, AppDbContext db) =>
        {
            var denied = await CheckAccess(db, user, businessId, false);
            if (denied is not null) return denied;
            if (page is < 1 or > 100000) return Results.BadRequest(new { message = "Invalid page." });

            var term = search?.Trim().ToUpperInvariant() ?? "";
            if (term.Length > 80) return Results.BadRequest(new { message = "Search is too long." });

            var query = db.RetailCategories.AsNoTracking().Where(x => x.BusinessId == businessId);
            if (term.Length > 0) query = query.Where(x => x.NameKey.Contains(term));

            var total = await query.CountAsync();
            var items = await query.OrderBy(x => x.NameKey).ThenBy(x => x.Id)
                .Skip((page - 1) * PageSize).Take(PageSize)
                .Select(x => new { x.Id, x.Name, x.IsActive }).ToListAsync();

            return Results.Ok(new { items, total, page, pageSize = PageSize });
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

            var category = new RetailCategory { BusinessId = businessId, Name = name, NameKey = key };
            db.RetailCategories.Add(category);
            try { await db.SaveChangesAsync(); }
            catch (DbUpdateException error) when (IsDuplicate(error))
            {
                return Results.Conflict(new { message = "Category already exists." });
            }

            return Results.Created($"/api/businesses/{businessId}/categories/{category.Id}",
                new { category.Id, category.Name, category.IsActive });
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

            category.Name = name;
            category.NameKey = key;
            category.IsActive = request.IsActive.Value;
            try { await db.SaveChangesAsync(); }
            catch (DbUpdateException error) when (IsDuplicate(error))
            {
                return Results.Conflict(new { message = "Category already exists." });
            }
            return Results.Ok(new { category.Id, category.Name, category.IsActive });
        });

        var units = app.MapGroup("/api/businesses/{businessId:long}/units")
            .RequireAuthorization();

        units.MapGet("", async (long businessId, string? search, int page,
            ClaimsPrincipal user, AppDbContext db) =>
        {
            var denied = await CheckAccess(db, user, businessId, false);
            if (denied is not null) return denied;
            if (page is < 1 or > 100000) return Results.BadRequest(new { message = "Invalid page." });

            var term = search?.Trim().ToUpperInvariant() ?? "";
            if (term.Length > 60) return Results.BadRequest(new { message = "Search is too long." });

            var query = db.RetailUnits.AsNoTracking().Where(x => x.BusinessId == businessId);
            if (term.Length > 0)
                query = query.Where(x => x.NameKey.Contains(term) || x.Code.Contains(term));

            var total = await query.CountAsync();
            var items = await query.OrderBy(x => x.NameKey).ThenBy(x => x.Id)
                .Skip((page - 1) * PageSize).Take(PageSize)
                .Select(x => new { x.Id, x.Code, x.Name, x.IsActive }).ToListAsync();

            return Results.Ok(new { items, total, page, pageSize = PageSize });
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
            catch (DbUpdateException error) when (IsDuplicate(error))
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

            unit.Name = name;
            unit.NameKey = key;
            unit.Code = code;
            unit.IsActive = request.IsActive.Value;
            try { await db.SaveChangesAsync(); }
            catch (DbUpdateException error) when (IsDuplicate(error))
            {
                return Results.Conflict(new { message = "Unit code or name already exists." });
            }
            return Results.Ok(new { unit.Id, unit.Code, unit.Name, unit.IsActive });
        });
    }

    private static bool ValidUnit(string name, string code) =>
        name.Length is >= 2 and <= 60 &&
        code.Length is >= 1 and <= 12 &&
        code.All(ch => char.IsAsciiLetterOrDigit(ch) || ch == '-');

    private static bool IsDuplicate(DbUpdateException error) =>
        error.InnerException is SqlException { Number: 2601 or 2627 };

    private static async Task<IResult?> CheckAccess(AppDbContext db, ClaimsPrincipal user,
        long businessId, bool write)
    {
        if (!long.TryParse(user.FindFirstValue(ClaimTypes.NameIdentifier), out var userId))
            return Results.NotFound();

        var role = await db.BusinessUsers.AsNoTracking()
            .Where(x => x.BusinessId == businessId && x.UserId == userId)
            .Select(x => x.RoleCode)
            .SingleOrDefaultAsync();
        if (role is not ("Owner" or "Manager"))
            return Results.NotFound();

        if (write && !await db.BusinessSubscriptions.AsNoTracking().AnyAsync(x =>
                x.BusinessId == businessId &&
                (x.Status == "Active" || x.Status == "Trial" && x.TrialEndsAtUtc > DateTime.UtcNow)))
            return Results.Problem("Subscription is not active. Editing is unavailable.",
                statusCode: StatusCodes.Status403Forbidden);

        return null;
    }

    private sealed record CategoryRequest(string? Name, bool? IsActive);
    private sealed record UnitRequest(string? Code, string? Name, bool? IsActive);
}
