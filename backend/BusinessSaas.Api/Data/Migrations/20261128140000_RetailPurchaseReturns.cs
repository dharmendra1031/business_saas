using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace BusinessSaas.Api.Data.Migrations;

[DbContext(typeof(AppDbContext))]
[Migration("20261128140000_RetailPurchaseReturns")]
public sealed class RetailPurchaseReturns : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable(
            name: "RetailPurchaseReturns",
            columns: table => new
            {
                Id = table.Column<long>(type: "bigint", nullable: false).Annotation("SqlServer:Identity", "1, 1"),
                BusinessId = table.Column<long>(type: "bigint", nullable: false),
                ReceivingLineId = table.Column<long>(type: "bigint", nullable: false),
                ActorUserId = table.Column<long>(type: "bigint", nullable: false),
                Quantity = table.Column<decimal>(type: "decimal(18,3)", precision: 18, scale: 3, nullable: false),
                RequestKey = table.Column<string>(type: "nvarchar(80)", maxLength: 80, nullable: false),
                Reason = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                PostedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_RetailPurchaseReturns", x => x.Id);
                table.ForeignKey("FK_RetailPurchaseReturns_Businesses_BusinessId", x => x.BusinessId, "Businesses", "Id", onDelete: ReferentialAction.Restrict);
                table.ForeignKey("FK_RetailPurchaseReturns_RetailReceivingLines_ReceivingLineId", x => x.ReceivingLineId, "RetailReceivingLines", "Id", onDelete: ReferentialAction.Restrict);
                table.ForeignKey("FK_RetailPurchaseReturns_AspNetUsers_ActorUserId", x => x.ActorUserId, "AspNetUsers", "Id", onDelete: ReferentialAction.Restrict);
                table.CheckConstraint("CK_RetailPurchaseReturns_Quantity", "[Quantity] > 0");
            });
        migrationBuilder.CreateIndex("IX_RetailPurchaseReturns_ActorUserId", "RetailPurchaseReturns", "ActorUserId");
        migrationBuilder.CreateIndex("IX_RetailPurchaseReturns_BusinessId_RequestKey", "RetailPurchaseReturns",
            new[] { "BusinessId", "RequestKey" }, unique: true);
        migrationBuilder.CreateIndex("IX_RetailPurchaseReturns_ReceivingLineId_BusinessId", "RetailPurchaseReturns",
            new[] { "ReceivingLineId", "BusinessId" });
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("IF EXISTS (SELECT 1 FROM RetailPurchaseReturns) THROW 51000, 'Posted purchase returns prevent downgrade.', 1;");
        migrationBuilder.DropTable("RetailPurchaseReturns");
    }
}
