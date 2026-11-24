using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace BusinessSaas.Api.Data.Migrations;

[DbContext(typeof(AppDbContext))]
[Migration("20261124190000_RetailReceiptCustomerDetails")]
public sealed class RetailReceiptCustomerDetails : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<string>("Address", "RetailCustomers",
            type: "nvarchar(300)", maxLength: 300, nullable: true);
        migrationBuilder.AddColumn<long>("CustomerId", "RetailCashSales", type: "bigint", nullable: true);
        migrationBuilder.AddColumn<string>("CustomerName", "RetailCashSales",
            type: "nvarchar(120)", maxLength: 120, nullable: true);
        migrationBuilder.AddColumn<bool>("ReceiptShowCustomerName", "Businesses",
            type: "bit", nullable: false, defaultValue: true);
        migrationBuilder.AddColumn<string>("CustomerPhone", "RetailCashSales",
            type: "nvarchar(25)", maxLength: 25, nullable: true);
        migrationBuilder.AddColumn<bool>("ReceiptShowCustomerPhone", "Businesses",
            type: "bit", nullable: false, defaultValue: true);
        migrationBuilder.AddColumn<string>("CustomerAddress", "RetailCashSales",
            type: "nvarchar(300)", maxLength: 300, nullable: true);
        migrationBuilder.AddColumn<bool>("ReceiptShowCustomerAddress", "Businesses",
            type: "bit", nullable: false, defaultValue: true);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropColumn("ReceiptShowCustomerName", "Businesses");
        migrationBuilder.DropColumn("CustomerName", "RetailCashSales");
        migrationBuilder.DropColumn("ReceiptShowCustomerPhone", "Businesses");
        migrationBuilder.DropColumn("CustomerPhone", "RetailCashSales");
        migrationBuilder.DropColumn("ReceiptShowCustomerAddress", "Businesses");
        migrationBuilder.DropColumn("CustomerAddress", "RetailCashSales");
        migrationBuilder.DropColumn("CustomerId", "RetailCashSales");
        migrationBuilder.DropColumn("Address", "RetailCustomers");
    }
}
