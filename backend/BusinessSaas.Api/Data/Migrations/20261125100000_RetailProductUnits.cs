using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace BusinessSaas.Api.Data.Migrations;

[DbContext(typeof(AppDbContext))]
[Migration("20261125100000_RetailProductUnits")]
public sealed class RetailProductUnits : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable("RetailProductUnits", table => new
        {
            ProductId = table.Column<long>(type: "bigint", nullable: false),
            UnitId = table.Column<long>(type: "bigint", nullable: false),
            Factor = table.Column<decimal>(type: "decimal(18,3)", precision: 18, scale: 3, nullable: false)
        }, constraints: table =>
        {
            table.PrimaryKey("PK_RetailProductUnits", x => new { x.ProductId, x.UnitId });
            table.ForeignKey("FK_RetailProductUnits_RetailProducts_ProductId", x => x.ProductId,
                "RetailProducts", "Id", onDelete: ReferentialAction.Cascade);
            table.ForeignKey("FK_RetailProductUnits_RetailUnits_UnitId", x => x.UnitId,
                "RetailUnits", "Id", onDelete: ReferentialAction.Restrict);
            table.CheckConstraint("CK_RetailProductUnits_Factor", "[Factor] > 0 AND [Factor] <= 9999999.999");
        });
        migrationBuilder.CreateIndex("IX_RetailProductUnits_UnitId", "RetailProductUnits", "UnitId");
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable("RetailProductUnits");
    }
}
