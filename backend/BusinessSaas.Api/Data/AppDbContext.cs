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

public sealed class ProductCategory
{
    public long Id { get; set; }
    public long BusinessId { get; set; }
    public string Name { get; set; } = "";
    public string NameKey { get; set; } = "";
}

public sealed class AppDbContext(DbContextOptions<AppDbContext> options)
    : IdentityUserContext<AppUser, long>(options)
{
    public DbSet<BusinessType> BusinessTypes => Set<BusinessType>();
    public DbSet<Business> Businesses => Set<Business>();
    public DbSet<BusinessUser> BusinessUsers => Set<BusinessUser>();
    public DbSet<BusinessSubscription> BusinessSubscriptions => Set<BusinessSubscription>();
    public DbSet<BusinessLocation> BusinessLocations => Set<BusinessLocation>();
    public DbSet<ProductCategory> ProductCategories => Set<ProductCategory>();

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

        builder.Entity<ProductCategory>(entity =>
        {
            entity.HasIndex(x => new { x.BusinessId, x.NameKey }).IsUnique();
            entity.Property(x => x.Name).HasMaxLength(100).IsRequired();
            entity.Property(x => x.NameKey).HasMaxLength(100).IsRequired();
            entity.HasOne<Business>().WithMany()
                .HasForeignKey(x => x.BusinessId).OnDelete(DeleteBehavior.Cascade);
        });

        builder.Entity<BusinessLocation>(entity =>
        {
            entity.HasIndex(x => new { x.BusinessId, x.Code }).IsUnique();
            entity.Property(x => x.Code).HasMaxLength(30).IsRequired();
            entity.Property(x => x.Name).HasMaxLength(120).IsRequired();
            entity.HasOne<Business>().WithMany()
                .HasForeignKey(x => x.BusinessId).OnDelete(DeleteBehavior.Cascade);
        });
    }
}
