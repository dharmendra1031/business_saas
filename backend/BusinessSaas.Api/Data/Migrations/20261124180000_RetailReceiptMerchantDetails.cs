using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace BusinessSaas.Api.Data.Migrations;

[DbContext(typeof(AppDbContext))]
[Migration("20261124180000_RetailReceiptMerchantDetails")]
public sealed class RetailReceiptMerchantDetails : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<string>("ReceiptLegalName", "Businesses",
            type: "nvarchar(150)", maxLength: 150, nullable: true);
        migrationBuilder.AddColumn<bool>("ReceiptShowLegalName", "Businesses",
            type: "bit", nullable: false, defaultValue: true);
        migrationBuilder.AddColumn<string>("MerchantLegalName", "RetailCashSales",
            type: "nvarchar(150)", maxLength: 150, nullable: true);
        migrationBuilder.AddColumn<string>("ReceiptAddress", "Businesses",
            type: "nvarchar(300)", maxLength: 300, nullable: true);
        migrationBuilder.AddColumn<bool>("ReceiptShowAddress", "Businesses",
            type: "bit", nullable: false, defaultValue: true);
        migrationBuilder.AddColumn<string>("MerchantAddress", "RetailCashSales",
            type: "nvarchar(300)", maxLength: 300, nullable: true);
        migrationBuilder.AddColumn<string>("ReceiptPhone", "Businesses",
            type: "nvarchar(25)", maxLength: 25, nullable: true);
        migrationBuilder.AddColumn<bool>("ReceiptShowPhone", "Businesses",
            type: "bit", nullable: false, defaultValue: true);
        migrationBuilder.AddColumn<string>("MerchantPhone", "RetailCashSales",
            type: "nvarchar(25)", maxLength: 25, nullable: true);
        migrationBuilder.AddColumn<string>("ReceiptEmail", "Businesses",
            type: "nvarchar(254)", maxLength: 254, nullable: true);
        migrationBuilder.AddColumn<bool>("ReceiptShowEmail", "Businesses",
            type: "bit", nullable: false, defaultValue: true);
        migrationBuilder.AddColumn<string>("MerchantEmail", "RetailCashSales",
            type: "nvarchar(254)", maxLength: 254, nullable: true);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropColumn("MerchantLegalName", "RetailCashSales");
        migrationBuilder.DropColumn("ReceiptShowLegalName", "Businesses");
        migrationBuilder.DropColumn("ReceiptLegalName", "Businesses");
        migrationBuilder.DropColumn("MerchantAddress", "RetailCashSales");
        migrationBuilder.DropColumn("ReceiptShowAddress", "Businesses");
        migrationBuilder.DropColumn("ReceiptAddress", "Businesses");
        migrationBuilder.DropColumn("MerchantPhone", "RetailCashSales");
        migrationBuilder.DropColumn("ReceiptShowPhone", "Businesses");
        migrationBuilder.DropColumn("ReceiptPhone", "Businesses");
        migrationBuilder.DropColumn("MerchantEmail", "RetailCashSales");
        migrationBuilder.DropColumn("ReceiptShowEmail", "Businesses");
        migrationBuilder.DropColumn("ReceiptEmail", "Businesses");
    }
}
