using System.Data;
using System.Security.Claims;
using BusinessSaas.Api.Data;
using Microsoft.EntityFrameworkCore;

namespace BusinessSaas.Api.Features;

public static class PlatformPaymentReviewEndpoints
{
    public static void MapPlatformPaymentReviewEndpoints(this WebApplication app)
    {
        var group = app.MapGroup("/api/platform/billing").RequireAuthorization();

        group.MapGet("/reconciliation", async (ClaimsPrincipal user,
            IConfiguration configuration, AppDbContext db, int page = 1, int pageSize = 20) =>
        {
            if (!PlatformOwner.IsOwner(user, configuration))
                return Results.Forbid();
            if (page is < 1 or > 100000 || pageSize is < 1 or > 50)
                return Results.BadRequest(new { message = "Choose a valid page and page size (1–50)." });

            var total = await db.SubscriptionPayments.CountAsync();
            var rows = await (from payment in db.SubscriptionPayments.AsNoTracking()
                              join request in db.SubscriptionPaymentRequests.AsNoTracking()
                                  on payment.PaymentRequestId equals request.Id
                              join business in db.Businesses.AsNoTracking()
                                  on request.BusinessId equals business.Id
                              join subscription in db.BusinessSubscriptions.AsNoTracking()
                                  on request.BusinessId equals subscription.BusinessId
                              orderby payment.Id descending
                              select new
                              {
                                  payment.Id, payment.PaymentRequestId, payment.ProviderPaymentId,
                                  request.BusinessId, businessName = business.Name,
                                  payment.Amount, payment.CurrencyCode,
                                  payment.ActivationStatus, payment.CapturedAtUtc,
                                  subscriptionStatus = subscription.Status,
                                  subscription.PaidEndsAtUtc,
                                  refundedAmount = db.SubscriptionRefunds.AsNoTracking()
                                      .Where(x => x.SubscriptionPaymentId == payment.Id)
                                      .Sum(x => (decimal?)x.Amount) ?? 0m
                              }).Skip((page - 1) * pageSize).Take(pageSize).ToListAsync();

            var items = rows.Select(x => new
            {
                x.Id, x.PaymentRequestId, x.ProviderPaymentId, x.BusinessId,
                x.businessName, x.Amount, x.CurrencyCode, x.ActivationStatus,
                x.CapturedAtUtc, x.subscriptionStatus, x.PaidEndsAtUtc,
                x.refundedAmount, netAmount = x.Amount - x.refundedAmount
            });

            var unresolvedOrders = await (from request in db.SubscriptionPaymentRequests.AsNoTracking()
                                          join business in db.Businesses.AsNoTracking()
                                              on request.BusinessId equals business.Id
                                          where request.Status == "OrderUncertain" ||
                                                request.Status == "CreatingOrder"
                                          orderby request.Id descending
                                          select new
                                          {
                                              request.Id, request.BusinessId,
                                              businessName = business.Name,
                                              request.Amount, request.CurrencyCode,
                                              request.Status, request.CreatedAtUtc
                                          }).Take(50).ToListAsync();

            return Results.Ok(new { items, total, page, pageSize, unresolvedOrders });
        });

        group.MapPost("/payment-requests/{id:long}/reconcile-order", async (long id,
            ReconcileOrderRequest input, ClaimsPrincipal user, IConfiguration configuration,
            AppDbContext db, IRazorpayGateway gateway, CancellationToken cancellationToken) =>
        {
            if (!PlatformOwner.IsOwner(user, configuration))
                return Results.Forbid();
            if (!RazorpayConfiguration.IsEnabled(configuration))
                return Results.StatusCode(StatusCodes.Status503ServiceUnavailable);

            var orderId = input.OrderId?.Trim() ?? "";
            var reason = input.Reason?.Trim() ?? "";
            if (id <= 0 || orderId.Length is < 10 or > 80 ||
                !orderId.StartsWith("order_", StringComparison.Ordinal) ||
                !orderId.All(ch => char.IsAsciiLetterOrDigit(ch) || ch == '_') ||
                reason.Length is < 10 or > 200)
                return Results.BadRequest(new { message = "Valid provider order ID and review reason are required." });

            var prepared = await db.SubscriptionPaymentRequests.AsNoTracking()
                .SingleOrDefaultAsync(x => x.Id == id, cancellationToken);
            if (prepared is null)
                return Results.NotFound();
            if (prepared.Status == "OrderCreated" && prepared.ProviderOrderId == orderId)
                return Results.Ok(new { id, orderId, status = "OrderCreated", alreadyReconciled = true });
            if (prepared.Status is not ("OrderUncertain" or "CreatingOrder") ||
                prepared.ProviderOrderId is not null || prepared.CurrencyCode != "INR")
                return Results.Conflict(new { message = "This request does not need order reconciliation." });

            GatewayOrder verified;
            try
            {
                verified = await gateway.FetchOrder(orderId, cancellationToken);
            }
            catch (Exception error) when (error is HttpRequestException or TaskCanceledException
                or System.Text.Json.JsonException or ArgumentException)
            {
                return Results.StatusCode(StatusCodes.Status503ServiceUnavailable);
            }

            long amount;
            try { amount = RazorpayConfiguration.ToPaise(prepared.Amount); }
            catch (ArgumentOutOfRangeException) { return Results.Conflict(); }

            if (verified.Id != orderId || verified.Receipt != $"bs_{id}" ||
                verified.Amount != amount || verified.Currency != prepared.CurrencyCode ||
                verified.Status is not ("created" or "attempted" or "paid"))
                return Results.Conflict(new { message = "Gateway order does not match this payment request." });

            await using var transaction =
                await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken);
            var current = await db.SubscriptionPaymentRequests
                .SingleAsync(x => x.Id == id, cancellationToken);
            if (current.Status == "OrderCreated" && current.ProviderOrderId == orderId)
                return Results.Ok(new { id, orderId, status = current.Status, alreadyReconciled = true });
            if (current.Status is not ("OrderUncertain" or "CreatingOrder") ||
                current.ProviderOrderId is not null)
                return Results.Conflict(new { message = "Payment request changed; reload reconciliation." });

            current.ProviderOrderId = orderId;
            current.Status = "OrderCreated";
            current.OrderCreatedAtUtc = DateTime.UtcNow;
            db.PlatformAuditEvents.Add(new PlatformAuditEvent
            {
                ActorUserId = long.Parse(user.FindFirstValue(ClaimTypes.NameIdentifier)!),
                Action = "Billing.OrderReconciled",
                TargetType = "PaymentRequest",
                TargetId = id,
                Reason = reason,
                NewValue = orderId
            });
            await db.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            return Results.Ok(new { id, orderId, status = "OrderCreated", alreadyReconciled = false });
        });
    }

    private sealed record ReconcileOrderRequest(string? OrderId, string? Reason);
}
