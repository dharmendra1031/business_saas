using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace BusinessSaas.Api.Data.Migrations;

[DbContext(typeof(AppDbContext))]
[Migration("20261128180000_RetailTransferReturn")]
public sealed class RetailTransferReturn : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropCheckConstraint("CK_RetailStockTransferReceipts_Valid", "RetailStockTransferReceipts");
        migrationBuilder.AddCheckConstraint("CK_RetailStockTransferReceipts_Valid", "RetailStockTransferReceipts",
            "[Quantity] > 0 AND [Kind] IN ('Received','Damaged','Missing','Returned')");
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("IF EXISTS (SELECT 1 FROM RetailStockTransferReceipts WHERE Kind = 'Returned') THROW 51000, 'Posted transfer returns prevent downgrade.', 1;");
        migrationBuilder.DropCheckConstraint("CK_RetailStockTransferReceipts_Valid", "RetailStockTransferReceipts");
        migrationBuilder.AddCheckConstraint("CK_RetailStockTransferReceipts_Valid", "RetailStockTransferReceipts",
            "[Quantity] > 0 AND [Kind] IN ('Received','Damaged','Missing')");
    }
}
