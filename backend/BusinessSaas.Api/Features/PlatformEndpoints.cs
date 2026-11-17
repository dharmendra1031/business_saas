using System.Security.Claims;
using BusinessSaas.Api.Data;
using Microsoft.EntityFrameworkCore;

namespace BusinessSaas.Api.Features;

public static class PlatformEndpoints
{
    public static void MapPlatformEndpoints(this WebApplication app)
    {
        var group = app.MapGroup("/api/platform").RequireAuthorization();

        group.MapGet("/access", (ClaimsPrincipal user, IConfiguration configuration) =>
        {
            var isOwner = PlatformOwner.IsOwner(user, configuration);
            return Results.Ok(new
            {
                roleCode = isOwner ? "SuperAdmin" : "Customer",
                isPlatformOwner = isOwner,
                canViewBusinesses = isOwner,
                canManageLicenses = isOwner
            });
        });

        group.MapGet("/license-access", (ClaimsPrincipal user, IConfiguration configuration) =>
            PlatformOwner.IsOwner(user, configuration)
                ? Results.Ok(new { canManageLicenses = true })
                : Results.Forbid());

        group.MapGet("/businesses", async (ClaimsPrincipal user, AppDbContext db, IConfiguration configuration,
            string? search, int page = 1, int pageSize = 20) =>
        {
            if (!PlatformOwner.IsOwner(user, configuration))
                return Results.Forbid();

            if (page is < 1 or > 100000 || pageSize is < 1 or > 50)
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
                                   SubscriptionStatus = subscription == null ? null : subscription.Status,
                                   TrialEndsAtUtc = subscription == null
                                       ? (DateTime?)null : subscription.TrialEndsAtUtc
                               }).Skip((page - 1) * pageSize).Take(pageSize).ToListAsync();

            return Results.Ok(new { items, total, page, pageSize });
        });

        group.MapGet("/businesses/{businessId:long}", async (
            long businessId, ClaimsPrincipal user, AppDbContext db, IConfiguration configuration) =>
        {
            if (!PlatformOwner.IsOwner(user, configuration))
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

}
