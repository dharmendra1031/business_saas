using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace BusinessSaas.Api.Data.Migrations;

[DbContext(typeof(AppDbContext))]
[Migration("20261128190000_RetailTransferDraft")]
public sealed class RetailTransferDraft : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropCheckConstraint("CK_RetailStockTransfers_Valid", "RetailStockTransfers");
        migrationBuilder.AddCheckConstraint("CK_RetailStockTransfers_Valid", "RetailStockTransfers", "[FromLocationId] <> [ToLocationId] AND [Status] IN ('Draft','Requested','Approved','InTransit','Completed','Cancelled')");
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("IF EXISTS (SELECT 1 FROM RetailStockTransfers WHERE Status = 'Draft') THROW 51000, 'Saved transfer drafts prevent downgrade.', 1;");
        migrationBuilder.DropCheckConstraint("CK_RetailStockTransfers_Valid", "RetailStockTransfers");
        migrationBuilder.AddCheckConstraint("CK_RetailStockTransfers_Valid", "RetailStockTransfers", "[FromLocationId] <> [ToLocationId] AND [Status] IN ('Requested','Approved','InTransit','Completed','Cancelled')");
    }
}
