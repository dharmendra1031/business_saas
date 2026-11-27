using System.Data;
using System.Security.Claims;
using BusinessSaas.Api.Data;
using Microsoft.EntityFrameworkCore;

namespace BusinessSaas.Api.Features;

public static class CreditCollections
{
    internal static IQueryable<RetailCashSale> Invoices(AppDbContext db, long businessId, long customerId) =>
        db.RetailCashSales.AsNoTracking().Where(x => x.BusinessId == businessId &&
            x.CustomerId == customerId && x.CreditDueDate != null &&
            db.RetailCustomers.Any(customer => customer.Id == customerId && customer.BusinessId == businessId));

    internal static IQueryable<RetailCreditPayment> Payments(AppDbContext db, long businessId, long saleId) =>
        db.RetailCreditPayments.AsNoTracking().Where(x => x.BusinessId == businessId && x.SaleId == saleId);

    internal static IQueryable<RetailCreditPayment> CustomerPayments(AppDbContext db, long businessId, long customerId) =>
        from payment in db.RetailCreditPayments.AsNoTracking()
        join sale in Invoices(db, businessId, customerId) on payment.SaleId equals sale.Id
        where payment.BusinessId == businessId
        select payment;

    internal static Task<decimal?> PaidAsync(AppDbContext db, long businessId, long saleId, CancellationToken cancellationToken = default) =>
        Payments(db, businessId, saleId).SumAsync(x => (decimal?)x.Amount, cancellationToken);

    private sealed record PaymentRequest(string RequestKey, decimal Amount, decimal ExpectedOutstanding,
        bool CashReceivedConfirmed);

