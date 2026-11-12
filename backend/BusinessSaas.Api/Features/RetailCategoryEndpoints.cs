using System.Security.Claims;
using BusinessSaas.Api.Data;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;

namespace BusinessSaas.Api.Features;

public static class RetailCategoryEndpoints
{
    public static void MapRetailCategoryEndpoints(this WebApplication app)
    {
        var group = app.MapGroup("/api/businesses/{businessId:long}/categories")
            .RequireAuthorization();

        group.MapGet("", async (long businessId, ClaimsPrincipal user, AppDbContext db) =>
        {
            var role = await GetRole(user, businessId, db);
            if (role is null)
                return Results.NotFound();

            var categories = await db.ProductCategories.AsNoTracking()
                .Where(x => x.BusinessId == businessId)
                .OrderBy(x => x.Name)
                .Select(x => new { x.Id, x.Name })
                .ToListAsync();

            return Results.Ok(categories);
        });

        group.MapPost("", async (long businessId, CategoryRequest request,
            ClaimsPrincipal user, AppDbContext db) =>
        {
            var role = await GetRole(user, businessId, db);
            if (role is null)
                return Results.NotFound();
            if (role is not ("Owner" or "Manager") || !await CanOperate(businessId, db))
                return Results.Forbid();

            var name = request.Name?.Trim() ?? "";
            if (name.Length is < 2 or > 100)
                return Results.BadRequest(new { message = "Category name must be 2–100 characters." });

            var nameKey = name.ToUpperInvariant();
            if (await db.ProductCategories.AnyAsync(x =>
                x.BusinessId == businessId && x.NameKey == nameKey))
                return Duplicate();

            var category = new ProductCategory
            {
                BusinessId = businessId,
                Name = name,
                NameKey = nameKey
            };
            db.ProductCategories.Add(category);

            try
            {
                await db.SaveChangesAsync();
            }
            catch (DbUpdateException error) when (
                error.InnerException is SqlException { Number: 2601 or 2627 })
            {
                return Duplicate();
            }

            return Results.Ok(new { category.Id, category.Name });
        });

        group.MapPut("{categoryId:long}", async (long businessId, long categoryId,
            CategoryRequest request, ClaimsPrincipal user, AppDbContext db) =>
        {
            var role = await GetRole(user, businessId, db);
            if (role is null)
                return Results.NotFound();
            if (role is not ("Owner" or "Manager") || !await CanOperate(businessId, db))
                return Results.Forbid();

            var category = await db.ProductCategories.SingleOrDefaultAsync(x =>
                x.Id == categoryId && x.BusinessId == businessId);
            if (category is null)
                return Results.NotFound();

            var name = request.Name?.Trim() ?? "";
            if (name.Length is < 2 or > 100)
                return Results.BadRequest(new { message = "Category name must be 2–100 characters." });

            var nameKey = name.ToUpperInvariant();
            if (await db.ProductCategories.AnyAsync(x => x.BusinessId == businessId &&
                x.NameKey == nameKey && x.Id != categoryId))
                return Duplicate();

            category.Name = name;
            category.NameKey = nameKey;

            try
            {
                await db.SaveChangesAsync();
            }
            catch (DbUpdateException error) when (
                error.InnerException is SqlException { Number: 2601 or 2627 })
            {
                return Duplicate();
            }

            return Results.Ok(new { category.Id, category.Name });
        });
    }

    private static Task<string?> GetRole(ClaimsPrincipal principal, long businessId, AppDbContext db)
    {
        if (!long.TryParse(principal.FindFirstValue(ClaimTypes.NameIdentifier), out var userId))
            return Task.FromResult<string?>(null);

        return (from member in db.BusinessUsers.AsNoTracking()
                join business in db.Businesses.AsNoTracking()
                    on member.BusinessId equals business.Id
                join type in db.BusinessTypes.AsNoTracking()
                    on business.BusinessTypeId equals type.Id
                where business.Id == businessId && member.UserId == userId
                    && type.Code == "RETAIL"
                    && (member.RoleCode == "Owner" || member.RoleCode == "Manager"
                        || member.RoleCode == "Cashier")
                select member.RoleCode).SingleOrDefaultAsync();
    }

    private static Task<bool> CanOperate(long businessId, AppDbContext db)
    {
        var now = DateTime.UtcNow;
        return db.BusinessSubscriptions.AsNoTracking()
            .AnyAsync(x => x.BusinessId == businessId &&
                (x.Status == "Active" || (x.Status == "Trial" && x.TrialEndsAtUtc > now)));
    }

    private static IResult Duplicate() =>
        Results.Conflict(new { message = "This category already exists in this business." });

    private sealed record CategoryRequest(string? Name);
}
