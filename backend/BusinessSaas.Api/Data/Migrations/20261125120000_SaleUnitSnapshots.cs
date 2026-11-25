using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace BusinessSaas.Api.Data.Migrations;

[DbContext(typeof(AppDbContext))]
[Migration("20261125120000_SaleUnitSnapshots")]
public sealed class SaleUnitSnapshots : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<long>("SelectedUnitId", "RetailCashSaleLine", type: "bigint", nullable: true);
        migrationBuilder.AddColumn<decimal>("ConversionFactor", "RetailCashSaleLine", type: "decimal(18,3)", precision: 18, scale: 3, nullable: true);
        migrationBuilder.AddColumn<string>("BaseUnitCode", "RetailCashSaleLine", type: "nvarchar(12)", maxLength: 12, nullable: true);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("IF EXISTS (SELECT 1 FROM RetailCashSaleLine WHERE SelectedUnitId IS NOT NULL) THROW 51000, 'Converted sales history cannot be downgraded.', 1;");
        migrationBuilder.DropColumn("BaseUnitCode", "RetailCashSaleLine");
        migrationBuilder.DropColumn("ConversionFactor", "RetailCashSaleLine");
        migrationBuilder.DropColumn("SelectedUnitId", "RetailCashSaleLine");
    }
}
