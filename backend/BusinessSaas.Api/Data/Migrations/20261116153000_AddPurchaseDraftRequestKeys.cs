using BusinessSaas.Api.Data;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BusinessSaas.Api.Data.Migrations;

[DbContext(typeof(AppDbContext))]
[Migration("20261116153000_AddPurchaseDraftRequestKeys")]
public sealed class AddPurchaseDraftRequestKeys : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<string>(
            name: "CreationKey", table: "RetailPurchaseOrders",
            type: "nvarchar(80)", maxLength: 80, nullable: true);

        migrationBuilder.AddColumn<string>(
            name: "CreationKey", table: "RetailReceivings",
            type: "nvarchar(80)", maxLength: 80, nullable: true);

        migrationBuilder.CreateIndex(
            name: "IX_RetailPurchaseOrders_BusinessId_CreatedByUserId_CreationKey",
            table: "RetailPurchaseOrders",
            columns: new[] { "BusinessId", "CreatedByUserId", "CreationKey" },
            unique: true, filter: "[CreationKey] IS NOT NULL");

        migrationBuilder.CreateIndex(
            name: "IX_RetailReceivings_BusinessId_PurchaseOrderId_CreatedByUserId_CreationKey",
            table: "RetailReceivings",
            columns: new[] { "BusinessId", "PurchaseOrderId", "CreatedByUserId", "CreationKey" },
            unique: true, filter: "[CreationKey] IS NOT NULL");
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropIndex(
            name: "IX_RetailPurchaseOrders_BusinessId_CreatedByUserId_CreationKey",
            table: "RetailPurchaseOrders");

        migrationBuilder.DropIndex(
            name: "IX_RetailReceivings_BusinessId_PurchaseOrderId_CreatedByUserId_CreationKey",
            table: "RetailReceivings");

        migrationBuilder.DropColumn(name: "CreationKey", table: "RetailPurchaseOrders");
        migrationBuilder.DropColumn(name: "CreationKey", table: "RetailReceivings");
    }
}
