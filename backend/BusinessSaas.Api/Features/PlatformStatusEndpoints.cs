using System.Security.Claims;
using BusinessSaas.Api.Data;
using Microsoft.EntityFrameworkCore;

namespace BusinessSaas.Api.Features;

// A narrow global control: UTC windows expire naturally; no background worker or tenant flag duplication.
public static class PlatformStatusEndpoints
{
    public static void MapPlatformStatus(this WebApplication app)
    {
        app.MapGet("/api/public/platform-availability", async (AppDbContext db) =>
        {
            var now = DateTime.UtcNow;
            var maintenance = await db.PlatformMaintenanceWindows.AsNoTracking()
                .Where(x => !x.IsCancelled && x.StartUtc <= now && x.EndUtc > now)
                .OrderByDescending(x => x.Mode == "Unavailable")
                .ThenByDescending(x => x.Mode == "ReadOnly")
                .ThenByDescending(x => x.Id)
                .Select(x => new { x.Id, x.Mode, x.Title, x.Message, x.EndUtc }).FirstOrDefaultAsync();
            var announcements = await db.PlatformAnnouncements.AsNoTracking()
                .Where(x => !x.IsArchived && x.StartUtc <= now && x.EndUtc > now)
                .OrderByDescending(x => x.Id).Take(5)
                .Select(x => new { x.Id, x.Level, x.Title, x.Message, x.EndUtc }).ToListAsync();
            return Results.Ok(new { serverTimeUtc = now, maintenance, announcements });
        });

        var group = app.MapGroup("/api/platform").RequireAuthorization();
        group.MapGet("/maintenance", async (ClaimsPrincipal actor, IConfiguration config, AppDbContext db) =>
        {
            if (!PlatformOwner.IsOwner(actor, config)) return Results.Forbid();
            return Results.Ok(await db.PlatformMaintenanceWindows.AsNoTracking()
                .OrderByDescending(x => x.Id).Take(30).ToListAsync());
        });
        group.MapPost("/maintenance", async (ScheduleWindow input, ClaimsPrincipal actor,
            IConfiguration config, AppDbContext db) =>
        {
            if (!PlatformOwner.IsOwner(actor, config)) return Results.Forbid();
            if (!ValidText(input.Title, 3, 100) || !ValidText(input.Message, 10, 300) ||
                !ValidText(input.Reason, 10, 200) ||
                input.Mode is not ("NoticeOnly" or "ReadOnly" or "Unavailable") ||
                !ValidWindow(input.StartUtc, input.EndUtc))
                return Results.BadRequest(new { message = "Enter a valid UTC window, mode, public notice and audit reason." });

            await using var tx = await db.Database.BeginTransactionAsync(System.Data.IsolationLevel.Serializable);
            if (await db.PlatformMaintenanceWindows.AnyAsync(x => !x.IsCancelled &&
                x.StartUtc < input.EndUtc && x.EndUtc > input.StartUtc))
                return Results.Conflict(new { message = "The maintenance period overlaps another window." });

            var actorId = long.Parse(actor.FindFirstValue(ClaimTypes.NameIdentifier)!);
            var window = new PlatformMaintenanceWindow {
                Mode = input.Mode, Title = input.Title!.Trim(), Message = input.Message!.Trim(),
                StartUtc = input.StartUtc, EndUtc = input.EndUtc,
                Reason = input.Reason!.Trim(), CreatedByUserId = actorId
            };
            db.PlatformMaintenanceWindows.Add(window);
            await db.SaveChangesAsync();
            db.PlatformAuditEvents.Add(new PlatformAuditEvent {
                ActorUserId = actorId, Action = "Maintenance.Scheduled", TargetType = "Maintenance",
                TargetId = window.Id, Reason = window.Reason, NewValue = window.Mode
            });
            await db.SaveChangesAsync();
            await tx.CommitAsync();
            return Results.Created($"/api/platform/maintenance/{window.Id}", window);
        });
        group.MapPost("/maintenance/{id:long}/cancel", async (long id, ReasonInput input,
            ClaimsPrincipal actor, IConfiguration config, AppDbContext db) =>
        {
            if (!PlatformOwner.IsOwner(actor, config)) return Results.Forbid();
            if (!ValidText(input.Reason, 10, 200)) return Results.BadRequest();
            await using var tx = await db.Database.BeginTransactionAsync();
            var updated = await db.PlatformMaintenanceWindows
                .Where(x => x.Id == id && !x.IsCancelled)
                .ExecuteUpdateAsync(s => s.SetProperty(x => x.IsCancelled, true));
            if (updated != 1) return Results.Conflict(new { message = "Maintenance window unavailable or cancelled." });
            db.PlatformAuditEvents.Add(new PlatformAuditEvent {
                ActorUserId = long.Parse(actor.FindFirstValue(ClaimTypes.NameIdentifier)!),
                Action = "Maintenance.Cancelled", TargetType = "Maintenance", TargetId = id,
                Reason = input.Reason!.Trim()
            });
            await db.SaveChangesAsync();
            await tx.CommitAsync();
            return Results.Ok(new { id, cancelled = true });
        });

        group.MapGet("/announcements", async (ClaimsPrincipal actor, IConfiguration config, AppDbContext db) =>
        {
            if (!PlatformOwner.IsOwner(actor, config)) return Results.Forbid();
            return Results.Ok(await db.PlatformAnnouncements.AsNoTracking()
                .OrderByDescending(x => x.Id).Take(30).ToListAsync());
        });
        group.MapPost("/announcements", async (PublishNotice input, ClaimsPrincipal actor,
            IConfiguration config, AppDbContext db) =>
        {
            if (!PlatformOwner.IsOwner(actor, config)) return Results.Forbid();
            if (!ValidText(input.Title, 3, 100) || !ValidText(input.Message, 10, 300) ||
                !ValidText(input.Reason, 10, 200) ||
                input.Level is not ("Information" or "Warning" or "Critical") ||
                !ValidWindow(input.StartUtc, input.EndUtc))
                return Results.BadRequest(new { message = "Enter valid UTC dates, message, level and audit reason." });

            await using var tx = await db.Database.BeginTransactionAsync();
            var actorId = long.Parse(actor.FindFirstValue(ClaimTypes.NameIdentifier)!);
            var notice = new PlatformAnnouncement {
                Level = input.Level, Title = input.Title!.Trim(), Message = input.Message!.Trim(),
                StartUtc = input.StartUtc, EndUtc = input.EndUtc,
                Reason = input.Reason!.Trim(), CreatedByUserId = actorId
            };
            db.PlatformAnnouncements.Add(notice);
            await db.SaveChangesAsync();
            db.PlatformAuditEvents.Add(new PlatformAuditEvent {
                ActorUserId = actorId, Action = "Announcement.Published", TargetType = "Announcement",
                TargetId = notice.Id, Reason = notice.Reason, NewValue = notice.Level
            });
            await db.SaveChangesAsync();
            await tx.CommitAsync();
            return Results.Created($"/api/platform/announcements/{notice.Id}", notice);
        });
        group.MapPost("/announcements/{id:long}/archive", async (long id, ReasonInput input,
            ClaimsPrincipal actor, IConfiguration config, AppDbContext db) =>
        {
            if (!PlatformOwner.IsOwner(actor, config)) return Results.Forbid();
            if (!ValidText(input.Reason, 10, 200)) return Results.BadRequest();
            await using var tx = await db.Database.BeginTransactionAsync();
            var updated = await db.PlatformAnnouncements.Where(x => x.Id == id && !x.IsArchived)
                .ExecuteUpdateAsync(s => s.SetProperty(x => x.IsArchived, true));
            if (updated != 1) return Results.Conflict(new { message = "Announcement unavailable or already archived." });
            db.PlatformAuditEvents.Add(new PlatformAuditEvent {
                ActorUserId = long.Parse(actor.FindFirstValue(ClaimTypes.NameIdentifier)!),
                Action = "Announcement.Archived", TargetType = "Announcement", TargetId = id,
                Reason = input.Reason!.Trim()
            });
            await db.SaveChangesAsync();
            await tx.CommitAsync();
            return Results.Ok(new { id, archived = true });
        });
    }

