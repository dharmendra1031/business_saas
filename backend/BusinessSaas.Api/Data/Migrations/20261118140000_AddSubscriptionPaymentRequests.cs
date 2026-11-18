using BusinessSaas.Api.Data;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BusinessSaas.Api.Data.Migrations;

[DbContext(typeof(AppDbContext))]
[Migration("20261118140000_AddSubscriptionPaymentRequests")]
public sealed class AddSubscriptionPaymentRequests : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable(
            name: "SubscriptionPaymentRequests",
            columns: table => new
            {
                Id = table.Column<long>(type: "bigint", nullable: false)
                    .Annotation("SqlServer:Identity", "1, 1"),
                BillingDraftId = table.Column<long>(type: "bigint", nullable: false),
                BusinessId = table.Column<long>(type: "bigint", nullable: false),
                CurrencyCode = table.Column<string>(type: "nvarchar(3)", maxLength: 3, nullable: false),
                Amount = table.Column<decimal>(type: "decimal(18,2)", nullable: false),
                RequestKey = table.Column<string>(type: "nvarchar(80)", maxLength: 80, nullable: false),
                Status = table.Column<string>(type: "nvarchar(24)", maxLength: 24, nullable: false),
                CreatedByUserId = table.Column<long>(type: "bigint", nullable: false),
                CreatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_SubscriptionPaymentRequests", x => x.Id);
                table.ForeignKey("FK_SubscriptionPaymentRequests_SubscriptionBillingDrafts_BillingDraftId",
                    x => x.BillingDraftId, "SubscriptionBillingDrafts", "Id",
                    onDelete: ReferentialAction.Restrict);
                table.ForeignKey("FK_SubscriptionPaymentRequests_Businesses_BusinessId",
                    x => x.BusinessId, "Businesses", "Id", onDelete: ReferentialAction.Restrict);
                table.ForeignKey("FK_SubscriptionPaymentRequests_AspNetUsers_CreatedByUserId",
                    x => x.CreatedByUserId, "AspNetUsers", "Id", onDelete: ReferentialAction.Restrict);
            });

        migrationBuilder.CreateIndex("IX_SubscriptionPaymentRequests_BillingDraftId",
            "SubscriptionPaymentRequests", "BillingDraftId", unique: true);
        migrationBuilder.CreateIndex("IX_SubscriptionPaymentRequests_BusinessId_RequestKey",
            "SubscriptionPaymentRequests", new[] { "BusinessId", "RequestKey" }, unique: true);
        migrationBuilder.CreateIndex("IX_SubscriptionPaymentRequests_CreatedAtUtc_Id",
            "SubscriptionPaymentRequests", new[] { "CreatedAtUtc", "Id" });
        migrationBuilder.CreateIndex("IX_SubscriptionPaymentRequests_CreatedByUserId",
            "SubscriptionPaymentRequests", "CreatedByUserId");
    }

    protected override void Down(MigrationBuilder migrationBuilder) =>
        migrationBuilder.DropTable("SubscriptionPaymentRequests");
}
