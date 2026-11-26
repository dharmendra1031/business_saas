using System.Data;
using System.Security.Claims;
using BusinessSaas.Api.Data;
using Microsoft.EntityFrameworkCore;

namespace BusinessSaas.Api.Features;

public static class RetailCashRegister
{
    public sealed record OpenRequest(long LocationId, decimal? OpeningCash, string? RequestKey);

    public static void MapRetailCashRegister(this WebApplication app)
    {
        var group = app.MapGroup("/api/businesses/{businessId:long}/cash-register").RequireAuthorization();
        group.MapGet("/current", Current);
        group.MapPost("/open", Open);
    }

    private static IQueryable<RetailCashRegisterSession> Active(AppDbContext db, long businessId, long locationId, long actor) =>
        db.RetailCashRegisterSessions.AsNoTracking().Where(x => x.BusinessId == businessId &&
            x.LocationId == locationId && x.ActorUserId == actor && x.ClosedAtUtc == null);

    private static async Task<bool> CanOpen(AppDbContext db, long businessId, IConfiguration config,
        CancellationToken cancellationToken)
    {
        if (!config.GetValue<bool>("RetailCashRegister:OpeningEnabled")) return false;
        var subscription = await db.BusinessSubscriptions.AsNoTracking()
            .SingleOrDefaultAsync(x => x.BusinessId == businessId, cancellationToken);
        return subscription is not null && SubscriptionAccess.CanOperate(subscription, DateTime.UtcNow);
    }

    private static object Detail(RetailCashRegisterSession session) => new
    {
        session.Id, session.LocationId, session.ActorUserId, session.CurrencyCode,
        session.RequestKey, session.OpeningCash,
        OpenedAtUtc = DateTime.SpecifyKind(session.OpenedAtUtc, DateTimeKind.Utc),
        ClosedAtUtc = session.ClosedAtUtc.HasValue
            ? DateTime.SpecifyKind(session.ClosedAtUtc.Value, DateTimeKind.Utc) : (DateTime?)null
    };

    private static async Task<IResult> Current(long businessId, long locationId, ClaimsPrincipal user,
        AppDbContext db, IConfiguration config, CancellationToken cancellationToken)
    {
        if (!long.TryParse(user.FindFirstValue(ClaimTypes.NameIdentifier), out var actor) ||
            await RetailCashSales.Shop(db, businessId, actor, locationId,
                cancellationToken: cancellationToken, allowManager: false) is not { } business)
            return Results.NotFound();
        var session = await Active(db, businessId, locationId, actor).SingleOrDefaultAsync(cancellationToken);
        return Results.Ok(new { business.CurrencyCode, CanOpen = session is null &&
            await CanOpen(db, businessId, config, cancellationToken), Session = session is null ? null : Detail(session) });
    }

    private static async Task<IResult> Open(long businessId, OpenRequest request, ClaimsPrincipal user,
        AppDbContext db, IConfiguration config, CancellationToken cancellationToken)
    {
        if (!long.TryParse(user.FindFirstValue(ClaimTypes.NameIdentifier), out var actor))
            return Results.NotFound();

        try
        {
            await using var tx = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken);
            var business = await RetailCashSales.Shop(db, businessId, actor, request.LocationId,
                cancellationToken: cancellationToken, allowManager: false);
            if (business is null) return Results.NotFound();
            if (request.OpeningCash is not { } opening || opening < 0m || opening > 9999999999999999.99m ||
                decimal.Round(opening, 2) != opening || string.IsNullOrWhiteSpace(request.RequestKey) ||
                request.RequestKey.Length is < 12 or > 80)
                return Results.BadRequest(new { message = "Enter a valid opening cash amount and request key." });

            var prior = await db.RetailCashRegisterSessions.AsNoTracking().SingleOrDefaultAsync(x =>
                x.BusinessId == businessId && x.RequestKey == request.RequestKey, cancellationToken);
            if (prior is not null)
            {
                if (prior.ActorUserId != actor || prior.LocationId != request.LocationId || prior.OpeningCash != opening)
                    return Results.Conflict(new { message = "Request key belongs to a different register opening." });
                await tx.CommitAsync(cancellationToken);
                return Results.Ok(Detail(prior));
            }
            if (!await CanOpen(db, businessId, config, cancellationToken))
                return Results.Conflict(new { message = "Register opening is not enabled on this development host." });
            if (await Active(db, businessId, request.LocationId, actor).AnyAsync(cancellationToken))
                return Results.Conflict(new { message = "You already have an open register at this location." });

            var session = new RetailCashRegisterSession
            {
                BusinessId = businessId, LocationId = request.LocationId, ActorUserId = actor,
                CurrencyCode = business.CurrencyCode, RequestKey = request.RequestKey,
                OpeningCash = opening, OpenedAtUtc = DateTime.UtcNow
            };
            db.RetailCashRegisterSessions.Add(session);
            await db.SaveChangesAsync(cancellationToken);
            await tx.CommitAsync(cancellationToken);
            return Results.Ok(Detail(session));
        }
        catch (Exception error) when (StockPosting.IsSqlConflict(error))
        {
            return Results.Conflict(new { message = "Register opening is being updated. Check the current register or retry the same request key." });
        }
    }
}
