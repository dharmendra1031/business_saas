using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace BusinessSaas.Api.Data.Migrations;

[DbContext(typeof(AppDbContext))]
[Migration("20261123150000_AllowFractionalCashSaleQuantity")]
public sealed class AllowFractionalCashSaleQuantity : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AlterColumn<decimal>(
            name: "Quantity", table: "RetailCashSaleLine", type: "decimal(18,3)",
            precision: 18, scale: 3, nullable: false,
            oldClrType: typeof(int), oldType: "int");
        migrationBuilder.AddCheckConstraint(
            name: "CK_RetailCashSaleLine_Quantity", table: "RetailCashSaleLine",
            sql: "[Quantity] >= 0.001 AND [Quantity] <= 1000000");
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("""
            IF EXISTS (SELECT 1 FROM RetailCashSaleLine WHERE Quantity <> FLOOR(Quantity))
                THROW 51010, 'Fractional cash quantities cannot be downgraded without data loss.', 1;
            """);
        migrationBuilder.DropCheckConstraint(
            name: "CK_RetailCashSaleLine_Quantity", table: "RetailCashSaleLine");
        migrationBuilder.AlterColumn<int>(
            name: "Quantity", table: "RetailCashSaleLine", type: "int", nullable: false,
            oldClrType: typeof(decimal), oldType: "decimal(18,3)", oldPrecision: 18, oldScale: 3);
    }
}
