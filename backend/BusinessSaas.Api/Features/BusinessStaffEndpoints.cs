using System.ComponentModel.DataAnnotations;
using System.Security.Claims;
using BusinessSaas.Api.Data;
using Microsoft.AspNetCore.Identity;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;

namespace BusinessSaas.Api.Features;

public static class BusinessStaffEndpoints
{
    public static void MapBusinessStaffEndpoints(this WebApplication app)
    {
        var group = app.MapGroup("/api/businesses/{businessId:long}/staff")
            .RequireAuthorization();

        group.MapGet("", async (long businessId, ClaimsPrincipal user, AppDbContext db) =>
        {
            if (!await IsOwner(user, db, businessId))
                return Results.NotFound();

            var staff = await (from member in db.BusinessUsers.AsNoTracking()
                               join account in db.Users.AsNoTracking()
                                   on member.UserId equals account.Id
                               where member.BusinessId == businessId
                               orderby member.RoleCode, account.Email
                               select new { userId = account.Id, account.Email, member.RoleCode })
                .ToListAsync();

            return Results.Ok(staff);
        });

        group.MapPost("", async (long businessId, AddStaffRequest request,
            ClaimsPrincipal user, AppDbContext db, UserManager<AppUser> users) =>
        {
            if (!await IsOwner(user, db, businessId))
                return Results.NotFound();

            var role = request.RoleCode?.Trim() ?? "";
            var email = request.Email?.Trim() ?? "";
            if (role is not ("Manager" or "Cashier"))
                return Results.BadRequest(new { message = "Choose Manager or Cashier." });
            if (email.Length > 256 || !new EmailAddressAttribute().IsValid(email))
                return Results.BadRequest(new { message = "Enter a valid account email." });

            var account = await users.FindByEmailAsync(email);
            if (account is null)
                return Results.BadRequest(new
                {
                    message = "This account does not exist. Ask the user to register first."
                });

            if (await db.BusinessUsers.AnyAsync(x =>
                x.BusinessId == businessId && x.UserId == account.Id))
                return Results.Conflict(new { message = "This user already has business access." });

            db.BusinessUsers.Add(new BusinessUser
            {
                BusinessId = businessId, UserId = account.Id, RoleCode = role
            });
            try
            {
                await db.SaveChangesAsync();
            }
            catch (DbUpdateException error) when (
                error.InnerException is SqlException { Number: 2601 or 2627 })
            {
                return Results.Conflict(new { message = "This user already has business access." });
            }

            return Results.Ok(new { userId = account.Id, email = account.Email, roleCode = role });
        });

        group.MapDelete("{userId:long}", async (long businessId, long userId,
            ClaimsPrincipal user, AppDbContext db) =>
        {
            if (!await IsOwner(user, db, businessId))
                return Results.NotFound();

            var staff = await db.BusinessUsers.SingleOrDefaultAsync(x =>
                x.BusinessId == businessId && x.UserId == userId && x.RoleCode != "Owner");
            if (staff is null)
                return Results.NotFound();

            db.BusinessUsers.Remove(staff);
            await db.SaveChangesAsync();
            return Results.NoContent();
        });
    }

    private static Task<bool> IsOwner(ClaimsPrincipal user, AppDbContext db, long businessId)
    {
        if (!long.TryParse(user.FindFirstValue(ClaimTypes.NameIdentifier), out var userId))
            return Task.FromResult(false);

        return db.BusinessUsers.AsNoTracking().AnyAsync(x =>
            x.BusinessId == businessId && x.UserId == userId && x.RoleCode == "Owner");
    }

    private sealed record AddStaffRequest(string? Email, string? RoleCode);
}
