using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace BusinessSaas.Api.Data.Migrations;

[DbContext(typeof(AppDbContext))]
[Migration("20261128160000_RetailRegisterClosing")]
public sealed class RetailRegisterClosing : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<decimal>("ClosingExpectedCash", "RetailCashRegisterSessions", type: "decimal(18,2)", precision: 18, scale: 2, nullable: true);
        migrationBuilder.AddColumn<decimal>("ClosingActualCash", "RetailCashRegisterSessions", type: "decimal(18,2)", precision: 18, scale: 2, nullable: true);
        migrationBuilder.AddColumn<long>("ClosedByUserId", "RetailCashRegisterSessions", type: "bigint", nullable: true);
        migrationBuilder.AddColumn<string>("ClosingRequestKey", "RetailCashRegisterSessions", type: "nvarchar(80)", maxLength: 80, nullable: true);
        migrationBuilder.AddColumn<string>("ClosingReason", "RetailCashRegisterSessions", type: "nvarchar(200)", maxLength: 200, nullable: true);
        migrationBuilder.CreateIndex("IX_RetailCashRegisterSessions_ClosedByUserId", "RetailCashRegisterSessions", "ClosedByUserId");
        migrationBuilder.AddForeignKey("FK_RetailCashRegisterSessions_AspNetUsers_ClosedByUserId",
            "RetailCashRegisterSessions", "ClosedByUserId", "AspNetUsers", principalColumn: "Id", onDelete: ReferentialAction.Restrict);
        migrationBuilder.AddCheckConstraint("CK_RetailCashRegisterSessions_Closing", "RetailCashRegisterSessions", "[ClosingExpectedCash] IS NULL AND [ClosingActualCash] IS NULL AND [ClosedByUserId] IS NULL AND [ClosingRequestKey] IS NULL AND [ClosingReason] IS NULL OR ([ClosedAtUtc] IS NOT NULL AND [ClosingExpectedCash] IS NOT NULL AND [ClosingActualCash] IS NOT NULL AND [ClosingActualCash] >= 0 AND [ClosedByUserId] IS NOT NULL AND [ClosingRequestKey] IS NOT NULL AND [ClosingReason] IS NOT NULL)");
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("IF EXISTS (SELECT 1 FROM RetailCashRegisterSessions WHERE ClosingRequestKey IS NOT NULL) THROW 51000, 'Posted register closings prevent downgrade.', 1;");
        migrationBuilder.DropCheckConstraint("CK_RetailCashRegisterSessions_Closing", "RetailCashRegisterSessions");
        migrationBuilder.DropForeignKey("FK_RetailCashRegisterSessions_AspNetUsers_ClosedByUserId", "RetailCashRegisterSessions");
        migrationBuilder.DropIndex("IX_RetailCashRegisterSessions_ClosedByUserId", "RetailCashRegisterSessions");
        migrationBuilder.DropColumn("ClosingExpectedCash", "RetailCashRegisterSessions");
        migrationBuilder.DropColumn("ClosingActualCash", "RetailCashRegisterSessions");
        migrationBuilder.DropColumn("ClosedByUserId", "RetailCashRegisterSessions");
        migrationBuilder.DropColumn("ClosingRequestKey", "RetailCashRegisterSessions");
        migrationBuilder.DropColumn("ClosingReason", "RetailCashRegisterSessions");
    }
}
