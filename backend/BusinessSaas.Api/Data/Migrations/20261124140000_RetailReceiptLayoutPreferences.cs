using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace BusinessSaas.Api.Data.Migrations;

[DbContext(typeof(AppDbContext))]
[Migration("20261124140000_RetailReceiptLayoutPreferences")]
public sealed class RetailReceiptLayoutPreferences : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        // Existing businesses retain a readable 80mm layout by default.
        migrationBuilder.AddColumn<string>("ReceiptPaperSize", "Businesses",
            type: "nvarchar(12)", maxLength: 12, nullable: false, defaultValue: "Thermal80");
        migrationBuilder.AddColumn<bool>("ReceiptShowTime", "Businesses",
            type: "bit", nullable: false, defaultValue: true);
        migrationBuilder.AddColumn<bool>("ReceiptShowUnitPrice", "Businesses",
            type: "bit", nullable: false, defaultValue: true);
        migrationBuilder.AddColumn<bool>("ReceiptShowCashReceived", "Businesses",
            type: "bit", nullable: false, defaultValue: true);
        migrationBuilder.AddColumn<bool>("ReceiptShowChange", "Businesses",
            type: "bit", nullable: false, defaultValue: true);
        migrationBuilder.AddColumn<bool>("ReceiptShowFooter", "Businesses",
            type: "bit", nullable: false, defaultValue: true);
        migrationBuilder.AddColumn<string>("ReceiptFooterText", "Businesses",
            type: "nvarchar(120)", maxLength: 120, nullable: false, defaultValue: "Thank you for shopping!");
        migrationBuilder.AddColumn<int>("ReceiptSettingsRevision", "Businesses",
            type: "int", nullable: false, defaultValue: 0);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropColumn("ReceiptSettingsRevision", "Businesses");
        migrationBuilder.DropColumn("ReceiptFooterText", "Businesses");
        migrationBuilder.DropColumn("ReceiptShowFooter", "Businesses");
        migrationBuilder.DropColumn("ReceiptShowChange", "Businesses");
        migrationBuilder.DropColumn("ReceiptShowCashReceived", "Businesses");
        migrationBuilder.DropColumn("ReceiptShowUnitPrice", "Businesses");
        migrationBuilder.DropColumn("ReceiptShowTime", "Businesses");
        migrationBuilder.DropColumn("ReceiptPaperSize", "Businesses");
    }
}
