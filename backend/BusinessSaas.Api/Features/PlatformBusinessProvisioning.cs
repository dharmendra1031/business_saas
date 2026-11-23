using System.Data.Common;
using System.Security.Claims;
using BusinessSaas.Api.Data;
using Microsoft.AspNetCore.Identity;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;

namespace BusinessSaas.Api.Features;

public static class PlatformBusinessProvisioning
{
    private const string TrialPlanCode = "STARTER";
    private static readonly string[] RequiredModules = ["CATALOG", "PURCHASE", "INVENTORY"];

    public static void MapPlatformBusinessProvisioning(this WebApplication app)
    {
        var group = app.MapGroup("/api/platform").RequireAuthorization();

        group.MapGet("/provisioning-eligibility", async (ClaimsPrincipal actor,
            IConfiguration config, AppDbContext db, IWebHostEnvironment environment) =>
        {
            if (!PlatformOwner.IsOwner(actor, config)) return Results.Forbid();

            var types = await BusinessEndpoints.ProvisionableTypes(db)
                .Select(x => new { x.Id, x.Code, x.Name }).ToListAsync();
            var trialDays = GetTrialDays(config);
            var block = trialDays is null ? TrialUnavailable :
                types.Count == 0 ? TypeUnavailable : await GetPlatformBlock(db, environment);
            if (block is not null) types.Clear();
            return Results.Ok(new { types, block, trialDays });
        });

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
            UserManager<AppUser> users, IWebHostEnvironment environment) =>
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
                plan != TrialPlanCode)
                return Results.BadRequest(new
                {
                    message = "Provide a business name, verified owner, first location and a valid Starter trial."
                });

            if (!BusinessEndpoints.ValidMarket(country, currency, zone))
                return Results.BadRequest(new { message = "Choose a supported country, currency and timezone." });

            var owner = await users.FindByEmailAsync(email);
            if (owner is null || PlatformOwner.IsOwnerId(owner.Id, config))
                return Results.BadRequest(new { message = "Choose an existing verified business owner account." });
            if (!owner.EmailConfirmed)
                return Results.Conflict(new { message = "The owner must verify their email before assignment." });

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
                var subscription = await db.BusinessSubscriptions.AsNoTracking()
                    .Where(x => x.BusinessId == existing.Id)
                    .Select(x => new { x.PlanCode, x.TrialEndsAtUtc }).SingleOrDefaultAsync();
                var matches = existing.Name == name && existing.BusinessTypeId == request.BusinessTypeId &&
                    existing.CountryCode == country && existing.CurrencyCode == currency &&
                    existing.TimeZoneId == zone && firstLocation == location && subscription?.PlanCode == plan;
                return matches ? Results.Ok(new { existing.Id, alreadyExists = true, subscription!.TrialEndsAtUtc }) :
                    Results.Conflict(new { message = "This request key was already used for another setup. Reload before retrying." });
            }

            var before = await CheckExistingAsync();
            if (before is not Microsoft.AspNetCore.Http.HttpResults.NotFound) return before;

            var trialDays = GetTrialDays(config);
            if (trialDays is null)
                return Results.Json(TrialUnavailable, statusCode: StatusCodes.Status503ServiceUnavailable);
            if (request.ExpectedTrialDays is int expected && expected != trialDays.Value)
                return Results.Conflict(new ProvisioningBlock(
                    "TRIAL_DURATION_CHANGED", "The trial duration changed. Review and confirm the updated duration."));

            var actorId = long.Parse(actor.FindFirstValue(ClaimTypes.NameIdentifier)!);
            await using var transaction = await db.Database.BeginTransactionAsync(
                System.Data.IsolationLevel.Serializable);
            try
            {
                // Recheck eligibility after entering the provisioning transaction:
                // a stale wizard selection cannot bypass a freshly paused business type.
                var typeCode = await BusinessEndpoints.ProvisionableTypes(db)
                    .Where(x => x.Id == request.BusinessTypeId)
                    .Select(x => x.Code).SingleOrDefaultAsync();
                var block = typeCode is null ? TypeUnavailable : await GetPlatformBlock(db, environment);
                if (block is not null) return Results.Conflict(block);

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
                var trialEndsAtUtc = DateTime.UtcNow.AddDays(trialDays.Value);
                db.BusinessSubscriptions.Add(new BusinessSubscription
                {
                    BusinessId = business.Id, PlanCode = plan, Status = "Trial",
                    TrialEndsAtUtc = trialEndsAtUtc
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
                    new { business.Id, alreadyExists = false, trialEndsAtUtc });
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

    private static readonly ProvisioningBlock TypeUnavailable = new(
        "BUSINESS_TYPE_NOT_AVAILABLE", "New business provisioning is unavailable for this type.");
    private static readonly ProvisioningBlock TrialUnavailable = new(
        "TRIAL_CONFIGURATION_UNAVAILABLE", "Trial configuration is not available.");

    private static int? GetTrialDays(IConfiguration config) =>
        int.TryParse(config["Saas:TrialDays"], out var days) && days is >= 1 and <= 90 ? days : null;

    private static async Task<ProvisioningBlock?> GetPlatformBlock(
        AppDbContext db, IWebHostEnvironment environment)
    {
        if (await PlatformStatusEndpoints.ActiveMaintenance(db) is not null)
            return new("PLATFORM_MAINTENANCE",
                "New business creation is temporarily paused for maintenance.");

        foreach (var module in RequiredModules)
            if (!await PlatformModuleReleaseEndpoints.Available(db, environment, module))
                return new("MODULE_NOT_AVAILABLE",
                    $"Required module {module} is not available for new businesses.");

        return null;
    }

    private sealed record ProvisioningBlock(string Code, string Message);

    private sealed record ProvisionBusinessRequest(
        string? Name, long BusinessTypeId, string? CountryCode,
        string? CurrencyCode, string? TimeZoneId, string? OwnerEmail,
        string? LocationName, string? PlanCode, string? RequestKey, int? ExpectedTrialDays);
}
