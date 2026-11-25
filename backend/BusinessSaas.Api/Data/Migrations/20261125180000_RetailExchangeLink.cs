using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace BusinessSaas.Api.Data.Migrations;

[DbContext(typeof(AppDbContext))]
[Migration("20261125180000_RetailExchangeLink")]
public sealed class RetailExchangeLink : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<long>("ReplacementSaleId", "RetailCashReturns", type: "bigint", nullable: true);
        migrationBuilder.AddColumn<decimal>("NetCashSettlement", "RetailCashReturns",
            type: "decimal(18,2)", precision: 18, scale: 2, nullable: true);
        migrationBuilder.CreateIndex("IX_RetailCashReturns_ReplacementSaleId", "RetailCashReturns",
            "ReplacementSaleId", unique: true, filter: "[ReplacementSaleId] IS NOT NULL");
        migrationBuilder.AddForeignKey("FK_RetailCashReturns_RetailCashSales_ReplacementSaleId",
            "RetailCashReturns", "ReplacementSaleId", "RetailCashSales", principalColumn: "Id",
            onDelete: ReferentialAction.Restrict);
        migrationBuilder.AddCheckConstraint("CK_RetailCashReturns_ExchangeLink", "RetailCashReturns",
            "([ReplacementSaleId] IS NULL AND [NetCashSettlement] IS NULL) OR ([ReplacementSaleId] IS NOT NULL AND [NetCashSettlement] IS NOT NULL AND [ReplacementSaleId] <> [SaleId])");
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("IF EXISTS (SELECT 1 FROM RetailCashReturns WHERE ReplacementSaleId IS NOT NULL OR NetCashSettlement IS NOT NULL) THROW 51000, 'Posted exchange links cannot be discarded by downgrade.', 1;");
        migrationBuilder.DropCheckConstraint("CK_RetailCashReturns_ExchangeLink", "RetailCashReturns");
        migrationBuilder.DropForeignKey("FK_RetailCashReturns_RetailCashSales_ReplacementSaleId", "RetailCashReturns");
        migrationBuilder.DropIndex("IX_RetailCashReturns_ReplacementSaleId", "RetailCashReturns");
        migrationBuilder.DropColumn("NetCashSettlement", "RetailCashReturns");
        migrationBuilder.DropColumn("ReplacementSaleId", "RetailCashReturns");
    }
}
