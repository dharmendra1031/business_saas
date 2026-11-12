using BusinessSaas.Api.Data;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BusinessSaas.Api.Data.Migrations;

[DbContext(typeof(AppDbContext))]
[Migration("20261112083000_AddRetailProducts")]
public sealed class AddRetailProducts : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable(
            name: "RetailProducts",
            columns: table => new
            {
                Id = table.Column<long>(type: "bigint", nullable: false)
                    .Annotation("SqlServer:Identity", "1, 1"),
                BusinessId = table.Column<long>(type: "bigint", nullable: false),
                CategoryId = table.Column<long>(type: "bigint", nullable: false),
                UnitId = table.Column<long>(type: "bigint", nullable: false),
                Name = table.Column<string>(type: "nvarchar(150)", maxLength: 150, nullable: false),
                Sku = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                Barcode = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: true),
                SalePrice = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                TaxClassification = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false),
                IsActive = table.Column<bool>(type: "bit", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_RetailProducts", x => x.Id);
                table.ForeignKey(
                    "FK_RetailProducts_Businesses_BusinessId",
                    x => x.BusinessId, "Businesses", "Id", onDelete: ReferentialAction.Cascade);
                table.ForeignKey(
                    "FK_RetailProducts_RetailCategories_CategoryId",
                    x => x.CategoryId, "RetailCategories", "Id", onDelete: ReferentialAction.Restrict);
                table.ForeignKey(
                    "FK_RetailProducts_RetailUnits_UnitId",
                    x => x.UnitId, "RetailUnits", "Id", onDelete: ReferentialAction.Restrict);
            });

        migrationBuilder.CreateIndex(
            name: "IX_RetailProducts_BusinessId_Sku",
            table: "RetailProducts",
            columns: new[] { "BusinessId", "Sku" },
            unique: true);
        migrationBuilder.CreateIndex(
            name: "IX_RetailProducts_BusinessId_Barcode",
            table: "RetailProducts",
            columns: new[] { "BusinessId", "Barcode" },
            unique: true,
            filter: "[Barcode] IS NOT NULL");
        migrationBuilder.CreateIndex(
            name: "IX_RetailProducts_CategoryId",
            table: "RetailProducts",
            column: "CategoryId");
        migrationBuilder.CreateIndex(
            name: "IX_RetailProducts_UnitId",
            table: "RetailProducts",
            column: "UnitId");
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable("RetailProducts");
    }
}
