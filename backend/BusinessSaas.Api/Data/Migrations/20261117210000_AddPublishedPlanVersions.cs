using BusinessSaas.Api.Data;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BusinessSaas.Api.Data.Migrations;

[DbContext(typeof(AppDbContext))]
[Migration("20261117210000_AddPublishedPlanVersions")]
public sealed class AddPublishedPlanVersions : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable(
            name: "PublishedPlanVersions",
            columns: table => new
            {
                Id = table.Column<long>(type: "bigint", nullable: false)
                    .Annotation("SqlServer:Identity", "1, 1"),
                PlanCode = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false),
                Version = table.Column<int>(type: "int", nullable: false),
                SourceDraftRevision = table.Column<int>(type: "int", nullable: false),
                CurrencyCode = table.Column<string>(type: "nvarchar(3)", maxLength: 3, nullable: false),
                MonthlyPrice = table.Column<decimal>(type: "decimal(18,2)", nullable: false),
                QuarterlyPrice = table.Column<decimal>(type: "decimal(18,2)", nullable: false),
                YearlyPrice = table.Column<decimal>(type: "decimal(18,2)", nullable: false),
                MaxLocations = table.Column<int>(type: "int", nullable: true),
                MaxUsers = table.Column<int>(type: "int", nullable: true),
                MaxProducts = table.Column<int>(type: "int", nullable: true),
                PublishedByUserId = table.Column<long>(type: "bigint", nullable: false),
                PublishedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_PublishedPlanVersions", x => x.Id);
                table.ForeignKey("FK_PublishedPlanVersions_AspNetUsers_PublishedByUserId",
                    x => x.PublishedByUserId, "AspNetUsers", "Id", onDelete: ReferentialAction.Restrict);
            });
        migrationBuilder.CreateIndex("IX_PublishedPlanVersions_PublishedByUserId",
            "PublishedPlanVersions", "PublishedByUserId");
        migrationBuilder.CreateIndex("IX_PublishedPlanVersions_PlanCode_Version",
            "PublishedPlanVersions", new[] { "PlanCode", "Version" }, unique: true);
    }

    protected override void Down(MigrationBuilder migrationBuilder) =>
        migrationBuilder.DropTable(name: "PublishedPlanVersions");
}
