using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace BusinessSaas.Api.Data.Migrations;

[DbContext(typeof(AppDbContext))]
[Migration("20261125140000_CustomerCreditLimit")]
public sealed class CustomerCreditLimit : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<decimal>("CreditLimit", "RetailCustomers", type: "decimal(18,2)",
            precision: 18, scale: 2, nullable: false, defaultValue: 0m);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("IF EXISTS (SELECT 1 FROM RetailCustomers WHERE CreditLimit <> 0) THROW 51000, 'Configured customer credit limits cannot be downgraded.', 1;");
        migrationBuilder.DropColumn("CreditLimit", "RetailCustomers");
    }
}
