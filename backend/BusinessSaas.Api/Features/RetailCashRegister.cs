using System.Data;
using System.Security.Claims;
using BusinessSaas.Api.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace BusinessSaas.Api.Features;

public static class RetailCashRegister
{
    public sealed record OpenRequest(long LocationId, decimal? OpeningCash, string? RequestKey);
    public sealed record CloseRequest(long LocationId, decimal? ExpectedCash, decimal? ActualCash, string? Reason, string? RequestKey);
    public sealed record MovementRequest(long LocationId, bool? IsCashIn, decimal? Amount, string? Reason, string? RequestKey);

    public static void MapRetailCashRegister(this WebApplication app)
    {
        var group = app.MapGroup("/api/businesses/{businessId:long}/cash-register").RequireAuthorization();
        group.MapGet("", List);
        group.MapGet("/current", Current);
        group.MapPost("/{sessionId:long}/close", Close);
        group.MapPost("/open", Open);
        group.MapGet("/{sessionId:long}/summary", Summary);
        group.MapPost("/{sessionId:long}/movements", RecordMovement);
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

    private static async Task<bool> HasActiveSubscription(AppDbContext db, long businessId,
        CancellationToken cancellationToken)
    {
        var subscription = await db.BusinessSubscriptions.AsNoTracking()
            .SingleOrDefaultAsync(x => x.BusinessId == businessId, cancellationToken);
        return subscription is not null && SubscriptionAccess.CanOperate(subscription, DateTime.UtcNow);
    }

    private static async Task<bool> CanOpen(AppDbContext db, long businessId, IConfiguration config,
        CancellationToken cancellationToken) =>
        config.GetValue<bool>("RetailCashRegister:OpeningEnabled") &&
        await HasActiveSubscription(db, businessId, cancellationToken);

    private static bool ClosingEnabled(IConfiguration config) =>
        config.GetValue<bool>("RetailCashRegister:ClosingEnabled") &&
        config.GetValue<bool>("RetailCashRegister:LinkingEnabled");

    private static async Task<bool> CanClose(AppDbContext db, long businessId, IConfiguration config,
        IWebHostEnvironment environment, CancellationToken cancellationToken) =>
        ClosingEnabled(config) &&
        await HasActiveSubscription(db, businessId, cancellationToken) &&
        await PlatformModuleReleaseEndpoints.Available(db, environment, "POS") &&
        await PlatformStatusEndpoints.ActiveMaintenance(db) is null;

    private static object Detail(RetailCashRegisterSession session) => new
    {
        session.Id, session.LocationId, session.ActorUserId, session.CurrencyCode,
        session.RequestKey, session.OpeningCash, session.ClosingExpectedCash, session.ClosingActualCash,
        session.ClosedByUserId, session.ClosingReason, session.ClosingRequestKey,
        Variance = session.ClosingActualCash - session.ClosingExpectedCash,
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

    private static object MovementDetail(RetailCashRegisterMovement movement) => new
    {
        movement.Id, movement.RegisterSessionId, movement.ActorUserId, movement.IsCashIn, movement.Amount,
        movement.Reason, movement.RequestKey,
        PostedAtUtc = DateTime.SpecifyKind(movement.PostedAtUtc, DateTimeKind.Utc)
    };

    private static async Task<IResult> RecordMovement(long businessId, long sessionId, MovementRequest request,
        ClaimsPrincipal user, AppDbContext db, IConfiguration config, CancellationToken cancellationToken)
    {
        if (!long.TryParse(user.FindFirstValue(ClaimTypes.NameIdentifier), out var actor))
            return Results.NotFound();

        try
        {
            await using var tx = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken);
            if (await RetailCashSales.Shop(db, businessId, actor, request.LocationId, ownerOnly: true,
                cancellationToken: cancellationToken) is null) return Results.NotFound();
            var session = await db.RetailCashRegisterSessions.AsNoTracking().SingleOrDefaultAsync(x =>
                x.Id == sessionId && x.BusinessId == businessId && x.LocationId == request.LocationId, cancellationToken);
            if (session is null) return Results.NotFound();

            var reason = request.Reason?.Trim() ?? "";
            if (request.IsCashIn is not { } isCashIn || request.Amount is not { } amount ||
                amount <= 0m || amount > 9999999999999999.99m || decimal.Round(amount, 2) != amount ||
                reason.Length is < 5 or > 200 || string.IsNullOrWhiteSpace(request.RequestKey) ||
                request.RequestKey.Length is < 12 or > 80)
                return Results.BadRequest(new { message = "Enter a valid cash movement, reason and request key." });

            var prior = await db.RetailCashRegisterMovements.AsNoTracking().SingleOrDefaultAsync(x =>
                x.RegisterSessionId == sessionId && x.RequestKey == request.RequestKey, cancellationToken);
            if (prior is not null)
            {
                if (prior.ActorUserId != actor || prior.IsCashIn != isCashIn || prior.Amount != amount || prior.Reason != reason)
                    return Results.Conflict(new { message = "Request key belongs to a different cash movement." });
                await tx.CommitAsync(cancellationToken);
                return Results.Ok(MovementDetail(prior));
            }

            if (!config.GetValue<bool>("RetailCashRegister:MovementsEnabled") ||
                !config.GetValue<bool>("RetailCashRegister:LinkingEnabled"))
                return Results.Conflict(new { message = "Cash register movements are not released on this host." });
            if (session.ClosedAtUtc is not null || session.OpenedAtUtc > DateTime.UtcNow)
                return Results.Conflict(new { message = "Cash movements require an open register." });
            var subscription = await db.BusinessSubscriptions.AsNoTracking()
                .SingleOrDefaultAsync(x => x.BusinessId == businessId, cancellationToken);
            if (subscription is null || !SubscriptionAccess.CanOperate(subscription, DateTime.UtcNow))
                return Results.Conflict(new { message = "Business is not enabled for financial posting." });

            var movement = new RetailCashRegisterMovement
            {
                RegisterSessionId = sessionId, ActorUserId = actor, RequestKey = request.RequestKey,
                IsCashIn = isCashIn, Amount = amount, Reason = reason, PostedAtUtc = DateTime.UtcNow
            };
            db.RetailCashRegisterMovements.Add(movement);
            await db.SaveChangesAsync(cancellationToken);
            await tx.CommitAsync(cancellationToken);
            return Results.Ok(MovementDetail(movement));
        }
        catch (Exception error) when (StockPosting.IsSqlConflict(error))
        {
            return Results.Conflict(new { message = "Register is being updated. Retry the same request key." });
        }
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

            var (totals, error) = await Calculate(db, session, cancellationToken);
            if (error is not null) return error;
            if (session.ClosingExpectedCash is { } snapshot && snapshot != totals!.ExpectedCash)
                return Results.Conflict(new { code = "REGISTER_RECONCILIATION_REQUIRED", message = "Closed register totals no longer match its closing snapshot." });
            await tx.CommitAsync(cancellationToken);
            return Results.Ok(totals);
        }
        catch (Exception error) when (StockPosting.IsSqlConflict(error))
        {
            return Results.Conflict(new { message = "Register transactions are changing. Reload the summary." });
        }
    }

