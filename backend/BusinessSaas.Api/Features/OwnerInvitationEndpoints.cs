using System.ComponentModel.DataAnnotations;
using System.Data;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using BusinessSaas.Api.Data;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.EntityFrameworkCore;

namespace BusinessSaas.Api.Features;

// Owner invites verify only email ownership. They never grant a business role by themselves;
// MFA-protected Super Admin provisioning assigns the Owner membership separately.
public static class OwnerInvitationEndpoints
{
    public static void MapOwnerInvitationEndpoints(this WebApplication app)
    {
        var platform = app.MapGroup("/api/platform/owner-invitations").RequireAuthorization();
        platform.MapPost("", async (InviteRequest input, ClaimsPrincipal actor,
            IConfiguration config, UserManager<AppUser> users, AppDbContext db,
            IOwnerInvitationMailer mailer, ILogger<Program> logger, CancellationToken ct) =>
        {
            if (!PlatformOwner.IsOwner(actor, config)) return Results.Forbid();
            var email = input.Email?.Trim() ?? "";
            var reason = input.Reason?.Trim() ?? "";
            if (email.Length is < 5 or > 254 ||
                !new EmailAddressAttribute().IsValid(email) ||
                reason.Length is < 10 or > 200)
                return Results.BadRequest(new { message = "Enter a valid email and an audit reason (10–200 characters)." });
            if (!mailer.IsConfigured ||
                !Uri.TryCreate(config["OwnerInvitations:PublicBaseUrl"], UriKind.Absolute, out var url) ||
                url.Scheme != Uri.UriSchemeHttps || !string.IsNullOrEmpty(url.Query) ||
                !string.IsNullOrEmpty(url.Fragment) || url.AbsolutePath != "/")
                return Results.Json(new { message = "Verified invitation email is not configured." },
                    statusCode: StatusCodes.Status503ServiceUnavailable);
            if (await users.FindByEmailAsync(email) is not null)
                return Results.Conflict(new { message = "This account already exists. Verify it and use Owner lookup." });

            var normalizedEmail = users.NormalizeEmail(email);
            var now = DateTime.UtcNow;
            // Avoid accidental repeat invitations and mail floods; token is never returned.
            await using var tx = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, ct);
            if (await db.PlatformOwnerInvitations.AnyAsync(x =>
                x.NormalizedEmail == normalizedEmail && x.CreatedAtUtc > now.AddMinutes(-10), ct))
                return Results.StatusCode(StatusCodes.Status429TooManyRequests);
            var pending = await db.PlatformOwnerInvitations.Where(x =>
                x.NormalizedEmail == normalizedEmail && x.AcceptedAtUtc == null &&
                x.RevokedAtUtc == null).ToListAsync(ct);
            foreach (var old in pending) old.RevokedAtUtc = now;

            var token = WebEncoders.Base64UrlEncode(RandomNumberGenerator.GetBytes(32));
            var invite = new PlatformOwnerInvitation {
                Email = email, NormalizedEmail = normalizedEmail, TokenHash = Hash(token),
                CreatedAtUtc = now, ExpiresAtUtc = now.AddHours(48),
                CreatedByUserId = long.Parse(actor.FindFirstValue(ClaimTypes.NameIdentifier)!),
                Reason = reason
            };
            db.PlatformOwnerInvitations.Add(invite);
            await db.SaveChangesAsync(ct);
            db.PlatformAuditEvents.Add(new PlatformAuditEvent {
                ActorUserId = invite.CreatedByUserId, Action = "OwnerInvitation.Issued",
                TargetType = "OwnerInvitation", TargetId = invite.Id, Reason = reason
            });
            await db.SaveChangesAsync(ct);
            await tx.CommitAsync(ct);
            try
            {
                await mailer.SendAsync(email,
                    new Uri(url, "?invite=" + Uri.EscapeDataString(token)).ToString(), ct);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                logger.LogWarning("Owner invitation delivery failed for invitation {InvitationId}", invite.Id);
                await db.PlatformOwnerInvitations.Where(x => x.Id == invite.Id &&
                    x.AcceptedAtUtc == null && x.RevokedAtUtc == null)
                    .ExecuteUpdateAsync(s => s.SetProperty(x => x.RevokedAtUtc, DateTime.UtcNow), ct);
                return Results.Json(new { message = "Email delivery failed. No active invitation remains. Check SMTP settings." },
                    statusCode: StatusCodes.Status503ServiceUnavailable);
            }
            return Results.Ok(new { invite.Id, invite.Email, invite.ExpiresAtUtc, status = "Sent" });
        });

