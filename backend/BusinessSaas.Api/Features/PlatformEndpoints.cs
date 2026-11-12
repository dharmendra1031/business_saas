using System.Security.Claims;
using BusinessSaas.Api.Data;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace BusinessSaas.Api.Features;

public static class PlatformEndpoints
{
    public static void MapPlatformEndpoints(this WebApplication app)
    {
        var group = app.MapGroup("/api/platform").RequireAuthorization();

        group.MapGet("/access", async (ClaimsPrincipal user, AppDbContext db) =>
        {
            var userId = CurrentUserId(user);
            var permissions = await db.Set<IdentityUserClaim<long>>().AsNoTracking()
                .Where(x => x.UserId == userId && x.ClaimType == "platform.permission")
                .Select(x => x.ClaimValue)
                .ToListAsync();

            return Results.Ok(new
            {
                canViewBusinesses = permissions.Contains("businesses.view"),
                canManageLicenses = permissions.Contains("licenses.manage")
            });
        });

        group.MapGet("/license-access", async (ClaimsPrincipal user, AppDbContext db) =>
            await HasPermission(user, db, "licenses.manage")
                ? Results.Ok(new { canManageLicenses = true })
                : Results.Forbid());

        group.MapGet("/businesses", async (ClaimsPrincipal user, AppDbContext db,
            string? search, int page = 1, int pageSize = 20) =>
        {
            if (!await HasPermission(user, db, "businesses.view"))
                return Results.Forbid();

            if (page < 1 || pageSize is < 1 or > 50)
                return Results.BadRequest(new { message = "Choose a valid page and page size (1–50)." });

            var term = search?.Trim() ?? "";
            if (term.Length > 100)
                return Results.BadRequest(new { message = "Search must be 100 characters or less." });

            var query = db.Businesses.AsNoTracking()
                .Where(x => term == "" || x.Name.Contains(term));
            var total = await query.CountAsync();

            var items = await (from business in query
                               join owner in db.Users.AsNoTracking()
                                   on business.CreatedByUserId equals owner.Id
                               join type in db.BusinessTypes.AsNoTracking()
                                   on business.BusinessTypeId equals type.Id
                               join sub in db.BusinessSubscriptions.AsNoTracking()
                                   on business.Id equals sub.BusinessId into subscriptions
                               from subscription in subscriptions.DefaultIfEmpty()
                               orderby business.Id descending
                               select new
                               {
                                   business.Id,
                                   business.Name,
                                   business.CountryCode,
                                   TypeName = type.Name,
                                   OwnerEmail = owner.Email,
                                   PlanCode = subscription == null ? null : subscription.PlanCode,
                                   SubscriptionStatus = subscription == null ? null : subscription.Status
                               }).Skip((page - 1) * pageSize).Take(pageSize).ToListAsync();

            return Results.Ok(new { items, total, page, pageSize });
        });

        group.MapGet("/businesses/{businessId:long}", async (
            long businessId, ClaimsPrincipal user, AppDbContext db) =>
        {
            if (!await HasPermission(user, db, "businesses.view"))
                return Results.Forbid();

            var details = await (from business in db.Businesses.AsNoTracking()
                                 join owner in db.Users.AsNoTracking()
                                     on business.CreatedByUserId equals owner.Id
                                 join type in db.BusinessTypes.AsNoTracking()
                                     on business.BusinessTypeId equals type.Id
                                 join sub in db.BusinessSubscriptions.AsNoTracking()
                                     on business.Id equals sub.BusinessId into subscriptions
                                 from subscription in subscriptions.DefaultIfEmpty()
                                 where business.Id == businessId
                                 select new
                                 {
                                     business.Id,
                                     business.Name,
                                     business.CountryCode,
                                     business.CurrencyCode,
                                     business.TimeZoneId,
                                     business.CreatedAtUtc,
                                     TypeName = type.Name,
                                     OwnerEmail = owner.Email,
                                     PlanCode = subscription == null ? null : subscription.PlanCode,
                                     SubscriptionStatus = subscription == null ? null : subscription.Status,
                                     TrialEndsAtUtc = subscription == null
                                         ? (DateTime?)null : subscription.TrialEndsAtUtc
                                 }).SingleOrDefaultAsync();

            return details is null ? Results.NotFound() : Results.Ok(details);
        });
    }

    private static long CurrentUserId(ClaimsPrincipal user) =>
        long.Parse(user.FindFirstValue(ClaimTypes.NameIdentifier)!);

    private static Task<bool> HasPermission(ClaimsPrincipal user, AppDbContext db, string permission) =>
        db.Set<IdentityUserClaim<long>>().AsNoTracking()
            .AnyAsync(x => x.UserId == CurrentUserId(user) &&
                x.ClaimType == "platform.permission" && x.ClaimValue == permission);
}
