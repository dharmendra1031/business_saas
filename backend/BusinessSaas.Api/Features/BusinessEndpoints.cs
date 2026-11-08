using System.Security.Claims;
using BusinessSaas.Api.Data;
using Microsoft.EntityFrameworkCore;

namespace BusinessSaas.Api.Features;

public static class BusinessEndpoints
{
    public static void MapBusinessEndpoints(this WebApplication app)
    {
        var group = app.MapGroup("/api").RequireAuthorization();

        group.MapGet("/business-types", async (AppDbContext db) =>
            await db.BusinessTypes.AsNoTracking()
                .Where(x => x.IsSupported)
                .OrderBy(x => x.Name)
                .Select(x => new { x.Id, x.Code, x.Name })
                .ToListAsync());

        group.MapGet("/businesses", async (ClaimsPrincipal principal, AppDbContext db) =>
        {
            var userId = CurrentUserId(principal);
            return await (from member in db.BusinessUsers.AsNoTracking()
                          join business in db.Businesses.AsNoTracking()
                            on member.BusinessId equals business.Id
                          join type in db.BusinessTypes.AsNoTracking()
                            on business.BusinessTypeId equals type.Id
                          where member.UserId == userId
                          orderby business.Name
                          select new
                          {
                              business.Id,
                              business.Name,
                              business.CountryCode,
                              business.CurrencyCode,
                              business.TimeZoneId,
                              business.BusinessTypeId,
                              TypeName = type.Name,
                              TypeCode = type.Code,
                              member.RoleCode
                          }).ToListAsync();
        });

        group.MapPost("/businesses", async (CreateBusinessRequest request,
            ClaimsPrincipal principal, AppDbContext db) =>
        {
            var userId = CurrentUserId(principal);
            var name = request.Name?.Trim() ?? "";
            var country = request.CountryCode?.Trim().ToUpperInvariant() ?? "";
            var currency = request.CurrencyCode?.Trim().ToUpperInvariant() ?? "";
            var timeZone = request.TimeZoneId?.Trim() ?? "";
            var requestKey = request.RequestKey?.Trim() ?? "";

            if (name.Length is < 2 or > 150 || country.Length != 2 ||
                currency.Length != 3 || timeZone.Length is < 3 or > 80 ||
                requestKey.Length is < 12 or > 80)
                return Results.BadRequest(new { message = "Check business name, country, currency and timezone." });

            var existing = await db.Businesses.AsNoTracking()
                .FirstOrDefaultAsync(x => x.CreatedByUserId == userId && x.CreationKey == requestKey);
            if (existing is not null)
                return Results.Ok(new { existing.Id });

            var supported = await db.BusinessTypes.AnyAsync(x =>
                x.Id == request.BusinessTypeId && x.IsSupported);
            if (!supported)
                return Results.BadRequest(new { message = "This business type is not available yet." });

            await using var transaction = await db.Database.BeginTransactionAsync();

            var business = new Business
            {
                Name = name,
                BusinessTypeId = request.BusinessTypeId,
                CreatedByUserId = userId,
                CreationKey = requestKey,
                CountryCode = country,
                CurrencyCode = currency,
                TimeZoneId = timeZone
            };

            db.Businesses.Add(business);
            await db.SaveChangesAsync();

            db.BusinessUsers.Add(new BusinessUser
            {
                BusinessId = business.Id,
                UserId = userId,
                RoleCode = "Owner"
            });
            db.BusinessLocations.Add(new BusinessLocation
            {
                BusinessId = business.Id,
                Code = "MAIN",
                Name = "Main Shop"
            });

            await db.SaveChangesAsync();
            await transaction.CommitAsync();

            return Results.Created($"/api/businesses/{business.Id}/dashboard", new { business.Id });
        });

        group.MapGet("/businesses/{businessId:long}/dashboard", async (
            long businessId, ClaimsPrincipal principal, AppDbContext db) =>
        {
            var userId = CurrentUserId(principal);
            var isMember = await db.BusinessUsers.AsNoTracking().AnyAsync(
                x => x.BusinessId == businessId && x.UserId == userId);
            if (!isMember)
                return Results.NotFound();

            var details = await (from business in db.Businesses.AsNoTracking()
                                 join type in db.BusinessTypes.AsNoTracking()
                                     on business.BusinessTypeId equals type.Id
                                 where business.Id == businessId
                                 select new
                                 {
                                     business.Id,
                                     business.Name,
                                     business.CurrencyCode,
                                     business.CountryCode,
                                     business.TimeZoneId,
                                     TypeCode = type.Code,
                                     TypeName = type.Name
                                 }).SingleAsync();

            var locations = await db.BusinessLocations.AsNoTracking()
                .Where(x => x.BusinessId == businessId)
                .OrderBy(x => x.Id)
                .Select(x => new { x.Id, x.Name, x.Code })
                .ToListAsync();

            return Results.Ok(new { business = details, locations });
        });
    }

    private static long CurrentUserId(ClaimsPrincipal principal) =>
        long.Parse(principal.FindFirstValue(ClaimTypes.NameIdentifier)!);

    private sealed record CreateBusinessRequest(
        string? Name, long BusinessTypeId, string? CountryCode,
        string? CurrencyCode, string? TimeZoneId, string? RequestKey);
}
