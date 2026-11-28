using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;

namespace BusinessSaas.Api.Data;

public sealed class AppUser : IdentityUser<long>;

public sealed class PlatformOwnerInvitation
{
    public long Id { get; set; }
    public string Email { get; set; } = "";
    public string NormalizedEmail { get; set; } = "";
    public string TokenHash { get; set; } = "";
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
    public DateTime ExpiresAtUtc { get; set; }
    public DateTime? AcceptedAtUtc { get; set; }
    public DateTime? RevokedAtUtc { get; set; }
    public long CreatedByUserId { get; set; }
    public long? AcceptedUserId { get; set; }
    public string Reason { get; set; } = "";
}

public sealed class PlatformAuditEvent
{
    public long Id { get; set; }
    public long ActorUserId { get; set; }
    public string Action { get; set; } = "";
    public string TargetType { get; set; } = "";
    public long? TargetId { get; set; }
    public string? Reason { get; set; }
    public string? PreviousValue { get; set; }
    public string? NewValue { get; set; }
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
}


public sealed class PlatformModulePolicy
{
    public long Id { get; set; }
    public string Code { get; set; } = "";
    public string ReleaseState { get; set; } = "Available";
    public int Revision { get; set; }
    public long UpdatedByUserId { get; set; }
    public DateTime UpdatedAtUtc { get; set; } = DateTime.UtcNow;
}

public sealed class PlatformMaintenanceWindow
{
    public long Id { get; set; }
    public string Mode { get; set; } = "NoticeOnly";
    public string Title { get; set; } = "";
    public string Message { get; set; } = "";
    public DateTime StartUtc { get; set; }
    public DateTime EndUtc { get; set; }
    public bool IsCancelled { get; set; }
    public string Reason { get; set; } = "";
    public long CreatedByUserId { get; set; }
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
}

public sealed class PlatformAnnouncement
{
    public long Id { get; set; }
    public string Level { get; set; } = "Information";
    public string Title { get; set; } = "";
    public string Message { get; set; } = "";
    public DateTime StartUtc { get; set; }
    public DateTime EndUtc { get; set; }
    public bool IsArchived { get; set; }
    public string Reason { get; set; } = "";
    public long CreatedByUserId { get; set; }
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
}

public sealed class BusinessType
{
    public long Id { get; set; }
    public string Code { get; set; } = "";
    public string Name { get; set; } = "";
    public bool IsSupported { get; set; }
    // Installed capability remains IsSupported; policy only controls new provisioning.
    public string ReleaseState { get; set; } = "Available";
    public int ReleaseRevision { get; set; }
}

public sealed class Business
{
    public long Id { get; set; }
    public long BusinessTypeId { get; set; }
    public long CreatedByUserId { get; set; }
    public string CreationKey { get; set; } = "";
    public string Name { get; set; } = "";
    public string CountryCode { get; set; } = "";
    public string CurrencyCode { get; set; } = "";
    public string TimeZoneId { get; set; } = "";
    // Owner-managed Retail payment methods. Credit stays off until its ledger is released.
    public bool CashSalesEnabled { get; set; } = true;
    public bool CreditSalesEnabled { get; set; }
    public int BillingSettingsRevision { get; set; }
    // Retail receipt layout: presentation only. Monetary/legal fields cannot be suppressed.
    public string? ReceiptLegalName { get; set; }
    public bool ReceiptShowLegalName { get; set; } = true;
    public string? ReceiptAddress { get; set; }
    public bool ReceiptShowAddress { get; set; } = true;
    public string? ReceiptPhone { get; set; }
    public bool ReceiptShowPhone { get; set; } = true;
    public string? ReceiptEmail { get; set; }
    public bool ReceiptShowEmail { get; set; } = true;
    public byte[]? ReceiptLogo { get; set; }
    public bool ReceiptShowLogo { get; set; } = true;
    public int ReceiptPrintCopies { get; set; } = 1;
    public bool ReceiptAutoOpen { get; set; }
    public string ReceiptPaperSize { get; set; } = "Thermal80";
    public bool ReceiptShowTime { get; set; } = true;
    public bool ReceiptShowUnitPrice { get; set; } = true;
    public bool ReceiptShowCashReceived { get; set; } = true;
    public bool ReceiptShowChange { get; set; } = true;
    public bool ReceiptShowBranch { get; set; } = true;
    public bool ReceiptShowSku { get; set; } = true;
    public bool ReceiptShowUnit { get; set; } = true;
    public bool ReceiptShowCustomerName { get; set; } = true;
    public bool ReceiptShowCustomerPhone { get; set; } = true;
    public bool ReceiptShowCustomerAddress { get; set; } = true;
    public bool ReceiptShowFooter { get; set; } = true;
    public string ReceiptFooterText { get; set; } = "Thank you for shopping!";
    public int ReceiptSettingsRevision { get; set; }
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
}

public sealed class BusinessUser
{
    public long Id { get; set; }
    public long BusinessId { get; set; }
    public long UserId { get; set; }
    public string RoleCode { get; set; } = "Owner";
}

public sealed class SubscriptionPlanDraft
{
    public long Id { get; set; }
    public string PlanCode { get; set; } = "";
    public int Revision { get; set; } = 1;
    public string? CurrencyCode { get; set; }
    public decimal? MonthlyPrice { get; set; }
    public decimal? QuarterlyPrice { get; set; }
    public decimal? YearlyPrice { get; set; }
    public int? MaxLocations { get; set; }
    public int? MaxUsers { get; set; }
    public int? MaxProducts { get; set; }
    public long UpdatedByUserId { get; set; }
    public DateTime UpdatedAtUtc { get; set; } = DateTime.UtcNow;
}

public sealed class PublishedPlanVersion
{
    public long Id { get; set; }
    public string PlanCode { get; set; } = "";
    public int Version { get; set; }
    public int SourceDraftRevision { get; set; }
    public string CurrencyCode { get; set; } = "";
    public decimal MonthlyPrice { get; set; }
    public decimal QuarterlyPrice { get; set; }
    public decimal YearlyPrice { get; set; }
    public int? MaxLocations { get; set; }
    public int? MaxUsers { get; set; }
    public int? MaxProducts { get; set; }
    public long PublishedByUserId { get; set; }
    public DateTime PublishedAtUtc { get; set; } = DateTime.UtcNow;
}

public sealed class SubscriptionBillingDraft
{
    public long Id { get; set; }
    public long BusinessId { get; set; }
    public long PublishedPlanVersionId { get; set; }
    public string PlanCode { get; set; } = "";
    public string BillingCycle { get; set; } = "";
    public string CurrencyCode { get; set; } = "";
    public decimal Amount { get; set; }
    public string RequestKey { get; set; } = "";
    public DateOnly? PeriodStartDate { get; set; }
    public DateOnly? PeriodEndDate { get; set; }
    public long CreatedByUserId { get; set; }
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
}

public sealed class SubscriptionPaymentRequest
{
    public long Id { get; set; }
    public long BillingDraftId { get; set; }
    public long BusinessId { get; set; }
    public string CurrencyCode { get; set; } = "";
    public decimal Amount { get; set; }
    public string RequestKey { get; set; } = "";
    public string Status { get; set; } = "AwaitingGateway";
    public string? ProviderOrderId { get; set; }
    public DateTime? OrderCreatedAtUtc { get; set; }
    public long CreatedByUserId { get; set; }
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
}

public sealed class SubscriptionPayment
{
    public long Id { get; set; }
    public long PaymentRequestId { get; set; }
    public string ProviderPaymentId { get; set; } = "";
    public string ProviderOrderId { get; set; } = "";
    public string CurrencyCode { get; set; } = "";
    public decimal Amount { get; set; }
    public string ActivationStatus { get; set; } = "Review";
    public DateTime CapturedAtUtc { get; set; } = DateTime.UtcNow;
}

public sealed class SubscriptionRefund
{
    public long Id { get; set; }
    public long SubscriptionPaymentId { get; set; }
    public string ProviderRefundId { get; set; } = "";
    public string CurrencyCode { get; set; } = "";
    public decimal Amount { get; set; }
    public string Status { get; set; } = "Processed";
    public DateTime ObservedAtUtc { get; set; } = DateTime.UtcNow;
}

public sealed class RazorpayWebhookEvent
{
    public long Id { get; set; }
    public string EventId { get; set; } = "";
    public long SubscriptionPaymentId { get; set; }
    public DateTime ReceivedAtUtc { get; set; } = DateTime.UtcNow;
}

public sealed class BusinessSubscription
{
    public long Id { get; set; }
    public long BusinessId { get; set; }
    public string PlanCode { get; set; } = "STARTER";
    public string Status { get; set; } = "Trial";
    public string? StatusBeforeSuspension { get; set; }
    public DateTime TrialEndsAtUtc { get; set; }
    public DateTime? PaidStartsAtUtc { get; set; }
    public DateTime? PaidEndsAtUtc { get; set; }
    public long? PaidPlanVersionId { get; set; }
}

