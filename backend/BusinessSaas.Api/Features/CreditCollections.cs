using System.Data;
using System.Security.Claims;
using BusinessSaas.Api.Data;
using Microsoft.EntityFrameworkCore;

namespace BusinessSaas.Api.Features;

public static class CreditCollections
{
    internal static IQueryable<RetailCashSale> Invoices(AppDbContext db, long businessId, long customerId) =>
        db.RetailCashSales.AsNoTracking().Where(x => x.BusinessId == businessId &&
            x.CustomerId == customerId && x.CreditDueDate != null);

    internal static IQueryable<RetailCreditPayment> Payments(AppDbContext db, long businessId, long saleId) =>
        db.RetailCreditPayments.AsNoTracking().Where(x => x.BusinessId == businessId && x.SaleId == saleId);

    internal static IQueryable<RetailCreditPayment> CustomerPayments(AppDbContext db, long businessId, long customerId) =>
        from payment in db.RetailCreditPayments.AsNoTracking()
        join sale in Invoices(db, businessId, customerId) on payment.SaleId equals sale.Id
        where payment.BusinessId == businessId
        select payment;

    internal static Task<decimal?> PaidAsync(AppDbContext db, long businessId, long saleId) =>
        Payments(db, businessId, saleId).SumAsync(x => (decimal?)x.Amount);

    private sealed record PaymentRequest(string RequestKey, decimal Amount, decimal ExpectedOutstanding,
        bool CashReceivedConfirmed);

    internal static void MapCreditCollections(RouteGroupBuilder customers)
    {
        customers.MapGet("/{customerId:long}/credit-invoices/{saleId:long}/payments", async (
            long businessId, long customerId, long saleId, ClaimsPrincipal user, AppDbContext db,
            int page = 1, string? requestKey = null) =>
        {
            if (await RetailBillingSettingsEndpoints.OwnerId(db, businessId, user) is null)
                return Results.NotFound();
            if (page is < 1 or > 100000 || requestKey is { Length: > 80 })
                return Results.BadRequest(new { message = "Invalid payment history filter." });
            if (!await Invoices(db, businessId, customerId).AnyAsync(x => x.Id == saleId))
                return Results.NotFound();
            var query = Payments(db, businessId, saleId);
            if (requestKey is not null) query = query.Where(x => x.RequestKey == requestKey);
            var records = await query.OrderByDescending(x => x.Id).Skip((page - 1) * 20).Take(21).ToListAsync();
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
                var payment = new RetailCreditPayment
                {
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

    private static object View(RetailCreditPayment payment) => new {
        payment.Id, payment.SaleId, payment.RequestKey, payment.Amount, payment.ActorUserId,
        Method = "Cash", PostedAtUtc = DateTime.SpecifyKind(payment.PostedAtUtc, DateTimeKind.Utc)
    };
}
