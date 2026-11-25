using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace BusinessSaas.Api.Data.Migrations;

[DbContext(typeof(AppDbContext))]
[Migration("20261125110000_PurchaseUnitSnapshots")]
public sealed class PurchaseUnitSnapshots : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<long>("SelectedUnitId", "RetailPurchaseOrderLines", type: "bigint", nullable: true);
        migrationBuilder.AddColumn<string>("SelectedUnitCode", "RetailPurchaseOrderLines", type: "nvarchar(12)", maxLength: 12, nullable: true);
        migrationBuilder.AddColumn<decimal>("ConversionFactor", "RetailPurchaseOrderLines", type: "decimal(18,3)", precision: 18, scale: 3, nullable: true);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("IF EXISTS (SELECT 1 FROM RetailPurchaseOrderLines WHERE SelectedUnitId IS NOT NULL) THROW 51000, 'Converted purchase history cannot be downgraded.', 1;");
        migrationBuilder.DropColumn("ConversionFactor", "RetailPurchaseOrderLines");
        migrationBuilder.DropColumn("SelectedUnitCode", "RetailPurchaseOrderLines");
        migrationBuilder.DropColumn("SelectedUnitId", "RetailPurchaseOrderLines");
    }
}
