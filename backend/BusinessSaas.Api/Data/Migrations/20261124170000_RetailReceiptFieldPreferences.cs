using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace BusinessSaas.Api.Data.Migrations;

[DbContext(typeof(AppDbContext))]
[Migration("20261124170000_RetailReceiptFieldPreferences")]
public sealed class RetailReceiptFieldPreferences : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<bool>("ReceiptShowBranch", "Businesses",
            type: "bit", nullable: false, defaultValue: true);
        migrationBuilder.AddColumn<bool>("ReceiptShowSku", "Businesses",
            type: "bit", nullable: false, defaultValue: true);
        migrationBuilder.AddColumn<bool>("ReceiptShowUnit", "Businesses",
            type: "bit", nullable: false, defaultValue: true);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropColumn("ReceiptShowBranch", "Businesses");
        migrationBuilder.DropColumn("ReceiptShowSku", "Businesses");
        migrationBuilder.DropColumn("ReceiptShowUnit", "Businesses");
    }
}
