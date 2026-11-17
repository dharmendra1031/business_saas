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
                    x.TargetId, x.Reason, x.PreviousValue, x.NewValue, x.CreatedAtUtc
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

        group.MapGet("/plans", async (ClaimsPrincipal user, AppDbContext db,
            IConfiguration configuration) =>
        {
            if (!PlatformOwner.IsOwner(user, configuration))
                return Results.Forbid();

            var assigned = await db.BusinessSubscriptions.AsNoTracking()
                .GroupBy(x => x.PlanCode)
                .Select(g => new { Code = g.Key, Count = g.Count() })
                .ToListAsync();

            var drafts = await db.SubscriptionPlanDrafts.AsNoTracking().ToListAsync();
            return Results.Ok(new
            {
                items = Plans.Select(plan => new
                {
                    plan.Code, plan.Name, plan.IsPublic,
                    assignedBusinesses = assigned.FirstOrDefault(x => x.Code == plan.Code)?.Count ?? 0,
                    pricingConfigured = false, // Drafts are never published or enforced.
                    draft = drafts.Where(x => x.PlanCode == plan.Code)
                        .Select(x => new
                        {
                            x.Id, x.Revision, x.CurrencyCode,
                            x.MonthlyPrice, x.QuarterlyPrice, x.YearlyPrice,
                            x.MaxLocations, x.MaxUsers, x.MaxProducts,
                            x.UpdatedAtUtc
                        }).SingleOrDefault()
                }),
                unknownPlanAssignments = assigned
                    .Where(x => !Plans.Any(plan => plan.Code == x.Code))
                    .Sum(x => x.Count)
            });
        });

        group.MapPost("/plans/{planCode}/draft", async (string planCode,
            SavePlanDraftRequest request, ClaimsPrincipal user, AppDbContext db,
            IConfiguration configuration) =>
        {
            if (!PlatformOwner.IsOwner(user, configuration))
                return Results.Forbid();

            var code = planCode.Trim().ToUpperInvariant();
            if (!Plans.Any(plan => plan.Code == code))
                return Results.NotFound();

            var reason = request.Reason?.Trim() ?? "";
            var currency = request.CurrencyCode?.Trim().ToUpperInvariant();
            var hasPrice = request.MonthlyPrice.HasValue || request.QuarterlyPrice.HasValue ||
                request.YearlyPrice.HasValue;
            if (request.ExpectedRevision < 0 || reason.Length is < 10 or > 200 ||
                (currency is not null && (currency.Length != 3 ||
                    !currency.All(char.IsAsciiLetterUpper))) ||
                (hasPrice && currency is null) ||
                !ValidPrice(request.MonthlyPrice) ||
                !ValidPrice(request.QuarterlyPrice) ||
                !ValidPrice(request.YearlyPrice) ||
                !ValidLimit(request.MaxLocations, 1000) ||
                !ValidLimit(request.MaxUsers, 10000) ||
                !ValidLimit(request.MaxProducts, 1000000))
                return Results.BadRequest(new { message = "Check prices (non-negative, two decimals), currency, limits and the 10–200 character reason." });

            var original = await db.SubscriptionPlanDrafts.AsNoTracking()
                .SingleOrDefaultAsync(x => x.PlanCode == code);
            if ((original?.Revision ?? 0) != request.ExpectedRevision)
                return Results.Conflict(new { message = "Plan draft has changed. Reload before saving." });

            var actorId = long.Parse(user.FindFirstValue(ClaimTypes.NameIdentifier)!);
            var next = new SubscriptionPlanDraft
            {
                PlanCode = code,
                Revision = request.ExpectedRevision + 1,
                CurrencyCode = currency,
                MonthlyPrice = request.MonthlyPrice,
                QuarterlyPrice = request.QuarterlyPrice,
                YearlyPrice = request.YearlyPrice,
                MaxLocations = request.MaxLocations,
                MaxUsers = request.MaxUsers,
                MaxProducts = request.MaxProducts,
                UpdatedByUserId = actorId,
                UpdatedAtUtc = DateTime.UtcNow
            };

            await using var transaction = await db.Database.BeginTransactionAsync();
            if (original is null)
            {
                db.SubscriptionPlanDrafts.Add(next);
            }
            else
            {
                var updated = await db.SubscriptionPlanDrafts
                    .Where(x => x.Id == original.Id && x.Revision == request.ExpectedRevision)
                    .ExecuteUpdateAsync(setters => setters
                        .SetProperty(x => x.Revision, next.Revision)
                        .SetProperty(x => x.CurrencyCode, next.CurrencyCode)
                        .SetProperty(x => x.MonthlyPrice, next.MonthlyPrice)
                        .SetProperty(x => x.QuarterlyPrice, next.QuarterlyPrice)
                        .SetProperty(x => x.YearlyPrice, next.YearlyPrice)
                        .SetProperty(x => x.MaxLocations, next.MaxLocations)
                        .SetProperty(x => x.MaxUsers, next.MaxUsers)
                        .SetProperty(x => x.MaxProducts, next.MaxProducts)
                        .SetProperty(x => x.UpdatedByUserId, actorId)
                        .SetProperty(x => x.UpdatedAtUtc, next.UpdatedAtUtc));
                if (updated != 1)
                    return Results.Conflict(new { message = "Plan draft was updated elsewhere. Reload." });
            }

            var audit = new PlatformAuditEvent
            {
                ActorUserId = actorId,
                Action = "Plan.DraftSaved",
                TargetType = "Plan",
                TargetId = original?.Id,
                Reason = reason,
                PreviousValue = original is null ? null : DraftSummary(original),
                NewValue = DraftSummary(next)
            };
            db.PlatformAuditEvents.Add(audit);
            try
            {
                await db.SaveChangesAsync();
            }
            catch (DbUpdateException) when (original is null)
            {
                return Results.Conflict(new { message = "A plan draft could not be created. Reload before retrying." });
            }

            if (original is null)
            {
                audit.TargetId = next.Id;
                await db.SaveChangesAsync();
            }
            await transaction.CommitAsync();
            return Results.Ok(new { code, revision = next.Revision, auditId = audit.Id });
        });

        group.MapPost("/businesses/{businessId:long}/subscription-status",
            async (long businessId, ChangeSubscriptionStatusRequest request,
                ClaimsPrincipal user, AppDbContext db, IConfiguration configuration) =>
        {
            if (!PlatformOwner.IsOwner(user, configuration))
                return Results.Forbid();

            var reason = request.Reason?.Trim() ?? "";
            if (businessId <= 0 || reason.Length is < 10 or > 200 ||
                request.Action is not ("Suspend" or "Reactivate") ||
                request.ExpectedStatus is not ("Trial" or "Active" or "Suspended"))
                return Results.BadRequest(new { message = "Choose Suspend or Reactivate, current status and a 10–200 character reason." });

            var current = await db.BusinessSubscriptions.AsNoTracking()
                .Where(x => x.BusinessId == businessId)
                .Select(x => new { x.Status, x.StatusBeforeSuspension })
                .SingleOrDefaultAsync();
            if (current is null)
                return Results.NotFound();
            if (current.Status != request.ExpectedStatus)
                return Results.Conflict(new { message = "Subscription status has changed. Reload." });

            string newStatus;
            string? priorStatus;
            if (request.Action == "Suspend" && current.Status is "Trial" or "Active")
            {
                newStatus = "Suspended";
                priorStatus = current.Status;
            }
            else if (request.Action == "Reactivate" && current.Status == "Suspended" &&
                current.StatusBeforeSuspension is "Trial" or "Active")
            {
                newStatus = current.StatusBeforeSuspension;
                priorStatus = null;
            }
            else
                return Results.Conflict(new { message = "Subscription cannot make that transition." });

            await using var transaction = await db.Database.BeginTransactionAsync();
            var updated = await db.BusinessSubscriptions.Where(x =>
                    x.BusinessId == businessId && x.Status == current.Status &&
                    x.StatusBeforeSuspension == current.StatusBeforeSuspension)
                .ExecuteUpdateAsync(setters => setters
                    .SetProperty(x => x.Status, newStatus)
                    .SetProperty(x => x.StatusBeforeSuspension, priorStatus));
            if (updated != 1)
                return Results.Conflict(new { message = "Subscription changed elsewhere. Reload." });

            var audit = new PlatformAuditEvent
            {
                ActorUserId = long.Parse(user.FindFirstValue(ClaimTypes.NameIdentifier)!),
                Action = request.Action == "Suspend"
                    ? "Subscription.Suspended" : "Subscription.Reactivated",
                TargetType = "Business",
                TargetId = businessId,
                Reason = reason,
                PreviousValue = current.Status,
                NewValue = newStatus
            };
            db.PlatformAuditEvents.Add(audit);
            await db.SaveChangesAsync();
            await transaction.CommitAsync();

            return Results.Ok(new { businessId, status = newStatus, auditId = audit.Id });
        });

        group.MapPost("/businesses/{businessId:long}/extend-trial", async (long businessId,
            ExtendTrialRequest request, ClaimsPrincipal user, AppDbContext db,
            IConfiguration configuration) =>
        {
            if (!PlatformOwner.IsOwner(user, configuration))
                return Results.Forbid();

            var reason = request.Reason?.Trim() ?? "";
            if (businessId <= 0 || request.AdditionalDays is < 1 or > 30 ||
                reason.Length is < 10 or > 200 || request.ExpectedTrialEndsAtUtc.Kind != DateTimeKind.Utc)
                return Results.BadRequest(new
                {
                    message = "Enter 1–30 additional days, a 10–200 character reason, and the current UTC trial expiry."
                });

            var now = DateTime.UtcNow;
            var current = await db.BusinessSubscriptions.AsNoTracking()
                .Where(x => x.BusinessId == businessId)
                .Select(x => new { x.Status, x.TrialEndsAtUtc })
                .SingleOrDefaultAsync();
            if (current is null) return Results.NotFound();
            if (current.Status != "Trial")
                return Results.Conflict(new { message = "Only trial subscriptions can be extended." });
            if (current.TrialEndsAtUtc != request.ExpectedTrialEndsAtUtc)
                return Results.Conflict(new { message = "Trial expiry has changed. Reload before extending." });

            var newExpiry = (current.TrialEndsAtUtc > now ? current.TrialEndsAtUtc : now)
                .AddDays(request.AdditionalDays);
            if (newExpiry > now.AddDays(90))
                return Results.BadRequest(new { message = "The new expiry must be within 90 days." });

            await using var transaction = await db.Database.BeginTransactionAsync();
            var updated = await db.BusinessSubscriptions.Where(x => x.BusinessId == businessId &&
                    x.Status == "Trial" && x.TrialEndsAtUtc == request.ExpectedTrialEndsAtUtc)
                .ExecuteUpdateAsync(setters => setters.SetProperty(x => x.TrialEndsAtUtc, newExpiry));
            if (updated != 1)
            {
                await transaction.RollbackAsync();
                return Results.Conflict(new { message = "Trial was updated elsewhere. Reload before extending." });
            }

            var audit = new PlatformAuditEvent
            {
                ActorUserId = long.Parse(user.FindFirstValue(ClaimTypes.NameIdentifier)!),
                Action = "Business.TrialExtended",
                TargetType = "Business",
                TargetId = businessId,
                Reason = reason,
                PreviousValue = current.TrialEndsAtUtc.ToString("O"),
                NewValue = newExpiry.ToString("O")
            };
            db.PlatformAuditEvents.Add(audit);
            await db.SaveChangesAsync();
            await transaction.CommitAsync();

            return Results.Ok(new { businessId, trialEndsAtUtc = newExpiry, auditId = audit.Id });
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
                                     StatusBeforeSuspension = subscription == null ? null : subscription.StatusBeforeSuspension,
                                     TrialEndsAtUtc = subscription == null
                                         ? (DateTime?)null : subscription.TrialEndsAtUtc
                                 }).SingleOrDefaultAsync();

            return details is null ? Results.NotFound() : Results.Ok(details);
        });
    }

    private static readonly PlanDefinition[] Plans =
    [
        new("STARTER", "Starter", true),
        new("GROWTH", "Growth", true),
        new("PROFESSIONAL", "Professional", true),
        new("BUSINESS", "Business", true),
        new("ENTERPRISE", "Enterprise", false)
    ];

    private static bool ValidPrice(decimal? value) =>
        value is null || (value >= 0 && value <= 99999999m &&
            decimal.Round(value.Value, 2) == value.Value);

    private static bool ValidLimit(int? value, int maximum) =>
        value is null || value is > 0 && value <= maximum;

    private static string DraftSummary(SubscriptionPlanDraft value) =>
        $"v{value.Revision} {value.CurrencyCode ?? "-"} " +
        $"M:{value.MonthlyPrice?.ToString("0.00") ?? "-"} " +
        $"Q:{value.QuarterlyPrice?.ToString("0.00") ?? "-"} " +
        $"Y:{value.YearlyPrice?.ToString("0.00") ?? "-"} " +
        $"L:{value.MaxLocations?.ToString() ?? "-"} " +
        $"U:{value.MaxUsers?.ToString() ?? "-"} " +
        $"P:{value.MaxProducts?.ToString() ?? "-"}";

    private sealed record PlanDefinition(string Code, string Name, bool IsPublic);
    private sealed record SavePlanDraftRequest(
        int ExpectedRevision, string? CurrencyCode,
        decimal? MonthlyPrice, decimal? QuarterlyPrice, decimal? YearlyPrice,
        int? MaxLocations, int? MaxUsers, int? MaxProducts, string? Reason);
    private sealed record ChangeSubscriptionStatusRequest(
        string? Action, string? ExpectedStatus, string? Reason);
    private sealed record ExtendTrialRequest(int AdditionalDays, DateTime ExpectedTrialEndsAtUtc, string? Reason);
}
