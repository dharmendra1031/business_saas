using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace BusinessSaas.Api.Data.Migrations;

[DbContext(typeof(AppDbContext))]
[Migration("20261124160000_RetailReceiptSnapshots")]
public sealed class RetailReceiptSnapshots : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        // Preserve unknown historical fields as NULL; do not backfill from current masters.
        migrationBuilder.AddColumn<string>("BusinessName", "RetailCashSales",
            type: "nvarchar(150)", maxLength: 150, nullable: true);
        migrationBuilder.AddColumn<string>("LocationName", "RetailCashSales",
            type: "nvarchar(120)", maxLength: 120, nullable: true);
        migrationBuilder.AddColumn<string>("TimeZoneId", "RetailCashSales",
            type: "nvarchar(80)", maxLength: 80, nullable: true);
        migrationBuilder.AddColumn<string>("ProductSku", "RetailCashSaleLine",
            type: "nvarchar(50)", maxLength: 50, nullable: true);
        migrationBuilder.AddColumn<string>("UnitCode", "RetailCashSaleLine",
            type: "nvarchar(12)", maxLength: 12, nullable: true);
        migrationBuilder.AddColumn<decimal>("LineTotal", "RetailCashSaleLine",
            type: "decimal(18,2)", precision: 18, scale: 2, nullable: true);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropColumn("LineTotal", "RetailCashSaleLine");
        migrationBuilder.DropColumn("UnitCode", "RetailCashSaleLine");
        migrationBuilder.DropColumn("ProductSku", "RetailCashSaleLine");
        migrationBuilder.DropColumn("TimeZoneId", "RetailCashSales");
        migrationBuilder.DropColumn("LocationName", "RetailCashSales");
        migrationBuilder.DropColumn("BusinessName", "RetailCashSales");
    }
}
