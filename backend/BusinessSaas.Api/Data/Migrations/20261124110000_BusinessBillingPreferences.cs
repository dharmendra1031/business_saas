using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace BusinessSaas.Api.Data.Migrations;

[DbContext(typeof(AppDbContext))]
[Migration("20261124110000_BusinessBillingPreferences")]
public sealed class BusinessBillingPreferences : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        // Existing Retail businesses retain their currently working cash checkout.
        migrationBuilder.AddColumn<bool>("CashSalesEnabled", "Businesses",
            type: "bit", nullable: false, defaultValue: true);
        migrationBuilder.AddColumn<bool>("CreditSalesEnabled", "Businesses",
            type: "bit", nullable: false, defaultValue: false);
        migrationBuilder.AddColumn<int>("BillingSettingsRevision", "Businesses",
            type: "int", nullable: false, defaultValue: 0);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropColumn("BillingSettingsRevision", "Businesses");
        migrationBuilder.DropColumn("CreditSalesEnabled", "Businesses");
        migrationBuilder.DropColumn("CashSalesEnabled", "Businesses");
    }
}
