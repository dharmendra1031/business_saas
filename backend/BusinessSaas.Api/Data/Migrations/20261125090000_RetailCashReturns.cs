using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace BusinessSaas.Api.Data.Migrations;

[DbContext(typeof(AppDbContext))]
[Migration("20261125090000_RetailCashReturns")]
public sealed class RetailCashReturns : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable("RetailCashReturns", table => new
        {
            Id = table.Column<long>(type: "bigint", nullable: false).Annotation("SqlServer:Identity", "1, 1"),
            BusinessId = table.Column<long>(type: "bigint", nullable: false),
            SaleId = table.Column<long>(type: "bigint", nullable: false),
            ActorUserId = table.Column<long>(type: "bigint", nullable: false),
            RequestKey = table.Column<string>(type: "nvarchar(80)", maxLength: 80, nullable: false),
            Reason = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
            Total = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
            PostedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false)
        }, constraints: table =>
        {
            table.PrimaryKey("PK_RetailCashReturns", x => x.Id);
            table.ForeignKey("FK_RetailCashReturns_Businesses_BusinessId", x => x.BusinessId, "Businesses", "Id", onDelete: ReferentialAction.Restrict);
            table.ForeignKey("FK_RetailCashReturns_RetailCashSales_SaleId", x => x.SaleId, "RetailCashSales", "Id", onDelete: ReferentialAction.Restrict);
            table.ForeignKey("FK_RetailCashReturns_AspNetUsers_ActorUserId", x => x.ActorUserId, "AspNetUsers", "Id", onDelete: ReferentialAction.Restrict);
        });
        migrationBuilder.CreateTable("RetailCashReturnLines", table => new
        {
            Id = table.Column<long>(type: "bigint", nullable: false).Annotation("SqlServer:Identity", "1, 1"),
            ReturnId = table.Column<long>(type: "bigint", nullable: false),
            SaleLineId = table.Column<long>(type: "bigint", nullable: false),
            Quantity = table.Column<decimal>(type: "decimal(18,3)", precision: 18, scale: 3, nullable: false),
            Amount = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
            Restock = table.Column<bool>(type: "bit", nullable: false)
        }, constraints: table =>
        {
            table.PrimaryKey("PK_RetailCashReturnLines", x => x.Id);
            table.ForeignKey("FK_RetailCashReturnLines_RetailCashReturns_ReturnId", x => x.ReturnId, "RetailCashReturns", "Id", onDelete: ReferentialAction.Restrict);
            table.ForeignKey("FK_RetailCashReturnLines_RetailCashSaleLine_SaleLineId", x => x.SaleLineId, "RetailCashSaleLine", "Id", onDelete: ReferentialAction.Restrict);
        });
        migrationBuilder.CreateIndex("IX_RetailCashReturns_ActorUserId", "RetailCashReturns", "ActorUserId");
        migrationBuilder.CreateIndex("IX_RetailCashReturns_SaleId", "RetailCashReturns", "SaleId");
        migrationBuilder.CreateIndex("IX_RetailCashReturns_BusinessId_RequestKey", "RetailCashReturns", new[] { "BusinessId", "RequestKey" }, unique: true);
        migrationBuilder.CreateIndex("IX_RetailCashReturnLines_SaleLineId", "RetailCashReturnLines", "SaleLineId");
        migrationBuilder.CreateIndex("IX_RetailCashReturnLines_ReturnId_SaleLineId", "RetailCashReturnLines", new[] { "ReturnId", "SaleLineId" }, unique: true);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("IF EXISTS (SELECT 1 FROM RetailCashReturns) THROW 51000, 'Posted returns cannot be discarded by downgrade.', 1;");
        migrationBuilder.DropTable("RetailCashReturnLines");
        migrationBuilder.DropTable("RetailCashReturns");
    }
}
