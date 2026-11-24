using System.Data;
using System.Security.Claims;
using BusinessSaas.Api.Data;
using Microsoft.EntityFrameworkCore;

namespace BusinessSaas.Api.Features;

// Business Owner payment preferences. Never treat an enabled UI switch as payment authorization.
public static class RetailBillingSettingsEndpoints
{
    public static void MapRetailBillingSettings(this WebApplication app)
    {
        var group = app.MapGroup("/api/businesses/{businessId:long}/billing-settings")
            .RequireAuthorization();

        group.MapGet("", async (long businessId, ClaimsPrincipal user, AppDbContext db) =>
        {
            if (await OwnerId(db, businessId, user) is null) return Results.NotFound();
            var business = await db.Businesses.AsNoTracking().SingleAsync(x => x.Id == businessId);
            return Results.Ok(View(business));
        });

        group.MapPut("", async (long businessId, BillingSettingsRequest request,
            ClaimsPrincipal user, AppDbContext db) =>
        {
            var actorId = await OwnerId(db, businessId, user);
            if (actorId is null) return Results.NotFound();
            if (request.ExpectedRevision < 0 || request.Reason?.Trim().Length is not (>= 10 and <= 200))
                return Results.BadRequest(new { message = "Enter a 10–200 character change reason and valid revision." });
            // Credit cannot be advertised or turned on until the customer receivable
            // and collection flows are implemented, tested and explicitly released.
            if (request.CreditSalesEnabled)
                return Results.Conflict(new { code = "CREDIT_NOT_RELEASED",
                    message = "Customer Credit is not released. Keep it off until its ledger and collections are ready." });

            await using var tx = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable);
            var business = await db.Businesses.AsNoTracking()
                .SingleOrDefaultAsync(x => x.Id == businessId);
            if (business is null) return Results.NotFound();
            if (business.BillingSettingsRevision != request.ExpectedRevision)
                return Results.Conflict(new { code = "BILLING_SETTINGS_CHANGED",
                    message = "Billing settings changed. Reload before saving." });

            if (business.CashSalesEnabled == request.CashSalesEnabled &&
                business.CreditSalesEnabled == request.CreditSalesEnabled)
                return Results.Ok(new { settings = View(business), unchanged = true });

            var nextRevision = business.BillingSettingsRevision + 1;
            var changed = await db.Businesses.Where(x => x.Id == businessId &&
                    x.BillingSettingsRevision == request.ExpectedRevision)
                .ExecuteUpdateAsync(setters => setters
                    .SetProperty(x => x.CashSalesEnabled, request.CashSalesEnabled)
                    .SetProperty(x => x.CreditSalesEnabled, false)
                    .SetProperty(x => x.BillingSettingsRevision, nextRevision));
            if (changed != 1)
                return Results.Conflict(new { code = "BILLING_SETTINGS_CHANGED",
                    message = "Billing settings changed. Reload before saving." });

            db.PlatformAuditEvents.Add(new PlatformAuditEvent {
                ActorUserId = actorId.Value, Action = "Business.BillingSettingsChanged",
                TargetType = "Business", TargetId = businessId,
                PreviousValue = $"cash={business.CashSalesEnabled};credit={business.CreditSalesEnabled}",
                NewValue = $"cash={request.CashSalesEnabled};credit=false",
                Reason = request.Reason.Trim()
            });
            await db.SaveChangesAsync();
            await tx.CommitAsync();

            business.CashSalesEnabled = request.CashSalesEnabled;
            business.CreditSalesEnabled = false;
            business.BillingSettingsRevision = nextRevision;
            return Results.Ok(new { settings = View(business), unchanged = false });
        });

        // A static, source-controlled RDLC consumes these parameters; never edit
        // report XML at runtime and never allow required financial fields to hide.
        var receipt = app.MapGroup("/api/businesses/{businessId:long}/receipt-settings")
            .RequireAuthorization();

        receipt.MapGet("", async (long businessId, ClaimsPrincipal user, AppDbContext db) =>
        {
            if (await OwnerId(db, businessId, user) is null) return Results.NotFound();
            var business = await db.Businesses.AsNoTracking().SingleAsync(x => x.Id == businessId);
            return Results.Ok(ReceiptView(business));
        });

