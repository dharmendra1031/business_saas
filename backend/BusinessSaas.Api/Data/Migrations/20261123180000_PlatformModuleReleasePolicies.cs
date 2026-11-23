using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace BusinessSaas.Api.Data.Migrations;

[DbContext(typeof(AppDbContext))]
[Migration("20261123180000_PlatformModuleReleasePolicies")]
public sealed class PlatformModuleReleasePolicies : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable(
            name: "PlatformModulePolicies",
            columns: table => new
            {
                Id = table.Column<long>(type: "bigint", nullable: false).Annotation("SqlServer:Identity", "1, 1"),
                Code = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: false),
                ReleaseState = table.Column<string>(type: "nvarchar(24)", maxLength: 24, nullable: false),
                Revision = table.Column<int>(type: "int", nullable: false),
                UpdatedByUserId = table.Column<long>(type: "bigint", nullable: false),
                UpdatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false)
            },
            constraints: table => table.PrimaryKey("PK_PlatformModulePolicies", x => x.Id));
        migrationBuilder.CreateIndex("IX_PlatformModulePolicies_Code",
            "PlatformModulePolicies", "Code", unique: true);
    }

    protected override void Down(MigrationBuilder migrationBuilder) =>
        migrationBuilder.DropTable("PlatformModulePolicies");
}