public sealed class BusinessLocation
{
    public long Id { get; set; }
    public long BusinessId { get; set; }
    public string Code { get; set; } = "MAIN";
    public string Name { get; set; } = "Main Shop";
    public bool IsPrimary { get; set; } = true;
}

public sealed class RetailCategory
{
    public long Id { get; set; }
    public long BusinessId { get; set; }
    public string Name { get; set; } = "";
    public string NameKey { get; set; } = "";
    public long? ParentId { get; set; }
    public bool IsActive { get; set; } = true;
}

public sealed class RetailBrand
{
    public long Id { get; set; }
    public long BusinessId { get; set; }
    public string Name { get; set; } = "";
    public string NameKey { get; set; } = "";
    public bool IsActive { get; set; } = true;
}

public sealed class RetailUnit
{
    public long Id { get; set; }
    public long BusinessId { get; set; }
    public string Code { get; set; } = "";
    public string Name { get; set; } = "";
    public string NameKey { get; set; } = "";
    public bool IsActive { get; set; } = true;
}

public sealed class RetailProduct
{
    public long Id { get; set; }
    public long BusinessId { get; set; }
    public long? GlobalProductId { get; set; }
    public long CategoryId { get; set; }
    public long UnitId { get; set; }
    public long? BrandId { get; set; }
    public string Name { get; set; } = "";
    public string Sku { get; set; } = "";
    public string? Barcode { get; set; }
    public decimal SalePrice { get; set; }
    public decimal? ReorderLevel { get; set; }
    public string TaxClassification { get; set; } = "";
    public bool IsActive { get; set; } = true;
    public List<RetailProductUnit> Conversions { get; set; } = [];
}

public sealed class RetailProductUnit
{
    public long ProductId { get; set; }
    public long UnitId { get; set; }
    public decimal Factor { get; set; }
    public decimal? SalePrice { get; set; }
}

public sealed class RetailSupplier
{
    public long Id { get; set; }
    public long BusinessId { get; set; }
    public string Name { get; set; } = "";
    public string NameKey { get; set; } = "";
    public string Phone { get; set; } = "";
    public string Email { get; set; } = "";
    public bool IsActive { get; set; } = true;
}

public sealed class RetailCustomer
{
    public decimal CreditLimit { get; set; }
    public string? Address { get; set; }
    public long Id { get; set; }
    public long BusinessId { get; set; }
    public string Name { get; set; } = "";
    public string NameKey { get; set; } = "";
    public string Phone { get; set; } = "";
    public string Email { get; set; } = "";
    public bool IsActive { get; set; } = true;
}

public sealed class RetailStockBalance
{
    public long Id { get; set; }
    public long BusinessId { get; set; }
    public long LocationId { get; set; }
    public long ProductId { get; set; }
    public decimal Quantity { get; set; }
}

public sealed class RetailStockMovement
{
    public long Id { get; set; }
    public long BusinessId { get; set; }
    public long LocationId { get; set; }
    public long ProductId { get; set; }
    public long ActorUserId { get; set; }
    public string SourceType { get; set; } = "";
    public long? SourceId { get; set; }
    public string? Reason { get; set; }
    public decimal QuantityChange { get; set; }
    public decimal BalanceAfter { get; set; }
    public DateTime PostedAtUtc { get; set; } = DateTime.UtcNow;
}

public sealed class RetailStockAdjustment
{
    public long Id { get; set; }
    public long BusinessId { get; set; }
    public long LocationId { get; set; }
    public long ProductId { get; set; }
    public long RequestedByUserId { get; set; }
    public long? ReviewedByUserId { get; set; }
    public long? PostedByUserId { get; set; }
    public decimal SystemQuantity { get; set; }
    public decimal CountedQuantity { get; set; }
    public string Reason { get; set; } = "";
    public string? Remarks { get; set; }
    public string? ReviewRemarks { get; set; }
    public string Status { get; set; } = "Pending Approval";
    public DateTime RequestedAtUtc { get; set; } = DateTime.UtcNow;
    public DateTime? ReviewedAtUtc { get; set; }
    public DateTime? PostedAtUtc { get; set; }
}

public sealed class RetailPurchaseOrder
{
    public long Id { get; set; }
    public string? CreationKey { get; set; }
    public long BusinessId { get; set; }
    public long LocationId { get; set; }
    public long SupplierId { get; set; }
    public long CreatedByUserId { get; set; }
    public long? ApprovedByUserId { get; set; }
    public string Status { get; set; } = "Draft";
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
    public DateTime? ApprovedAtUtc { get; set; }
}

public sealed class RetailPurchaseOrderLine
{
    public long Id { get; set; }
    public long PurchaseOrderId { get; set; }
    public long ProductId { get; set; }
    public decimal OrderedQuantity { get; set; }
    public decimal UnitCost { get; set; }
    public long? SelectedUnitId { get; set; }
    public string? SelectedUnitCode { get; set; }
    public decimal? ConversionFactor { get; set; }
}

public sealed class RetailReceiving
{
    public long Id { get; set; }
    public string? CreationKey { get; set; }
    public long BusinessId { get; set; }
    public long PurchaseOrderId { get; set; }
    public long CreatedByUserId { get; set; }
    public long? PostedByUserId { get; set; }
    public string Status { get; set; } = "Draft";
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
    public DateTime? PostedAtUtc { get; set; }
}

public sealed class RetailReceivingLine
{
    public long Id { get; set; }
    public long ReceivingId { get; set; }
    public long PurchaseOrderLineId { get; set; }
    public decimal Quantity { get; set; }
}

public sealed class RetailPurchaseReturn
{
    public long Id { get; set; }
    public long BusinessId { get; set; }
    public long ReceivingLineId { get; set; }
    public long ActorUserId { get; set; }
    public decimal Quantity { get; set; }
    public string RequestKey { get; set; } = "";
    public string Reason { get; set; } = "";
    public DateTime PostedAtUtc { get; set; } = DateTime.UtcNow;
}

public sealed class RetailSupplierDocument
{
    public long Id { get; set; }
    public long BusinessId { get; set; }
    public long SupplierId { get; set; }
    public long ReceivingId { get; set; }
    public long ActorUserId { get; set; }
    public long? PurchaseReturnId { get; set; }
    public string Kind { get; set; } = "";
    public string CurrencyCode { get; set; } = "";
    public decimal Amount { get; set; }
    public string Reference { get; set; } = "";
    public string RequestKey { get; set; } = "";
    public DateTime PostedAtUtc { get; set; } = DateTime.UtcNow;
}

public sealed class RetailStockTransfer
{
    public long Id { get; set; }
    public long BusinessId { get; set; }
    public long FromLocationId { get; set; }
    public long ToLocationId { get; set; }
    public long RequestedByUserId { get; set; }
    public long? ApprovedByUserId { get; set; }
    public long? DispatchedByUserId { get; set; }
    public string RequestKey { get; set; } = "";
    public string Status { get; set; } = "";
    public string Reason { get; set; } = "";
    public DateTime RequestedAtUtc { get; set; }
    public DateTime? ApprovedAtUtc { get; set; }
    public DateTime? DispatchedAtUtc { get; set; }
}

public sealed class RetailStockTransferLine
{
    public long Id { get; set; }
    public long TransferId { get; set; }
    public long ProductId { get; set; }
    public decimal Quantity { get; set; }
    public string UnitCode { get; set; } = "";
}

public sealed class RetailStockTransferReceipt
{
    public long Id { get; set; }
    public long TransferLineId { get; set; }
    public long BusinessId { get; set; }
    public long ActorUserId { get; set; }
    public decimal Quantity { get; set; }
    public string Kind { get; set; } = "";
    public string RequestKey { get; set; } = "";
    public string Reason { get; set; } = "";
    public DateTime PostedAtUtc { get; set; }
}

