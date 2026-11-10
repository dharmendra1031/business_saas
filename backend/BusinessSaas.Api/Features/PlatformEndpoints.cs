using System.Security.Claims;
using BusinessSaas.Api.Data;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace BusinessSaas.Api.Features;

public static class PlatformEndpoints
{
    public static void MapPlatformEndpoints(this WebApplication app)
    {
        app.MapGet("/api/platform/license-access", async (
            ClaimsPrincipal user, AppDbContext db) =>
        {
            var id = user.FindFirstValue(ClaimTypes.NameIdentifier);
            if (!long.TryParse(id, out var userId))
                return Results.Unauthorized();

            var permitted = await db.Set<IdentityUserClaim<long>>()
                .AsNoTracking()
                .AnyAsync(x => x.UserId == userId &&
                    x.ClaimType == "platform.permission" &&
                    x.ClaimValue == "licenses.manage");

            return permitted
                ? Results.Ok(new { canManageLicenses = true })
                : Results.Forbid();
        }).RequireAuthorization();
    }
}
