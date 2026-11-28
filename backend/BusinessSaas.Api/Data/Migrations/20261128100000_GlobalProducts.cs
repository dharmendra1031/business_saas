using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace BusinessSaas.Api.Data.Migrations;

[DbContext(typeof(AppDbContext))]
[Migration("20261128100000_GlobalProducts")]
public sealed class GlobalProducts : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable("GlobalProducts", table => new
        {
            Id = table.Column<long>(type: "bigint", nullable: false).Annotation("SqlServer:Identity", "1, 1"),
            Name = table.Column<string>(type: "nvarchar(150)", maxLength: 150, nullable: false),
            NameKey = table.Column<string>(type: "nvarchar(150)", maxLength: 150, nullable: false),
            Barcode = table.Column<string>(type: "nvarchar(14)", maxLength: 14, nullable: true),
            Brand = table.Column<string>(type: "nvarchar(80)", maxLength: 80, nullable: false),
            Category = table.Column<string>(type: "nvarchar(80)", maxLength: 80, nullable: false),
            PackSize = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: false),
            Unit = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
            IsActive = table.Column<bool>(type: "bit", nullable: false),
            CreatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false)
        }, constraints: table => table.PrimaryKey("PK_GlobalProducts", x => x.Id));
        migrationBuilder.CreateIndex("IX_GlobalProducts_Barcode", "GlobalProducts", "Barcode",
            unique: true, filter: "[Barcode] IS NOT NULL");
        migrationBuilder.CreateIndex("IX_GlobalProducts_NameKey_Id", "GlobalProducts", new[] { "NameKey", "Id" });
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("IF EXISTS (SELECT 1 FROM GlobalProducts) THROW 51000, 'Global catalogue data cannot be discarded by downgrade.', 1;");
        migrationBuilder.DropTable("GlobalProducts");
    }
}
