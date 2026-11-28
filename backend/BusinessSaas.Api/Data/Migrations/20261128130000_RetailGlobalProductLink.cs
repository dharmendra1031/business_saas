using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace BusinessSaas.Api.Data.Migrations;

[DbContext(typeof(AppDbContext))]
[Migration("20261128130000_RetailGlobalProductLink")]
public sealed class RetailGlobalProductLink : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<long>("GlobalProductId", "RetailProducts", type: "bigint", nullable: true);
        migrationBuilder.CreateIndex("IX_RetailProducts_GlobalProductId", "RetailProducts", "GlobalProductId");
        migrationBuilder.CreateIndex("IX_RetailProducts_BusinessId_GlobalProductId", "RetailProducts",
            new[] { "BusinessId", "GlobalProductId" }, unique: true, filter: "[GlobalProductId] IS NOT NULL");
        migrationBuilder.AddForeignKey("FK_RetailProducts_GlobalProducts_GlobalProductId",
            "RetailProducts", "GlobalProductId", "GlobalProducts", principalColumn: "Id",
            onDelete: ReferentialAction.Restrict);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("IF EXISTS (SELECT 1 FROM RetailProducts WHERE GlobalProductId IS NOT NULL) THROW 51000, 'Catalogue links prevent downgrade.', 1;");
        migrationBuilder.DropForeignKey("FK_RetailProducts_GlobalProducts_GlobalProductId", "RetailProducts");
        migrationBuilder.DropIndex("IX_RetailProducts_BusinessId_GlobalProductId", "RetailProducts");
        migrationBuilder.DropIndex("IX_RetailProducts_GlobalProductId", "RetailProducts");
        migrationBuilder.DropColumn("GlobalProductId", "RetailProducts");
    }
}
