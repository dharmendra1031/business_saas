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
            var isConfiguredOwner = PlatformOwner.IsConfiguredOwner(user, configuration);
            var isVerified = PlatformOwner.IsOwner(user, configuration);
            return Results.Ok(new
            {
                roleCode = isConfiguredOwner ? "SuperAdmin" : "Customer",
                isPlatformOwner = isConfiguredOwner,
                mfaVerified = isVerified,
                canViewBusinesses = isVerified,
                canManageLicenses = isVerified
            });
        });

        group.MapGet("/audit", async (ClaimsPrincipal user, AppDbContext db,
            IConfiguration configuration, int page = 1, int pageSize = 20) =>
        {
            if (!PlatformOwner.IsOwner(user, configuration))
                return Results.Forbid();

            if (page is < 1 or > 100000 || pageSize is < 1 or > 50)
                return Results.BadRequest(new { message = "Choose a valid page and page size (1–50)." });

            var total = await db.PlatformAuditEvents.CountAsync();
            var items = await db.PlatformAuditEvents.AsNoTracking()
                .OrderByDescending(x => x.Id)
                .Skip((page - 1) * pageSize).Take(pageSize)
                .Select(x => new
                {
                    x.Id, x.ActorUserId, x.Action, x.TargetType,
                    x.TargetId, x.Reason, x.CreatedAtUtc
                }).ToListAsync();

            return Results.Ok(new { items, total, page, pageSize });
        });

        group.MapGet("/license-access", (ClaimsPrincipal user, IConfiguration configuration) =>
            PlatformOwner.IsOwner(user, configuration)
                ? Results.Ok(new { canManageLicenses = true })
                : Results.Forbid());

        group.MapGet("/overview", async (ClaimsPrincipal user, AppDbContext db,
            IConfiguration configuration, int months = 6) =>
        {
            if (!PlatformOwner.IsOwner(user, configuration))
                return Results.Forbid();
            if (months is not (6 or 12))
                return Results.BadRequest(new { message = "Choose 6 or 12 months." });

            var ownerId = long.Parse(configuration["Platform:OwnerUserId"]!);
            var now = DateTime.UtcNow;
            var periodStart = new DateTime(now.Year, now.Month, 1, 0, 0, 0, DateTimeKind.Utc)
                .AddMonths(1 - months);
            var periodEnd = periodStart.AddMonths(months);

            var totalCustomers = await db.Users.CountAsync(x => x.Id != ownerId);
            var totalBusinesses = await db.Businesses.CountAsync();
            var activeSubscriptions = await db.BusinessSubscriptions.CountAsync(x => x.Status == "Active");
            var activeTrials = await db.BusinessSubscriptions.CountAsync(x =>
                x.Status == "Trial" && x.TrialEndsAtUtc > now);
            var expiredTrials = await db.BusinessSubscriptions.CountAsync(x =>
                x.Status == "Trial" && x.TrialEndsAtUtc <= now);

            var growthCounts = await db.Businesses.AsNoTracking()
                .Where(x => x.CreatedAtUtc >= periodStart && x.CreatedAtUtc < periodEnd)
                .GroupBy(x => new { x.CreatedAtUtc.Year, x.CreatedAtUtc.Month })
                .Select(group => new
                {
                    group.Key.Year,
                    group.Key.Month,
                    Count = group.Count()
                }).ToListAsync();

            var businessGrowth = Enumerable.Range(0, months).Select(index =>
            {
                var month = periodStart.AddMonths(index);
                return new
                {
                    year = month.Year,
                    month = month.Month,
                    businessesCreated = growthCounts.FirstOrDefault(x =>
                        x.Year == month.Year && x.Month == month.Month)?.Count ?? 0
                };
            }).ToArray();

            var businessesByPlan = await db.BusinessSubscriptions.AsNoTracking()
                .GroupBy(x => x.PlanCode)
                .Select(group => new { planCode = group.Key, businessCount = group.Count() })
                .OrderByDescending(x => x.businessCount).ThenBy(x => x.planCode)
                .ToListAsync();

            var expiringUntil = now.AddDays(7);
            var expiringQuery = db.BusinessSubscriptions.AsNoTracking().Where(x =>
                x.Status == "Trial" &&
                x.TrialEndsAtUtc > now && x.TrialEndsAtUtc <= expiringUntil);
            var expiringTrialCount = await expiringQuery.CountAsync();
            var expiringTrials = await (from trial in expiringQuery
                                       join business in db.Businesses.AsNoTracking()
                                           on trial.BusinessId equals business.Id
                                       join owner in db.Users.AsNoTracking()
                                           on business.CreatedByUserId equals owner.Id
                                       join type in db.BusinessTypes.AsNoTracking()
                                           on business.BusinessTypeId equals type.Id
                                       orderby trial.TrialEndsAtUtc, business.Id
                                       select new
                                       {
                                           businessId = business.Id,
                                           businessName = business.Name,
                                           ownerEmail = owner.Email,
                                           businessType = type.Name,
                                           trialEndsAtUtc = trial.TrialEndsAtUtc
                                       }).Take(8).ToListAsync();

            var recentActivity = await db.PlatformAuditEvents.AsNoTracking()
                .OrderByDescending(x => x.Id).Take(8)
                .Select(x => new
                {
                    x.Id, x.ActorUserId, x.Action, x.TargetType,
                    x.TargetId, x.CreatedAtUtc
                }).ToListAsync();

            return Results.Ok(new
            {
                totalCustomers, totalBusinesses, activeSubscriptions, activeTrials, expiredTrials,
                businessGrowth, businessesByPlan,
                unassignedBusinesses = Math.Max(0, totalBusinesses - businessesByPlan.Sum(x => x.businessCount)),
                expiringTrialCount, expiringTrials, recentActivity
            });
        });

        group.MapGet("/customers", async (ClaimsPrincipal user, AppDbContext db,
            IConfiguration configuration, string? search, int page = 1, int pageSize = 20) =>
        {
            if (!PlatformOwner.IsOwner(user, configuration))
                return Results.Forbid();

            if (page is < 1 or > 100000 || pageSize is < 1 or > 50)
                return Results.BadRequest(new { message = "Choose a valid page and page size (1–50)." });

            var term = search?.Trim().ToUpperInvariant() ?? "";
            if (term.Length > 100)
                return Results.BadRequest(new { message = "Search must be 100 characters or less." });

            var ownerId = long.Parse(configuration["Platform:OwnerUserId"]!);
            var query = db.Users.AsNoTracking().Where(x => x.Id != ownerId);
            if (term.Length > 0)
                query = query.Where(x => x.NormalizedEmail != null &&
                    x.NormalizedEmail.Contains(term));

            var total = await query.CountAsync();
            var items = await query.OrderBy(x => x.Id)
                .Skip((page - 1) * pageSize).Take(pageSize)
                .Select(x => new
                {
                    x.Id,
                    x.Email,
                    x.EmailConfirmed,
                    ownedBusinessCount = db.Businesses.Count(b => b.CreatedByUserId == x.Id)
                }).ToListAsync();

            return Results.Ok(new { items, total, page, pageSize });
        });

        group.MapGet("/customers/{customerId:long}", async (long customerId,
            ClaimsPrincipal user, AppDbContext db, IConfiguration configuration) =>
        {
            if (!PlatformOwner.IsOwner(user, configuration))
                return Results.Forbid();

            var ownerId = long.Parse(configuration["Platform:OwnerUserId"]!);
            var customer = await db.Users.AsNoTracking()
                .Where(x => x.Id == customerId && x.Id != ownerId)
                .Select(x => new { x.Id, x.Email, x.EmailConfirmed })
                .SingleOrDefaultAsync();
            if (customer is null) return Results.NotFound();

            var businesses = await (from business in db.Businesses.AsNoTracking()
                                    join type in db.BusinessTypes.AsNoTracking()
                                        on business.BusinessTypeId equals type.Id
                                    join sub in db.BusinessSubscriptions.AsNoTracking()
                                        on business.Id equals sub.BusinessId into subscriptions
                                    from subscription in subscriptions.DefaultIfEmpty()
                                    where business.CreatedByUserId == customerId
                                    orderby business.Id descending
                                    select new
                                    {
                                        business.Id,
                                        business.Name,
                                        TypeName = type.Name,
                                        PlanCode = subscription == null ? null : subscription.PlanCode,
                                        SubscriptionStatus = subscription == null ? null : subscription.Status
                                    }).ToListAsync();

            return Results.Ok(new { customer.Id, customer.Email, customer.EmailConfirmed, businesses });
        });

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
