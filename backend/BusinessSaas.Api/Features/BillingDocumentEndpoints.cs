using System.Data;
using System.Data.Common;
using System.Security.Claims;
using System.Text.Json;
using System.Text.RegularExpressions;
using BusinessSaas.Api.Data;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;

namespace BusinessSaas.Api.Features;

public static class BillingDocumentEndpoints
{
    public static void MapBillingDocumentEndpoints(this WebApplication app)
    {
        var group = app.MapGroup("/api/platform/billing").RequireAuthorization();

        group.MapGet("/tax-profiles/{businessId:long}", async (long businessId,
            ClaimsPrincipal user, IConfiguration config, AppDbContext db) =>
        {
            if (!PlatformOwner.IsOwner(user, config)) return Results.Forbid();
            var item = await db.BillingTaxProfiles.AsNoTracking()
                .SingleOrDefaultAsync(x => x.BusinessId == businessId);
            return item is null ? Results.NotFound() : Results.Ok(item);
        });

        group.MapPost("/tax-profiles/{businessId:long}", async (long businessId,
            TaxProfileInput input, ClaimsPrincipal user, IConfiguration config, AppDbContext db) =>
        {
            if (!PlatformOwner.IsOwner(user, config)) return Results.Forbid();
            if (businessId <= 0 || !IsValid(input))
                return Results.BadRequest(new
                {
                    message = "Invalid issuer, recipient, GST classification, series, revision or reason."
                });

            if (!await db.Businesses.AnyAsync(x =>
                x.Id == businessId && x.CountryCode == "IN" && x.CurrencyCode == "INR"))
                return Results.Conflict(new { message = "Tax drafts currently support India/INR only." });

            await using var transaction =
                await db.Database.BeginTransactionAsync(IsolationLevel.Serializable);
            var profile = await db.BillingTaxProfiles
                .SingleOrDefaultAsync(x => x.BusinessId == businessId);
            if (profile is null ? input.ExpectedRevision != 0 :
                profile.Revision != input.ExpectedRevision)
                return Results.Conflict(new { message = "Profile revision is stale." });

            var actor = long.Parse(user.FindFirstValue(ClaimTypes.NameIdentifier)!);
            profile ??= new BillingTaxProfile { BusinessId = businessId, Revision = 0 };
            if (profile.Id == 0) db.BillingTaxProfiles.Add(profile);
            profile.Revision++;
            profile.IssuerName = input.IssuerName!.Trim();
            profile.IssuerAddress = input.IssuerAddress!.Trim();
            profile.IssuerGstin = input.IssuerGstin!.Trim().ToUpperInvariant();
            profile.IssuerStateCode = input.IssuerStateCode!.Trim();
            profile.RecipientName = input.RecipientName!.Trim();
            profile.RecipientAddress = input.RecipientAddress!.Trim();
            profile.RecipientGstin = string.IsNullOrWhiteSpace(input.RecipientGstin)
                ? null : input.RecipientGstin.Trim().ToUpperInvariant();
            profile.RecipientStateCode = input.RecipientStateCode!.Trim();
            profile.ServiceCode = input.ServiceCode!.Trim();
            profile.ServiceDescription = input.ServiceDescription!.Trim();
            profile.InvoiceSeries = input.InvoiceSeries!.Trim().ToUpperInvariant();
            profile.GstRate = input.GstRate;
            profile.PricesIncludeTax = input.PricesIncludeTax;
            profile.UpdatedByUserId = actor;
            profile.UpdatedAtUtc = DateTime.UtcNow;

            try { await db.SaveChangesAsync(); }
            catch (DbUpdateException error) when (UniqueConflict(error))
            {
                return Results.Conflict(new { message = "Tax profile already exists. Reload it." });
            }

            await Audit(db, actor, "Billing.TaxProfileSaved",
                profile.Id, input.Reason!, "Revision " + profile.Revision);
            await transaction.CommitAsync();
            return Results.Ok(new
            {
                profile.Id, profile.Revision,
                verificationStatus = "PendingExternalReview",
                statutoryIssuanceEnabled = false
            });
        });

        group.MapPost("/invoice-drafts", async (DraftRequest input,
            ClaimsPrincipal user, IConfiguration config, AppDbContext db) =>
        {
            if (!PlatformOwner.IsOwner(user, config)) return Results.Forbid();
            if (input.Id <= 0 || !ValidReason(input.Reason))
                return Results.BadRequest(new { message = "Payment ID and review reason are required." });

            await using var transaction =
                await db.Database.BeginTransactionAsync(IsolationLevel.Serializable);

            var existing = await db.BillingDocumentDrafts.AsNoTracking()
                .SingleOrDefaultAsync(x => x.SubscriptionPaymentId == input.Id &&
                    x.DocumentType == "Invoice");
            if (existing is not null)
                return Results.Ok(new { id = existing.Id, status = existing.Status, alreadyExists = true });

            var payment = await (from paid in db.SubscriptionPayments.AsNoTracking()
                                 join request in db.SubscriptionPaymentRequests.AsNoTracking()
                                     on paid.PaymentRequestId equals request.Id
                                 where paid.Id == input.Id
                                 select new
                                 {
                                     paid.Id, paid.Amount, paid.CurrencyCode,
                                     paid.ProviderPaymentId,
                                     paymentOrder = paid.ProviderOrderId,
                                     requestOrder = request.ProviderOrderId,
                                     request.BusinessId, request.Status,
                                     expectedAmount = request.Amount
                                 }).SingleOrDefaultAsync();

            if (payment is null) return Results.NotFound();
            if (payment.Status is not ("Captured" or "CapturedReview") ||
                payment.paymentOrder != payment.requestOrder ||
                string.IsNullOrWhiteSpace(payment.paymentOrder) ||
                string.IsNullOrWhiteSpace(payment.ProviderPaymentId) ||
                payment.Amount != payment.expectedAmount ||
                payment.Amount <= 0 || payment.CurrencyCode != "INR")
                return Results.Conflict(new { message = "A gateway-verified captured INR payment is required." });

            var profile = await db.BillingTaxProfiles.AsNoTracking()
                .SingleOrDefaultAsync(x => x.BusinessId == payment.BusinessId);
            if (profile is null || !profile.PricesIncludeTax)
                return Results.Conflict(new { message = "Configure tax-inclusive prices before preparing the draft." });

            var tax = BillingTaxCalculator.Inclusive(payment.Amount, profile.GstRate,
                profile.IssuerStateCode == profile.RecipientStateCode);
            var actor = long.Parse(user.FindFirstValue(ClaimTypes.NameIdentifier)!);
            var now = DateTime.UtcNow;

            var draft = new BillingDocumentDraft
            {
                BusinessId = payment.BusinessId,
                SubscriptionPaymentId = payment.Id,
                BillingTaxProfileId = profile.Id,
                TaxProfileRevision = profile.Revision,
                DocumentType = "Invoice",
                Status = "Draft",
                TaxSnapshotJson = JsonSerializer.Serialize(new
                {
                    profile.IssuerName, profile.IssuerAddress, profile.IssuerGstin,
                    profile.IssuerStateCode, profile.RecipientName, profile.RecipientAddress,
                    profile.RecipientGstin, profile.RecipientStateCode,
                    profile.ServiceCode, profile.ServiceDescription,
                    profile.InvoiceSeries, profile.GstRate, profile.PricesIncludeTax
                }),
                CurrencyCode = payment.CurrencyCode,
                FinancialYearStart = now.Month >= 4 ? now.Year : now.Year - 1,
                GrossAmount = tax.Gross,
                TaxableAmount = tax.Taxable,
                CgstAmount = tax.Cgst,
                SgstAmount = tax.Sgst,
                IgstAmount = tax.Igst,
                CreatedByUserId = actor,
                CreatedAtUtc = now
            };
            db.BillingDocumentDrafts.Add(draft);
            try { await db.SaveChangesAsync(); }
            catch (DbUpdateException error) when (UniqueConflict(error))
            {
                return Results.Conflict(new { message = "Invoice draft already exists. Reload it." });
            }
            await Audit(db, actor, "Billing.InvoiceDrafted",
                draft.Id, input.Reason!, "Payment " + payment.Id);
            await transaction.CommitAsync();
            return Results.Created("/api/platform/billing/document-drafts/" + draft.Id,
                new { id = draft.Id, status = draft.Status, statutoryDocument = false });
        });

        group.MapPost("/credit-note-drafts", async (DraftRequest input,
            ClaimsPrincipal user, IConfiguration config, AppDbContext db) =>
        {
            if (!PlatformOwner.IsOwner(user, config)) return Results.Forbid();
            if (input.Id <= 0 || !ValidReason(input.Reason))
                return Results.BadRequest(new { message = "Refund ID and review reason are required." });

            await using var transaction =
                await db.Database.BeginTransactionAsync(IsolationLevel.Serializable);

            var existing = await db.BillingDocumentDrafts.AsNoTracking()
                .SingleOrDefaultAsync(x => x.SubscriptionRefundId == input.Id);
            if (existing is not null)
                return Results.Ok(new { id = existing.Id, status = existing.Status, alreadyExists = true });

            var refund = await db.SubscriptionRefunds.AsNoTracking()
                .SingleOrDefaultAsync(x => x.Id == input.Id && x.Status == "Processed");
            if (refund is null) return Results.NotFound();

            var invoice = await db.BillingDocumentDrafts.AsNoTracking()
                .SingleOrDefaultAsync(x => x.SubscriptionPaymentId ==
                    refund.SubscriptionPaymentId && x.DocumentType == "Invoice");
            if (invoice is null || invoice.CurrencyCode != refund.CurrencyCode)
                return Results.Conflict(new { message = "Matching original invoice draft is required." });

            var previous = await db.BillingDocumentDrafts.AsNoTracking()
                .Where(x => x.OriginalDocumentId == invoice.Id && x.DocumentType == "CreditNote")
                .GroupBy(x => x.OriginalDocumentId)
                .Select(g => new
                {
                    Gross = g.Sum(x => x.GrossAmount),
                    Taxable = g.Sum(x => x.TaxableAmount)
                }).SingleOrDefaultAsync();

            using var snapshot = JsonDocument.Parse(invoice.TaxSnapshotJson);
            var issuerState = snapshot.RootElement.GetProperty("IssuerStateCode").GetString();
            var recipientState = snapshot.RootElement.GetProperty("RecipientStateCode").GetString();

            TaxBreakdown credit;
            try
            {
                credit = BillingTaxCalculator.Refund(refund.Amount,
                    invoice.GrossAmount, invoice.TaxableAmount,
                    previous?.Gross ?? 0m, previous?.Taxable ?? 0m,
                    issuerState == recipientState);
            }
            catch (ArgumentOutOfRangeException)
            {
                return Results.Conflict(new { message = "Credit amount exceeds the original draft." });
            }

            var actor = long.Parse(user.FindFirstValue(ClaimTypes.NameIdentifier)!);
            var now = DateTime.UtcNow;
            var draft = new BillingDocumentDraft
            {
                BusinessId = invoice.BusinessId,
                SubscriptionPaymentId = invoice.SubscriptionPaymentId,
                SubscriptionRefundId = refund.Id,
                OriginalDocumentId = invoice.Id,
                BillingTaxProfileId = invoice.BillingTaxProfileId,
                TaxProfileRevision = invoice.TaxProfileRevision,
                DocumentType = "CreditNote",
                Status = "Draft",
                TaxSnapshotJson = invoice.TaxSnapshotJson,
                CurrencyCode = refund.CurrencyCode,
                FinancialYearStart = now.Month >= 4 ? now.Year : now.Year - 1,
                GrossAmount = credit.Gross,
                TaxableAmount = credit.Taxable,
                CgstAmount = credit.Cgst,
                SgstAmount = credit.Sgst,
                IgstAmount = credit.Igst,
                CreatedByUserId = actor,
                CreatedAtUtc = now
            };
            db.BillingDocumentDrafts.Add(draft);
            try { await db.SaveChangesAsync(); }
            catch (DbUpdateException error) when (UniqueConflict(error))
            {
                return Results.Conflict(new { message = "Credit-note draft already exists. Reload it." });
            }
            await Audit(db, actor, "Billing.CreditNoteDrafted",
                draft.Id, input.Reason!, "Refund " + refund.Id);
            await transaction.CommitAsync();
            return Results.Created("/api/platform/billing/document-drafts/" + draft.Id,
                new { id = draft.Id, status = draft.Status, statutoryDocument = false });
        });

        group.MapGet("/document-drafts/{id:long}", async (long id,
            ClaimsPrincipal user, IConfiguration config, AppDbContext db) =>
        {
            if (!PlatformOwner.IsOwner(user, config)) return Results.Forbid();
            var draft = await db.BillingDocumentDrafts.AsNoTracking()
                .SingleOrDefaultAsync(x => x.Id == id);
            return draft is null ? Results.NotFound() : Results.Ok(draft);
        });
    }