    internal static async Task<PlatformMaintenanceWindow?> ActiveMaintenance(AppDbContext db)
    {
        var now = DateTime.UtcNow;
        return await db.PlatformMaintenanceWindows.AsNoTracking()
            .Where(x => !x.IsCancelled && x.StartUtc <= now && x.EndUtc > now &&
                x.Mode != "NoticeOnly")
            .OrderByDescending(x => x.Mode == "Unavailable").FirstOrDefaultAsync();
    }

    private static bool ValidText(string? input, int min, int max) =>
        input?.Trim() is { } value && value.Length >= min && value.Length <= max &&
        !value.Contains('<') && !value.Contains('>');

    private static bool ValidWindow(DateTime start, DateTime end)
    {
        var now = DateTime.UtcNow;
        return start.Kind == DateTimeKind.Utc && end.Kind == DateTimeKind.Utc &&
            start >= now.AddMinutes(-2) && start <= now.AddDays(30) && end > start &&
            end - start <= TimeSpan.FromHours(24);
    }

    private sealed record ScheduleWindow(string? Mode, string? Title, string? Message,
        DateTime StartUtc, DateTime EndUtc, string? Reason);
    private sealed record PublishNotice(string? Level, string? Title, string? Message,
        DateTime StartUtc, DateTime EndUtc, string? Reason);
    private sealed record ReasonInput(string? Reason);
}
