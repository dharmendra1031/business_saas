using BusinessSaas.Api.Data;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BusinessSaas.Api.Data.Migrations;

[DbContext(typeof(AppDbContext))]
[Migration("20261112114300_AddRetailCategoriesAndUnits")]
public sealed class AddRetailCategoriesAndUnits : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable(
            name: "RetailCategories",
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
                table.PrimaryKey("PK_RetailCategories", x => x.Id);
                table.ForeignKey("FK_RetailCategories_Businesses_BusinessId",
                    x => x.BusinessId, "Businesses", "Id", onDelete: ReferentialAction.Cascade);
            });

        migrationBuilder.CreateTable(
            name: "RetailUnits",
            columns: table => new
            {
                Id = table.Column<long>(type: "bigint", nullable: false)
                    .Annotation("SqlServer:Identity", "1, 1"),
                BusinessId = table.Column<long>(type: "bigint", nullable: false),
                Code = table.Column<string>(type: "nvarchar(12)", maxLength: 12, nullable: false),
                Name = table.Column<string>(type: "nvarchar(60)", maxLength: 60, nullable: false),
                NameKey = table.Column<string>(type: "nvarchar(60)", maxLength: 60, nullable: false),
                IsActive = table.Column<bool>(type: "bit", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_RetailUnits", x => x.Id);
                table.ForeignKey("FK_RetailUnits_Businesses_BusinessId",
                    x => x.BusinessId, "Businesses", "Id", onDelete: ReferentialAction.Cascade);
            });

        migrationBuilder.CreateIndex("IX_RetailCategories_BusinessId_NameKey",
            "RetailCategories", new[] { "BusinessId", "NameKey" }, unique: true);
        migrationBuilder.CreateIndex("IX_RetailUnits_BusinessId_Code",
            "RetailUnits", new[] { "BusinessId", "Code" }, unique: true);
        migrationBuilder.CreateIndex("IX_RetailUnits_BusinessId_NameKey",
            "RetailUnits", new[] { "BusinessId", "NameKey" }, unique: true);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable("RetailCategories");
        migrationBuilder.DropTable("RetailUnits");
    }
}