    private static async Task Audit(AppDbContext db, long actorId, string action,
        long targetId, string reason, string details)
    {
        db.PlatformAuditEvents.Add(new PlatformAuditEvent
        {
            ActorUserId = actorId,
            Action = action,
            TargetType = "BillingDocument",
            TargetId = targetId,
            Reason = reason.Trim(),
            NewValue = details
        });
        await db.SaveChangesAsync();
    }

    private static bool ValidReason(string? reason) =>
        reason?.Trim().Length is >= 10 and <= 200;

    private static bool UniqueConflict(DbUpdateException error) =>
        error.InnerException is SqlException { Number: 2601 or 2627 } or
            DbException { ErrorCode: 19 };

    private static bool IsValid(TaxProfileInput input)
    {
        static bool Size(string? value, int min, int max) =>
            value?.Trim().Length is int length && length >= min && length <= max;

        var issuer = input.IssuerGstin?.Trim().ToUpperInvariant() ?? "";
        var recipient = input.RecipientGstin?.Trim().ToUpperInvariant() ?? "";
        var issuerState = input.IssuerStateCode?.Trim() ?? "";
        var recipientState = input.RecipientStateCode?.Trim() ?? "";

        return input.ExpectedRevision >= 0 && ValidReason(input.Reason) &&
            Size(input.IssuerName, 3, 160) && Size(input.IssuerAddress, 10, 300) &&
            Size(input.RecipientName, 3, 160) && Size(input.RecipientAddress, 10, 300) &&
            Size(input.ServiceDescription, 5, 160) &&
            Regex.IsMatch(issuer, "^[0-9]{2}[A-Z0-9]{13}$") &&
            issuerState.Length == 2 && issuerState.All(char.IsAsciiDigit) &&
            recipientState.Length == 2 && recipientState.All(char.IsAsciiDigit) &&
            issuer.StartsWith(issuerState, StringComparison.Ordinal) &&
            (recipient.Length == 0 ||
                Regex.IsMatch(recipient, "^[0-9]{2}[A-Z0-9]{13}$") &&
                recipient.StartsWith(recipientState, StringComparison.Ordinal)) &&
            Regex.IsMatch(input.ServiceCode?.Trim() ?? "", "^[0-9]{4,8}$") &&
            Regex.IsMatch(input.InvoiceSeries?.Trim().ToUpperInvariant() ?? "", "^[A-Z0-9]{2,5}$") &&
            input.GstRate is >= 0 and <= 100 &&
            input.GstRate == decimal.Round(input.GstRate, 2);
    }

    private sealed record TaxProfileInput(int ExpectedRevision, string? IssuerName,
        string? IssuerAddress, string? IssuerGstin, string? IssuerStateCode,
        string? RecipientName, string? RecipientAddress, string? RecipientGstin,
        string? RecipientStateCode, string? ServiceCode, string? ServiceDescription,
        string? InvoiceSeries, decimal GstRate, bool PricesIncludeTax, string? Reason);

    private sealed record DraftRequest(long Id, string? Reason);
}
