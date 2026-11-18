using BusinessSaas.Api.Data;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BusinessSaas.Api.Data.Migrations;

[DbContext(typeof(AppDbContext))]
[Migration("20261118170000_AddVerifiedSubscriptionRefunds")]
public sealed class AddVerifiedSubscriptionRefunds : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable(
            name: "SubscriptionRefunds",
            columns: table => new
            {
                Id = table.Column<long>(type: "bigint", nullable: false)
                    .Annotation("SqlServer:Identity", "1, 1"),
                SubscriptionPaymentId = table.Column<long>(type: "bigint", nullable: false),
                ProviderRefundId = table.Column<string>(type: "nvarchar(80)",
                    maxLength: 80, nullable: false),
                CurrencyCode = table.Column<string>(type: "nvarchar(3)",
                    maxLength: 3, nullable: false),
                Amount = table.Column<decimal>(type: "decimal(18,2)", nullable: false),
                Status = table.Column<string>(type: "nvarchar(24)",
                    maxLength: 24, nullable: false),
                ObservedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_SubscriptionRefunds", x => x.Id);
                table.ForeignKey("FK_SubscriptionRefunds_SubscriptionPayments_SubscriptionPaymentId",
                    x => x.SubscriptionPaymentId, "SubscriptionPayments", "Id",
                    onDelete: ReferentialAction.Restrict);
            });
        migrationBuilder.CreateIndex("IX_SubscriptionRefunds_ProviderRefundId",
            "SubscriptionRefunds", "ProviderRefundId", unique: true);
        migrationBuilder.CreateIndex("IX_SubscriptionRefunds_SubscriptionPaymentId",
            "SubscriptionRefunds", "SubscriptionPaymentId");
    }

    protected override void Down(MigrationBuilder migrationBuilder) =>
        migrationBuilder.DropTable("SubscriptionRefunds");
}
