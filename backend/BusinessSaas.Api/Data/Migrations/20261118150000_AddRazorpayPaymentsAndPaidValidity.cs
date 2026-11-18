using BusinessSaas.Api.Data;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BusinessSaas.Api.Data.Migrations;

[DbContext(typeof(AppDbContext))]
[Migration("20261118150000_AddRazorpayPaymentsAndPaidValidity")]
public sealed class AddRazorpayPaymentsAndPaidValidity : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<string>("ProviderOrderId", "SubscriptionPaymentRequests",
            type: "nvarchar(80)", maxLength: 80, nullable: true);
        migrationBuilder.AddColumn<DateTime>("OrderCreatedAtUtc", "SubscriptionPaymentRequests",
            type: "datetime2", nullable: true);
        migrationBuilder.CreateIndex("IX_SubscriptionPaymentRequests_ProviderOrderId",
            "SubscriptionPaymentRequests", "ProviderOrderId", unique: true,
            filter: "[ProviderOrderId] IS NOT NULL");

        migrationBuilder.AddColumn<DateTime>("PaidStartsAtUtc", "BusinessSubscriptions",
            type: "datetime2", nullable: true);
        migrationBuilder.AddColumn<DateTime>("PaidEndsAtUtc", "BusinessSubscriptions",
            type: "datetime2", nullable: true);
        migrationBuilder.AddColumn<long>("PaidPlanVersionId", "BusinessSubscriptions",
            type: "bigint", nullable: true);
        migrationBuilder.CreateIndex("IX_BusinessSubscriptions_PaidPlanVersionId",
            "BusinessSubscriptions", "PaidPlanVersionId");
        migrationBuilder.AddForeignKey("FK_BusinessSubscriptions_PublishedPlanVersions_PaidPlanVersionId",
            "BusinessSubscriptions", "PaidPlanVersionId", "PublishedPlanVersions",
            principalColumn: "Id", onDelete: ReferentialAction.Restrict);

        migrationBuilder.CreateTable("SubscriptionPayments", table => new
        {
            Id = table.Column<long>(type: "bigint", nullable: false)
                .Annotation("SqlServer:Identity", "1, 1"),
            PaymentRequestId = table.Column<long>(type: "bigint", nullable: false),
            ProviderPaymentId = table.Column<string>(type: "nvarchar(80)", maxLength: 80, nullable: false),
            ProviderOrderId = table.Column<string>(type: "nvarchar(80)", maxLength: 80, nullable: false),
            CurrencyCode = table.Column<string>(type: "nvarchar(3)", maxLength: 3, nullable: false),
            Amount = table.Column<decimal>(type: "decimal(18,2)", nullable: false),
            ActivationStatus = table.Column<string>(type: "nvarchar(24)", maxLength: 24, nullable: false),
            CapturedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false)
        }, constraints: table =>
        {
            table.PrimaryKey("PK_SubscriptionPayments", x => x.Id);
            table.ForeignKey("FK_SubscriptionPayments_SubscriptionPaymentRequests_PaymentRequestId",
                x => x.PaymentRequestId, "SubscriptionPaymentRequests", "Id",
                onDelete: ReferentialAction.Restrict);
        });
        migrationBuilder.CreateIndex("IX_SubscriptionPayments_PaymentRequestId",
            "SubscriptionPayments", "PaymentRequestId", unique: true);
        migrationBuilder.CreateIndex("IX_SubscriptionPayments_ProviderPaymentId",
            "SubscriptionPayments", "ProviderPaymentId", unique: true);

        migrationBuilder.CreateTable("RazorpayWebhookEvents", table => new
        {
            Id = table.Column<long>(type: "bigint", nullable: false)
                .Annotation("SqlServer:Identity", "1, 1"),
            EventId = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
            SubscriptionPaymentId = table.Column<long>(type: "bigint", nullable: false),
            ReceivedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false)
        }, constraints: table =>
        {
            table.PrimaryKey("PK_RazorpayWebhookEvents", x => x.Id);
            table.ForeignKey("FK_RazorpayWebhookEvents_SubscriptionPayments_SubscriptionPaymentId",
                x => x.SubscriptionPaymentId, "SubscriptionPayments", "Id",
                onDelete: ReferentialAction.Restrict);
        });
        migrationBuilder.CreateIndex("IX_RazorpayWebhookEvents_EventId",
            "RazorpayWebhookEvents", "EventId", unique: true);
        migrationBuilder.CreateIndex("IX_RazorpayWebhookEvents_SubscriptionPaymentId",
            "RazorpayWebhookEvents", "SubscriptionPaymentId");
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable("RazorpayWebhookEvents");
        migrationBuilder.DropTable("SubscriptionPayments");
        migrationBuilder.DropForeignKey("FK_BusinessSubscriptions_PublishedPlanVersions_PaidPlanVersionId",
            "BusinessSubscriptions");
        migrationBuilder.DropIndex("IX_BusinessSubscriptions_PaidPlanVersionId", "BusinessSubscriptions");
        migrationBuilder.DropColumn("PaidStartsAtUtc", "BusinessSubscriptions");
        migrationBuilder.DropColumn("PaidEndsAtUtc", "BusinessSubscriptions");
        migrationBuilder.DropColumn("PaidPlanVersionId", "BusinessSubscriptions");
        migrationBuilder.DropIndex("IX_SubscriptionPaymentRequests_ProviderOrderId",
            "SubscriptionPaymentRequests");
        migrationBuilder.DropColumn("ProviderOrderId", "SubscriptionPaymentRequests");
        migrationBuilder.DropColumn("OrderCreatedAtUtc", "SubscriptionPaymentRequests");
    }
}