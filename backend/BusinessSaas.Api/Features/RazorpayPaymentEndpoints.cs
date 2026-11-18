using System.Data;
using System.Net;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using BusinessSaas.Api.Data;
using Microsoft.EntityFrameworkCore;

namespace BusinessSaas.Api.Features;

public static class RazorpayPaymentEndpoints
{
    private const string WebhookPath = "/api/payments/razorpay/webhook";
    private const int MaxWebhookBytes = 131072;

    public static void MapRazorpayPaymentEndpoints(this WebApplication app)
    {
        var checkout = app.MapGroup("/api/businesses/{businessId:long}/billing")
            .RequireAuthorization();

        checkout.MapGet("/checkout", async (long businessId, ClaimsPrincipal user,
            IConfiguration configuration, AppDbContext db) =>
        {
            if (!await IsOwner(db, user, businessId))
                return Results.NotFound();
            if (!RazorpayConfiguration.IsEnabled(configuration))
                return Results.Ok(new { enabled = false });

            var candidate = await GetCurrentRequest(db, businessId);
            if (candidate is null)
                return Results.Ok(new { enabled = true, available = false });
            return Results.Ok(new
            {
                enabled = true, available = true, paymentRequestId = candidate.Id,
                candidate.Amount, candidate.CurrencyCode, candidate.Status,
                canPay = candidate.Status is "AwaitingGateway" or "OrderCreated"
            });
        });

        checkout.MapPost("/checkout", async (long businessId, CheckoutRequest input,
            ClaimsPrincipal user, IConfiguration configuration, IRazorpayGateway gateway,
            AppDbContext db, ILoggerFactory loggerFactory, CancellationToken cancellationToken) =>
        {
            if (!await IsOwner(db, user, businessId))
                return Results.NotFound();
            if (!RazorpayConfiguration.IsEnabled(configuration))
                return Results.StatusCode(StatusCodes.Status503ServiceUnavailable);

            var candidate = await GetCurrentRequest(db, businessId);
            if (candidate is null || candidate.Id != input.PaymentRequestId)
                return Results.Conflict(new { message = "There is no eligible billing period to pay." });

            long amount;
            try { amount = RazorpayConfiguration.ToPaise(candidate.Amount); }
            catch (ArgumentOutOfRangeException)
            {
                return Results.Conflict(new { message = "Billing amount is not supported." });
            }

            if (candidate.ProviderOrderId is { Length: > 0 } &&
                candidate.Status == "OrderCreated")
                return CheckoutResult(candidate.Id, candidate.ProviderOrderId, amount, configuration);

            if (candidate.Status != "AwaitingGateway")
                return Results.Conflict(new
                {
                    message = "Gateway order is pending or needs reconciliation. Contact support; do not retry charging."
                });

            var reserved = await db.SubscriptionPaymentRequests
                .Where(x => x.Id == candidate.Id && x.Status == "AwaitingGateway" &&
                    x.ProviderOrderId == null)
                .ExecuteUpdateAsync(setters => setters
                    .SetProperty(x => x.Status, "CreatingOrder"), cancellationToken);
            if (reserved != 1)
                return Results.Conflict(new { message = "Another checkout is preparing this order." });

            try
            {
                var order = await gateway.CreateOrder(candidate.Id, amount, cancellationToken);
                if (!order.Id.StartsWith("order_", StringComparison.Ordinal) ||
                    order.Id.Length is < 10 or > 80 || order.Amount != amount ||
                    order.Currency != "INR" || order.Status != "created")
                    throw new InvalidOperationException("Unexpected order terms returned by gateway.");

                var saved = await db.SubscriptionPaymentRequests
                    .Where(x => x.Id == candidate.Id && x.Status == "CreatingOrder")
                    .ExecuteUpdateAsync(setters => setters
                        .SetProperty(x => x.ProviderOrderId, order.Id)
                        .SetProperty(x => x.Status, "OrderCreated")
                        .SetProperty(x => x.OrderCreatedAtUtc, DateTime.UtcNow),
                        cancellationToken);
                if (saved != 1)
                    return Results.StatusCode(StatusCodes.Status503ServiceUnavailable);

                return CheckoutResult(candidate.Id, order.Id, amount, configuration);
            }
            catch (Exception error) when (error is HttpRequestException or TaskCanceledException
                or JsonException or InvalidOperationException)
            {
                loggerFactory.CreateLogger("RazorpayCheckout")
                    .LogWarning("Checkout order creation needs reconciliation for request {RequestId}",
                        candidate.Id);
                await db.SubscriptionPaymentRequests
                    .Where(x => x.Id == candidate.Id && x.Status == "CreatingOrder")
                    .ExecuteUpdateAsync(setters => setters
                        .SetProperty(x => x.Status, "OrderUncertain"), CancellationToken.None);
                return Results.StatusCode(StatusCodes.Status503ServiceUnavailable);
            }
        });

        app.MapPost(WebhookPath, async (HttpRequest request, IConfiguration configuration,
            IRazorpayGateway gateway, AppDbContext db, ILoggerFactory loggerFactory,
            CancellationToken cancellationToken) =>
        {
            if (!RazorpayConfiguration.IsEnabled(configuration))
                return Results.StatusCode(StatusCodes.Status503ServiceUnavailable);
            if (request.ContentLength > MaxWebhookBytes)
                return Results.StatusCode(StatusCodes.Status413PayloadTooLarge);

            var signature = request.Headers["X-Razorpay-Signature"].ToString();
            var eventId = request.Headers["X-Razorpay-Event-Id"].ToString();
            if (signature.Length != 64 || eventId.Length is < 8 or > 100 ||
                !eventId.All(ch => char.IsAsciiLetterOrDigit(ch) || ch is '_' or '-'))
                return Results.Unauthorized();

            using var buffer = new MemoryStream();
            var chunk = new byte[8192];
            while (true)
            {
                var read = await request.Body.ReadAsync(chunk, cancellationToken);
                if (read == 0) break;
                if (buffer.Length + read > MaxWebhookBytes)
                    return Results.StatusCode(StatusCodes.Status413PayloadTooLarge);
                buffer.Write(chunk, 0, read);
            }

            var raw = buffer.ToArray();
            var valid = VerifySignature(raw, signature, configuration["Razorpay:WebhookSecret"]!) ||
                configuration["Razorpay:PreviousWebhookSecret"] is { Length: >= 20 } previous &&
                    VerifySignature(raw, signature, previous);
            if (!valid)
                return Results.Unauthorized();

            JsonDocument payload;
            try { payload = JsonDocument.Parse(raw); }
            catch (JsonException) { return Results.BadRequest(); }

            using (payload)
            {
                var root = payload.RootElement;
                if (!root.TryGetProperty("event", out var eventType) ||
                    eventType.ValueKind != JsonValueKind.String)
                    return Results.BadRequest();
                if (eventType.GetString() != "payment.captured")
                    return Results.Ok(new { accepted = true, ignored = true });

                string paymentId, orderId, currency, status;
                long amount;
                try
                {
                    var entity = root.GetProperty("payload").GetProperty("payment")
                        .GetProperty("entity");
                    paymentId = entity.GetProperty("id").GetString()!;
                    orderId = entity.GetProperty("order_id").GetString()!;
                    currency = entity.GetProperty("currency").GetString()!;
                    status = entity.GetProperty("status").GetString()!;
                    amount = entity.GetProperty("amount").GetInt64();
                }
                catch (Exception error) when (error is JsonException or InvalidOperationException
                    or KeyNotFoundException)
                {
                    return Results.BadRequest();
                }

                if (status != "captured" || currency != "INR" ||
                    string.IsNullOrWhiteSpace(orderId) || string.IsNullOrWhiteSpace(paymentId))
                    return Results.BadRequest();

                if (await db.RazorpayWebhookEvents.AsNoTracking().AnyAsync(x => x.EventId == eventId,
                    cancellationToken))
                    return Results.Ok(new { accepted = true, alreadyProcessed = true });

                GatewayPayment verified;
                try { verified = await gateway.FetchPayment(paymentId, cancellationToken); }
                catch (Exception error) when (error is HttpRequestException or TaskCanceledException
                    or JsonException or ArgumentException)
                {
                    loggerFactory.CreateLogger("RazorpayWebhook")
                        .LogWarning("Webhook payment lookup unavailable for event {EventId}", eventId);
                    return Results.StatusCode(StatusCodes.Status503ServiceUnavailable);
                }

                if (verified.Id != paymentId || verified.OrderId != orderId ||
                    verified.Amount != amount || verified.Currency != currency ||
                    verified.Status != "captured")
                    return Results.Conflict(new { message = "Gateway verification mismatch." });

                await using var transaction =
                    await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken);

                if (await db.RazorpayWebhookEvents.AnyAsync(x => x.EventId == eventId, cancellationToken))
                    return Results.Ok(new { accepted = true, alreadyProcessed = true });

                var prepared = await db.SubscriptionPaymentRequests
                    .SingleOrDefaultAsync(x => x.ProviderOrderId == orderId, cancellationToken);
                if (prepared is null || prepared.Status is not ("OrderCreated" or "Captured" or "CapturedReview"))
                    return Results.StatusCode(StatusCodes.Status503ServiceUnavailable);

                long expected;
                try { expected = RazorpayConfiguration.ToPaise(prepared.Amount); }
                catch (ArgumentOutOfRangeException) { return Results.Conflict(); }
                if (expected != amount || prepared.CurrencyCode != currency)
                    return Results.Conflict(new { message = "Captured amount or currency differs from billing snapshot." });

                var existingPayment = await db.SubscriptionPayments
                    .SingleOrDefaultAsync(x => x.PaymentRequestId == prepared.Id ||
                        x.ProviderPaymentId == paymentId, cancellationToken);
                if (existingPayment is not null)
                {
                    if (existingPayment.PaymentRequestId != prepared.Id ||
                        existingPayment.ProviderPaymentId != paymentId)
                        return Results.Conflict(new { message = "Conflicting payment attempt requires reconciliation." });

                    db.RazorpayWebhookEvents.Add(new RazorpayWebhookEvent
                    {
                        EventId = eventId, SubscriptionPaymentId = existingPayment.Id
                    });
                    await db.SaveChangesAsync(cancellationToken);
                    await transaction.CommitAsync(cancellationToken);
                    return Results.Ok(new { accepted = true, alreadyPaid = true });
                }

                var draft = await db.SubscriptionBillingDrafts.AsNoTracking()
                    .SingleAsync(x => x.Id == prepared.BillingDraftId, cancellationToken);
                var business = await db.Businesses.AsNoTracking()
                    .SingleAsync(x => x.Id == prepared.BusinessId, cancellationToken);
                var subscription = await db.BusinessSubscriptions
                    .SingleAsync(x => x.BusinessId == prepared.BusinessId, cancellationToken);

                if (draft.BusinessId != prepared.BusinessId || draft.Amount != prepared.Amount ||
                    draft.CurrencyCode != prepared.CurrencyCode || draft.PeriodStartDate is null ||
                    draft.PeriodEndDate is null || draft.PeriodEndDate <= draft.PeriodStartDate)
                    return Results.Conflict(new { message = "Billing period is invalid." });

                DateTime fromUtc, untilUtc;
                try
                {
                    var zone = TimeZoneInfo.FindSystemTimeZoneById(business.TimeZoneId);
                    fromUtc = TimeZoneInfo.ConvertTimeToUtc(
                        draft.PeriodStartDate.Value.ToDateTime(TimeOnly.MinValue), zone);
                    untilUtc = TimeZoneInfo.ConvertTimeToUtc(
                        draft.PeriodEndDate.Value.ToDateTime(TimeOnly.MinValue), zone);
                }
                catch (Exception error) when (error is TimeZoneNotFoundException or InvalidTimeZoneException
                    or ArgumentException)
                {
                    return Results.Conflict(new { message = "Billing timezone needs review." });
                }

                var now = DateTime.UtcNow;
                var startNow = fromUtc <= now && now < untilUtc &&
                    (subscription.PaidEndsAtUtc is null || subscription.PaidEndsAtUtc <= fromUtc);
                var renewal = subscription.PaidEndsAtUtc == fromUtc &&
                    subscription.PaidStartsAtUtc.HasValue &&
                    subscription.PaidPlanVersionId == draft.PublishedPlanVersionId &&
                    subscription.Status is "Active" or "Suspended";
                var activate = startNow || renewal;

                var payment = new SubscriptionPayment
                {
                    PaymentRequestId = prepared.Id,
                    ProviderPaymentId = paymentId,
                    ProviderOrderId = orderId,
                    Amount = prepared.Amount,
                    CurrencyCode = prepared.CurrencyCode,
                    ActivationStatus = activate
                        ? subscription.Status == "Suspended" ? "HeldSuspended" : "Activated"
                        : "NeedsReview"
                };
                db.SubscriptionPayments.Add(payment);
                prepared.Status = activate ? "Captured" : "CapturedReview";

                if (activate)
                {
                    subscription.PaidStartsAtUtc = renewal
                        ? subscription.PaidStartsAtUtc : fromUtc;
                    subscription.PaidEndsAtUtc = untilUtc;
                    subscription.PaidPlanVersionId = draft.PublishedPlanVersionId;
                    subscription.PlanCode = draft.PlanCode;
                    if (subscription.Status == "Suspended")
                        subscription.StatusBeforeSuspension = "Active";
                    else
                        subscription.Status = "Active";
                }

                await db.SaveChangesAsync(cancellationToken);
                db.RazorpayWebhookEvents.Add(new RazorpayWebhookEvent
                {
                    EventId = eventId, SubscriptionPaymentId = payment.Id
                });
                await db.SaveChangesAsync(cancellationToken);
                await transaction.CommitAsync(cancellationToken);
                return Results.Ok(new { accepted = true, activated = activate });
            }
        });
    }

    private static IResult CheckoutResult(long requestId, string orderId, long amount,
        IConfiguration configuration) => Results.Ok(new
        {
            paymentRequestId = requestId,
            orderId, amount, currency = "INR",
            keyId = configuration["Razorpay:KeyId"]
        });

    private static async Task<bool> IsOwner(AppDbContext db, ClaimsPrincipal user, long businessId) =>
        long.TryParse(user.FindFirstValue(ClaimTypes.NameIdentifier), out var userId) &&
        await db.BusinessUsers.AsNoTracking().AnyAsync(x =>
            x.BusinessId == businessId && x.UserId == userId && x.RoleCode == "Owner");

    private static async Task<SubscriptionPaymentRequest?> GetCurrentRequest(
        AppDbContext db, long businessId)
    {
        var business = await db.Businesses.AsNoTracking()
            .SingleOrDefaultAsync(x => x.Id == businessId);
        if (business is null || business.CountryCode != "IN" || business.CurrencyCode != "INR")
            return null;

        DateOnly today;
        try
        {
            today = DateOnly.FromDateTime(TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow,
                TimeZoneInfo.FindSystemTimeZoneById(business.TimeZoneId)));
        }
        catch (Exception error) when (error is TimeZoneNotFoundException or InvalidTimeZoneException)
        {
            return null;
        }

        return await (from payment in db.SubscriptionPaymentRequests.AsNoTracking()
                      join draft in db.SubscriptionBillingDrafts.AsNoTracking()
                          on payment.BillingDraftId equals draft.Id
                      where payment.BusinessId == businessId &&
                            draft.PeriodStartDate <= today && draft.PeriodEndDate > today
                      orderby payment.Id descending
                      select payment).FirstOrDefaultAsync();
    }

    private static bool VerifySignature(byte[] body, string signature, string secret)
    {
        byte[] supplied;
        try { supplied = Convert.FromHexString(signature); }
        catch (FormatException) { return false; }
        var expected = HMACSHA256.HashData(Encoding.UTF8.GetBytes(secret), body);
        return supplied.Length == expected.Length &&
            CryptographicOperations.FixedTimeEquals(supplied, expected);
    }

    private sealed record CheckoutRequest(long PaymentRequestId);
}