        var publicGroup = app.MapGroup("/api/auth/owner-invitations")
            .RequireRateLimiting("register");
        publicGroup.MapPost("/inspect", async (TokenRequest input, AppDbContext db) =>
        {
            if (!ValidToken(input.Token)) return Results.NotFound();
            var hash = Hash(input.Token!);
            var now = DateTime.UtcNow;
            var invite = await db.PlatformOwnerInvitations.AsNoTracking()
                .Where(x => x.TokenHash == hash && x.ExpiresAtUtc > now &&
                    x.RevokedAtUtc == null && x.AcceptedAtUtc == null)
                .Select(x => new { x.Email, x.ExpiresAtUtc }).SingleOrDefaultAsync();
            return invite is null ? Results.NotFound() : Results.Ok(invite);
        });
        publicGroup.MapPost("/accept", async (AcceptRequest input, AppDbContext db,
            UserManager<AppUser> users, CancellationToken ct) =>
        {
            if (!ValidToken(input.Token) || string.IsNullOrEmpty(input.Password))
                return Results.BadRequest(new { message = "Invitation and password are required." });
            var now = DateTime.UtcNow;
            await using var tx = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, ct);
            var hash = Hash(input.Token!);
            var invite = await db.PlatformOwnerInvitations.SingleOrDefaultAsync(x =>
                x.TokenHash == hash && x.ExpiresAtUtc > now &&
                x.RevokedAtUtc == null && x.AcceptedAtUtc == null, ct);
            if (invite is null) return Results.Conflict(new { message = "Invitation expired or already used." });
            if (await users.FindByEmailAsync(invite.Email) is not null)
                return Results.Conflict(new { message = "An account with this email already exists." });

            var result = await users.CreateAsync(new AppUser {
                UserName = invite.Email, Email = invite.Email, EmailConfirmed = true
            }, input.Password);
            if (!result.Succeeded)
                return Results.BadRequest(new {
                    message = string.Join(" ", result.Errors.Select(x => x.Description))
                });
            var created = await users.FindByEmailAsync(invite.Email);
            if (created is null) throw new InvalidOperationException("Invited account creation failed.");
            invite.AcceptedAtUtc = now;
            invite.AcceptedUserId = created.Id;
            db.PlatformAuditEvents.Add(new PlatformAuditEvent {
                ActorUserId = invite.CreatedByUserId,
                Action = "OwnerInvitation.Accepted",
                TargetType = "OwnerInvitation", TargetId = invite.Id,
                NewValue = $"UserId:{created.Id}"
            });
            await db.SaveChangesAsync(ct);
            await tx.CommitAsync(ct);
            return Results.Ok(new { verified = true, email = invite.Email,
                message = "Account verified. Sign in, then ask the Super Admin to assign your business." });
        });
    }

    private static string Hash(string token) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(token)));

    private static bool ValidToken(string? token) =>
        token is { Length: 43 } && token.All(x =>
            char.IsAsciiLetterOrDigit(x) || x is '-' or '_');

    private sealed record InviteRequest(string? Email, string? Reason);
    private sealed record TokenRequest(string? Token);
    private sealed record AcceptRequest(string? Token, string? Password);
}
