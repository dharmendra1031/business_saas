using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace BusinessSaas.Api.Data.Migrations;

[DbContext(typeof(AppDbContext))]
[Migration("20261124200000_RetailReceiptLogoPreferences")]
public sealed class RetailReceiptLogoPreferences : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<byte[]>("ReceiptLogo", "Businesses",
            type: "varbinary(max)", maxLength: 32768, nullable: true);
        migrationBuilder.AddColumn<bool>("ReceiptShowLogo", "Businesses",
            type: "bit", nullable: false, defaultValue: true);
        migrationBuilder.AddColumn<int>("ReceiptPrintCopies", "Businesses",
            type: "int", nullable: false, defaultValue: 1);
        migrationBuilder.AddColumn<byte[]>("MerchantLogo", "RetailCashSales",
            type: "varbinary(max)", maxLength: 32768, nullable: true);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropColumn("MerchantLogo", "RetailCashSales");
        migrationBuilder.DropColumn("ReceiptPrintCopies", "Businesses");
        migrationBuilder.DropColumn("ReceiptShowLogo", "Businesses");
        migrationBuilder.DropColumn("ReceiptLogo", "Businesses");
    }
}