        receipt.MapPut("", async (long businessId, ReceiptSettingsRequest request,
            ClaimsPrincipal user, AppDbContext db) =>
        {
            var actor = await OwnerId(db, businessId, user);
            if (actor is null) return Results.NotFound();
            var subscription = await db.BusinessSubscriptions.AsNoTracking()
                .SingleOrDefaultAsync(x => x.BusinessId == businessId);
            if (subscription is null || !SubscriptionAccess.CanOperate(subscription, DateTime.UtcNow))
                return Results.StatusCode(StatusCodes.Status403Forbidden);
            if (request.ExpectedRevision < 0 ||
                request.PaperSize is not ("Thermal80" or "A4") ||
                request.FooterText is null || request.FooterText.Length > 120 ||
                request.FooterText.Any(char.IsControl) ||
                !ValidOptionalText(request.LegalName, 150) ||
                !ValidOptionalText(request.Address, 300) ||
                !ValidOptionalText(request.Phone, 25) ||
                !ValidOptionalText(request.Email, 254) ||
                !ValidEmail(request.Email) ||
                request.Reason?.Trim().Length is not (>= 10 and <= 200))
                return Results.BadRequest(new {
                    message = "Check paper size, merchant details, email, footer and the 10–200 character change reason."
                });

            await using var tx = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable);
            var business = await db.Businesses.AsNoTracking()
                .SingleOrDefaultAsync(x => x.Id == businessId);
            if (business is null) return Results.NotFound();
            if (business.ReceiptSettingsRevision != request.ExpectedRevision)
                return Results.Conflict(new {
                    code = "RECEIPT_SETTINGS_CHANGED",
                    message = "Invoice print settings changed. Reload before saving."
                });

            var legalName = request.LegalName is null ? business.ReceiptLegalName : request.LegalName.Trim();
            var showLegalName = request.ShowLegalName ?? business.ReceiptShowLegalName;
            var address = request.Address is null ? business.ReceiptAddress : request.Address.Trim();
            var showAddress = request.ShowAddress ?? business.ReceiptShowAddress;
            var phone = request.Phone is null ? business.ReceiptPhone : request.Phone.Trim();
            var showPhone = request.ShowPhone ?? business.ReceiptShowPhone;
            var email = request.Email is null ? business.ReceiptEmail : request.Email.Trim();
            var showEmail = request.ShowEmail ?? business.ReceiptShowEmail;
            var showCustomerName = request.ShowCustomerName ?? business.ReceiptShowCustomerName;
            var showCustomerPhone = request.ShowCustomerPhone ?? business.ReceiptShowCustomerPhone;
            var showCustomerAddress = request.ShowCustomerAddress ?? business.ReceiptShowCustomerAddress;
            var footer = request.FooterText.Trim();
            if (business.ReceiptPaperSize == request.PaperSize &&
                business.ReceiptShowTime == request.ShowTime &&
                business.ReceiptShowUnitPrice == request.ShowUnitPrice &&
                business.ReceiptShowCashReceived == request.ShowCashReceived &&
                business.ReceiptShowChange == request.ShowChange &&
                business.ReceiptShowBranch == (request.ShowBranch ?? business.ReceiptShowBranch) &&
                business.ReceiptShowSku == (request.ShowSku ?? business.ReceiptShowSku) &&
                business.ReceiptShowUnit == (request.ShowUnit ?? business.ReceiptShowUnit) &&
                business.ReceiptShowFooter == request.ShowFooter &&
                business.ReceiptLegalName == legalName && business.ReceiptShowLegalName == showLegalName &&
                business.ReceiptAddress == address && business.ReceiptShowAddress == showAddress &&
                business.ReceiptPhone == phone && business.ReceiptShowPhone == showPhone &&
                business.ReceiptEmail == email && business.ReceiptShowEmail == showEmail &&
                business.ReceiptShowCustomerName == showCustomerName &&
                business.ReceiptShowCustomerPhone == showCustomerPhone &&
                business.ReceiptShowCustomerAddress == showCustomerAddress &&
                business.ReceiptFooterText == footer)
                return Results.Ok(new { settings = ReceiptView(business), unchanged = true });

            var newRevision = business.ReceiptSettingsRevision + 1;
            var changed = await db.Businesses.Where(x => x.Id == businessId &&
                x.ReceiptSettingsRevision == request.ExpectedRevision)
                .ExecuteUpdateAsync(update => update
                    .SetProperty(x => x.ReceiptPaperSize, request.PaperSize)
                    .SetProperty(x => x.ReceiptShowTime, request.ShowTime)
                    .SetProperty(x => x.ReceiptShowUnitPrice, request.ShowUnitPrice)
                    .SetProperty(x => x.ReceiptShowCashReceived, request.ShowCashReceived)
                    .SetProperty(x => x.ReceiptShowChange, request.ShowChange)
                    .SetProperty(x => x.ReceiptShowBranch, request.ShowBranch ?? business.ReceiptShowBranch)
                    .SetProperty(x => x.ReceiptShowSku, request.ShowSku ?? business.ReceiptShowSku)
                    .SetProperty(x => x.ReceiptShowUnit, request.ShowUnit ?? business.ReceiptShowUnit)
                    .SetProperty(x => x.ReceiptShowFooter, request.ShowFooter)
                    .SetProperty(x => x.ReceiptLegalName, legalName)
                    .SetProperty(x => x.ReceiptShowLegalName, showLegalName)
                    .SetProperty(x => x.ReceiptAddress, address)
                    .SetProperty(x => x.ReceiptShowAddress, showAddress)
                    .SetProperty(x => x.ReceiptPhone, phone)
                    .SetProperty(x => x.ReceiptShowPhone, showPhone)
                    .SetProperty(x => x.ReceiptEmail, email)
                    .SetProperty(x => x.ReceiptShowEmail, showEmail)
                    .SetProperty(x => x.ReceiptShowCustomerName, showCustomerName)
                    .SetProperty(x => x.ReceiptShowCustomerPhone, showCustomerPhone)
                    .SetProperty(x => x.ReceiptShowCustomerAddress, showCustomerAddress)
                    .SetProperty(x => x.ReceiptFooterText, footer)
                    .SetProperty(x => x.ReceiptSettingsRevision, newRevision));
            if (changed != 1)
                return Results.Conflict(new {
                    code = "RECEIPT_SETTINGS_CHANGED",
                    message = "Invoice print settings changed. Reload before saving."
                });

            db.PlatformAuditEvents.Add(new PlatformAuditEvent {
                ActorUserId = actor.Value,
                Action = "Business.ReceiptSettingsChanged",
                TargetType = "Business", TargetId = businessId,
                PreviousValue = $"revision={business.ReceiptSettingsRevision};paper={business.ReceiptPaperSize}",
                // Keep the audit summary inside the existing 100-character limit; no raw contact data.
                NewValue = $"v={newRevision};p={request.PaperSize};flags=" +
                    string.Concat(new[] { request.ShowTime, request.ShowUnitPrice, request.ShowCashReceived,
                        request.ShowChange, request.ShowFooter, request.ShowBranch ?? business.ReceiptShowBranch,
                        request.ShowSku ?? business.ReceiptShowSku, request.ShowUnit ?? business.ReceiptShowUnit,
                        showLegalName, showAddress, showPhone, showEmail, showCustomerName, showCustomerPhone, showCustomerAddress }.Select(value => value ? "1" : "0")) +
                    $";merchantChanged={business.ReceiptLegalName != legalName || business.ReceiptAddress != address || business.ReceiptPhone != phone || business.ReceiptEmail != email}",
                Reason = request.Reason.Trim()
            });
            await db.SaveChangesAsync();
            await tx.CommitAsync();

            business.ReceiptPaperSize = request.PaperSize;
            business.ReceiptShowTime = request.ShowTime;
            business.ReceiptShowUnitPrice = request.ShowUnitPrice;
            business.ReceiptShowCashReceived = request.ShowCashReceived;
            business.ReceiptShowChange = request.ShowChange;
            business.ReceiptShowBranch = request.ShowBranch ?? business.ReceiptShowBranch;
            business.ReceiptShowSku = request.ShowSku ?? business.ReceiptShowSku;
            business.ReceiptShowUnit = request.ShowUnit ?? business.ReceiptShowUnit;
            business.ReceiptShowFooter = request.ShowFooter;
            business.ReceiptLegalName = legalName;
            business.ReceiptShowLegalName = showLegalName;
            business.ReceiptAddress = address;
            business.ReceiptShowAddress = showAddress;
            business.ReceiptPhone = phone;
            business.ReceiptShowPhone = showPhone;
            business.ReceiptEmail = email;
            business.ReceiptShowEmail = showEmail;
            business.ReceiptShowCustomerName = showCustomerName;
            business.ReceiptShowCustomerPhone = showCustomerPhone;
            business.ReceiptShowCustomerAddress = showCustomerAddress;
            business.ReceiptFooterText = footer;
            business.ReceiptSettingsRevision = newRevision;
            return Results.Ok(new { settings = ReceiptView(business), unchanged = false });
        });
    }

    private static object ReceiptView(Business business) => new {
        paperSize = business.ReceiptPaperSize,
        showTime = business.ReceiptShowTime,
        showUnitPrice = business.ReceiptShowUnitPrice,
        showCashReceived = business.ReceiptShowCashReceived,
        showChange = business.ReceiptShowChange,
        showBranch = business.ReceiptShowBranch,
        showSku = business.ReceiptShowSku,
        showUnit = business.ReceiptShowUnit,
        showFooter = business.ReceiptShowFooter,
        legalName = business.ReceiptLegalName ?? "",
        showLegalName = business.ReceiptShowLegalName,
        address = business.ReceiptAddress ?? "",
        showAddress = business.ReceiptShowAddress,
        phone = business.ReceiptPhone ?? "",
        showPhone = business.ReceiptShowPhone,
        email = business.ReceiptEmail ?? "",
        showEmail = business.ReceiptShowEmail,
        showCustomerName = business.ReceiptShowCustomerName,
        showCustomerPhone = business.ReceiptShowCustomerPhone,
        showCustomerAddress = business.ReceiptShowCustomerAddress,
        footerText = business.ReceiptFooterText,
        revision = business.ReceiptSettingsRevision,
        // Business name, receipt ID, date, product/qty, line total, payable total
        // are statutory/transaction-critical and NOT configurable.
        mandatoryFields = new[] { "Business name", "Receipt number", "Date",
            "Items and quantities", "Line totals", "Total payable" },
        rendererAvailable = false
    };

    private sealed record ReceiptSettingsRequest(
        string PaperSize, bool ShowTime, bool ShowUnitPrice,
        bool ShowCashReceived, bool ShowChange, bool ShowFooter,
        string FooterText, int ExpectedRevision, string? Reason,
        bool? ShowBranch = null, bool? ShowSku = null, bool? ShowUnit = null,
        string? LegalName = null, bool? ShowLegalName = null,
        string? Address = null, bool? ShowAddress = null,
        string? Phone = null, bool? ShowPhone = null,
        string? Email = null, bool? ShowEmail = null,
        bool? ShowCustomerName = null, bool? ShowCustomerPhone = null, bool? ShowCustomerAddress = null);

    private static bool ValidOptionalText(string? value, int maxLength) =>
        value is null || (value.Length <= maxLength && !value.Any(char.IsControl));

    private static bool ValidEmail(string? value) => string.IsNullOrWhiteSpace(value) ||
        (System.Net.Mail.MailAddress.TryCreate(value.Trim(), out var address) &&
            string.Equals(address.Address, value.Trim(), StringComparison.OrdinalIgnoreCase));

    private static object View(Business business) => new {
        cashSalesEnabled = business.CashSalesEnabled,
        creditSalesEnabled = business.CreditSalesEnabled,
        creditAvailable = false,
        revision = business.BillingSettingsRevision,
        salesPaused = !business.CashSalesEnabled && !business.CreditSalesEnabled
    };

    private static async Task<long?> OwnerId(AppDbContext db, long businessId, ClaimsPrincipal user)
    {
        if (!long.TryParse(user.FindFirstValue(ClaimTypes.NameIdentifier), out var actor))
            return null;
        return await (from membership in db.BusinessUsers.AsNoTracking()
                      join business in db.Businesses.AsNoTracking()
                          on membership.BusinessId equals business.Id
                      join type in db.BusinessTypes.AsNoTracking()
                          on business.BusinessTypeId equals type.Id
                      where membership.BusinessId == businessId && membership.UserId == actor &&
                            membership.RoleCode == "Owner" && type.Code == "RETAIL"
                      select (long?)membership.UserId).SingleOrDefaultAsync();
    }

    private sealed record BillingSettingsRequest(bool CashSalesEnabled, bool CreditSalesEnabled,
        int ExpectedRevision, string? Reason);
}