public sealed class AppDbContext(DbContextOptions<AppDbContext> options)
    : IdentityUserContext<AppUser, long>(options)
{
    public DbSet<PlatformAuditEvent> PlatformAuditEvents => Set<PlatformAuditEvent>();
    public DbSet<PlatformOwnerInvitation> PlatformOwnerInvitations => Set<PlatformOwnerInvitation>();
    public DbSet<PlatformModulePolicy> PlatformModulePolicies => Set<PlatformModulePolicy>();
    public DbSet<PlatformMaintenanceWindow> PlatformMaintenanceWindows => Set<PlatformMaintenanceWindow>();
    public DbSet<PlatformAnnouncement> PlatformAnnouncements => Set<PlatformAnnouncement>();
    public DbSet<BusinessType> BusinessTypes => Set<BusinessType>();
    public DbSet<Business> Businesses => Set<Business>();
    public DbSet<BusinessUser> BusinessUsers => Set<BusinessUser>();
    public DbSet<BusinessSubscription> BusinessSubscriptions => Set<BusinessSubscription>();
    public DbSet<SubscriptionPlanDraft> SubscriptionPlanDrafts => Set<SubscriptionPlanDraft>();
    public DbSet<PublishedPlanVersion> PublishedPlanVersions => Set<PublishedPlanVersion>();
    public DbSet<SubscriptionBillingDraft> SubscriptionBillingDrafts => Set<SubscriptionBillingDraft>();
    public DbSet<SubscriptionPaymentRequest> SubscriptionPaymentRequests => Set<SubscriptionPaymentRequest>();
    public DbSet<SubscriptionPayment> SubscriptionPayments => Set<SubscriptionPayment>();
    public DbSet<SubscriptionRefund> SubscriptionRefunds => Set<SubscriptionRefund>();
    public DbSet<BillingTaxProfile> BillingTaxProfiles => Set<BillingTaxProfile>();
    public DbSet<BillingDocumentDraft> BillingDocumentDrafts => Set<BillingDocumentDraft>();
    public DbSet<RazorpayWebhookEvent> RazorpayWebhookEvents => Set<RazorpayWebhookEvent>();
    public DbSet<BusinessLocation> BusinessLocations => Set<BusinessLocation>();

    public DbSet<RetailCategory> RetailCategories => Set<RetailCategory>();
    public DbSet<RetailBrand> RetailBrands => Set<RetailBrand>();
    public DbSet<RetailUnit> RetailUnits => Set<RetailUnit>();
    public DbSet<RetailCashSale> RetailCashSales => Set<RetailCashSale>();
    public DbSet<RetailCashReturn> RetailCashReturns => Set<RetailCashReturn>();
    public DbSet<RetailCreditPayment> RetailCreditPayments => Set<RetailCreditPayment>();
    public DbSet<RetailCashRegisterSession> RetailCashRegisterSessions => Set<RetailCashRegisterSession>();
    public DbSet<RetailCashRegisterMovement> RetailCashRegisterMovements => Set<RetailCashRegisterMovement>();
    public DbSet<GlobalProduct> GlobalProducts => Set<GlobalProduct>();
    public DbSet<RetailProduct> RetailProducts => Set<RetailProduct>();
    public DbSet<RetailProductUnit> RetailProductUnits => Set<RetailProductUnit>();
    public DbSet<RetailSupplier> RetailSuppliers => Set<RetailSupplier>();
    public DbSet<RetailCustomer> RetailCustomers => Set<RetailCustomer>();
    public DbSet<RetailStockBalance> RetailStockBalances => Set<RetailStockBalance>();
    public DbSet<RetailStockMovement> RetailStockMovements => Set<RetailStockMovement>();
    public DbSet<RetailStockTransfer> RetailStockTransfers => Set<RetailStockTransfer>();
    public DbSet<RetailStockTransferLine> RetailStockTransferLines => Set<RetailStockTransferLine>();
    public DbSet<RetailStockTransferReceipt> RetailStockTransferReceipts => Set<RetailStockTransferReceipt>();
    public DbSet<RetailStockAdjustment> RetailStockAdjustments => Set<RetailStockAdjustment>();
    public DbSet<RetailPurchaseOrder> RetailPurchaseOrders => Set<RetailPurchaseOrder>();
    public DbSet<RetailPurchaseOrderLine> RetailPurchaseOrderLines => Set<RetailPurchaseOrderLine>();
    public DbSet<RetailReceiving> RetailReceivings => Set<RetailReceiving>();
    public DbSet<RetailReceivingLine> RetailReceivingLines => Set<RetailReceivingLine>();
    public DbSet<RetailPurchaseReturn> RetailPurchaseReturns => Set<RetailPurchaseReturn>();
    public DbSet<RetailSupplierDocument> RetailSupplierDocuments => Set<RetailSupplierDocument>();

    protected override void OnModelCreating(ModelBuilder builder)
    {
        base.OnModelCreating(builder);

        builder.Entity<PlatformOwnerInvitation>(entity =>
        {
            entity.Property(x => x.Email).HasMaxLength(254).IsRequired();
            entity.Property(x => x.NormalizedEmail).HasMaxLength(254).IsRequired();
            entity.Property(x => x.TokenHash).HasMaxLength(64).IsRequired();
            entity.Property(x => x.Reason).HasMaxLength(200).IsRequired();
            entity.HasIndex(x => x.TokenHash).IsUnique();
            entity.HasIndex(x => new { x.NormalizedEmail, x.CreatedAtUtc });
            entity.HasOne<AppUser>().WithMany()
                .HasForeignKey(x => x.CreatedByUserId).OnDelete(DeleteBehavior.Restrict);
        });

        builder.Entity<PlatformAuditEvent>(entity =>
        {
            entity.Property(x => x.Action).HasMaxLength(60).IsRequired();
            entity.Property(x => x.TargetType).HasMaxLength(40).IsRequired();
            entity.Property(x => x.Reason).HasMaxLength(250);
            entity.Property(x => x.PreviousValue).HasMaxLength(3000);
            entity.Property(x => x.NewValue).HasMaxLength(3000);
            entity.HasIndex(x => new { x.CreatedAtUtc, x.Id });
            entity.HasOne<AppUser>().WithMany()
                .HasForeignKey(x => x.ActorUserId).OnDelete(DeleteBehavior.Restrict);
        });

        builder.Entity<PlatformModulePolicy>(entity =>
        {
            entity.Property(x => x.Code).HasMaxLength(40).IsRequired();
            entity.Property(x => x.ReleaseState).HasMaxLength(24).IsRequired();
            entity.HasIndex(x => x.Code).IsUnique();
        });

        builder.Entity<PlatformMaintenanceWindow>(entity =>
        {
            entity.Property(x => x.Mode).HasMaxLength(20).IsRequired();
            entity.Property(x => x.Title).HasMaxLength(100).IsRequired();
            entity.Property(x => x.Message).HasMaxLength(300).IsRequired();
            entity.Property(x => x.Reason).HasMaxLength(200).IsRequired();
            entity.HasIndex(x => new { x.StartUtc, x.EndUtc });
        });

        builder.Entity<PlatformAnnouncement>(entity =>
        {
            entity.Property(x => x.Level).HasMaxLength(20).IsRequired();
            entity.Property(x => x.Title).HasMaxLength(100).IsRequired();
            entity.Property(x => x.Message).HasMaxLength(300).IsRequired();
            entity.Property(x => x.Reason).HasMaxLength(200).IsRequired();
            entity.HasIndex(x => new { x.StartUtc, x.EndUtc });
        });

        builder.Entity<BusinessType>(entity =>
        {
            entity.HasIndex(x => x.Code).IsUnique();
            entity.Property(x => x.Code).HasMaxLength(40).IsRequired();
            entity.Property(x => x.Name).HasMaxLength(100).IsRequired();
            entity.Property(x => x.ReleaseState).HasMaxLength(24).IsRequired();
            entity.HasData(new BusinessType
            {
                Id = 1,
                Code = "RETAIL",
                Name = "General Retail Shop",
                IsSupported = true
            });
        });

        builder.Entity<Business>(entity =>
        {
            entity.Property(x => x.Name).HasMaxLength(150).IsRequired();
            entity.Property(x => x.CreationKey).HasMaxLength(80).IsRequired();
            entity.Property(x => x.CountryCode).HasMaxLength(2).IsRequired();
            entity.Property(x => x.CurrencyCode).HasMaxLength(3).IsRequired();
            entity.Property(x => x.TimeZoneId).HasMaxLength(80).IsRequired();
            entity.Property(x => x.ReceiptLegalName).HasMaxLength(150);
            entity.Property(x => x.ReceiptAddress).HasMaxLength(300);
            entity.Property(x => x.ReceiptPhone).HasMaxLength(25);
            entity.Property(x => x.ReceiptEmail).HasMaxLength(254);
            entity.Property(x => x.ReceiptLogo).HasMaxLength(32768);
            entity.Property(x => x.ReceiptPaperSize).HasMaxLength(12).IsRequired();
            entity.Property(x => x.ReceiptFooterText).HasMaxLength(120).IsRequired();
            entity.HasIndex(x => new { x.CreatedByUserId, x.CreationKey }).IsUnique();
            entity.HasOne<BusinessType>().WithMany()
                .HasForeignKey(x => x.BusinessTypeId).OnDelete(DeleteBehavior.Restrict);
            entity.HasOne<AppUser>().WithMany()
                .HasForeignKey(x => x.CreatedByUserId).OnDelete(DeleteBehavior.Restrict);
        });

        builder.Entity<BusinessUser>(entity =>
        {
            entity.HasIndex(x => new { x.BusinessId, x.UserId }).IsUnique();
            entity.Property(x => x.RoleCode).HasMaxLength(20).IsRequired();
            entity.HasOne<Business>().WithMany()
                .HasForeignKey(x => x.BusinessId).OnDelete(DeleteBehavior.Cascade);
            entity.HasOne<AppUser>().WithMany()
                .HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.Restrict);
        });

        builder.Entity<SubscriptionPlanDraft>(entity =>
        {
            entity.HasIndex(x => x.PlanCode).IsUnique();
            entity.Property(x => x.PlanCode).HasMaxLength(30).IsRequired();
            entity.Property(x => x.CurrencyCode).HasMaxLength(3);
            entity.Property(x => x.MonthlyPrice).HasPrecision(18, 2);
            entity.Property(x => x.QuarterlyPrice).HasPrecision(18, 2);
            entity.Property(x => x.YearlyPrice).HasPrecision(18, 2);
            entity.HasOne<AppUser>().WithMany()
                .HasForeignKey(x => x.UpdatedByUserId).OnDelete(DeleteBehavior.Restrict);
        });

        builder.Entity<PublishedPlanVersion>(entity =>
        {
            entity.HasIndex(x => new { x.PlanCode, x.Version }).IsUnique();
            entity.Property(x => x.PlanCode).HasMaxLength(30).IsRequired();
            entity.Property(x => x.CurrencyCode).HasMaxLength(3).IsRequired();
            entity.Property(x => x.MonthlyPrice).HasPrecision(18, 2);
            entity.Property(x => x.QuarterlyPrice).HasPrecision(18, 2);
            entity.Property(x => x.YearlyPrice).HasPrecision(18, 2);
            entity.HasOne<AppUser>().WithMany()
                .HasForeignKey(x => x.PublishedByUserId).OnDelete(DeleteBehavior.Restrict);
        });

        builder.Entity<SubscriptionBillingDraft>(entity =>
        {
            entity.Property(x => x.PlanCode).HasMaxLength(30).IsRequired();
            entity.Property(x => x.BillingCycle).HasMaxLength(12).IsRequired();
            entity.Property(x => x.CurrencyCode).HasMaxLength(3).IsRequired();
            entity.Property(x => x.RequestKey).HasMaxLength(80).IsRequired();
            entity.Property(x => x.Amount).HasPrecision(18, 2);
            entity.HasIndex(x => new { x.BusinessId, x.RequestKey }).IsUnique();
            entity.HasIndex(x => new { x.BusinessId, x.PeriodStartDate })
                .IsUnique().HasFilter("[PeriodStartDate] IS NOT NULL");
            entity.Property(x => x.PeriodStartDate).HasColumnType("date");
            entity.Property(x => x.PeriodEndDate).HasColumnType("date");
            entity.HasIndex(x => new { x.CreatedAtUtc, x.Id });
            entity.HasOne<Business>().WithMany()
                .HasForeignKey(x => x.BusinessId).OnDelete(DeleteBehavior.Restrict);
            entity.HasOne<PublishedPlanVersion>().WithMany()
                .HasForeignKey(x => x.PublishedPlanVersionId).OnDelete(DeleteBehavior.Restrict);
            entity.HasOne<AppUser>().WithMany()
                .HasForeignKey(x => x.CreatedByUserId).OnDelete(DeleteBehavior.Restrict);
        });

        builder.Entity<SubscriptionPaymentRequest>(entity =>
        {
            entity.Property(x => x.CurrencyCode).HasMaxLength(3).IsRequired();
            entity.Property(x => x.Amount).HasPrecision(18, 2);
            entity.Property(x => x.RequestKey).HasMaxLength(80).IsRequired();
            entity.Property(x => x.Status).HasMaxLength(24).IsRequired();
            entity.Property(x => x.ProviderOrderId).HasMaxLength(80);
            entity.HasIndex(x => x.ProviderOrderId).IsUnique()
                .HasFilter("[ProviderOrderId] IS NOT NULL");
            entity.HasIndex(x => x.BillingDraftId).IsUnique();
            entity.HasIndex(x => new { x.BusinessId, x.RequestKey }).IsUnique();
            entity.HasIndex(x => new { x.CreatedAtUtc, x.Id });
            entity.HasOne<SubscriptionBillingDraft>().WithMany()
                .HasForeignKey(x => x.BillingDraftId).OnDelete(DeleteBehavior.Restrict);
            entity.HasOne<Business>().WithMany()
                .HasForeignKey(x => x.BusinessId).OnDelete(DeleteBehavior.Restrict);
            entity.HasOne<AppUser>().WithMany()
                .HasForeignKey(x => x.CreatedByUserId).OnDelete(DeleteBehavior.Restrict);
        });

        builder.Entity<SubscriptionPayment>(entity =>
        {
            entity.Property(x => x.ProviderPaymentId).HasMaxLength(80).IsRequired();
            entity.Property(x => x.ProviderOrderId).HasMaxLength(80).IsRequired();
            entity.Property(x => x.CurrencyCode).HasMaxLength(3).IsRequired();
            entity.Property(x => x.Amount).HasPrecision(18, 2);
            entity.Property(x => x.ActivationStatus).HasMaxLength(24).IsRequired();
            entity.HasIndex(x => x.PaymentRequestId).IsUnique();
            entity.HasIndex(x => x.ProviderPaymentId).IsUnique();
            entity.HasOne<SubscriptionPaymentRequest>().WithMany()
                .HasForeignKey(x => x.PaymentRequestId).OnDelete(DeleteBehavior.Restrict);
        });

        builder.Entity<SubscriptionRefund>(entity =>
        {
            entity.Property(x => x.ProviderRefundId).HasMaxLength(80).IsRequired();
            entity.Property(x => x.CurrencyCode).HasMaxLength(3).IsRequired();
            entity.Property(x => x.Status).HasMaxLength(24).IsRequired();
            entity.Property(x => x.Amount).HasPrecision(18, 2);
            entity.HasIndex(x => x.ProviderRefundId).IsUnique();
            entity.HasIndex(x => x.SubscriptionPaymentId);
            entity.HasOne<SubscriptionPayment>().WithMany()
                .HasForeignKey(x => x.SubscriptionPaymentId).OnDelete(DeleteBehavior.Restrict);
        });

        builder.Entity<RazorpayWebhookEvent>(entity =>
        {
            entity.Property(x => x.EventId).HasMaxLength(100).IsRequired();
            entity.HasIndex(x => x.EventId).IsUnique();
            entity.HasOne<SubscriptionPayment>().WithMany()
                .HasForeignKey(x => x.SubscriptionPaymentId).OnDelete(DeleteBehavior.Restrict);
        });

        builder.Entity<BillingTaxProfile>(entity =>
        {
            entity.Property(x => x.IssuerName).HasMaxLength(160).IsRequired();
            entity.Property(x => x.IssuerAddress).HasMaxLength(300).IsRequired();
            entity.Property(x => x.IssuerGstin).HasMaxLength(15).IsRequired();
            entity.Property(x => x.IssuerStateCode).HasMaxLength(2).IsRequired();
            entity.Property(x => x.RecipientName).HasMaxLength(160).IsRequired();
            entity.Property(x => x.RecipientAddress).HasMaxLength(300).IsRequired();
            entity.Property(x => x.RecipientGstin).HasMaxLength(15);
            entity.Property(x => x.RecipientStateCode).HasMaxLength(2).IsRequired();
            entity.Property(x => x.ServiceCode).HasMaxLength(8).IsRequired();
            entity.Property(x => x.ServiceDescription).HasMaxLength(160).IsRequired();
            entity.Property(x => x.InvoiceSeries).HasMaxLength(5).IsRequired();
            entity.Property(x => x.GstRate).HasPrecision(5, 2);
            entity.HasIndex(x => x.BusinessId).IsUnique();
            entity.HasOne<Business>().WithMany()
                .HasForeignKey(x => x.BusinessId).OnDelete(DeleteBehavior.Restrict);
            entity.HasOne<AppUser>().WithMany()
                .HasForeignKey(x => x.UpdatedByUserId).OnDelete(DeleteBehavior.Restrict);
        });

        builder.Entity<BillingDocumentDraft>(entity =>
        {
            entity.Property(x => x.DocumentType).HasMaxLength(20).IsRequired();
            entity.Property(x => x.Status).HasMaxLength(20).IsRequired();
            entity.Property(x => x.DocumentNumber).HasMaxLength(16);
            entity.Property(x => x.TaxSnapshotJson).HasMaxLength(2000).IsRequired();
            entity.Property(x => x.CurrencyCode).HasMaxLength(3).IsRequired();
            entity.Property(x => x.GrossAmount).HasPrecision(18, 2);
            entity.Property(x => x.TaxableAmount).HasPrecision(18, 2);
            entity.Property(x => x.CgstAmount).HasPrecision(18, 2);
            entity.Property(x => x.SgstAmount).HasPrecision(18, 2);
            entity.Property(x => x.IgstAmount).HasPrecision(18, 2);
            entity.HasIndex(x => x.BusinessId);
            entity.HasIndex(x => new { x.SubscriptionPaymentId, x.DocumentType }).IsUnique().HasFilter("[DocumentType] = 'Invoice'");
            entity.HasIndex(x => x.SubscriptionRefundId).IsUnique().HasFilter("[SubscriptionRefundId] IS NOT NULL");
            entity.HasIndex(x => x.OriginalDocumentId);
            entity.HasOne<Business>().WithMany()
                .HasForeignKey(x => x.BusinessId).OnDelete(DeleteBehavior.Restrict);
            entity.HasOne<SubscriptionPayment>().WithMany()
                .HasForeignKey(x => x.SubscriptionPaymentId).OnDelete(DeleteBehavior.Restrict);
            entity.HasOne<SubscriptionRefund>().WithMany()
                .HasForeignKey(x => x.SubscriptionRefundId).OnDelete(DeleteBehavior.Restrict);
            entity.HasOne<BillingDocumentDraft>().WithMany()
                .HasForeignKey(x => x.OriginalDocumentId).OnDelete(DeleteBehavior.Restrict);
            entity.HasOne<BillingTaxProfile>().WithMany()
                .HasForeignKey(x => x.BillingTaxProfileId).OnDelete(DeleteBehavior.Restrict);
            entity.HasOne<AppUser>().WithMany()
                .HasForeignKey(x => x.CreatedByUserId).OnDelete(DeleteBehavior.Restrict);
        });

        builder.Entity<BusinessSubscription>(entity =>
        {
            entity.HasIndex(x => x.BusinessId).IsUnique();
            entity.Property(x => x.PlanCode).HasMaxLength(30).IsRequired();
            entity.Property(x => x.Status).HasMaxLength(20).IsRequired();
            entity.Property(x => x.StatusBeforeSuspension).HasMaxLength(20);
            // SQL Server datetime2 and SQLite do not store DateTimeKind. These values are UTC.
            entity.Property(x => x.TrialEndsAtUtc).HasConversion(
                value => value, value => DateTime.SpecifyKind(value, DateTimeKind.Utc));
            entity.Property(x => x.PaidStartsAtUtc).HasConversion(
                value => value, value => value.HasValue
                    ? DateTime.SpecifyKind(value.Value, DateTimeKind.Utc) : null);
            entity.Property(x => x.PaidEndsAtUtc).HasConversion(
                value => value, value => value.HasValue
                    ? DateTime.SpecifyKind(value.Value, DateTimeKind.Utc) : null);
            entity.HasOne<PublishedPlanVersion>().WithMany()
                .HasForeignKey(x => x.PaidPlanVersionId).OnDelete(DeleteBehavior.Restrict);
            entity.HasOne<Business>().WithMany()
                .HasForeignKey(x => x.BusinessId).OnDelete(DeleteBehavior.Restrict);
        });

        builder.Entity<BusinessLocation>(entity =>
        {
            entity.HasIndex(x => new { x.BusinessId, x.Code }).IsUnique();
            entity.Property(x => x.Code).HasMaxLength(30).IsRequired();
            entity.Property(x => x.Name).HasMaxLength(120).IsRequired();
            entity.HasOne<Business>().WithMany()
                .HasForeignKey(x => x.BusinessId).OnDelete(DeleteBehavior.Cascade);
        });
        builder.Entity<RetailCategory>(entity =>
        {
            entity.Property(x => x.Name).HasMaxLength(80).IsRequired();
            entity.Property(x => x.NameKey).HasMaxLength(80).IsRequired();
            entity.HasIndex(x => new { x.BusinessId, x.NameKey }).IsUnique();
            entity.HasOne<Business>().WithMany()
                .HasForeignKey(x => x.BusinessId).OnDelete(DeleteBehavior.Cascade);
            entity.HasOne<RetailCategory>().WithMany()
                .HasForeignKey(x => x.ParentId).OnDelete(DeleteBehavior.Restrict);
        });

        builder.Entity<RetailBrand>(entity =>
        {
            entity.Property(x => x.Name).HasMaxLength(80).IsRequired();
            entity.Property(x => x.NameKey).HasMaxLength(80).IsRequired();
            entity.HasIndex(x => new { x.BusinessId, x.NameKey }).IsUnique();
            entity.HasOne<Business>().WithMany()
                .HasForeignKey(x => x.BusinessId).OnDelete(DeleteBehavior.Cascade);
        });

        builder.Entity<RetailUnit>(entity =>
        {
            entity.Property(x => x.Code).HasMaxLength(12).IsRequired();
            entity.Property(x => x.Name).HasMaxLength(60).IsRequired();
            entity.Property(x => x.NameKey).HasMaxLength(60).IsRequired();
            entity.HasIndex(x => new { x.BusinessId, x.Code }).IsUnique();
            entity.HasIndex(x => new { x.BusinessId, x.NameKey }).IsUnique();
            entity.HasOne<Business>().WithMany()
                .HasForeignKey(x => x.BusinessId).OnDelete(DeleteBehavior.Cascade);
        });

        builder.Entity<GlobalProduct>(entity =>
        {
            entity.Property(x => x.Revision).IsConcurrencyToken();
            entity.Property(x => x.Name).HasMaxLength(150).IsRequired();
            entity.Property(x => x.NameKey).HasMaxLength(150).IsRequired();
            entity.Property(x => x.Barcode).HasMaxLength(14);
            entity.Property(x => x.Brand).HasMaxLength(80).IsRequired();
            entity.Property(x => x.Category).HasMaxLength(80).IsRequired();
            entity.Property(x => x.PackSize).HasMaxLength(40).IsRequired();
            entity.Property(x => x.Unit).HasMaxLength(20).IsRequired();
            entity.HasIndex(x => x.Barcode).IsUnique().HasFilter("[Barcode] IS NOT NULL");
            entity.HasIndex(x => new { x.NameKey, x.Id });
            entity.Property<string>("BrandKey").HasMaxLength(80)
                .HasComputedColumnSql("UPPER([Brand])", stored: true);
            entity.Property<string>("PackSizeKey").HasMaxLength(40)
                .HasComputedColumnSql("UPPER([PackSize])", stored: true);
            entity.HasIndex("NameKey", "BrandKey", "PackSizeKey", "Unit")
                .IsUnique().HasFilter("[Barcode] IS NULL");
        });

        builder.Entity<RetailProduct>(entity =>
        {
            entity.ToTable(t => t.HasCheckConstraint("CK_RetailProducts_ReorderLevel",
                "[ReorderLevel] IS NULL OR [ReorderLevel] >= 0"));
            entity.Property(x => x.ReorderLevel).HasPrecision(18, 3);
            entity.Property(x => x.Name).HasMaxLength(150).IsRequired();
            entity.Property(x => x.Sku).HasMaxLength(50).IsRequired();
            entity.Property(x => x.Barcode).HasMaxLength(64);
            entity.Property(x => x.SalePrice).HasPrecision(18, 2);
            entity.Property(x => x.TaxClassification).HasMaxLength(30).IsRequired();
            entity.HasIndex(x => new { x.BusinessId, x.Name, x.Id });
            entity.HasIndex(x => new { x.BusinessId, x.Sku }).IsUnique();
            entity.HasIndex(x => new { x.BusinessId, x.GlobalProductId })
                .IsUnique().HasFilter("[GlobalProductId] IS NOT NULL");
            entity.HasOne<GlobalProduct>().WithMany()
                .HasForeignKey(x => x.GlobalProductId).OnDelete(DeleteBehavior.Restrict);
            entity.HasIndex(x => new { x.BusinessId, x.Barcode })
                .IsUnique().HasFilter("[Barcode] IS NOT NULL");
            entity.HasOne<Business>().WithMany()
                .HasForeignKey(x => x.BusinessId).OnDelete(DeleteBehavior.Cascade);
            entity.HasOne<RetailCategory>().WithMany()
                .HasForeignKey(x => x.CategoryId).OnDelete(DeleteBehavior.Restrict);
            entity.HasOne<RetailUnit>().WithMany()
                .HasForeignKey(x => x.UnitId).OnDelete(DeleteBehavior.Restrict);
            entity.HasOne<RetailBrand>().WithMany()
                .HasForeignKey(x => x.BrandId).OnDelete(DeleteBehavior.Restrict);
        });

        builder.Entity<RetailProductUnit>(entity =>
        {
            entity.ToTable("RetailProductUnits", t => t.HasCheckConstraint("CK_RetailProductUnits_Factor",
                "[Factor] > 0 AND [Factor] <= 9999999.999"));
            entity.HasKey(x => new { x.ProductId, x.UnitId });
            entity.Property(x => x.Factor).HasPrecision(18, 3);
            entity.Property(x => x.SalePrice).HasPrecision(18, 2);
            entity.HasOne<RetailProduct>().WithMany(x => x.Conversions)
                .HasForeignKey(x => x.ProductId).OnDelete(DeleteBehavior.Cascade);
            entity.HasOne<RetailUnit>().WithMany().HasForeignKey(x => x.UnitId).OnDelete(DeleteBehavior.Restrict);
        });

        builder.Entity<RetailSupplier>(entity =>
        {
            entity.Property(x => x.Name).HasMaxLength(120).IsRequired();
            entity.Property(x => x.NameKey).HasMaxLength(120).IsRequired();
            entity.Property(x => x.Phone).HasMaxLength(25).IsRequired();
            entity.Property(x => x.Email).HasMaxLength(160).IsRequired();
            entity.HasIndex(x => new { x.BusinessId, x.NameKey }).IsUnique();
            entity.HasOne<Business>().WithMany()
                .HasForeignKey(x => x.BusinessId).OnDelete(DeleteBehavior.Cascade);
        });

        builder.Entity<RetailCustomer>(entity =>
        {
            entity.Property(x => x.CreditLimit).HasPrecision(18, 2);
            entity.Property(x => x.Address).HasMaxLength(300);
            entity.Property(x => x.Name).HasMaxLength(120).IsRequired();
            entity.Property(x => x.NameKey).HasMaxLength(120).IsRequired();
            entity.Property(x => x.Phone).HasMaxLength(25).IsRequired();
            entity.Property(x => x.Email).HasMaxLength(160).IsRequired();
            entity.HasIndex(x => new { x.BusinessId, x.NameKey });
            entity.HasOne<Business>().WithMany()
                .HasForeignKey(x => x.BusinessId).OnDelete(DeleteBehavior.Cascade);
        });
        builder.Entity<RetailStockBalance>(entity =>
        {
            entity.ToTable(x => x.HasCheckConstraint("CK_RetailStockBalances_Quantity", "[Quantity] >= 0"));
            entity.Property(x => x.Quantity).HasPrecision(18, 3);
            entity.HasIndex(x => new { x.BusinessId, x.LocationId, x.ProductId }).IsUnique();
            entity.HasOne<Business>().WithMany()
                .HasForeignKey(x => x.BusinessId).OnDelete(DeleteBehavior.Restrict);
            entity.HasOne<BusinessLocation>().WithMany()
                .HasForeignKey(x => x.LocationId).OnDelete(DeleteBehavior.Restrict);
            entity.HasOne<RetailProduct>().WithMany()
                .HasForeignKey(x => x.ProductId).OnDelete(DeleteBehavior.Restrict);
        });

        builder.Entity<RetailCashSale>(entity =>
        {
            entity.HasOne<RetailCashRegisterSession>().WithMany().HasForeignKey(x => x.RegisterSessionId)
                .OnDelete(DeleteBehavior.Restrict);
            entity.Property(x => x.MerchantLegalName).HasMaxLength(150);
            entity.Property(x => x.MerchantAddress).HasMaxLength(300);
            entity.Property(x => x.MerchantPhone).HasMaxLength(25);
            entity.Property(x => x.MerchantEmail).HasMaxLength(254);
            entity.Property(x => x.CustomerName).HasMaxLength(120);
            entity.Property(x => x.CustomerPhone).HasMaxLength(25);
            entity.Property(x => x.CustomerAddress).HasMaxLength(300);
            entity.Property(x => x.MerchantLogo).HasMaxLength(32768);
            entity.Property(x => x.BusinessName).HasMaxLength(150);
            entity.Property(x => x.LocationName).HasMaxLength(120);
            entity.Property(x => x.TimeZoneId).HasMaxLength(80);
            entity.Property(x => x.RequestKey).HasMaxLength(80);
            entity.Property(x => x.CurrencyCode).HasMaxLength(3);
            entity.Property(x => x.Total).HasPrecision(18, 2);
            entity.Property(x => x.Tendered).HasPrecision(18, 2);
            entity.HasIndex(x => new { x.BusinessId, x.PostedAtUtc, x.Id });
            entity.HasIndex(x => new { x.BusinessId, x.CustomerId, x.CreditDueDate });
            entity.HasIndex(x => new { x.BusinessId, x.CashierId, x.RequestKey }).IsUnique();
            entity.HasOne<Business>().WithMany().HasForeignKey(x => x.BusinessId).OnDelete(DeleteBehavior.Restrict);
            entity.HasOne<BusinessLocation>().WithMany().HasForeignKey(x => x.LocationId).OnDelete(DeleteBehavior.Restrict);
            entity.HasOne<AppUser>().WithMany().HasForeignKey(x => x.CashierId).OnDelete(DeleteBehavior.Restrict);
        });

        builder.Entity<RetailCashSaleLine>(entity =>
        {
            entity.Property(x => x.ProductSku).HasMaxLength(50);
            entity.Property(x => x.UnitCode).HasMaxLength(12);
            entity.Property(x => x.BaseUnitCode).HasMaxLength(12);
            entity.Property(x => x.ConversionFactor).HasPrecision(18, 3);
            entity.Property(x => x.LineTotal).HasPrecision(18, 2);
            // SQL Server stores decimals numerically. SQLite test storage uses TEXT affinity,
            // where comparing decimal text to numeric literals would reject valid sales.
            // The API validates three-decimal quantities on both providers.
            if (Database.IsSqlServer())
                entity.ToTable(x => x.HasCheckConstraint("CK_RetailCashSaleLine_Quantity",
                    "[Quantity] >= 0.001 AND [Quantity] <= 1000000"));
            entity.Property(x => x.Quantity).HasPrecision(18, 3);
            entity.Property(x => x.ProductName).HasMaxLength(200);
            entity.Property(x => x.UnitPrice).HasPrecision(18, 2);
            entity.HasOne<RetailProduct>().WithMany().HasForeignKey(x => x.ProductId).OnDelete(DeleteBehavior.Restrict);
            entity.HasOne<RetailCashSale>().WithMany(x => x.Lines).HasForeignKey(x => x.SaleId);
        });

        builder.Entity<RetailCashRegisterMovement>(entity =>
        {
            entity.Property(x => x.RequestKey).HasMaxLength(80).IsRequired();
            entity.Property(x => x.Amount).HasPrecision(18, 2);
            entity.Property(x => x.Reason).HasMaxLength(200).IsRequired();
            entity.ToTable(t => t.HasCheckConstraint("CK_RetailCashRegisterMovements_Amount", "[Amount] > 0"));
            entity.HasIndex(x => new { x.RegisterSessionId, x.RequestKey }).IsUnique();
            entity.HasOne<RetailCashRegisterSession>().WithMany().HasForeignKey(x => x.RegisterSessionId)
                .OnDelete(DeleteBehavior.Restrict);
            entity.HasOne<AppUser>().WithMany().HasForeignKey(x => x.ActorUserId).OnDelete(DeleteBehavior.Restrict);
        });

        builder.Entity<RetailCashRegisterSession>(entity =>
        {
            entity.Property(x => x.CurrencyCode).HasMaxLength(3).IsRequired();
            entity.Property(x => x.RequestKey).HasMaxLength(80).IsRequired();
            entity.Property(x => x.OpeningCash).HasPrecision(18, 2);
            entity.Property(x => x.ClosingExpectedCash).HasPrecision(18, 2);
            entity.Property(x => x.ClosingActualCash).HasPrecision(18, 2);
            entity.Property(x => x.ClosingRequestKey).HasMaxLength(80);
            entity.Property(x => x.ClosingReason).HasMaxLength(200);
            entity.HasOne<AppUser>().WithMany().HasForeignKey(x => x.ClosedByUserId).OnDelete(DeleteBehavior.Restrict);
            entity.ToTable(t =>
            {
                t.HasCheckConstraint("CK_RetailCashRegisterSessions_AmountsAndDates",
                    "[OpeningCash] >= 0 AND ([ClosedAtUtc] IS NULL OR [ClosedAtUtc] >= [OpenedAtUtc])");
                t.HasCheckConstraint("CK_RetailCashRegisterSessions_Closing", "[ClosingExpectedCash] IS NULL AND [ClosingActualCash] IS NULL AND [ClosedByUserId] IS NULL AND [ClosingRequestKey] IS NULL AND [ClosingReason] IS NULL OR ([ClosedAtUtc] IS NOT NULL AND [ClosingExpectedCash] IS NOT NULL AND [ClosingActualCash] IS NOT NULL AND [ClosingActualCash] >= 0 AND [ClosedByUserId] IS NOT NULL AND [ClosingRequestKey] IS NOT NULL AND [ClosingReason] IS NOT NULL)");
            });
            entity.HasIndex(x => new { x.BusinessId, x.RequestKey }).IsUnique();
            entity.HasIndex(x => new { x.BusinessId, x.LocationId, x.ActorUserId })
                .IsUnique().HasFilter("[ClosedAtUtc] IS NULL");
            entity.HasOne<Business>().WithMany().HasForeignKey(x => x.BusinessId).OnDelete(DeleteBehavior.Restrict);
            entity.HasOne<BusinessLocation>().WithMany().HasForeignKey(x => x.LocationId).OnDelete(DeleteBehavior.Restrict);
            entity.HasOne<AppUser>().WithMany().HasForeignKey(x => x.ActorUserId).OnDelete(DeleteBehavior.Restrict);
        });

        builder.Entity<RetailCreditPayment>(entity =>
        {
            entity.HasOne<RetailCashRegisterSession>().WithMany().HasForeignKey(x => x.RegisterSessionId)
                .OnDelete(DeleteBehavior.Restrict);
            entity.Property(x => x.Amount).HasPrecision(18, 2);
            entity.Property(x => x.RequestKey).HasMaxLength(80);
            entity.HasIndex(x => new { x.BusinessId, x.RequestKey }).IsUnique();
            entity.HasOne<Business>().WithMany().HasForeignKey(x => x.BusinessId).OnDelete(DeleteBehavior.Restrict);
            entity.HasOne<RetailCashSale>().WithMany().HasForeignKey(x => x.SaleId).OnDelete(DeleteBehavior.Restrict);
            entity.HasOne<AppUser>().WithMany().HasForeignKey(x => x.ActorUserId).OnDelete(DeleteBehavior.Restrict);
        });
        builder.Entity<RetailCashReturn>(entity =>
        {
            entity.HasOne<RetailCashRegisterSession>().WithMany().HasForeignKey(x => x.RegisterSessionId)
                .OnDelete(DeleteBehavior.Restrict);
            entity.Property(x => x.RequestKey).HasMaxLength(80);
            entity.Property(x => x.Reason).HasMaxLength(200);
            entity.Property(x => x.Total).HasPrecision(18, 2);
            entity.Property(x => x.NetCashSettlement).HasPrecision(18, 2);
            entity.ToTable(t => t.HasCheckConstraint("CK_RetailCashReturns_ExchangeLink",
                "([ReplacementSaleId] IS NULL AND [NetCashSettlement] IS NULL) OR ([ReplacementSaleId] IS NOT NULL AND [NetCashSettlement] IS NOT NULL AND [ReplacementSaleId] <> [SaleId])"));
            entity.HasIndex(x => x.ReplacementSaleId).IsUnique().HasFilter("[ReplacementSaleId] IS NOT NULL");
            entity.HasIndex(x => new { x.BusinessId, x.PostedAtUtc, x.Id });
            entity.HasIndex(x => new { x.BusinessId, x.RequestKey }).IsUnique();
            entity.HasOne<Business>().WithMany().HasForeignKey(x => x.BusinessId).OnDelete(DeleteBehavior.Restrict);
            entity.HasOne<RetailCashSale>().WithMany().HasForeignKey(x => x.SaleId).OnDelete(DeleteBehavior.Restrict);
            entity.HasOne<RetailCashSale>().WithMany().HasForeignKey(x => x.ReplacementSaleId).OnDelete(DeleteBehavior.Restrict);
            entity.HasOne<AppUser>().WithMany().HasForeignKey(x => x.ActorUserId).OnDelete(DeleteBehavior.Restrict);
        });
        builder.Entity<RetailCashReturnLine>(entity =>
        {
            entity.ToTable("RetailCashReturnLines");
            entity.Property(x => x.Quantity).HasPrecision(18, 3);
            entity.Property(x => x.Amount).HasPrecision(18, 2);
            entity.HasIndex(x => new { x.ReturnId, x.SaleLineId }).IsUnique();
            entity.HasOne<RetailCashReturn>().WithMany(x => x.Lines).HasForeignKey(x => x.ReturnId)
                .OnDelete(DeleteBehavior.Restrict);
            entity.HasOne<RetailCashSaleLine>().WithMany().HasForeignKey(x => x.SaleLineId)
                .OnDelete(DeleteBehavior.Restrict);
        });

        builder.Entity<RetailStockTransfer>(entity =>
        {
            entity.Property(x => x.RequestKey).HasMaxLength(80).IsRequired();
            entity.Property(x => x.Status).HasMaxLength(20).IsRequired();
            entity.Property(x => x.Reason).HasMaxLength(200).IsRequired();
            entity.HasIndex(x => new { x.BusinessId, x.RequestKey }).IsUnique();
            entity.HasIndex(x => new { x.BusinessId, x.FromLocationId, x.Id });
            entity.HasIndex(x => new { x.BusinessId, x.ToLocationId, x.Id });
            entity.HasIndex(x => x.FromLocationId);
            entity.HasIndex(x => x.ToLocationId);
            entity.HasIndex(x => x.RequestedByUserId);
            entity.HasIndex(x => x.ApprovedByUserId);
            entity.HasIndex(x => x.DispatchedByUserId);
            entity.HasOne<Business>().WithMany().HasForeignKey(x => x.BusinessId).OnDelete(DeleteBehavior.Restrict);
            entity.HasOne<BusinessLocation>().WithMany().HasForeignKey(x => x.FromLocationId).OnDelete(DeleteBehavior.Restrict);
            entity.HasOne<BusinessLocation>().WithMany().HasForeignKey(x => x.ToLocationId).OnDelete(DeleteBehavior.Restrict);
            entity.HasOne<AppUser>().WithMany().HasForeignKey(x => x.RequestedByUserId).OnDelete(DeleteBehavior.Restrict);
            entity.HasOne<AppUser>().WithMany().HasForeignKey(x => x.ApprovedByUserId).OnDelete(DeleteBehavior.Restrict);
            entity.HasOne<AppUser>().WithMany().HasForeignKey(x => x.DispatchedByUserId).OnDelete(DeleteBehavior.Restrict);
            entity.ToTable(t => t.HasCheckConstraint("CK_RetailStockTransfers_Valid", "[FromLocationId] <> [ToLocationId] AND [Status] IN ('Draft','Requested','Approved','InTransit','Completed','Cancelled')"));
        });

        builder.Entity<RetailStockTransferLine>(entity =>
        {
            entity.Property(x => x.Quantity) .HasPrecision(18, 3);
            entity.Property(x => x.UnitCode).HasMaxLength(12).IsRequired();
            entity.HasIndex(x => new { x.TransferId, x.ProductId }).IsUnique();
            entity.HasIndex(x => x.ProductId);
            entity.HasOne<RetailStockTransfer>().WithMany().HasForeignKey(x => x.TransferId).OnDelete(DeleteBehavior.Restrict);
            entity.HasOne<RetailProduct>().WithMany().HasForeignKey(x => x.ProductId).OnDelete(DeleteBehavior.Restrict);
            entity.ToTable(t => t.HasCheckConstraint("CK_RetailStockTransferLines_Valid", "[Quantity] > 0"));
        });

        builder.Entity<RetailStockTransferReceipt>(entity =>
        {
            entity.Property(x => x.Quantity) .HasPrecision(18, 3);
            entity.Property(x => x.Kind).HasMaxLength(12).IsRequired();
            entity.Property(x => x.RequestKey).HasMaxLength(80).IsRequired();
            entity.Property(x => x.Reason).HasMaxLength(200).IsRequired();
            entity.HasIndex(x => new { x.BusinessId, x.RequestKey }).IsUnique();
            entity.HasIndex(x => x.TransferLineId);
            entity.HasIndex(x => x.ActorUserId);
            entity.HasOne<RetailStockTransferLine>().WithMany().HasForeignKey(x => x.TransferLineId).OnDelete(DeleteBehavior.Restrict);
            entity.HasOne<Business>().WithMany().HasForeignKey(x => x.BusinessId).OnDelete(DeleteBehavior.Restrict);
            entity.HasOne<AppUser>().WithMany().HasForeignKey(x => x.ActorUserId).OnDelete(DeleteBehavior.Restrict);
            entity.ToTable(t => t.HasCheckConstraint("CK_RetailStockTransferReceipts_Valid", "[Quantity] > 0 AND [Kind] IN ('Received','Damaged','Missing','Returned')"));
        });

        builder.Entity<RetailStockMovement>(entity =>
        {
            entity.Property(x => x.SourceType).HasMaxLength(30).IsRequired();
            entity.Property(x => x.Reason).HasMaxLength(200);
            entity.Property(x => x.QuantityChange).HasPrecision(18, 3);
            entity.Property(x => x.BalanceAfter).HasPrecision(18, 3);
            entity.HasIndex(x => new { x.BusinessId, x.LocationId, x.ProductId, x.Id });
            entity.HasIndex(x => new { x.BusinessId, x.LocationId, x.ProductId, x.SourceType })
                .IsUnique().HasFilter("[SourceType] = 'Opening'");
            entity.HasIndex(x => new { x.BusinessId, x.SourceType, x.SourceId })
                .IsUnique().HasFilter("[SourceId] IS NOT NULL");
            entity.HasOne<Business>().WithMany()
                .HasForeignKey(x => x.BusinessId).OnDelete(DeleteBehavior.Restrict);
            entity.HasOne<BusinessLocation>().WithMany()
                .HasForeignKey(x => x.LocationId).OnDelete(DeleteBehavior.Restrict);
            entity.HasOne<RetailProduct>().WithMany()
                .HasForeignKey(x => x.ProductId).OnDelete(DeleteBehavior.Restrict);
            entity.HasOne<AppUser>().WithMany()
                .HasForeignKey(x => x.ActorUserId).OnDelete(DeleteBehavior.Restrict);
        });
        builder.Entity<RetailStockAdjustment>(entity =>
        {
            entity.Property(x => x.SystemQuantity).HasPrecision(18, 3);
            entity.Property(x => x.CountedQuantity).HasPrecision(18, 3);
            entity.Property(x => x.Reason).HasMaxLength(80).IsRequired();
            entity.Property(x => x.Remarks).HasMaxLength(500);
            entity.Property(x => x.ReviewRemarks).HasMaxLength(500);
            entity.Property(x => x.Status).HasMaxLength(25).IsRequired();
            entity.HasIndex(x => new { x.BusinessId, x.LocationId, x.Id });
            entity.HasOne<Business>().WithMany()
                .HasForeignKey(x => x.BusinessId).OnDelete(DeleteBehavior.Restrict);
            entity.HasOne<BusinessLocation>().WithMany()
                .HasForeignKey(x => x.LocationId).OnDelete(DeleteBehavior.Restrict);
            entity.HasOne<RetailProduct>().WithMany()
                .HasForeignKey(x => x.ProductId).OnDelete(DeleteBehavior.Restrict);
            entity.HasOne<AppUser>().WithMany()
                .HasForeignKey(x => x.RequestedByUserId).OnDelete(DeleteBehavior.Restrict);
            entity.HasOne<AppUser>().WithMany()
                .HasForeignKey(x => x.ReviewedByUserId).OnDelete(DeleteBehavior.Restrict);
            entity.HasOne<AppUser>().WithMany()
                .HasForeignKey(x => x.PostedByUserId).OnDelete(DeleteBehavior.Restrict);
        });
        builder.Entity<RetailPurchaseOrder>(entity =>
        {
            entity.Property(x => x.Status).HasMaxLength(20).IsRequired();
            entity.Property(x => x.CreationKey).HasMaxLength(80);
            entity.HasIndex(x => new { x.BusinessId, x.Status });
            entity.HasIndex(x => new { x.BusinessId, x.CreatedByUserId, x.CreationKey })
                .IsUnique().HasFilter("[CreationKey] IS NOT NULL");
            entity.HasOne<Business>().WithMany()
                .HasForeignKey(x => x.BusinessId).OnDelete(DeleteBehavior.Restrict);
            entity.HasOne<BusinessLocation>().WithMany()
                .HasForeignKey(x => x.LocationId).OnDelete(DeleteBehavior.Restrict);
            entity.HasOne<RetailSupplier>().WithMany()
                .HasForeignKey(x => x.SupplierId).OnDelete(DeleteBehavior.Restrict);
            entity.HasOne<AppUser>().WithMany()
                .HasForeignKey(x => x.CreatedByUserId).OnDelete(DeleteBehavior.Restrict);
            entity.HasOne<AppUser>().WithMany()
                .HasForeignKey(x => x.ApprovedByUserId).OnDelete(DeleteBehavior.Restrict);
        });

        builder.Entity<RetailPurchaseOrderLine>(entity =>
        {
            entity.Property(x => x.OrderedQuantity).HasPrecision(18, 3);
            entity.Property(x => x.SelectedUnitCode).HasMaxLength(12);
            entity.Property(x => x.ConversionFactor).HasPrecision(18, 3);
            entity.Property(x => x.UnitCost).HasPrecision(18, 2);
            entity.HasIndex(x => new { x.PurchaseOrderId, x.ProductId }).IsUnique();
            entity.HasOne<RetailPurchaseOrder>().WithMany()
                .HasForeignKey(x => x.PurchaseOrderId).OnDelete(DeleteBehavior.Restrict);
            entity.HasOne<RetailProduct>().WithMany()
                .HasForeignKey(x => x.ProductId).OnDelete(DeleteBehavior.Restrict);
        });

        builder.Entity<RetailReceiving>(entity =>
        {
            entity.Property(x => x.Status).HasMaxLength(20).IsRequired();
            entity.Property(x => x.CreationKey).HasMaxLength(80);
            entity.HasIndex(x => new { x.BusinessId, x.PurchaseOrderId, x.Status });
            entity.HasIndex(x => new { x.BusinessId, x.PurchaseOrderId, x.CreatedByUserId, x.CreationKey })
                .IsUnique().HasFilter("[CreationKey] IS NOT NULL");
            entity.HasOne<Business>().WithMany()
                .HasForeignKey(x => x.BusinessId).OnDelete(DeleteBehavior.Restrict);
            entity.HasOne<RetailPurchaseOrder>().WithMany()
                .HasForeignKey(x => x.PurchaseOrderId).OnDelete(DeleteBehavior.Restrict);
            entity.HasOne<AppUser>().WithMany()
                .HasForeignKey(x => x.CreatedByUserId).OnDelete(DeleteBehavior.Restrict);
            entity.HasOne<AppUser>().WithMany()
                .HasForeignKey(x => x.PostedByUserId).OnDelete(DeleteBehavior.Restrict);
        });

        builder.Entity<RetailSupplierDocument>(entity =>
        {
            entity.Property(x => x.Kind).HasMaxLength(12).IsRequired();
            entity.Property(x => x.CurrencyCode).HasMaxLength(3).IsRequired();
            entity.Property(x => x.Reference).HasMaxLength(120).IsRequired();
            entity.Property(x => x.RequestKey).HasMaxLength(80).IsRequired();
            entity.Property(x => x.Amount).HasPrecision(18, 2);
            entity.HasIndex(x => new { x.BusinessId, x.RequestKey }).IsUnique();
            entity.HasIndex(x => new { x.BusinessId, x.SupplierId, x.Kind, x.Reference }).IsUnique();
            entity.HasIndex(x => x.ReceivingId).IsUnique().HasFilter("[Kind] = 'Invoice'");
            entity.HasIndex(x => x.PurchaseReturnId).IsUnique().HasFilter("[PurchaseReturnId] IS NOT NULL");
            entity.HasIndex(x => x.SupplierId);
            entity.HasIndex(x => x.ActorUserId);
            entity.HasIndex(x => new { x.ReceivingId, x.Kind });
            entity.HasOne<Business>().WithMany().HasForeignKey(x => x.BusinessId).OnDelete(DeleteBehavior.Restrict);
            entity.HasOne<RetailSupplier>().WithMany().HasForeignKey(x => x.SupplierId).OnDelete(DeleteBehavior.Restrict);
            entity.HasOne<RetailReceiving>().WithMany().HasForeignKey(x => x.ReceivingId).OnDelete(DeleteBehavior.Restrict);
            entity.HasOne<AppUser>().WithMany().HasForeignKey(x => x.ActorUserId).OnDelete(DeleteBehavior.Restrict);
            entity.HasOne<RetailPurchaseReturn>().WithMany().HasForeignKey(x => x.PurchaseReturnId).OnDelete(DeleteBehavior.Restrict);
            entity.ToTable(t => t.HasCheckConstraint("CK_RetailSupplierDocuments_KindAmount", "[Amount] > 0 AND (([Kind] = 'CreditNote' AND [PurchaseReturnId] IS NOT NULL) OR ([Kind] IN ('Invoice', 'Payment') AND [PurchaseReturnId] IS NULL))"));
        });

        builder.Entity<RetailPurchaseReturn>(entity =>
        {
            entity.Property(x => x.Quantity).HasPrecision(18, 3);
            entity.Property(x => x.RequestKey).HasMaxLength(80).IsRequired();
            entity.Property(x => x.Reason).HasMaxLength(200).IsRequired();
            entity.HasIndex(x => new { x.BusinessId, x.RequestKey }).IsUnique();
            entity.HasIndex(x => new { x.ReceivingLineId, x.BusinessId });
            entity.HasOne<Business>().WithMany().HasForeignKey(x => x.BusinessId).OnDelete(DeleteBehavior.Restrict);
            entity.HasOne<RetailReceivingLine>().WithMany().HasForeignKey(x => x.ReceivingLineId).OnDelete(DeleteBehavior.Restrict);
            entity.HasOne<AppUser>().WithMany().HasForeignKey(x => x.ActorUserId).OnDelete(DeleteBehavior.Restrict);
            entity.ToTable(t => t.HasCheckConstraint("CK_RetailPurchaseReturns_Quantity", "[Quantity] > 0"));
        });

        builder.Entity<RetailReceivingLine>(entity =>
        {
            entity.Property(x => x.Quantity).HasPrecision(18, 3);
            entity.HasIndex(x => new { x.ReceivingId, x.PurchaseOrderLineId }).IsUnique();
            entity.HasOne<RetailReceiving>().WithMany()
                .HasForeignKey(x => x.ReceivingId).OnDelete(DeleteBehavior.Restrict);
            entity.HasOne<RetailPurchaseOrderLine>().WithMany()
                .HasForeignKey(x => x.PurchaseOrderLineId).OnDelete(DeleteBehavior.Restrict);
        });

    }
}
