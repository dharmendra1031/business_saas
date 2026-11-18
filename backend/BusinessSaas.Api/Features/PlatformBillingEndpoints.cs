using System.Data;
using System.Data.Common;
using System.Security.Claims;
using BusinessSaas.Api.Data;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;

namespace BusinessSaas.Api.Features;

public static class PlatformBillingEndpoints
{
    public static void MapPlatformBillingEndpoints(this WebApplication app)
    {
        var group = app.MapGroup("/api/platform/billing").RequireAuthorization();

        group.MapGet("/drafts", async (ClaimsPrincipal user, IConfiguration configuration,
            AppDbContext db, int page = 1, int pageSize = 20) =>
        {
            if (!PlatformOwner.IsOwner(user, configuration))
                return Results.Forbid();
            if (page is < 1 or > 100000 || pageSize is < 1 or > 50)
                return Results.BadRequest(new { message = "Choose a valid page and page size (1–50)." });

            var total = await db.SubscriptionBillingDrafts.CountAsync();
            var items = await (from draft in db.SubscriptionBillingDrafts.AsNoTracking()
                               join business in db.Businesses.AsNoTracking()
                                   on draft.BusinessId equals business.Id
                               orderby draft.Id descending
                               select new
                               {
                                   draft.Id, draft.BusinessId,
                                   businessName = business.Name,
                                   draft.PublishedPlanVersionId,
                                   draft.PlanCode, draft.BillingCycle,
                                   draft.CurrencyCode, draft.Amount,
                                   draft.PeriodStartDate, draft.PeriodEndDate,
                                   draft.CreatedAtUtc,
                                   status = "Draft"
                               })
                .Skip((page - 1) * pageSize).Take(pageSize).ToListAsync();

            return Results.Ok(new { items, total, page, pageSize });
        });

        group.MapGet("/payment-requests", async (ClaimsPrincipal user,
            IConfiguration configuration, AppDbContext db, int page = 1, int pageSize = 20) =>
        {
            if (!PlatformOwner.IsOwner(user, configuration))
                return Results.Forbid();
            if (page is < 1 or > 100000 || pageSize is < 1 or > 50)
                return Results.BadRequest(new { message = "Choose a valid page and page size (1–50)." });

            var total = await db.SubscriptionPaymentRequests.CountAsync();
            var items = await (from request in db.SubscriptionPaymentRequests.AsNoTracking()
                               join business in db.Businesses.AsNoTracking()
                                   on request.BusinessId equals business.Id
                               orderby request.Id descending
                               select new
                               {
                                   request.Id, request.BillingDraftId, request.BusinessId,
                                   businessName = business.Name, request.CurrencyCode,
                                   request.Amount, request.Status, request.CreatedAtUtc
                               }).Skip((page - 1) * pageSize).Take(pageSize).ToListAsync();
            return Results.Ok(new { items, total, page, pageSize });
        });

        group.MapPost("/drafts/{id:long}/payment-requests", async (long id,
            PreparePaymentRequest input, ClaimsPrincipal user, IConfiguration configuration,
            AppDbContext db) =>
        {
            if (!PlatformOwner.IsOwner(user, configuration))
                return Results.Forbid();

            var key = input.RequestKey?.Trim() ?? "";
            var reason = input.Reason?.Trim() ?? "";
            if (id <= 0 || key.Length is < 12 or > 80 ||
                !key.All(ch => char.IsAsciiLetterOrDigit(ch) || ch == '-') ||
                reason.Length is < 10 or > 200)
                return Results.BadRequest(new
                {
                    message = "Valid draft ID, request key and a 10–200 character reason are required."
                });

            await using var transaction =
                await db.Database.BeginTransactionAsync(IsolationLevel.Serializable);

            var draft = await db.SubscriptionBillingDrafts.AsNoTracking()
                .SingleOrDefaultAsync(x => x.Id == id);
            if (draft is null)
                return Results.NotFound();
            if (draft.Amount <= 0 || draft.CurrencyCode.Length != 3 ||
                draft.PeriodStartDate is null || draft.PeriodEndDate is null ||
                draft.PeriodEndDate <= draft.PeriodStartDate)
                return Results.Conflict(new
                {
                    message = "This draft has no valid amount or billing period."
                });

            var existing = await db.SubscriptionPaymentRequests.AsNoTracking()
                .SingleOrDefaultAsync(x => x.BillingDraftId == id);
            if (existing is not null)
                return existing.RequestKey == key
                    ? Results.Ok(new
                    {
                        id = existing.Id, existing.BillingDraftId, existing.Status,
                        existing.Amount, existing.CurrencyCode, alreadyExists = true
                    })
                    : Results.Conflict(new
                    {
                        message = "A payment request already exists for this billing draft."
                    });

            var keyUsed = await db.SubscriptionPaymentRequests.AnyAsync(x =>
                x.BusinessId == draft.BusinessId && x.RequestKey == key);
            if (keyUsed)
                return Results.Conflict(new { message = "Request key belongs to another draft." });

            var actorId = long.Parse(user.FindFirstValue(ClaimTypes.NameIdentifier)!);
            var request = new SubscriptionPaymentRequest
            {
                BillingDraftId = draft.Id,
                BusinessId = draft.BusinessId,
                Amount = draft.Amount,
                CurrencyCode = draft.CurrencyCode,
                RequestKey = key,
                CreatedByUserId = actorId
            };
            db.SubscriptionPaymentRequests.Add(request);

            try
            {
                await db.SaveChangesAsync();
            }
            catch (DbUpdateException error) when (error.InnerException is
                SqlException { Number: 2601 or 2627 } or DbException { ErrorCode: 19 })
            {
                return Results.Conflict(new { message = "Payment request already exists. Refresh the list." });
            }

            db.PlatformAuditEvents.Add(new PlatformAuditEvent
            {
                ActorUserId = actorId,
                Action = "Billing.PaymentPrepared",
                TargetType = "PaymentRequest",
                TargetId = request.Id,
                Reason = reason,
                NewValue = $"Draft {draft.Id} {request.CurrencyCode} {request.Amount:0.00}"
            });
            await db.SaveChangesAsync();
            await transaction.CommitAsync();

            return Results.Created($"/api/platform/billing/payment-requests/{request.Id}", new
            {
                id = request.Id, request.BillingDraftId, request.BusinessId,
                request.CurrencyCode, request.Amount, request.Status,
                alreadyExists = false
            });
        });

        group.MapGet("/payment-requests/{id:long}", async (long id,
            ClaimsPrincipal user, IConfiguration configuration, AppDbContext db) =>
        {
            if (!PlatformOwner.IsOwner(user, configuration))
                return Results.Forbid();
            var request = await db.SubscriptionPaymentRequests.AsNoTracking()
                .Where(x => x.Id == id)
                .Select(x => new
                {
                    x.Id, x.BillingDraftId, x.BusinessId, x.CurrencyCode,
                    x.Amount, x.Status, x.CreatedAtUtc
                }).SingleOrDefaultAsync();
            return request is null ? Results.NotFound() : Results.Ok(request);
        });

        group.MapGet("/drafts/{id:long}", async (long id, ClaimsPrincipal user,
            IConfiguration configuration, AppDbContext db) =>
        {
            if (!PlatformOwner.IsOwner(user, configuration))
                return Results.Forbid();
            var item = await (from draft in db.SubscriptionBillingDrafts.AsNoTracking()
                              join business in db.Businesses.AsNoTracking()
                                  on draft.BusinessId equals business.Id
                              where draft.Id == id
                              select new
                              {
                                  draft.Id, draft.BusinessId, businessName = business.Name,
                                  draft.PublishedPlanVersionId, draft.PlanCode,
                                  draft.BillingCycle, draft.CurrencyCode, draft.Amount,
                                  draft.PeriodStartDate, draft.PeriodEndDate,
                                  draft.CreatedAtUtc, status = "Draft"
                              }).SingleOrDefaultAsync();
            return item is null ? Results.NotFound() : Results.Ok(item);
        });

        group.MapGet("/businesses/{businessId:long}/preview", async (long businessId,
            string cycle, DateOnly? periodStartDate, ClaimsPrincipal user,
            IConfiguration configuration, AppDbContext db) =>
        {
            if (!PlatformOwner.IsOwner(user, configuration))
                return Results.Forbid();
            var periodEnd = PeriodEnd(periodStartDate, cycle);
            if (periodEnd is null)
                return Results.BadRequest(new
                {
                    message = "Choose a valid start date and Monthly, Quarterly or Yearly billing cycle."
                });

            var duplicatePeriod = await db.SubscriptionBillingDrafts.AsNoTracking()
                .AnyAsync(x => x.BusinessId == businessId &&
                    x.PeriodStartDate < periodEnd && x.PeriodEndDate > periodStartDate);
            if (duplicatePeriod)
                return Results.Conflict(new { message = "An overlapping billing draft already exists." });

            var terms = await ResolveTerms(db, businessId, cycle);
            if (terms is null)
                return Results.Conflict(new { message = "Business or matching published plan is unavailable." });

            return Results.Ok(new
            {
                businessId, businessName = terms.Value.BusinessName,
                planCode = terms.Value.PlanCode,
                publishedPlanVersionId = terms.Value.PlanVersionId,
                version = terms.Value.Version,
                billingCycle = cycle,
                periodStartDate, periodEndDate = periodEnd.Value,
                currencyCode = terms.Value.CurrencyCode,
                amount = terms.Value.Amount,
                status = "Preview"
            });
        });

        group.MapPost("/drafts", async (CreateBillingDraftRequest request, ClaimsPrincipal user,
            IConfiguration configuration, AppDbContext db) =>
        {
            if (!PlatformOwner.IsOwner(user, configuration))
                return Results.Forbid();

            var reason = request.Reason?.Trim() ?? "";
            var requestKey = request.RequestKey?.Trim() ?? "";
            var periodEnd = PeriodEnd(request.PeriodStartDate, request.BillingCycle);
            if (request.BusinessId <= 0 || request.ExpectedPlanVersionId <= 0 ||
                periodEnd is null || reason.Length is < 10 or > 200 ||
                requestKey.Length is < 12 or > 80 ||
                !requestKey.All(ch => char.IsAsciiLetterOrDigit(ch) || ch == '-'))
                return Results.BadRequest(new
                {
                    message = "Business, billing period, current plan version, request key and a 10–200 character reason are required."
                });

            // Serializable isolation prevents overlapping drafts racing through the period check.
            await using var transaction =
                await db.Database.BeginTransactionAsync(IsolationLevel.Serializable);

            var existing = await db.SubscriptionBillingDrafts.AsNoTracking()
                .SingleOrDefaultAsync(x => x.BusinessId == request.BusinessId &&
                    x.RequestKey == requestKey);
            if (existing is not null)
            {
                return existing.PublishedPlanVersionId == request.ExpectedPlanVersionId &&
                    existing.BillingCycle == request.BillingCycle &&
                    existing.PeriodStartDate == request.PeriodStartDate
                    ? Results.Ok(new { id = existing.Id, status = "Draft", alreadyExists = true })
                    : Results.Conflict(new { message = "Request key already belongs to different billing terms." });
            }

            var overlapping = await db.SubscriptionBillingDrafts.AsNoTracking()
                .AnyAsync(x => x.BusinessId == request.BusinessId &&
                    x.PeriodStartDate < periodEnd && x.PeriodEndDate > request.PeriodStartDate);
            if (overlapping)
                return Results.Conflict(new
                {
                    message = "A draft already covers some or all of this business's billing period."
                });

            var terms = await ResolveTerms(db, request.BusinessId, request.BillingCycle!);
            if (terms is null || terms.Value.PlanVersionId != request.ExpectedPlanVersionId)
                return Results.Conflict(new
                {
                    message = "Business or published plan has changed. Reload the preview."
                });

            var actorId = long.Parse(user.FindFirstValue(ClaimTypes.NameIdentifier)!);
            var draft = new SubscriptionBillingDraft
            {
                BusinessId = request.BusinessId,
                PublishedPlanVersionId = terms.Value.PlanVersionId,
                PlanCode = terms.Value.PlanCode,
                BillingCycle = request.BillingCycle!,
                PeriodStartDate = request.PeriodStartDate,
                PeriodEndDate = periodEnd.Value,
                CurrencyCode = terms.Value.CurrencyCode,
                Amount = terms.Value.Amount,
                RequestKey = requestKey,
                CreatedByUserId = actorId
            };

            db.SubscriptionBillingDrafts.Add(draft);
            try
            {
                await db.SaveChangesAsync();
            }
            catch (DbUpdateException error) when (error.InnerException is
                SqlException { Number: 2601 or 2627 } or DbException { ErrorCode: 19 })
            {
                return Results.Conflict(new { message = "A draft already exists for this period. Refresh the list." });
            }

            var audit = new PlatformAuditEvent
            {
                ActorUserId = actorId,
                Action = "Billing.DraftCreated",
                TargetType = "BillingDraft",
                TargetId = draft.Id,
                Reason = reason,
                NewValue = $"{draft.PlanCode} {draft.BillingCycle} " +
                    $"{draft.PeriodStartDate:yyyy-MM-dd} {draft.CurrencyCode} {draft.Amount:0.00}"
            };
            db.PlatformAuditEvents.Add(audit);
            await db.SaveChangesAsync();
            await transaction.CommitAsync();

            return Results.Created($"/api/platform/billing/drafts/{draft.Id}", new
            {
                id = draft.Id, status = "Draft", alreadyExists = false,
                draft.BusinessId, draft.PublishedPlanVersionId, draft.PlanCode,
                draft.BillingCycle, draft.PeriodStartDate, draft.PeriodEndDate,
                draft.CurrencyCode, draft.Amount, auditId = audit.Id
            });
        });
    }

