using BusinessSaas.Api.Data;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BusinessSaas.Api.Data.Migrations;

[DbContext(typeof(AppDbContext))]
[Migration("20261118100000_AddSubscriptionBillingDrafts")]
public sealed class AddSubscriptionBillingDrafts : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable(
            name: "SubscriptionBillingDrafts",
            columns: table => new
            {
                Id = table.Column<long>(type: "bigint", nullable: false)
                    .Annotation("SqlServer:Identity", "1, 1"),
                BusinessId = table.Column<long>(type: "bigint", nullable: false),
                PublishedPlanVersionId = table.Column<long>(type: "bigint", nullable: false),
                PlanCode = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false),
                BillingCycle = table.Column<string>(type: "nvarchar(12)", maxLength: 12, nullable: false),
                CurrencyCode = table.Column<string>(type: "nvarchar(3)", maxLength: 3, nullable: false),
                Amount = table.Column<decimal>(type: "decimal(18,2)", nullable: false),
                RequestKey = table.Column<string>(type: "nvarchar(80)", maxLength: 80, nullable: false),
                CreatedByUserId = table.Column<long>(type: "bigint", nullable: false),
                CreatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_SubscriptionBillingDrafts", x => x.Id);
                table.ForeignKey("FK_SubscriptionBillingDrafts_Businesses_BusinessId",
                    x => x.BusinessId, "Businesses", "Id", onDelete: ReferentialAction.Restrict);
                table.ForeignKey("FK_SubscriptionBillingDrafts_PublishedPlanVersions_PublishedPlanVersionId",
                    x => x.PublishedPlanVersionId, "PublishedPlanVersions", "Id",
                    onDelete: ReferentialAction.Restrict);
                table.ForeignKey("FK_SubscriptionBillingDrafts_AspNetUsers_CreatedByUserId",
                    x => x.CreatedByUserId, "AspNetUsers", "Id", onDelete: ReferentialAction.Restrict);
            });
        migrationBuilder.CreateIndex("IX_SubscriptionBillingDrafts_BusinessId_RequestKey",
            "SubscriptionBillingDrafts", new[] { "BusinessId", "RequestKey" }, unique: true);
        migrationBuilder.CreateIndex("IX_SubscriptionBillingDrafts_CreatedAtUtc_Id",
            "SubscriptionBillingDrafts", new[] { "CreatedAtUtc", "Id" });
        migrationBuilder.CreateIndex("IX_SubscriptionBillingDrafts_CreatedByUserId",
            "SubscriptionBillingDrafts", "CreatedByUserId");
        migrationBuilder.CreateIndex("IX_SubscriptionBillingDrafts_PublishedPlanVersionId",
            "SubscriptionBillingDrafts", "PublishedPlanVersionId");
    }

    protected override void Down(MigrationBuilder migrationBuilder) =>
        migrationBuilder.DropTable("SubscriptionBillingDrafts");
}
