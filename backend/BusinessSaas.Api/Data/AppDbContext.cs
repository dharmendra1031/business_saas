using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;

namespace BusinessSaas.Api.Data;

public sealed class AppUser : IdentityUser<long>;

public sealed class BusinessType
{
    public long Id { get; set; }
    public string Code { get; set; } = "";
    public string Name { get; set; } = "";
    public bool IsSupported { get; set; }
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
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
}

public sealed class BusinessUser
{
    public long Id { get; set; }
    public long BusinessId { get; set; }
    public long UserId { get; set; }
    public string RoleCode { get; set; } = "Owner";
}

public sealed class BusinessSubscription
{
    public long Id { get; set; }
    public long BusinessId { get; set; }
    public string PlanCode { get; set; } = "STARTER";
    public string Status { get; set; } = "Trial";
    public DateTime TrialEndsAtUtc { get; set; }
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
    public long CategoryId { get; set; }
    public long UnitId { get; set; }
    public string Name { get; set; } = "";
    public string Sku { get; set; } = "";
    public string? Barcode { get; set; }
    public decimal SalePrice { get; set; }
    public string TaxClassification { get; set; } = "";
    public bool IsActive { get; set; } = true;
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
}

public sealed class RetailReceiving
{
    public long Id { get; set; }
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

public sealed class AppDbContext(DbContextOptions<AppDbContext> options)
    : IdentityUserContext<AppUser, long>(options)
{
    public DbSet<BusinessType> BusinessTypes => Set<BusinessType>();
    public DbSet<Business> Businesses => Set<Business>();
    public DbSet<BusinessUser> BusinessUsers => Set<BusinessUser>();
    public DbSet<BusinessSubscription> BusinessSubscriptions => Set<BusinessSubscription>();
    public DbSet<BusinessLocation> BusinessLocations => Set<BusinessLocation>();

    public DbSet<RetailCategory> RetailCategories => Set<RetailCategory>();
    public DbSet<RetailUnit> RetailUnits => Set<RetailUnit>();
    public DbSet<RetailProduct> RetailProducts => Set<RetailProduct>();
    public DbSet<RetailSupplier> RetailSuppliers => Set<RetailSupplier>();
    public DbSet<RetailCustomer> RetailCustomers => Set<RetailCustomer>();
    public DbSet<RetailStockBalance> RetailStockBalances => Set<RetailStockBalance>();
    public DbSet<RetailStockMovement> RetailStockMovements => Set<RetailStockMovement>();
    public DbSet<RetailStockAdjustment> RetailStockAdjustments => Set<RetailStockAdjustment>();
    public DbSet<RetailPurchaseOrder> RetailPurchaseOrders => Set<RetailPurchaseOrder>();
    public DbSet<RetailPurchaseOrderLine> RetailPurchaseOrderLines => Set<RetailPurchaseOrderLine>();
    public DbSet<RetailReceiving> RetailReceivings => Set<RetailReceiving>();
    public DbSet<RetailReceivingLine> RetailReceivingLines => Set<RetailReceivingLine>();

    protected override void OnModelCreating(ModelBuilder builder)
    {
        base.OnModelCreating(builder);

        builder.Entity<BusinessType>(entity =>
        {
            entity.HasIndex(x => x.Code).IsUnique();
            entity.Property(x => x.Code).HasMaxLength(40).IsRequired();
            entity.Property(x => x.Name).HasMaxLength(100).IsRequired();
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

        builder.Entity<BusinessSubscription>(entity =>
        {
            entity.HasIndex(x => x.BusinessId).IsUnique();
            entity.Property(x => x.PlanCode).HasMaxLength(30).IsRequired();
            entity.Property(x => x.Status).HasMaxLength(20).IsRequired();
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

        builder.Entity<RetailProduct>(entity =>
        {
            entity.Property(x => x.Name).HasMaxLength(150).IsRequired();
            entity.Property(x => x.Sku).HasMaxLength(50).IsRequired();
            entity.Property(x => x.Barcode).HasMaxLength(64);
            entity.Property(x => x.SalePrice).HasPrecision(18, 2);
            entity.Property(x => x.TaxClassification).HasMaxLength(30).IsRequired();
            entity.HasIndex(x => new { x.BusinessId, x.Sku }).IsUnique();
            entity.HasIndex(x => new { x.BusinessId, x.Barcode })
                .IsUnique().HasFilter("[Barcode] IS NOT NULL");
            entity.HasOne<Business>().WithMany()
                .HasForeignKey(x => x.BusinessId).OnDelete(DeleteBehavior.Cascade);
            entity.HasOne<RetailCategory>().WithMany()
                .HasForeignKey(x => x.CategoryId).OnDelete(DeleteBehavior.Restrict);
            entity.HasOne<RetailUnit>().WithMany()
                .HasForeignKey(x => x.UnitId).OnDelete(DeleteBehavior.Restrict);
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

        builder.Entity<RetailStockMovement>(entity =>
        {
            entity.Property(x => x.SourceType).HasMaxLength(30).IsRequired();
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
            entity.HasIndex(x => new { x.BusinessId, x.Status });
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
            entity.HasIndex(x => new { x.BusinessId, x.PurchaseOrderId, x.Status });
            entity.HasOne<Business>().WithMany()
                .HasForeignKey(x => x.BusinessId).OnDelete(DeleteBehavior.Restrict);
            entity.HasOne<RetailPurchaseOrder>().WithMany()
                .HasForeignKey(x => x.PurchaseOrderId).OnDelete(DeleteBehavior.Restrict);
            entity.HasOne<AppUser>().WithMany()
                .HasForeignKey(x => x.CreatedByUserId).OnDelete(DeleteBehavior.Restrict);
            entity.HasOne<AppUser>().WithMany()
                .HasForeignKey(x => x.PostedByUserId).OnDelete(DeleteBehavior.Restrict);
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
