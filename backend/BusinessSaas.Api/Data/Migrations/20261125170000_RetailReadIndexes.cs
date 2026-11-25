using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace BusinessSaas.Api.Data.Migrations;

[DbContext(typeof(AppDbContext))]
[Migration("20261125170000_RetailReadIndexes")]
public sealed class RetailReadIndexes : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateIndex("IX_RetailProducts_BusinessId_Name_Id", "RetailProducts",
            new[] { "BusinessId", "Name", "Id" });
        migrationBuilder.CreateIndex("IX_RetailCashSales_BusinessId_PostedAtUtc_Id", "RetailCashSales",
            new[] { "BusinessId", "PostedAtUtc", "Id" });
        migrationBuilder.CreateIndex("IX_RetailCashReturns_BusinessId_PostedAtUtc_Id", "RetailCashReturns",
            new[] { "BusinessId", "PostedAtUtc", "Id" });
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropIndex("IX_RetailCashReturns_BusinessId_PostedAtUtc_Id", "RetailCashReturns");
        migrationBuilder.DropIndex("IX_RetailCashSales_BusinessId_PostedAtUtc_Id", "RetailCashSales");
        migrationBuilder.DropIndex("IX_RetailProducts_BusinessId_Name_Id", "RetailProducts");
    }
}
