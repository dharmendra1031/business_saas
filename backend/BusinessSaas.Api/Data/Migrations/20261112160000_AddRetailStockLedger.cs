using BusinessSaas.Api.Data;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BusinessSaas.Api.Data.Migrations;

[DbContext(typeof(AppDbContext))]
[Migration("20261112160000_AddRetailStockLedger")]
public sealed class AddRetailStockLedger : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable(
            name: "RetailStockBalances",
            columns: table => new
            {
                Id = table.Column<long>(type: "bigint", nullable: false)
                    .Annotation("SqlServer:Identity", "1, 1"),
                BusinessId = table.Column<long>(type: "bigint", nullable: false),
                LocationId = table.Column<long>(type: "bigint", nullable: false),
                ProductId = table.Column<long>(type: "bigint", nullable: false),
                Quantity = table.Column<decimal>(type: "decimal(18,3)", precision: 18, scale: 3, nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_RetailStockBalances", x => x.Id);
                table.CheckConstraint("CK_RetailStockBalances_Quantity", "[Quantity] >= 0");
                table.ForeignKey("FK_RetailStockBalances_Businesses_BusinessId",
                    x => x.BusinessId, "Businesses", "Id", onDelete: ReferentialAction.Restrict);
                table.ForeignKey("FK_RetailStockBalances_BusinessLocations_LocationId",
                    x => x.LocationId, "BusinessLocations", "Id", onDelete: ReferentialAction.Restrict);
                table.ForeignKey("FK_RetailStockBalances_RetailProducts_ProductId",
                    x => x.ProductId, "RetailProducts", "Id", onDelete: ReferentialAction.Restrict);
            });

        migrationBuilder.CreateIndex(
            name: "IX_RetailStockBalances_BusinessId_LocationId_ProductId",
            table: "RetailStockBalances",
            columns: new[] { "BusinessId", "LocationId", "ProductId" }, unique: true);
        migrationBuilder.CreateIndex("IX_RetailStockBalances_LocationId",
            "RetailStockBalances", "LocationId");
        migrationBuilder.CreateIndex("IX_RetailStockBalances_ProductId",
            "RetailStockBalances", "ProductId");

        migrationBuilder.CreateTable(
            name: "RetailStockMovements",
            columns: table => new
            {
                Id = table.Column<long>(type: "bigint", nullable: false)
                    .Annotation("SqlServer:Identity", "1, 1"),
                BusinessId = table.Column<long>(type: "bigint", nullable: false),
                LocationId = table.Column<long>(type: "bigint", nullable: false),
                ProductId = table.Column<long>(type: "bigint", nullable: false),
                ActorUserId = table.Column<long>(type: "bigint", nullable: false),
                SourceType = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false),
                SourceId = table.Column<long>(type: "bigint", nullable: true),
                QuantityChange = table.Column<decimal>(type: "decimal(18,3)", precision: 18, scale: 3, nullable: false),
                BalanceAfter = table.Column<decimal>(type: "decimal(18,3)", precision: 18, scale: 3, nullable: false),
                PostedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_RetailStockMovements", x => x.Id);
                table.ForeignKey("FK_RetailStockMovements_Businesses_BusinessId",
                    x => x.BusinessId, "Businesses", "Id", onDelete: ReferentialAction.Restrict);
                table.ForeignKey("FK_RetailStockMovements_BusinessLocations_LocationId",
                    x => x.LocationId, "BusinessLocations", "Id", onDelete: ReferentialAction.Restrict);
                table.ForeignKey("FK_RetailStockMovements_RetailProducts_ProductId",
                    x => x.ProductId, "RetailProducts", "Id", onDelete: ReferentialAction.Restrict);
                table.ForeignKey("FK_RetailStockMovements_AspNetUsers_ActorUserId",
                    x => x.ActorUserId, "AspNetUsers", "Id", onDelete: ReferentialAction.Restrict);
            });

        migrationBuilder.CreateIndex("IX_RetailStockMovements_ActorUserId",
            "RetailStockMovements", "ActorUserId");
        migrationBuilder.CreateIndex("IX_RetailStockMovements_LocationId",
            "RetailStockMovements", "LocationId");
        migrationBuilder.CreateIndex("IX_RetailStockMovements_ProductId",
            "RetailStockMovements", "ProductId");
        migrationBuilder.CreateIndex("IX_RetailStockMovements_BusinessId_LocationId_ProductId_Id",
            "RetailStockMovements", new[] { "BusinessId", "LocationId", "ProductId", "Id" });
        migrationBuilder.CreateIndex("IX_RetailStockMovements_BusinessId_LocationId_ProductId_SourceType",
            "RetailStockMovements", new[] { "BusinessId", "LocationId", "ProductId", "SourceType" },
            unique: true, filter: "[SourceType] = 'Opening'");
        migrationBuilder.CreateIndex("IX_RetailStockMovements_BusinessId_SourceType_SourceId",
            "RetailStockMovements", new[] { "BusinessId", "SourceType", "SourceId" },
            unique: true, filter: "[SourceId] IS NOT NULL");
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable("RetailStockMovements");
        migrationBuilder.DropTable("RetailStockBalances");
    }
}
