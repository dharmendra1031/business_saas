using System.Security.Claims;
using System.Data.Common;
using Microsoft.Data.SqlClient;
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
            IConfiguration configuration, long? eventId, long? actorUserId, int page = 1, int pageSize = 20) =>
        {
            if (!PlatformOwner.IsOwner(user, configuration))
                return Results.Forbid();

            if (page is < 1 or > 100000 || pageSize is < 1 or > 50)
                return Results.BadRequest(new { message = "Choose a valid page and page size (1–50)." });

            if (eventId is <= 0)
                return Results.BadRequest(new { message = "Choose a valid audit event ID." });
            if (actorUserId is <= 0)
                return Results.BadRequest(new { message = "Choose a valid audit actor ID." });

            var query = db.PlatformAuditEvents.AsNoTracking();
            if (eventId.HasValue) query = query.Where(x => x.Id == eventId.Value);
            if (actorUserId.HasValue) query = query.Where(x => x.ActorUserId == actorUserId.Value);
            var total = await query.CountAsync();
            var items = await query
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
            var activeSubscriptions = await db.BusinessSubscriptions.CountAsync(x =>
                x.Status == "Active" && x.PaidStartsAtUtc <= now &&
                x.PaidEndsAtUtc > now && x.PaidPlanVersionId.HasValue);
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

            // Paid expiry is derived from the recorded period, not a background status update.
            var paidQuery = db.BusinessSubscriptions.AsNoTracking().Where(x =>
                x.Status == "Active" && x.PaidPlanVersionId.HasValue &&
                x.PaidStartsAtUtc != null && x.PaidStartsAtUtc <= now && x.PaidEndsAtUtc != null &&
                x.PaidEndsAtUtc > x.PaidStartsAtUtc);
            var expiredPaidCount = await paidQuery.CountAsync(x => x.PaidEndsAtUtc <= now);
            var expiringPaidQuery = paidQuery.Where(x => x.PaidEndsAtUtc > now && x.PaidEndsAtUtc <= expiringUntil);
            var expiringPaidCount = await expiringPaidQuery.CountAsync();
            var expiringPaid = await (from paid in expiringPaidQuery
                                      join business in db.Businesses.AsNoTracking() on paid.BusinessId equals business.Id
                                      join owner in db.Users.AsNoTracking() on business.CreatedByUserId equals owner.Id
                                      join type in db.BusinessTypes.AsNoTracking() on business.BusinessTypeId equals type.Id
                                      orderby paid.PaidEndsAtUtc, business.Id
                                      select new
                                      {
                                          businessId = business.Id,
                                          businessName = business.Name,
                                          ownerEmail = owner.Email,
                                          businessType = type.Name,
                                          endsAtUtc = DateTime.SpecifyKind(paid.PaidEndsAtUtc!.Value, DateTimeKind.Utc)
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
                expiringTrialCount, expiringTrials, expiredPaidCount, expiringPaidCount, expiringPaid, recentActivity
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
            var published = await db.PublishedPlanVersions.AsNoTracking()
                .OrderByDescending(x => x.Version).ToListAsync();
            return Results.Ok(new
            {
                items = Plans.Select(plan => new
                {
                    plan.Code, plan.Name, plan.IsPublic,
                    assignedBusinesses = assigned.FirstOrDefault(x => x.Code == plan.Code)?.Count ?? 0,
                    pricingConfigured = published.Any(x => x.PlanCode == plan.Code),
                    published = published.Where(x => x.PlanCode == plan.Code)
                        .Select(x => new
                        {
                            x.Id, x.Version, x.SourceDraftRevision, x.CurrencyCode,
                            x.MonthlyPrice, x.QuarterlyPrice, x.YearlyPrice,
                            x.MaxLocations, x.MaxUsers, x.MaxProducts,
                            x.PublishedAtUtc
                        }).FirstOrDefault(),
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

        group.MapPost("/plans/{planCode}/publish", async (string planCode,
            PublishPlanRequest request, ClaimsPrincipal user, AppDbContext db,
            IConfiguration configuration) =>
        {
            if (!PlatformOwner.IsOwner(user, configuration))
                return Results.Forbid();

            var code = planCode.Trim().ToUpperInvariant();
            // Custom Enterprise prices require an approved contract workflow.
            if (code == "ENTERPRISE" || !Plans.Any(plan => plan.Code == code))
                return Results.BadRequest(new
                {
                    message = "Only the four public plans support catalog publication."
                });

            var reason = request.Reason?.Trim() ?? "";
            if (request.ExpectedDraftRevision <= 0 || request.ExpectedPublishedVersion < 0 ||
                reason.Length is < 10 or > 200)
                return Results.BadRequest(new
                {
                    message = "Reload draft/version and provide a 10–200 character approval reason."
                });

            await using var transaction = await db.Database.BeginTransactionAsync();
            var draft = await db.SubscriptionPlanDrafts.AsNoTracking()
                .SingleOrDefaultAsync(x => x.PlanCode == code);
            var current = await db.PublishedPlanVersions.AsNoTracking()
                .Where(x => x.PlanCode == code)
                .OrderByDescending(x => x.Version).FirstOrDefaultAsync();

            if (draft is null || draft.Revision != request.ExpectedDraftRevision ||
                (current?.Version ?? 0) != request.ExpectedPublishedVersion ||
                current?.SourceDraftRevision == draft.Revision)
                return Results.Conflict(new { message = "Draft or published plan has changed. Reload before publishing." });

            if (draft.CurrencyCode is null ||
                !ValidPublishedPrice(draft.MonthlyPrice) ||
                !ValidPublishedPrice(draft.QuarterlyPrice) ||
                !ValidPublishedPrice(draft.YearlyPrice) ||
                !ValidLimit(draft.MaxLocations, 1000) ||
                !ValidLimit(draft.MaxUsers, 10000) ||
                !ValidLimit(draft.MaxProducts, 1000000) ||
                draft.MaxLocations is null || draft.MaxUsers is null || draft.MaxProducts is null)
                return Results.BadRequest(new
                {
                    message = "Complete positive prices in one currency and all three limits before publishing."
                });

            var next = new PublishedPlanVersion
            {
                PlanCode = code,
                Version = (current?.Version ?? 0) + 1,
                SourceDraftRevision = draft.Revision,
                CurrencyCode = draft.CurrencyCode,
                MonthlyPrice = draft.MonthlyPrice.Value,
                QuarterlyPrice = draft.QuarterlyPrice.Value,
                YearlyPrice = draft.YearlyPrice.Value,
                MaxLocations = draft.MaxLocations,
                MaxUsers = draft.MaxUsers,
                MaxProducts = draft.MaxProducts,
                PublishedByUserId = long.Parse(user.FindFirstValue(ClaimTypes.NameIdentifier)!),
                PublishedAtUtc = DateTime.UtcNow
            };
            db.PublishedPlanVersions.Add(next);
            try
            {
                // Database uniqueness prevents two concurrent requests from publishing the same version.
                await db.SaveChangesAsync();
            }
            catch (DbUpdateException error) when (error.InnerException is
                SqlException { Number: 2601 or 2627 } or DbException { ErrorCode: 19 })
            {
                return Results.Conflict(new { message = "Plan was published elsewhere. Reload before retrying." });
            }

            var audit = new PlatformAuditEvent
            {
                ActorUserId = next.PublishedByUserId,
                Action = "Plan.Published",
                TargetType = "PlanVersion",
                TargetId = next.Id,
                Reason = reason,
                PreviousValue = current is null ? null : $"{code} v{current.Version}",
                NewValue = $"{code} v{next.Version}"
            };
            db.PlatformAuditEvents.Add(audit);
            await db.SaveChangesAsync();
            await transaction.CommitAsync();

            return Results.Ok(new
            {
                planVersionId = next.Id, code, next.Version,
                sourceDraftRevision = next.SourceDraftRevision, auditId = audit.Id
            });
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

            var subscription = await db.BusinessSubscriptions.AsNoTracking()
                .SingleAsync(x => x.BusinessId == businessId);
            var accessStatus = SubscriptionAccess.ResolveStatus(subscription.Status, subscription.TrialEndsAtUtc,
                subscription.PaidStartsAtUtc, subscription.PaidEndsAtUtc, subscription.PaidPlanVersionId, DateTime.UtcNow);

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

            return Results.Ok(new { businessId, status = newStatus, accessStatus, auditId = audit.Id });
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
            IConfiguration configuration, string? search, bool? emailConfirmed, int page = 1, int pageSize = 20) =>
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
            if (emailConfirmed.HasValue)
                query = query.Where(x => x.EmailConfirmed == emailConfirmed.Value);
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
                                        SubscriptionStatus = subscription == null ? null : subscription.Status,
                                        TrialEndsAtUtc = subscription == null ? (DateTime?)null : subscription.TrialEndsAtUtc,
                                        PaidStartsAtUtc = subscription == null ? null : subscription.PaidStartsAtUtc,
                                        PaidEndsAtUtc = subscription == null ? null : subscription.PaidEndsAtUtc,
                                        PaidPlanVersionId = subscription == null ? null : subscription.PaidPlanVersionId
                                    }).ToListAsync();

            var now = DateTime.UtcNow;
            return Results.Ok(new
            {
                customer.Id, customer.Email, customer.EmailConfirmed,
                businesses = businesses.Select(x => new
                {
                    x.Id, x.Name, x.TypeName, x.PlanCode, x.SubscriptionStatus,
                    AccessStatus = x.SubscriptionStatus is null ? null : SubscriptionAccess.ResolveStatus(
                        x.SubscriptionStatus, x.TrialEndsAtUtc, x.PaidStartsAtUtc,
                        x.PaidEndsAtUtc, x.PaidPlanVersionId, now)
                })
            });
        });

        group.MapGet("/businesses", async (ClaimsPrincipal user, AppDbContext db, IConfiguration configuration,
            string? search, string? ownerEmail, long? businessTypeId, string? planCode,
            string? status, int page = 1, int pageSize = 20) =>
        {
            if (!PlatformOwner.IsOwner(user, configuration))
                return Results.Forbid();

            if (page is < 1 or > 100000 || pageSize is < 1 or > 50)
                return Results.BadRequest(new { message = "Choose a valid page and page size (1–50)." });

            var term = search?.Trim() ?? "";
            if (term.Length > 100)
                return Results.BadRequest(new { message = "Search must be 100 characters or less." });
            var ownerTerm = ownerEmail?.Trim().ToUpperInvariant() ?? "";
            if (ownerTerm.Length > 254 || businessTypeId is <= 0)
                return Results.BadRequest(new { message = "Choose a valid owner email or business type filter." });

            var planFilter = planCode?.Trim().ToUpperInvariant() ?? "";
            var statusFilter = status?.Trim().ToUpperInvariant() ?? "";
            if (planFilter != "" && !Plans.Any(x => x.Code == planFilter))
                return Results.BadRequest(new { message = "Choose a valid plan filter." });
            if (statusFilter is not ("" or "TRIAL" or "TRIALEXPIRED" or "ACTIVE" or "INACTIVE" or "SUSPENDED" or "NONE"))
                return Results.BadRequest(new { message = "Choose a valid subscription status filter." });

            var now = DateTime.UtcNow;
            var query = from business in db.Businesses.AsNoTracking()
                               join owner in db.Users.AsNoTracking()
                                   on business.CreatedByUserId equals owner.Id
                               join type in db.BusinessTypes.AsNoTracking()
                                   on business.BusinessTypeId equals type.Id
                               join sub in db.BusinessSubscriptions.AsNoTracking()
                                   on business.Id equals sub.BusinessId into subscriptions
                               from subscription in subscriptions.DefaultIfEmpty()
                               where term == "" || business.Name.Contains(term)
                               where ownerTerm == "" || (owner.NormalizedEmail ?? "").Contains(ownerTerm)
                               where businessTypeId == null || business.BusinessTypeId == businessTypeId
                               select new
                               {
                                   business.Id,
                                   business.Name,
                                   business.CountryCode,
                                   CreatedAtUtc = DateTime.SpecifyKind(business.CreatedAtUtc, DateTimeKind.Utc),
                                   TypeName = type.Name,
                                   OwnerEmail = owner.Email,
                                   PlanCode = subscription == null ? null : subscription.PlanCode,
                                   SubscriptionStatus = subscription == null ? null : subscription.Status,
                                   DirectoryStatus = subscription == null ? "No subscription"
                                       : subscription.Status == "Trial"
                                           ? (subscription.TrialEndsAtUtc > now ? "Trial" : "Trial expired")
                                       : subscription.Status == "Suspended" ? "Suspended"
                                       : subscription.Status == "Active" && subscription.PaidStartsAtUtc <= now &&
                                           subscription.PaidEndsAtUtc > now && subscription.PaidPlanVersionId.HasValue
                                               ? "Active" : "Inactive",
                                   TrialEndsAtUtc = subscription == null
                                       ? (DateTime?)null : subscription.TrialEndsAtUtc
                               };

            if (planFilter != "") query = query.Where(x => x.PlanCode == planFilter);
            var directoryStatus = statusFilter switch
            {
                "TRIAL" => "Trial",
                "TRIALEXPIRED" => "Trial expired",
                "ACTIVE" => "Active",
                "INACTIVE" => "Inactive",
                "SUSPENDED" => "Suspended",
                "NONE" => "No subscription",
                _ => null
            };
            if (directoryStatus is not null) query = query.Where(x => x.DirectoryStatus == directoryStatus);
            var total = await query.CountAsync();
            var items = await query.OrderByDescending(x => x.Id)
                .Skip((page - 1) * pageSize).Take(pageSize).ToListAsync();

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
                                     LocationCount = db.BusinessLocations.Count(x => x.BusinessId == business.Id),
                                     PrimaryLocationName = db.BusinessLocations
                                         .Where(x => x.BusinessId == business.Id && x.IsPrimary)
                                         .OrderBy(x => x.Id).Select(x => x.Name).FirstOrDefault(),
                                     TypeName = type.Name,
                                     OwnerEmail = owner.Email,
                                     PlanCode = subscription == null ? null : subscription.PlanCode,
                                     SubscriptionStatus = subscription == null ? null : subscription.Status,
                                     StatusBeforeSuspension = subscription == null ? null : subscription.StatusBeforeSuspension,
                                     PaidStartsAtUtc = subscription == null ? null : subscription.PaidStartsAtUtc,
                                     PaidEndsAtUtc = subscription == null ? null : subscription.PaidEndsAtUtc,
                                     PaidPlanVersionId = subscription == null ? null : subscription.PaidPlanVersionId,
                                     PaidPlanVersion = db.PublishedPlanVersions
                                         .Where(x => subscription != null && x.Id == subscription.PaidPlanVersionId)
                                         .Select(x => (int?)x.Version).FirstOrDefault(),
                                     TrialEndsAtUtc = subscription == null
                                         ? (DateTime?)null : subscription.TrialEndsAtUtc
                                 }).SingleOrDefaultAsync();

            if (details is null) return Results.NotFound();
            var accessStatus = SubscriptionAccess.ResolveStatus(details.SubscriptionStatus, details.TrialEndsAtUtc,
                details.PaidStartsAtUtc, details.PaidEndsAtUtc, details.PaidPlanVersionId, DateTime.UtcNow);
            return Results.Ok(new
            {
                details.Id, details.Name, details.CountryCode, details.CurrencyCode, details.TimeZoneId,
                details.CreatedAtUtc, details.LocationCount, details.PrimaryLocationName, details.TypeName,
                details.OwnerEmail, details.PlanCode, details.SubscriptionStatus, details.StatusBeforeSuspension,
                details.PaidStartsAtUtc, details.PaidEndsAtUtc, details.PaidPlanVersionId, details.PaidPlanVersion,
                details.TrialEndsAtUtc, accessStatus
            });
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

    private static bool ValidPublishedPrice(decimal? value) =>
        value is > 0 and <= 99999999m && decimal.Round(value.Value, 2) == value.Value;

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
    private sealed record PublishPlanRequest(int ExpectedDraftRevision,
        int ExpectedPublishedVersion, string? Reason);
    private sealed record SavePlanDraftRequest(
        int ExpectedRevision, string? CurrencyCode,
        decimal? MonthlyPrice, decimal? QuarterlyPrice, decimal? YearlyPrice,
        int? MaxLocations, int? MaxUsers, int? MaxProducts, string? Reason);
    private sealed record ChangeSubscriptionStatusRequest(
        string? Action, string? ExpectedStatus, string? Reason);
    private sealed record ExtendTrialRequest(int AdditionalDays, DateTime ExpectedTrialEndsAtUtc, string? Reason);
}
