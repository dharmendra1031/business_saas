using System.Security.Claims;
using BusinessSaas.Api.Data;
using Microsoft.EntityFrameworkCore;

namespace BusinessSaas.Api.Features;

public static class PlatformTypeAvailabilityEndpoints
{
    private static readonly string[] States =
        ["Available", "Coming Soon", "Under Construction", "Paused", "Retired"];

    public static void MapPlatformTypeAvailability(this WebApplication app)
    {
        var group = app.MapGroup("/api/platform/business-type-controls").RequireAuthorization();

        group.MapGet("", async (ClaimsPrincipal user, IConfiguration configuration, AppDbContext db) =>
        {
            if (!PlatformOwner.IsOwner(user, configuration)) return Results.Forbid();
            var types = await db.BusinessTypes.AsNoTracking().OrderBy(x => x.Name)
                .Select(x => new { x.Id, x.Code, x.Name, x.IsSupported,
                    x.ReleaseState, x.ReleaseRevision }).ToListAsync();
            return Results.Ok(types.Select(x => new {
                x.Id, x.Code, x.Name, x.IsSupported, x.ReleaseState, x.ReleaseRevision,
                canProvision = x.IsSupported && x.Code == "RETAIL" && x.ReleaseState == "Available"
            }));
        });

        group.MapPost("/{code}/release", async (string code, UpdateRelease request,
            ClaimsPrincipal user, IConfiguration configuration, AppDbContext db) =>
        {
            if (!PlatformOwner.IsOwner(user, configuration)) return Results.Forbid();
            var typeCode = code.Trim().ToUpperInvariant();
            var reason = request.Reason?.Trim() ?? "";
            if (request.ExpectedRevision < 0 || reason.Length is < 10 or > 200 ||
                request.State is null || !States.Contains(request.State))
                return Results.BadRequest(new { message = "Select a valid state and provide a 10–200 character reason." });

            await using var tx = await db.Database.BeginTransactionAsync();
            var current = await db.BusinessTypes.AsNoTracking()
                .SingleOrDefaultAsync(x => x.Code == typeCode);
            if (current is null) return Results.NotFound();
            if (request.State == "Available" && (!current.IsSupported || current.Code != "RETAIL"))
                return Results.Conflict(new { message = "A type cannot be released without a verified, installed workflow." });
            if (request.ExpectedRevision != current.ReleaseRevision)
                return Results.Conflict(new { message = "Release policy changed. Reload before saving." });
            if (request.State == current.ReleaseState)
                return Results.Ok(new { current.Id, current.Code, current.ReleaseState,
                    current.ReleaseRevision, unchanged = true });

            var updated = await db.BusinessTypes.Where(x =>
                x.Id == current.Id && x.ReleaseRevision == request.ExpectedRevision)
                .ExecuteUpdateAsync(setters => setters
                    .SetProperty(x => x.ReleaseState, request.State)
                    .SetProperty(x => x.ReleaseRevision, x => x.ReleaseRevision + 1));
            if (updated != 1)
                return Results.Conflict(new { message = "Release policy changed. Reload before saving." });

            var audit = new PlatformAuditEvent
            {
                ActorUserId = long.Parse(user.FindFirstValue(ClaimTypes.NameIdentifier)!),
                Action = "BusinessType.ReleaseChanged",
                TargetType = "BusinessType", TargetId = current.Id,
                Reason = reason, PreviousValue = current.ReleaseState, NewValue = request.State
            };
            db.PlatformAuditEvents.Add(audit);
            await db.SaveChangesAsync();
            await tx.CommitAsync();
            return Results.Ok(new { current.Id, current.Code, releaseState = request.State,
                releaseRevision = current.ReleaseRevision + 1, auditId = audit.Id });
        });
    }

    private sealed record UpdateRelease(string? State, int ExpectedRevision, string? Reason);
}