    private static DateOnly? PeriodEnd(DateOnly? start, string? cycle)
    {
        if (start is null || start.Value.Year is < 2020 or > 2098)
            return null;

        return cycle switch
        {
            "Monthly" => start.Value.AddMonths(1),
            "Quarterly" => start.Value.AddMonths(3),
            "Yearly" => start.Value.AddYears(1),
            _ => null
        };
    }

    private static async Task<BillingTerms?> ResolveTerms(AppDbContext db, long businessId,
        string cycle)
    {
        var subscription = await (from business in db.Businesses.AsNoTracking()
                                  join plan in db.BusinessSubscriptions.AsNoTracking()
                                      on business.Id equals plan.BusinessId
                                  where business.Id == businessId
                                  select new
                                  {
                                      business.Name, business.CurrencyCode, plan.PlanCode
                                  }).SingleOrDefaultAsync();
        if (subscription is null) return null;

        var published = await db.PublishedPlanVersions.AsNoTracking()
            .Where(x => x.PlanCode == subscription.PlanCode)
            .OrderByDescending(x => x.Version).FirstOrDefaultAsync();
        if (published is null || published.CurrencyCode != subscription.CurrencyCode)
            return null;

        var amount = cycle switch
        {
            "Monthly" => published.MonthlyPrice,
            "Quarterly" => published.QuarterlyPrice,
            _ => published.YearlyPrice
        };
        if (amount <= 0) return null;

        return new BillingTerms(subscription.Name, subscription.PlanCode,
            published.Id, published.Version, published.CurrencyCode, amount);
    }

    private sealed record PreparePaymentRequest(string? RequestKey, string? Reason);

    private sealed record CreateBillingDraftRequest(long BusinessId,
        long ExpectedPlanVersionId, string? BillingCycle, DateOnly? PeriodStartDate,
        string? RequestKey, string? Reason);

    private readonly record struct BillingTerms(string BusinessName, string PlanCode,
        long PlanVersionId, int Version, string CurrencyCode, decimal Amount);
}
