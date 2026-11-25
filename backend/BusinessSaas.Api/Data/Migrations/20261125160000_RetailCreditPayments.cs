using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace BusinessSaas.Api.Data.Migrations;

[DbContext(typeof(AppDbContext))]
[Migration("20261125160000_RetailCreditPayments")]
public sealed class RetailCreditPayments : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable("RetailCreditPayments", table => new
        {
            Id = table.Column<long>(type: "bigint", nullable: false).Annotation("SqlServer:Identity", "1, 1"),
            BusinessId = table.Column<long>(type: "bigint", nullable: false),
            SaleId = table.Column<long>(type: "bigint", nullable: false),
            ActorUserId = table.Column<long>(type: "bigint", nullable: false),
            RequestKey = table.Column<string>(type: "nvarchar(80)", maxLength: 80, nullable: false),
            Amount = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
            PostedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false)
        }, constraints: table =>
        {
            table.PrimaryKey("PK_RetailCreditPayments", x => x.Id);
            table.ForeignKey("FK_RetailCreditPayments_Businesses_BusinessId", x => x.BusinessId, "Businesses", "Id", onDelete: ReferentialAction.Restrict);
            table.ForeignKey("FK_RetailCreditPayments_RetailCashSales_SaleId", x => x.SaleId, "RetailCashSales", "Id", onDelete: ReferentialAction.Restrict);
            table.ForeignKey("FK_RetailCreditPayments_AspNetUsers_ActorUserId", x => x.ActorUserId, "AspNetUsers", "Id", onDelete: ReferentialAction.Restrict);
        });
        migrationBuilder.CreateIndex("IX_RetailCreditPayments_ActorUserId", "RetailCreditPayments", "ActorUserId");
        migrationBuilder.CreateIndex("IX_RetailCreditPayments_SaleId", "RetailCreditPayments", "SaleId");
        migrationBuilder.CreateIndex("IX_RetailCreditPayments_BusinessId_RequestKey", "RetailCreditPayments", new[] { "BusinessId", "RequestKey" }, unique: true);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("IF EXISTS (SELECT 1 FROM RetailCreditPayments) THROW 51000, 'Posted collections cannot be discarded by downgrade.', 1;");
        migrationBuilder.DropTable("RetailCreditPayments");
    }
}
