using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace BusinessSaas.Api.Data.Migrations;

[DbContext(typeof(AppDbContext))]
[Migration("20261128110000_GlobalProductIdentityIndex")]
public sealed class GlobalProductIdentityIndex : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        // Do not discard or merge existing products. Fail explicitly if review is needed.
        migrationBuilder.Sql("""
            IF EXISTS (
                SELECT 1 FROM GlobalProducts WHERE Barcode IS NULL
                GROUP BY NameKey, UPPER(Brand), UPPER(PackSize), Unit
                HAVING COUNT(*) > 1
            ) THROW 51000, 'Resolve duplicate barcode-less global products before applying the identity index.', 1;
            """);
        migrationBuilder.AddColumn<string>("BrandKey", "GlobalProducts", type: "nvarchar(80)",
            maxLength: 80, nullable: true, computedColumnSql: "UPPER([Brand])", stored: true);
        migrationBuilder.AddColumn<string>("PackSizeKey", "GlobalProducts", type: "nvarchar(40)",
            maxLength: 40, nullable: true, computedColumnSql: "UPPER([PackSize])", stored: true);
        migrationBuilder.CreateIndex("IX_GlobalProducts_NameKey_BrandKey_PackSizeKey_Unit", "GlobalProducts",
            new[] { "NameKey", "BrandKey", "PackSizeKey", "Unit" }, unique: true, filter: "[Barcode] IS NULL");
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropIndex("IX_GlobalProducts_NameKey_BrandKey_PackSizeKey_Unit", "GlobalProducts");
        migrationBuilder.DropColumn("BrandKey", "GlobalProducts");
        migrationBuilder.DropColumn("PackSizeKey", "GlobalProducts");
    }
}
