using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace BusinessSaas.Api.Data.Migrations;

[DbContext(typeof(AppDbContext))]
[Migration("20261125150000_CreditSaleDueDate")]
public sealed class CreditSaleDueDate : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<DateOnly>("CreditDueDate", "RetailCashSales", type: "date", nullable: true);
        migrationBuilder.CreateIndex("IX_RetailCashSales_BusinessId_CustomerId_CreditDueDate",
            "RetailCashSales", new[] { "BusinessId", "CustomerId", "CreditDueDate" });
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("IF EXISTS (SELECT 1 FROM RetailCashSales WHERE CreditDueDate IS NOT NULL) THROW 51000, 'Posted credit invoices cannot be downgraded.', 1;");
        migrationBuilder.DropIndex("IX_RetailCashSales_BusinessId_CustomerId_CreditDueDate", "RetailCashSales");
        migrationBuilder.DropColumn("CreditDueDate", "RetailCashSales");
    }
}
