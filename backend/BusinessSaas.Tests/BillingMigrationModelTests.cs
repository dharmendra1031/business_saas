using BusinessSaas.Api.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Migrations;
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

        var migrations = db.GetService<IMigrationsAssembly>();
        var snapshot = migrations.ModelSnapshot?.Model;
        Assert.NotNull(snapshot);

        var model = db.GetService<IDesignTimeModel>().Model;
        var differences = db.GetService<IMigrationsModelDiffer>()
            .GetDifferences(snapshot!.GetRelationalModel(), model.GetRelationalModel());
        Assert.True(differences.Count == 0,
            "Unmigrated SQL model changes: " +
            string.Join(", ", differences.Select(change => change.GetType().Name +
                " " + (change.GetType().GetProperty("Name")?.GetValue(change) ?? ""))));
    }
}
