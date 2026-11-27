using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace BusinessSaas.Api.Data.Migrations;

[DbContext(typeof(AppDbContext))]
[Migration("20261127090000_RetailCashRegisterPostingLinks")]
public sealed class RetailCashRegisterPostingLinks : Migration
{
    private static readonly string[] Tables = ["RetailCashSales", "RetailCashReturns", "RetailCreditPayments"];

    protected override void Up(MigrationBuilder migrationBuilder)
    {
        foreach (var table in Tables)
        {
            migrationBuilder.AddColumn<long>("RegisterSessionId", table, type: "bigint", nullable: true);
            migrationBuilder.CreateIndex($"IX_{table}_RegisterSessionId", table, "RegisterSessionId");
            migrationBuilder.AddForeignKey($"FK_{table}_RetailCashRegisterSessions_RegisterSessionId",
                table, "RegisterSessionId", "RetailCashRegisterSessions", principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        foreach (var table in Tables)
            migrationBuilder.Sql($"IF EXISTS (SELECT 1 FROM [{table}] WHERE RegisterSessionId IS NOT NULL) THROW 51000, 'Posted cash register links cannot be discarded by downgrade.', 1;");
        foreach (var table in Tables)
        {
            migrationBuilder.DropForeignKey($"FK_{table}_RetailCashRegisterSessions_RegisterSessionId", table);
            migrationBuilder.DropIndex($"IX_{table}_RegisterSessionId", table);
            migrationBuilder.DropColumn("RegisterSessionId", table);
        }
    }
}
