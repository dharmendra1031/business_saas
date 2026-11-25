using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace BusinessSaas.Api.Data.Migrations;

[DbContext(typeof(AppDbContext))]
[Migration("20261125130000_AlternateUnitSalePrice")]
public sealed class AlternateUnitSalePrice : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<decimal>("SalePrice", "RetailProductUnits", type: "decimal(18,2)",
            precision: 18, scale: 2, nullable: true);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("IF EXISTS (SELECT 1 FROM RetailProductUnits WHERE SalePrice IS NOT NULL) THROW 51000, 'Configured alternate sale prices cannot be downgraded.', 1;");
        migrationBuilder.DropColumn("SalePrice", "RetailProductUnits");
    }
}
