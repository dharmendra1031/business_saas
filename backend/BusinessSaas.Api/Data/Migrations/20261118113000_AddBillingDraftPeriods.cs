using BusinessSaas.Api.Data;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BusinessSaas.Api.Data.Migrations;

[DbContext(typeof(AppDbContext))]
[Migration("20261118113000_AddBillingDraftPeriods")]
public sealed class AddBillingDraftPeriods : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        // Existing historical drafts have no known billing period; do not invent one.
        migrationBuilder.AddColumn<DateOnly>(
            name: "PeriodStartDate", table: "SubscriptionBillingDrafts",
            type: "date", nullable: true);
        migrationBuilder.AddColumn<DateOnly>(
            name: "PeriodEndDate", table: "SubscriptionBillingDrafts",
            type: "date", nullable: true);
        migrationBuilder.CreateIndex(
            name: "IX_SubscriptionBillingDrafts_BusinessId_PeriodStartDate",
            table: "SubscriptionBillingDrafts",
            columns: new[] { "BusinessId", "PeriodStartDate" },
            unique: true,
            filter: "[PeriodStartDate] IS NOT NULL");
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropIndex(
            name: "IX_SubscriptionBillingDrafts_BusinessId_PeriodStartDate",
            table: "SubscriptionBillingDrafts");
        migrationBuilder.DropColumn("PeriodStartDate", "SubscriptionBillingDrafts");
        migrationBuilder.DropColumn("PeriodEndDate", "SubscriptionBillingDrafts");
    }
}