    private sealed record RegisterTotals(long SessionId, string CurrencyCode, decimal OpeningCash,
        decimal CashSales, decimal CashRefunds, decimal NetExchangeSettlement, decimal CreditCollections,
        decimal CashIn, decimal CashOut, decimal ExpectedCash);

    private static async Task<(RegisterTotals? Totals, IResult? Error)> Calculate(AppDbContext db,
        RetailCashRegisterSession session, CancellationToken cancellationToken)
    {
        var businessId = session.BusinessId;
        var locationId = session.LocationId;
        var cutoff = session.ClosedAtUtc ?? DateTime.UtcNow;
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
                x.PostedAtUtc > cutoff ||
                returns.Any(r => r.ReplacementSaleId == x.Id &&
                    (r.BusinessId != businessId || r.RegisterSessionId != session.Id)), cancellationToken) ||
            await linkedReturns.AnyAsync(r => r.BusinessId != businessId ||
                r.ActorUserId != session.ActorUserId || r.PostedAtUtc < session.OpenedAtUtc ||
                r.PostedAtUtc > cutoff ||
                !cashDocuments.Any(s => s.Id == r.SaleId) ||
                (r.ReplacementSaleId == null && r.NetCashSettlement != null) ||
                (r.ReplacementSaleId != null && (r.NetCashSettlement == null ||
                    !linkedSales.Any(s => s.Id == r.ReplacementSaleId &&
                        s.Total - r.Total == r.NetCashSettlement))), cancellationToken) ||
            await payments.AnyAsync(x => x.BusinessId != businessId || x.ActorUserId != session.ActorUserId ||
                x.PostedAtUtc < session.OpenedAtUtc ||
                x.PostedAtUtc > cutoff ||
                !creditDocuments.Any(s => s.Id == x.SaleId), cancellationToken))
            return (null, Results.Conflict(new { code = "REGISTER_RECONCILIATION_REQUIRED",
                message = "Register posting links require reconciliation." }));

        // A replacement invoice is not another cash sale. Its return records the net exchange settlement.
        var cashSales = await linkedSales.Where(s => !returns.Any(r => r.ReplacementSaleId == s.Id))
            .SumAsync(s => (decimal?)s.Total, cancellationToken) ?? 0m;
        var refunds = await linkedReturns.Where(r => r.ReplacementSaleId == null)
            .SumAsync(r => (decimal?)r.Total, cancellationToken) ?? 0m;
        var exchangeSettlement = await linkedReturns.Where(r => r.ReplacementSaleId != null)
            .SumAsync(r => r.NetCashSettlement, cancellationToken) ?? 0m;
        var creditCollections = await payments.SumAsync(x => (decimal?)x.Amount, cancellationToken) ?? 0m;
        var movements = db.RetailCashRegisterMovements.AsNoTracking().Where(x => x.RegisterSessionId == session.Id);
        if (await movements.AnyAsync(x => x.PostedAtUtc < session.OpenedAtUtc ||
            x.PostedAtUtc > cutoff, cancellationToken))
            return (null, Results.Conflict(new { code = "REGISTER_RECONCILIATION_REQUIRED",
                message = "Register cash movements require reconciliation." }));
        var cashIn = await movements.Where(x => x.IsCashIn).SumAsync(x => (decimal?)x.Amount, cancellationToken) ?? 0m;
        var cashOut = await movements.Where(x => !x.IsCashIn).SumAsync(x => (decimal?)x.Amount, cancellationToken) ?? 0m;
        var expectedCash = session.OpeningCash + cashSales - refunds + exchangeSettlement + creditCollections + cashIn - cashOut;
        return (new RegisterTotals(session.Id, session.CurrencyCode, session.OpeningCash,
            cashSales, refunds, exchangeSettlement, creditCollections, cashIn, cashOut, expectedCash), null);
    }

    private static async Task<IResult> List(long businessId, long locationId, ClaimsPrincipal user,
        AppDbContext db, IConfiguration config, IWebHostEnvironment environment, CancellationToken cancellationToken,
        int page = 1, bool openOnly = true)
    {
        if (!long.TryParse(user.FindFirstValue(ClaimTypes.NameIdentifier), out var actor) ||
            await RetailCashSales.Shop(db, businessId, actor, locationId, ownerOnly: true,
                cancellationToken: cancellationToken) is null) return Results.NotFound();
        if (page is < 1 or > 100000) return Results.BadRequest();
        var query = db.RetailCashRegisterSessions.AsNoTracking()
            .Where(x => x.BusinessId == businessId && x.LocationId == locationId);
        if (openOnly) query = query.Where(x => x.ClosedAtUtc == null);
        var items = await query.OrderByDescending(x => x.Id).Skip((page - 1) * 20).Take(21).ToListAsync(cancellationToken);
        return Results.Ok(new { items = items.Take(20).Select(Detail), hasMore = items.Count > 20,
            canClose = await CanClose(db, businessId, config, environment, cancellationToken) });
    }

    private static async Task<IResult> Close(long businessId, long sessionId, CloseRequest request,
        ClaimsPrincipal user, AppDbContext db, IConfiguration config, IWebHostEnvironment environment,
        CancellationToken cancellationToken)
    {
        if (!long.TryParse(user.FindFirstValue(ClaimTypes.NameIdentifier), out var actor))
            return Results.NotFound();
        var reason = request.Reason?.Trim() ?? "";
        var key = request.RequestKey?.Trim() ?? "";
        const decimal max = 9999999999999999.99m;
        if (request.ActualCash is not { } actual || actual < 0 || actual > max || decimal.Round(actual, 2) != actual ||
            request.ExpectedCash is not { } expected || expected < -max || expected > max || decimal.Round(expected, 2) != expected ||
            key.Length is < 12 or > 80 || reason.Length > 200 || (actual != expected && reason.Length < 5))
            return Results.BadRequest(new { message = "Enter actual cash and a variance reason when cash differs." });
        try
        {
            await using var tx = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken);
            if (await RetailCashSales.Shop(db, businessId, actor, request.LocationId, ownerOnly: true,
                cancellationToken: cancellationToken) is null) return Results.NotFound();
            var session = await db.RetailCashRegisterSessions.SingleOrDefaultAsync(x =>
                x.Id == sessionId && x.BusinessId == businessId && x.LocationId == request.LocationId, cancellationToken);
            if (session is null) return Results.NotFound();
            if (session.ClosedAtUtc != null)
                return session.ClosingRequestKey == key && session.ClosedByUserId == actor &&
                    session.ClosingActualCash == actual && session.ClosingExpectedCash == expected && session.ClosingReason == reason
                    ? Results.Ok(Detail(session)) : Results.Conflict(new { message = "Register is already closed. Review its recorded closing." });
            // Recheck mutable access rules inside the posting transaction, not only in middleware.
            if (!await HasActiveSubscription(db, businessId, cancellationToken))
                return Results.Json(new { message = "Subscription access is unavailable, expired or suspended." }, statusCode: 403);
            if (!ClosingEnabled(config) || !await PlatformModuleReleaseEndpoints.Available(db, environment, "POS"))
                return Results.Conflict(new { code = "MODULE_NOT_AVAILABLE", message = "Register closing is not released on this host." });
            if (await PlatformStatusEndpoints.ActiveMaintenance(db) is not null)
                return Results.Json(new { code = "PLATFORM_MAINTENANCE", message = "Register closing is unavailable during maintenance." }, statusCode: 503);
            if (session.OpenedAtUtc > DateTime.UtcNow)
                return Results.Conflict(new { message = "Register opening time requires reconciliation." });
            var (totals, error) = await Calculate(db, session, cancellationToken);
            if (error is not null) return error;
            if (totals!.ExpectedCash != expected)
                return Results.Conflict(new { code = "REGISTER_BALANCE_CHANGED", message = "Register cash changed. Recount and refresh before closing." });
            session.ClosingExpectedCash = expected;
            session.ClosingActualCash = actual;
            session.ClosingRequestKey = key;
            session.ClosingReason = reason;
            session.ClosedByUserId = actor;
            session.ClosedAtUtc = DateTime.UtcNow;
            await db.SaveChangesAsync(cancellationToken);
            await tx.CommitAsync(cancellationToken);
            return Results.Ok(Detail(session));
        }
        catch (Exception error) when (StockPosting.IsSqlConflict(error))
        {
            return Results.Conflict(new { message = "Register is being updated. Check recorded closing or retry the same request." });
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
