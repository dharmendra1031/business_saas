using BusinessSaas.Api.Data;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BusinessSaas.Api.Data.Migrations;

[DbContext(typeof(AppDbContext))]
[Migration("20261113090000_AddRetailStockAdjustments")]
public sealed class AddRetailStockAdjustments : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable(
            name: "RetailStockAdjustments",
            columns: table => new
            {
                Id = table.Column<long>(type: "bigint", nullable: false)
                    .Annotation("SqlServer:Identity", "1, 1"),
                BusinessId = table.Column<long>(type: "bigint", nullable: false),
                LocationId = table.Column<long>(type: "bigint", nullable: false),
                ProductId = table.Column<long>(type: "bigint", nullable: false),
                RequestedByUserId = table.Column<long>(type: "bigint", nullable: false),
                ReviewedByUserId = table.Column<long>(type: "bigint", nullable: true),
                PostedByUserId = table.Column<long>(type: "bigint", nullable: true),
                SystemQuantity = table.Column<decimal>(type: "decimal(18,3)", precision: 18, scale: 3, nullable: false),
                CountedQuantity = table.Column<decimal>(type: "decimal(18,3)", precision: 18, scale: 3, nullable: false),
                Reason = table.Column<string>(type: "nvarchar(80)", maxLength: 80, nullable: false),
                Remarks = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                ReviewRemarks = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                Status = table.Column<string>(type: "nvarchar(25)", maxLength: 25, nullable: false),
                RequestedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                ReviewedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                PostedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_RetailStockAdjustments", x => x.Id);
                table.ForeignKey("FK_RetailStockAdjustments_Businesses_BusinessId",
                    x => x.BusinessId, "Businesses", "Id", onDelete: ReferentialAction.Restrict);
                table.ForeignKey("FK_RetailStockAdjustments_BusinessLocations_LocationId",
                    x => x.LocationId, "BusinessLocations", "Id", onDelete: ReferentialAction.Restrict);
                table.ForeignKey("FK_RetailStockAdjustments_RetailProducts_ProductId",
                    x => x.ProductId, "RetailProducts", "Id", onDelete: ReferentialAction.Restrict);
                table.ForeignKey("FK_RetailStockAdjustments_AspNetUsers_RequestedByUserId",
                    x => x.RequestedByUserId, "AspNetUsers", "Id", onDelete: ReferentialAction.Restrict);
                table.ForeignKey("FK_RetailStockAdjustments_AspNetUsers_ReviewedByUserId",
                    x => x.ReviewedByUserId, "AspNetUsers", "Id", onDelete: ReferentialAction.Restrict);
                table.ForeignKey("FK_RetailStockAdjustments_AspNetUsers_PostedByUserId",
                    x => x.PostedByUserId, "AspNetUsers", "Id", onDelete: ReferentialAction.Restrict);
            });

        migrationBuilder.CreateIndex("IX_RetailStockAdjustments_LocationId",
            "RetailStockAdjustments", "LocationId");
        migrationBuilder.CreateIndex("IX_RetailStockAdjustments_ProductId",
            "RetailStockAdjustments", "ProductId");
        migrationBuilder.CreateIndex("IX_RetailStockAdjustments_RequestedByUserId",
            "RetailStockAdjustments", "RequestedByUserId");
        migrationBuilder.CreateIndex("IX_RetailStockAdjustments_ReviewedByUserId",
            "RetailStockAdjustments", "ReviewedByUserId");
        migrationBuilder.CreateIndex("IX_RetailStockAdjustments_PostedByUserId",
            "RetailStockAdjustments", "PostedByUserId");
        migrationBuilder.CreateIndex("IX_RetailStockAdjustments_BusinessId_LocationId_Id",
            "RetailStockAdjustments", new[] { "BusinessId", "LocationId", "Id" });
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable("RetailStockAdjustments");
    }
}
