using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace BusinessSaas.Api.Data.Migrations;

[DbContext(typeof(AppDbContext))]
[Migration("20261123090000_AddRetailReorderLevel")]
public sealed class AddRetailReorderLevel : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<decimal>(
            name: "ReorderLevel", table: "RetailProducts",
            type: "decimal(18,3)", precision: 18, scale: 3, nullable: true);
        migrationBuilder.AddCheckConstraint(
            name: "CK_RetailProducts_ReorderLevel",
            table: "RetailProducts", sql: "[ReorderLevel] IS NULL OR [ReorderLevel] >= 0");
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropCheckConstraint(
            name: "CK_RetailProducts_ReorderLevel", table: "RetailProducts");
        migrationBuilder.DropColumn(name: "ReorderLevel", table: "RetailProducts");
    }
}
