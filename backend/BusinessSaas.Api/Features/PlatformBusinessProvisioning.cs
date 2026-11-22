using System.Data.Common;
using System.Security.Claims;
using BusinessSaas.Api.Data;
using Microsoft.AspNetCore.Identity;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;

namespace BusinessSaas.Api.Features;

// Platform-only business provisioning. The existing tenant-owned create route must
// be retired after the legacy integration fixtures have been migrated.
public static class PlatformBusinessProvisioning
{
    public static void MapPlatformBusinessProvisioning(this WebApplication app)
    {
        var group = app.MapGroup("/api/platform").RequireAuthorization();

        group.MapGet("/owner-lookup", async (string? email, ClaimsPrincipal actor,
            IConfiguration config, UserManager<AppUser> users) =>
        {
            if (!PlatformOwner.IsOwner(actor, config)) return Results.Forbid();
            var requestedEmail = email?.Trim() ?? "";
            if (requestedEmail.Length is < 5 or > 254)
                return Results.BadRequest(new { message = "Enter a valid owner email address." });

            var owner = await users.FindByEmailAsync(requestedEmail);
            if (owner is null || PlatformOwner.IsOwnerId(owner.Id, config))
                return Results.NotFound();
            if (!owner.EmailConfirmed)
                return Results.Conflict(new
                {
                    message = "This account's email has not been verified. Confirm ownership before assigning a business."
                });

            return Results.Ok(new { owner.Id, owner.Email, verified = true });
        });

        group.MapPost("/businesses", async (ProvisionBusinessRequest request,
            ClaimsPrincipal actor, IConfiguration config, AppDbContext db,
            UserManager<AppUser> users) =>
        {
            if (!PlatformOwner.IsOwner(actor, config)) return Results.Forbid();

            var name = request.Name?.Trim() ?? "";
            var email = request.OwnerEmail?.Trim() ?? "";
            var country = request.CountryCode?.Trim().ToUpperInvariant() ?? "";
            var currency = request.CurrencyCode?.Trim().ToUpperInvariant() ?? "";
            var zone = request.TimeZoneId?.Trim() ?? "";
            var location = request.LocationName?.Trim() ?? "";
            var key = request.RequestKey?.Trim() ?? "";
            var plan = request.PlanCode?.Trim().ToUpperInvariant() ?? "";

            if (name.Length is < 2 or > 150 || email.Length is < 5 or > 254 ||
                location.Length is < 2 or > 100 || key.Length is < 12 or > 80 ||
                plan != "STARTER")
                return Results.BadRequest(new
                {
                    message = "Provide a business name, verified owner, first location and a valid Starter trial."
                });

            if (!BusinessEndpoints.ValidMarket(country, currency, zone))
                return Results.BadRequest(new { message = "Choose a supported country, currency and timezone." });

            var typeCode = await db.BusinessTypes.AsNoTracking()
                .Where(x => x.Id == request.BusinessTypeId && x.IsSupported)
                .Select(x => x.Code).SingleOrDefaultAsync();
            if (typeCode is null)
                return Results.BadRequest(new { message = "This business template is not available." });

            var owner = await users.FindByEmailAsync(email);
            if (owner is null || PlatformOwner.IsOwnerId(owner.Id, config))
                return Results.BadRequest(new { message = "Choose an existing verified business owner account." });
            if (!owner.EmailConfirmed)
                return Results.Conflict(new { message = "The owner must verify their email before assignment." });

            var trialDays = config.GetValue<int>("Saas:TrialDays");
            if (trialDays is < 1 or > 90)
                return Results.Problem("Trial configuration is not available.", statusCode: 503);

            // The current CreatedByUserId column is also used by existing owner
            // directories. Keep its legacy owner association; the actual platform
            // provisioning actor is recorded independently in PlatformAuditEvents.
            async Task<IResult> CheckExistingAsync()
            {
                var existing = await db.Businesses.AsNoTracking()
                    .FirstOrDefaultAsync(x => x.CreatedByUserId == owner.Id && x.CreationKey == key);
                if (existing is null) return Results.NotFound();

                var firstLocation = await db.BusinessLocations.AsNoTracking()
                    .Where(x => x.BusinessId == existing.Id && x.IsPrimary)
                    .Select(x => x.Name).FirstOrDefaultAsync();
                var existingPlan = await db.BusinessSubscriptions.AsNoTracking()
                    .Where(x => x.BusinessId == existing.Id).Select(x => x.PlanCode).FirstOrDefaultAsync();
                var matches = existing.Name == name && existing.BusinessTypeId == request.BusinessTypeId &&
                    existing.CountryCode == country && existing.CurrencyCode == currency &&
                    existing.TimeZoneId == zone && firstLocation == location && existingPlan == plan;
                return matches ? Results.Ok(new { existing.Id, alreadyExists = true }) :
                    Results.Conflict(new { message = "This request key was already used for another setup. Reload before retrying." });
            }

            var before = await CheckExistingAsync();
            if (before is not Microsoft.AspNetCore.Http.HttpResults.NotFound) return before;

            var actorId = long.Parse(actor.FindFirstValue(ClaimTypes.NameIdentifier)!);
            await using var transaction = await db.Database.BeginTransactionAsync();
            try
            {
                var business = new Business
                {
                    Name = name,
                    BusinessTypeId = request.BusinessTypeId,
                    CreatedByUserId = owner.Id,
                    CreationKey = key,
                    CountryCode = country,
                    CurrencyCode = currency,
                    TimeZoneId = zone
                };
                db.Businesses.Add(business);
                await db.SaveChangesAsync();

                db.BusinessUsers.Add(new BusinessUser
                {
                    BusinessId = business.Id, UserId = owner.Id, RoleCode = "Owner"
                });
                db.BusinessLocations.Add(new BusinessLocation
                {
                    BusinessId = business.Id, Name = location, Code = "MAIN", IsPrimary = true
                });
                db.BusinessSubscriptions.Add(new BusinessSubscription
                {
                    BusinessId = business.Id, PlanCode = plan, Status = "Trial",
                    TrialEndsAtUtc = DateTime.UtcNow.AddDays(trialDays)
                });
                if (typeCode == "RETAIL") RetailModule.AddBusinessDefaults(db, business.Id);
                db.PlatformAuditEvents.Add(new PlatformAuditEvent
                {
                    ActorUserId = actorId,
                    Action = "Business.Provisioned",
                    TargetType = "Business",
                    TargetId = business.Id,
                    Reason = "Platform provisioning: verified account assigned as Owner",
                    NewValue = $"OwnerUserId:{owner.Id}"
                });
                await db.SaveChangesAsync();
                await transaction.CommitAsync();
                return Results.Created($"/api/platform/businesses/{business.Id}",
                    new { business.Id, alreadyExists = false });
            }
            catch (DbUpdateException error) when (error.InnerException is
                SqlException { Number: 2601 or 2627 } or DbException { ErrorCode: 19 })
            {
                await transaction.RollbackAsync();
                db.ChangeTracker.Clear();
                return await CheckExistingAsync();
            }
        });
    }

    private sealed record ProvisionBusinessRequest(
        string? Name, long BusinessTypeId, string? CountryCode,
        string? CurrencyCode, string? TimeZoneId, string? OwnerEmail,
        string? LocationName, string? PlanCode, string? RequestKey);
}
