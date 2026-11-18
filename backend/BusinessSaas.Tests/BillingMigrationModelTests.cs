using BusinessSaas.Api.Data;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace BusinessSaas.Tests;

public sealed class BillingMigrationModelTests
{
    [Fact]
    public void Sql_server_model_matches_latest_migration_snapshot()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlServer("Server=.;Database=BusinessSaasModelCheck;Trusted_Connection=True;TrustServerCertificate=True")
            .Options;
        using var db = new AppDbContext(options);

        // No database connection is required; EF compares the model and snapshot.
        Assert.False(db.Database.HasPendingModelChanges(),
            "SQL Server model differs from the latest reviewed EF Core migration.");
    }
}
