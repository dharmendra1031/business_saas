using System.Data;
using System.Security.Claims;
using BusinessSaas.Api.Data;
using Microsoft.EntityFrameworkCore;

namespace BusinessSaas.Api.Features;

// Business Owner payment preferences. Never treat an enabled UI switch as payment authorization.
public static class RetailBillingSettingsEndpoints
{
    public static void MapRetailBillingSettings(this WebApplication app)
    {
        var group = app.MapGroup("/api/businesses/{businessId:long}/billing-settings")
            .RequireAuthorization();

        group.MapGet("", async (long businessId, ClaimsPrincipal user, AppDbContext db) =>
        {
            if (await OwnerId(db, businessId, user) is null) return Results.NotFound();
            var business = await db.Businesses.AsNoTracking().SingleAsync(x => x.Id == businessId);
            return Results.Ok(View(business));
        });

        group.MapPut("", async (long businessId, BillingSettingsRequest request,
            ClaimsPrincipal user, AppDbContext db) =>
        {
            var actorId = await OwnerId(db, businessId, user);
            if (actorId is null) return Results.NotFound();
            if (request.ExpectedRevision < 0 || request.Reason?.Trim().Length is not (>= 10 and <= 200))
                return Results.BadRequest(new { message = "Enter a 10–200 character change reason and valid revision." });
            // Credit cannot be advertised or turned on until the customer receivable
            // and collection flows are implemented, tested and explicitly released.
            if (request.CreditSalesEnabled)
                return Results.Conflict(new { code = "CREDIT_NOT_RELEASED",
                    message = "Customer Credit is not released. Keep it off until its ledger and collections are ready." });

            await using var tx = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable);
            var business = await db.Businesses.AsNoTracking()
                .SingleOrDefaultAsync(x => x.Id == businessId);
            if (business is null) return Results.NotFound();
            if (business.BillingSettingsRevision != request.ExpectedRevision)
                return Results.Conflict(new { code = "BILLING_SETTINGS_CHANGED",
                    message = "Billing settings changed. Reload before saving." });

            if (business.CashSalesEnabled == request.CashSalesEnabled &&
                business.CreditSalesEnabled == request.CreditSalesEnabled)
                return Results.Ok(new { settings = View(business), unchanged = true });

            var nextRevision = business.BillingSettingsRevision + 1;
            var changed = await db.Businesses.Where(x => x.Id == businessId &&
                    x.BillingSettingsRevision == request.ExpectedRevision)
                .ExecuteUpdateAsync(setters => setters
                    .SetProperty(x => x.CashSalesEnabled, request.CashSalesEnabled)
                    .SetProperty(x => x.CreditSalesEnabled, false)
                    .SetProperty(x => x.BillingSettingsRevision, nextRevision));
            if (changed != 1)
                return Results.Conflict(new { code = "BILLING_SETTINGS_CHANGED",
                    message = "Billing settings changed. Reload before saving." });

            db.PlatformAuditEvents.Add(new PlatformAuditEvent {
                ActorUserId = actorId.Value, Action = "Business.BillingSettingsChanged",
                TargetType = "Business", TargetId = businessId,
                PreviousValue = $"cash={business.CashSalesEnabled};credit={business.CreditSalesEnabled}",
                NewValue = $"cash={request.CashSalesEnabled};credit=false",
                Reason = request.Reason.Trim()
            });
            await db.SaveChangesAsync();
            await tx.CommitAsync();

            business.CashSalesEnabled = request.CashSalesEnabled;
            business.CreditSalesEnabled = false;
            business.BillingSettingsRevision = nextRevision;
            return Results.Ok(new { settings = View(business), unchanged = false });
        });
    }

    private static object View(Business business) => new {
        cashSalesEnabled = business.CashSalesEnabled,
        creditSalesEnabled = business.CreditSalesEnabled,
        creditAvailable = false,
        revision = business.BillingSettingsRevision,
        salesPaused = !business.CashSalesEnabled && !business.CreditSalesEnabled
    };

    private static async Task<long?> OwnerId(AppDbContext db, long businessId, ClaimsPrincipal user)
    {
        if (!long.TryParse(user.FindFirstValue(ClaimTypes.NameIdentifier), out var actor))
            return null;
        return await (from membership in db.BusinessUsers.AsNoTracking()
                      join business in db.Businesses.AsNoTracking()
                          on membership.BusinessId equals business.Id
                      join type in db.BusinessTypes.AsNoTracking()
                          on business.BusinessTypeId equals type.Id
                      where membership.BusinessId == businessId && membership.UserId == actor &&
                            membership.RoleCode == "Owner" && type.Code == "RETAIL"
                      select (long?)membership.UserId).SingleOrDefaultAsync();
    }

    private sealed record BillingSettingsRequest(bool CashSalesEnabled, bool CreditSalesEnabled,
        int ExpectedRevision, string? Reason);
}
