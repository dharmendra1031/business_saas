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
                                   draft.CreatedAtUtc,
                                   status = "Draft"
                               })
                .Skip((page - 1) * pageSize).Take(pageSize).ToListAsync();

            return Results.Ok(new { items, total, page, pageSize });
        });

        group.MapGet("/businesses/{businessId:long}/preview", async (long businessId,
            string cycle, ClaimsPrincipal user, IConfiguration configuration, AppDbContext db) =>
        {
            if (!PlatformOwner.IsOwner(user, configuration))
                return Results.Forbid();
            if (!IsValidCycle(cycle))
                return Results.BadRequest(new { message = "Choose Monthly, Quarterly or Yearly." });

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
            if (request.BusinessId <= 0 || request.ExpectedPlanVersionId <= 0 ||
                !IsValidCycle(request.BillingCycle) || reason.Length is < 10 or > 200 ||
                requestKey.Length is < 12 or > 80 ||
                !requestKey.All(ch => char.IsAsciiLetterOrDigit(ch) || ch == '-'))
                return Results.BadRequest(new
                {
                    message = "Business, current plan version, billing cycle, request key and 10–200 character reason are required."
                });

            var existing = await db.SubscriptionBillingDrafts.AsNoTracking()
                .SingleOrDefaultAsync(x => x.BusinessId == request.BusinessId &&
                    x.RequestKey == requestKey);
            if (existing is not null)
            {
                return existing.PublishedPlanVersionId == request.ExpectedPlanVersionId &&
                    existing.BillingCycle == request.BillingCycle
                    ? Results.Ok(new { id = existing.Id, status = "Draft", alreadyExists = true })
                    : Results.Conflict(new { message = "Request key already belongs to a different billing draft." });
            }

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
                CurrencyCode = terms.Value.CurrencyCode,
                Amount = terms.Value.Amount,
                RequestKey = requestKey,
                CreatedByUserId = actorId
            };

            await using var transaction = await db.Database.BeginTransactionAsync();
            db.SubscriptionBillingDrafts.Add(draft);
            try
            {
                await db.SaveChangesAsync();
            }
            catch (DbUpdateException error) when (error.InnerException is
                SqlException { Number: 2601 or 2627 } or DbException { ErrorCode: 19 })
            {
                return Results.Conflict(new { message = "This draft request was already processed. Refresh the list." });
            }

            var audit = new PlatformAuditEvent
            {
                ActorUserId = actorId,
                Action = "Billing.DraftCreated",
                TargetType = "BillingDraft",
                TargetId = draft.Id,
                Reason = reason,
                NewValue = $"{draft.PlanCode} {draft.BillingCycle} {draft.CurrencyCode} {draft.Amount:0.00}"
            };
            db.PlatformAuditEvents.Add(audit);
            await db.SaveChangesAsync();
            await transaction.CommitAsync();

            return Results.Created($"/api/platform/billing/drafts/{draft.Id}", new
            {
                id = draft.Id, status = "Draft", alreadyExists = false,
                draft.BusinessId, draft.PublishedPlanVersionId, draft.PlanCode,
                draft.BillingCycle, draft.CurrencyCode, draft.Amount, auditId = audit.Id
            });
        });
    }

    private static bool IsValidCycle(string? cycle) =>
        cycle is "Monthly" or "Quarterly" or "Yearly";

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

    private sealed record CreateBillingDraftRequest(long BusinessId,
        long ExpectedPlanVersionId, string? BillingCycle, string? RequestKey, string? Reason);

    private readonly record struct BillingTerms(string BusinessName, string PlanCode,
        long PlanVersionId, int Version, string CurrencyCode, decimal Amount);
}