    internal static void MapCreditCollections(RouteGroupBuilder customers)
    {
        customers.MapGet("/outstanding-report", OutstandingReport);
        customers.MapGet("/{customerId:long}/credit-invoices/{saleId:long}/payments", async (
            long businessId, long customerId, long saleId, ClaimsPrincipal user, AppDbContext db,
            CancellationToken cancellationToken, int page = 1, string? requestKey = null) =>
        {
            if (await RetailBillingSettingsEndpoints.OwnerId(db, businessId, user, cancellationToken) is null)
                return Results.NotFound();
            if (page is < 1 or > 100000 || requestKey is { Length: > 80 })
                return Results.BadRequest(new { message = "Invalid payment history filter." });
            if (!await Invoices(db, businessId, customerId).AnyAsync(x => x.Id == saleId, cancellationToken))
                return Results.NotFound();
            var query = Payments(db, businessId, saleId);
            if (requestKey is not null) query = query.Where(x => x.RequestKey == requestKey);
            var records = await query.OrderByDescending(x => x.Id).Skip((page - 1) * 20).Take(21).ToListAsync(cancellationToken);
            return Results.Ok(new { items = records.Take(20).Select(View), hasMore = records.Count > 20 });
        });

        customers.MapPost("/{customerId:long}/credit-invoices/{saleId:long}/payments", async (
            long businessId, long customerId, long saleId, PaymentRequest request,
            ClaimsPrincipal user, AppDbContext db, IConfiguration config, IWebHostEnvironment environment) =>
        {
            var actor = await RetailBillingSettingsEndpoints.OwnerId(db, businessId, user);
            if (actor is null || !environment.IsDevelopment()) return Results.NotFound();
            if (string.IsNullOrWhiteSpace(request.RequestKey) || request.RequestKey.Length is < 12 or > 80 ||
                request.Amount <= 0 || request.Amount > 9999999999999999.99m ||
                decimal.Round(request.Amount, 2) != request.Amount ||
                request.ExpectedOutstanding < request.Amount || request.ExpectedOutstanding > 9999999999999999.99m ||
                decimal.Round(request.ExpectedOutstanding, 2) != request.ExpectedOutstanding ||
                !request.CashReceivedConfirmed)
                return Results.BadRequest(new { message = "Confirm actual cash received and enter a valid payment amount." });

            await using var tx = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable);
            try
            {
                var sale = await Invoices(db, businessId, customerId).SingleOrDefaultAsync(x => x.Id == saleId);
                if (sale is null) return Results.NotFound();
                var prior = await db.RetailCreditPayments.AsNoTracking().SingleOrDefaultAsync(x =>
                    x.BusinessId == businessId && x.RequestKey == request.RequestKey);
                if (prior is not null)
                    return prior.SaleId == saleId && prior.Amount == request.Amount
                        ? Results.Ok(View(prior)) : Results.Conflict(new { message = "Payment key belongs to another collection." });
                if (!config.GetValue<bool>("RetailCredit:CollectionsEnabled"))
                    return Results.Conflict(new { message = "Credit collection pilot is disabled." });
                var denied = await RetailMasterEndpoints.CheckAccess(db, user, businessId, true);
                if (denied is not null) return denied;

                var paid = await PaidAsync(db, businessId, saleId) ?? 0m;
                var outstanding = sale.Total - paid;
                if (outstanding != request.ExpectedOutstanding || request.Amount > outstanding)
                    return Results.Conflict(new { message = "Invoice outstanding changed. Reconcile the cash received before recording another payment." });
                var (registerId, registerError) = await RetailCashRegister.PostingSession(db, config,
                    businessId, sale.LocationId, actor.Value, sale.CurrencyCode);
                if (registerError is not null) return registerError;
                var payment = new RetailCreditPayment
                {
                    RegisterSessionId = registerId,
                    BusinessId = businessId, SaleId = saleId, ActorUserId = actor.Value,
                    RequestKey = request.RequestKey, Amount = request.Amount, PostedAtUtc = DateTime.UtcNow
                };
                db.RetailCreditPayments.Add(payment);
                await db.SaveChangesAsync();
                await tx.CommitAsync();
                return Results.Ok(View(payment));
            }
            catch (Exception error) when (StockPosting.IsSqlConflict(error))
            {
                return Results.Conflict(new { message = "Collection is being updated. Verify history or retry the same request key." });
            }
        });
    }

    private static async Task<IResult> OutstandingReport(long businessId, ClaimsPrincipal user,
        AppDbContext db, string? search, CancellationToken cancellationToken, int page = 1, bool overdueOnly = false)
    {
        if (await RetailBillingSettingsEndpoints.OwnerId(db, businessId, user, cancellationToken) is null)
            return Results.NotFound();
        var term = search?.Trim().ToUpperInvariant() ?? "";
        if (page is < 1 or > 100000 || term.Length > 100)
            return Results.BadRequest(new { message = "Invalid search or page." });

        var timeZone = await db.Businesses.Where(x => x.Id == businessId)
            .Select(x => x.TimeZoneId).SingleAsync(cancellationToken);
        var today = DateOnly.FromDateTime(TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow,
            TimeZoneInfo.FindSystemTimeZoneById(timeZone)));
        var cutoff30 = today.AddDays(-30);
        var cutoff60 = today.AddDays(-60);
        var cutoff90 = today.AddDays(-90);

        await using var tx = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken);
        try
        {
            var paidByInvoice = db.RetailCreditPayments.AsNoTracking()
                .Where(x => x.BusinessId == businessId).GroupBy(x => x.SaleId)
                .Select(g => new { SaleId = g.Key, Paid = (decimal?)g.Sum(x => x.Amount) });
            var customers = db.RetailCustomers.AsNoTracking().Where(x => x.BusinessId == businessId);
            if (term.Length > 0)
                customers = customers.Where(x => x.NameKey.Contains(term) || x.Phone.Contains(term));
            var balances = from sale in db.RetailCashSales.AsNoTracking()
                           join customer in customers on sale.CustomerId equals (long?)customer.Id
                           join payment in paidByInvoice on sale.Id equals payment.SaleId into payments
                           from payment in payments.DefaultIfEmpty()
                           where sale.BusinessId == businessId && sale.CreditDueDate != null
                           let outstanding = sale.Total - (payment.Paid ?? 0m)
                           where outstanding > 0 && (!overdueOnly || sale.CreditDueDate < today)
                           select new { CustomerId = customer.Id, customer.Name, customer.IsActive,
                               sale.CurrencyCode, DueDate = sale.CreditDueDate!.Value, Outstanding = outstanding };

            var totals = await balances.GroupBy(x => x.CurrencyCode).Select(g => new {
                CurrencyCode = g.Key,
                Outstanding = g.Sum(x => x.Outstanding),
                NotDue = g.Sum(x => x.DueDate >= today ? x.Outstanding : 0m),
                Overdue = g.Sum(x => x.DueDate < today ? x.Outstanding : 0m),
                Days1To30 = g.Sum(x => x.DueDate < today && x.DueDate >= cutoff30 ? x.Outstanding : 0m),
                Days31To60 = g.Sum(x => x.DueDate < cutoff30 && x.DueDate >= cutoff60 ? x.Outstanding : 0m),
                Days61To90 = g.Sum(x => x.DueDate < cutoff60 && x.DueDate >= cutoff90 ? x.Outstanding : 0m),
                DaysOver90 = g.Sum(x => x.DueDate < cutoff90 ? x.Outstanding : 0m),
                InvoiceCount = g.Count()
            }).OrderBy(x => x.CurrencyCode).ToListAsync(cancellationToken);

            var items = await balances.GroupBy(x => new { x.CustomerId, x.Name, x.IsActive, x.CurrencyCode })
                .Select(g => new {
                    g.Key.CustomerId, g.Key.Name, g.Key.IsActive, g.Key.CurrencyCode,
                    Outstanding = g.Sum(x => x.Outstanding),
                    Overdue = g.Sum(x => x.DueDate < today ? x.Outstanding : 0m),
                    OldestDueDate = g.Min(x => x.DueDate),
                    InvoiceCount = g.Count()
                }).OrderByDescending(x => x.Overdue).ThenByDescending(x => x.Outstanding)
                .ThenBy(x => x.CustomerId).ThenBy(x => x.CurrencyCode)
                .Skip((page - 1) * 20).Take(21).ToListAsync(cancellationToken);
            await tx.CommitAsync(cancellationToken);
            return Results.Ok(new { businessDate = today, timeZoneId = timeZone, totals,
                items = items.Take(20), page, hasMore = items.Count > 20 });
        }
        catch (Exception error) when (StockPosting.IsSqlConflict(error))
        {
            return Results.Conflict(new { message = "Outstanding balances are being updated. Reload the report." });
        }
    }

    private static object View(RetailCreditPayment payment) => new {
        payment.Id, payment.SaleId, payment.RequestKey, payment.Amount, payment.ActorUserId,
        Method = "Cash", PostedAtUtc = DateTime.SpecifyKind(payment.PostedAtUtc, DateTimeKind.Utc)
    };
}
