using BusinessSaas.Api.Data;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BusinessSaas.Api.Data.Migrations;

[DbContext(typeof(AppDbContext))]
[Migration("20261117200000_AddPlanDraftsAndSubscriptionSuspension")]
public sealed class AddPlanDraftsAndSubscriptionSuspension : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<string>(
            name: "StatusBeforeSuspension", table: "BusinessSubscriptions",
            type: "nvarchar(20)", maxLength: 20, nullable: true);

        migrationBuilder.CreateTable(
            name: "SubscriptionPlanDrafts",
            columns: table => new
            {
                Id = table.Column<long>(type: "bigint", nullable: false)
                    .Annotation("SqlServer:Identity", "1, 1"),
                PlanCode = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false),
                Revision = table.Column<int>(type: "int", nullable: false),
                CurrencyCode = table.Column<string>(type: "nvarchar(3)", maxLength: 3, nullable: true),
                MonthlyPrice = table.Column<decimal>(type: "decimal(18,2)", nullable: true),
                QuarterlyPrice = table.Column<decimal>(type: "decimal(18,2)", nullable: true),
                YearlyPrice = table.Column<decimal>(type: "decimal(18,2)", nullable: true),
                MaxLocations = table.Column<int>(type: "int", nullable: true),
                MaxUsers = table.Column<int>(type: "int", nullable: true),
                MaxProducts = table.Column<int>(type: "int", nullable: true),
                UpdatedByUserId = table.Column<long>(type: "bigint", nullable: false),
                UpdatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_SubscriptionPlanDrafts", x => x.Id);
                table.ForeignKey(
                    name: "FK_SubscriptionPlanDrafts_AspNetUsers_UpdatedByUserId",
                    column: x => x.UpdatedByUserId, principalTable: "AspNetUsers",
                    principalColumn: "Id", onDelete: ReferentialAction.Restrict);
            });

        migrationBuilder.CreateIndex("IX_SubscriptionPlanDrafts_PlanCode",
            "SubscriptionPlanDrafts", "PlanCode", unique: true);
        migrationBuilder.CreateIndex("IX_SubscriptionPlanDrafts_UpdatedByUserId",
            "SubscriptionPlanDrafts", "UpdatedByUserId");
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable("SubscriptionPlanDrafts");
        migrationBuilder.DropColumn("StatusBeforeSuspension", "BusinessSubscriptions");
    }
}
