using System.Data;
using System.Security.Claims;
using BusinessSaas.Api.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace BusinessSaas.Api.Features;

public static class RetailCashRegister
{
    public sealed record OpenRequest(long LocationId, decimal? OpeningCash, string? RequestKey);

    public static void MapRetailCashRegister(this WebApplication app)
    {
        var group = app.MapGroup("/api/businesses/{businessId:long}/cash-register").RequireAuthorization();
        group.MapGet("/current", Current);
        group.MapPost("/open", Open);
        group.MapGet("/{sessionId:long}/summary", Summary);
    }

    private static IQueryable<RetailCashRegisterSession> Active(AppDbContext db, long businessId, long locationId, long actor) =>
        db.RetailCashRegisterSessions.AsNoTracking().Where(x => x.BusinessId == businessId &&
            x.LocationId == locationId && x.ActorUserId == actor && x.ClosedAtUtc == null);

    // Resolve only for a new financial write inside its existing serializable transaction.
    // Recovery/replay returns the stored link, not a session selected at retry time.
    internal static async Task<(long? Id, IResult? Error)> PostingSession(AppDbContext db, IConfiguration config,
        long businessId, long locationId, long actor, string currencyCode)
    {
        if (!config.GetValue<bool>("RetailCashRegister:LinkingEnabled")) return (null, null);
        if (db.Database.CurrentTransaction is not { } transaction ||
            transaction.GetDbTransaction().IsolationLevel != IsolationLevel.Serializable)
            throw new InvalidOperationException("Register linking requires a serializable posting transaction.");
        var sessionId = await Active(db, businessId, locationId, actor)
            .Where(x => x.CurrencyCode == currencyCode && x.OpenedAtUtc <= DateTime.UtcNow)
            .Select(x => (long?)x.Id).SingleOrDefaultAsync();
        if (sessionId is null)
            return (null, Results.Conflict(new { code = "REGISTER_REQUIRED",
                message = "Open your cash register for this location and currency before recording cash." }));
        return (sessionId, null);
    }

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

    // Owner-only preview of linked posted cash. Closing must recalculate within its own transaction.
    private static async Task<IResult> Summary(long businessId, long sessionId, long locationId,
        ClaimsPrincipal user, AppDbContext db, CancellationToken cancellationToken)
    {
        if (!long.TryParse(user.FindFirstValue(ClaimTypes.NameIdentifier), out var actor))
            return Results.NotFound();

        try
        {
            await using var tx = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken);
            if (await RetailCashSales.Shop(db, businessId, actor, locationId, ownerOnly: true,
                cancellationToken: cancellationToken) is null) return Results.NotFound();

            var session = await db.RetailCashRegisterSessions.AsNoTracking().SingleOrDefaultAsync(x =>
                x.Id == sessionId && x.BusinessId == businessId && x.LocationId == locationId, cancellationToken);
            if (session is null) return Results.NotFound();

            var sales = db.RetailCashSales.AsNoTracking();
            var returns = db.RetailCashReturns.AsNoTracking();
            var linkedSales = sales.Where(x => x.RegisterSessionId == session.Id);
            var linkedReturns = returns.Where(x => x.RegisterSessionId == session.Id);
            var payments = db.RetailCreditPayments.AsNoTracking().Where(x => x.RegisterSessionId == session.Id);
            var cashDocuments = sales.Where(x => x.BusinessId == businessId && x.LocationId == locationId &&
                x.CurrencyCode == session.CurrencyCode && x.CreditDueDate == null);
            var creditDocuments = sales.Where(x => x.BusinessId == businessId && x.LocationId == locationId &&
                x.CurrencyCode == session.CurrencyCode && x.CreditDueDate != null);

            // Reject inconsistent links rather than silently excluding financial records.
            if (await linkedSales.AnyAsync(x => x.BusinessId != businessId || x.LocationId != locationId ||
                    x.CurrencyCode != session.CurrencyCode || x.CashierId != session.ActorUserId ||
                    x.CreditDueDate != null || x.PostedAtUtc < session.OpenedAtUtc ||
                    (session.ClosedAtUtc != null && x.PostedAtUtc > session.ClosedAtUtc) ||
                    returns.Any(r => r.ReplacementSaleId == x.Id &&
                        (r.BusinessId != businessId || r.RegisterSessionId != session.Id)), cancellationToken) ||
                await linkedReturns.AnyAsync(r => r.BusinessId != businessId ||
                    r.ActorUserId != session.ActorUserId || r.PostedAtUtc < session.OpenedAtUtc ||
                    (session.ClosedAtUtc != null && r.PostedAtUtc > session.ClosedAtUtc) ||
                    !cashDocuments.Any(s => s.Id == r.SaleId) ||
                    (r.ReplacementSaleId == null && r.NetCashSettlement != null) ||
                    (r.ReplacementSaleId != null && (r.NetCashSettlement == null ||
                        !linkedSales.Any(s => s.Id == r.ReplacementSaleId &&
                            s.Total - r.Total == r.NetCashSettlement))), cancellationToken) ||
                await payments.AnyAsync(x => x.BusinessId != businessId || x.ActorUserId != session.ActorUserId ||
                    x.PostedAtUtc < session.OpenedAtUtc ||
                    (session.ClosedAtUtc != null && x.PostedAtUtc > session.ClosedAtUtc) ||
                    !creditDocuments.Any(s => s.Id == x.SaleId), cancellationToken))
                return Results.Conflict(new { code = "REGISTER_RECONCILIATION_REQUIRED",
                    message = "Register posting links require reconciliation." });

            // A replacement invoice is not another cash sale. Its return records the net exchange settlement.
            var cashSales = await linkedSales.Where(s => !returns.Any(r => r.ReplacementSaleId == s.Id))
                .SumAsync(s => (decimal?)s.Total, cancellationToken) ?? 0m;
            var refunds = await linkedReturns.Where(r => r.ReplacementSaleId == null)
                .SumAsync(r => (decimal?)r.Total, cancellationToken) ?? 0m;
            var exchangeSettlement = await linkedReturns.Where(r => r.ReplacementSaleId != null)
                .SumAsync(r => r.NetCashSettlement, cancellationToken) ?? 0m;
            var creditCollections = await payments.SumAsync(x => (decimal?)x.Amount, cancellationToken) ?? 0m;
            var expectedCash = session.OpeningCash + cashSales - refunds + exchangeSettlement + creditCollections;
            await tx.CommitAsync(cancellationToken);

            return Results.Ok(new { SessionId = session.Id, session.CurrencyCode, session.OpeningCash,
                CashSales = cashSales, CashRefunds = refunds, NetExchangeSettlement = exchangeSettlement,
                CreditCollections = creditCollections, ExpectedCash = expectedCash });
        }
        catch (Exception error) when (StockPosting.IsSqlConflict(error))
        {
            return Results.Conflict(new { message = "Register transactions are changing. Reload the summary." });
        }
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
