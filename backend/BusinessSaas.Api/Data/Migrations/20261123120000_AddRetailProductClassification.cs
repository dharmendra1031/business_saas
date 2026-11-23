using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace BusinessSaas.Api.Data.Migrations;

[DbContext(typeof(AppDbContext))]
[Migration("20261123120000_AddRetailProductClassification")]
public sealed class AddRetailProductClassification : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<long>(
            name: "ParentId", table: "RetailCategories", type: "bigint", nullable: true);
        migrationBuilder.CreateIndex(
            name: "IX_RetailCategories_ParentId", table: "RetailCategories", column: "ParentId");
        migrationBuilder.AddForeignKey(
            name: "FK_RetailCategories_RetailCategories_ParentId",
            table: "RetailCategories", column: "ParentId",
            principalTable: "RetailCategories", principalColumn: "Id",
            onDelete: ReferentialAction.Restrict);

        migrationBuilder.CreateTable(
            name: "RetailBrands",
            columns: table => new
            {
                Id = table.Column<long>(type: "bigint", nullable: false)
                    .Annotation("SqlServer:Identity", "1, 1"),
                BusinessId = table.Column<long>(type: "bigint", nullable: false),
                Name = table.Column<string>(type: "nvarchar(80)", maxLength: 80, nullable: false),
                NameKey = table.Column<string>(type: "nvarchar(80)", maxLength: 80, nullable: false),
                IsActive = table.Column<bool>(type: "bit", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_RetailBrands", x => x.Id);
                table.ForeignKey("FK_RetailBrands_Businesses_BusinessId", x => x.BusinessId,
                    "Businesses", "Id", onDelete: ReferentialAction.Cascade);
            });
        migrationBuilder.CreateIndex(
            name: "IX_RetailBrands_BusinessId_NameKey", table: "RetailBrands",
            columns: new[] { "BusinessId", "NameKey" }, unique: true);

        migrationBuilder.AddColumn<long>(
            name: "BrandId", table: "RetailProducts", type: "bigint", nullable: true);
        migrationBuilder.CreateIndex(
            name: "IX_RetailProducts_BrandId", table: "RetailProducts", column: "BrandId");
        migrationBuilder.AddForeignKey(
            name: "FK_RetailProducts_RetailBrands_BrandId", table: "RetailProducts",
            column: "BrandId", principalTable: "RetailBrands", principalColumn: "Id",
            onDelete: ReferentialAction.Restrict);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropForeignKey("FK_RetailProducts_RetailBrands_BrandId", "RetailProducts");
        migrationBuilder.DropIndex("IX_RetailProducts_BrandId", "RetailProducts");
        migrationBuilder.DropColumn("BrandId", "RetailProducts");
        migrationBuilder.DropTable("RetailBrands");
        migrationBuilder.DropForeignKey("FK_RetailCategories_RetailCategories_ParentId", "RetailCategories");
        migrationBuilder.DropIndex("IX_RetailCategories_ParentId", "RetailCategories");
        migrationBuilder.DropColumn("ParentId", "RetailCategories");
    }
}
