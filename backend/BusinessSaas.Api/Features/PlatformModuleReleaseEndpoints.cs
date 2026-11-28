using System.Security.Claims;
using BusinessSaas.Api.Data;
using Microsoft.EntityFrameworkCore;

namespace BusinessSaas.Api.Features;

// Only compiled, verified Retail modules can be released. UI state never grants permissions.
public static class PlatformModuleReleaseEndpoints
{
    private static readonly (string Code, string Name)[] Modules =
    [
        ("CATALOG", "Catalog & Products"), ("PURCHASE", "Purchase Orders"),
        ("INVENTORY", "Inventory & Stock"), ("POS", "POS & Cash Register")
    ];
    private static readonly string[] States =
        ["Available", "Coming Soon", "Under Construction", "Paused", "Retired"];

    private static bool Installed(string code, IWebHostEnvironment environment) =>
        code switch {
            "CATALOG" or "PURCHASE" or "INVENTORY" => true,
            "POS" => RetailModule.PilotEnabled(environment),
            _ => false
        };

    internal static async Task<bool> Available(AppDbContext db, IWebHostEnvironment environment, string code)
    {
        if (!Installed(code, environment)) return false;
        var state = await db.PlatformModulePolicies.AsNoTracking()
            .Where(x => x.Code == code).Select(x => x.ReleaseState).FirstOrDefaultAsync();
        return state is null or "Available";
    }

    // All module gates apply to operations, not historical reads.
    internal static IEnumerable<string> ModulesForWrite(string[] parts)
    {
        if (parts.Length < 2) yield break;
        // ASP.NET routes are case-insensitive. Gate the same paths regardless of casing.
        var route = parts.Select(x => x.ToLowerInvariant()).ToArray();
        switch (route[1])
        {
            case "categories": case "units": case "brands": case "products":
            case "suppliers": case "global-products":
                yield return "CATALOG"; break;
            case "customers":
                yield return "CATALOG";
                if (route.Length == 6 && route[3] == "credit-invoices" && route[5] == "payments")
                    yield return "POS";
                break;
            case "purchase-orders":
                yield return "PURCHASE";
                if (route.Length == 6 && route[3] == "receivings" &&
                    route[5] is "post" or "returns")
                    yield return "INVENTORY";
                break;
            case "stock":
                yield return "INVENTORY"; break;
            case "cash-sales": case "credit-sales":
                yield return "POS";
                yield return "INVENTORY";
                break;
            case "cash-register":
                yield return "POS";
                break;
        }
    }

    public static void MapPlatformModuleRelease(this WebApplication app)
    {
        var group = app.MapGroup("/api/platform/module-controls").RequireAuthorization();
        group.MapGet("", async (ClaimsPrincipal actor, IConfiguration config, AppDbContext db,
            IWebHostEnvironment environment) =>
        {
            if (!PlatformOwner.IsOwner(actor, config)) return Results.Forbid();
            var policies = await db.PlatformModulePolicies.AsNoTracking().ToListAsync();
            var items = Modules.Select(module =>
            {
                var policy = policies.FirstOrDefault(x => x.Code == module.Code);
                var installed = Installed(module.Code, environment);
                return new {
                    module.Code, module.Name, installed,
                    releaseState = installed ? policy?.ReleaseState ?? "Available" : "Under Construction",
                    revision = policy?.Revision ?? 0,
                    canOperate = installed && policy?.ReleaseState is null or "Available"
                };
            });
            return Results.Ok(items);
        });

        group.MapPost("/{code}/release", async (string code, ModuleReleaseRequest input,
            ClaimsPrincipal actor, IConfiguration config, AppDbContext db,
            IWebHostEnvironment environment) =>
        {
            if (!PlatformOwner.IsOwner(actor, config)) return Results.Forbid();
            var moduleCode = code.Trim().ToUpperInvariant();
            var reason = input.Reason?.Trim() ?? "";
            if (!Modules.Any(x => x.Code == moduleCode)) return Results.NotFound();
            if (input.State is null || !States.Contains(input.State) ||
                input.ExpectedRevision < 0 || reason.Length is < 10 or > 200)
                return Results.BadRequest(new { message = "Valid state, expected revision and 10–200 character reason required." });
            if (input.State == "Available" && !Installed(moduleCode, environment))
                return Results.Conflict(new { message = "An uninstalled module cannot be released." });

            await using var transaction = await db.Database.BeginTransactionAsync();
            var old = await db.PlatformModulePolicies.AsNoTracking()
                .SingleOrDefaultAsync(x => x.Code == moduleCode);
            var actualRevision = old?.Revision ?? 0;
            var oldState = old?.ReleaseState ??
                (Installed(moduleCode, environment) ? "Available" : "Under Construction");
            if (actualRevision != input.ExpectedRevision)
                return Results.Conflict(new { message = "Module release policy changed; reload." });
            if (oldState == input.State)
                return Results.Ok(new { code = moduleCode, releaseState = oldState,
                    revision = actualRevision, unchanged = true });

            var actorId = long.Parse(actor.FindFirstValue(ClaimTypes.NameIdentifier)!);
            var nextRevision = actualRevision + 1;
            long policyId;
            if (old is null)
            {
                var record = new PlatformModulePolicy {
                    Code = moduleCode, ReleaseState = input.State, Revision = nextRevision,
                    UpdatedByUserId = actorId, UpdatedAtUtc = DateTime.UtcNow
                };
                db.PlatformModulePolicies.Add(record);
                try { await db.SaveChangesAsync(); }
                catch (DbUpdateException)
                {
                    return Results.Conflict(new { message = "Module release policy changed; reload." });
                }
                policyId = record.Id;
            }
            else
            {
                var count = await db.PlatformModulePolicies.Where(x =>
                    x.Code == moduleCode && x.Revision == actualRevision)
                    .ExecuteUpdateAsync(setters => setters
                        .SetProperty(x => x.ReleaseState, input.State)
                        .SetProperty(x => x.Revision, nextRevision)
                        .SetProperty(x => x.UpdatedByUserId, actorId)
                        .SetProperty(x => x.UpdatedAtUtc, DateTime.UtcNow));
                if (count != 1)
                    return Results.Conflict(new { message = "Module release policy changed; reload." });
                policyId = old.Id;
            }
            db.PlatformAuditEvents.Add(new PlatformAuditEvent {
                ActorUserId = actorId, Action = "Module.ReleaseChanged",
                TargetType = "Module", TargetId = policyId,
                PreviousValue = oldState, NewValue = input.State, Reason = reason
            });
            await db.SaveChangesAsync();
            await transaction.CommitAsync();
            return Results.Ok(new { code = moduleCode, releaseState = input.State,
                revision = nextRevision });
        });
    }

    private sealed record ModuleReleaseRequest(string? State, int ExpectedRevision, string? Reason);
}
