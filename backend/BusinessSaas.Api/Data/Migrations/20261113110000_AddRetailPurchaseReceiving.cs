using BusinessSaas.Api.Data;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BusinessSaas.Api.Data.Migrations;

[DbContext(typeof(AppDbContext))]
[Migration("20261113110000_AddRetailPurchaseReceiving")]
public sealed class AddRetailPurchaseReceiving : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable(
            name: "RetailPurchaseOrders",
            columns: table => new
            {
                Id = table.Column<long>(type: "bigint", nullable: false)
                    .Annotation("SqlServer:Identity", "1, 1"),
                BusinessId = table.Column<long>(type: "bigint", nullable: false),
                LocationId = table.Column<long>(type: "bigint", nullable: false),
                SupplierId = table.Column<long>(type: "bigint", nullable: false),
                CreatedByUserId = table.Column<long>(type: "bigint", nullable: false),
                ApprovedByUserId = table.Column<long>(type: "bigint", nullable: true),
                Status = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                CreatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                ApprovedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_RetailPurchaseOrders", x => x.Id);
                table.ForeignKey("FK_RetailPurchaseOrders_Businesses_BusinessId",
                    x => x.BusinessId, "Businesses", "Id", onDelete: ReferentialAction.Restrict);
                table.ForeignKey("FK_RetailPurchaseOrders_BusinessLocations_LocationId",
                    x => x.LocationId, "BusinessLocations", "Id", onDelete: ReferentialAction.Restrict);
                table.ForeignKey("FK_RetailPurchaseOrders_RetailSuppliers_SupplierId",
                    x => x.SupplierId, "RetailSuppliers", "Id", onDelete: ReferentialAction.Restrict);
                table.ForeignKey("FK_RetailPurchaseOrders_AspNetUsers_CreatedByUserId",
                    x => x.CreatedByUserId, "AspNetUsers", "Id", onDelete: ReferentialAction.Restrict);
                table.ForeignKey("FK_RetailPurchaseOrders_AspNetUsers_ApprovedByUserId",
                    x => x.ApprovedByUserId, "AspNetUsers", "Id", onDelete: ReferentialAction.Restrict);
            });

        migrationBuilder.CreateIndex("IX_RetailPurchaseOrders_ApprovedByUserId",
            "RetailPurchaseOrders", "ApprovedByUserId");
        migrationBuilder.CreateIndex("IX_RetailPurchaseOrders_CreatedByUserId",
            "RetailPurchaseOrders", "CreatedByUserId");
        migrationBuilder.CreateIndex("IX_RetailPurchaseOrders_LocationId",
            "RetailPurchaseOrders", "LocationId");
        migrationBuilder.CreateIndex("IX_RetailPurchaseOrders_SupplierId",
            "RetailPurchaseOrders", "SupplierId");
        migrationBuilder.CreateIndex("IX_RetailPurchaseOrders_BusinessId_Status",
            "RetailPurchaseOrders", new[] { "BusinessId", "Status" });

        migrationBuilder.CreateTable(
            name: "RetailPurchaseOrderLines",
            columns: table => new
            {
                Id = table.Column<long>(type: "bigint", nullable: false)
                    .Annotation("SqlServer:Identity", "1, 1"),
                PurchaseOrderId = table.Column<long>(type: "bigint", nullable: false),
                ProductId = table.Column<long>(type: "bigint", nullable: false),
                OrderedQuantity = table.Column<decimal>(type: "decimal(18,3)", precision: 18, scale: 3, nullable: false),
                UnitCost = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_RetailPurchaseOrderLines", x => x.Id);
                table.ForeignKey("FK_RetailPurchaseOrderLines_RetailPurchaseOrders_PurchaseOrderId",
                    x => x.PurchaseOrderId, "RetailPurchaseOrders", "Id", onDelete: ReferentialAction.Restrict);
                table.ForeignKey("FK_RetailPurchaseOrderLines_RetailProducts_ProductId",
                    x => x.ProductId, "RetailProducts", "Id", onDelete: ReferentialAction.Restrict);
            });

        migrationBuilder.CreateIndex("IX_RetailPurchaseOrderLines_ProductId",
            "RetailPurchaseOrderLines", "ProductId");
        migrationBuilder.CreateIndex("IX_RetailPurchaseOrderLines_PurchaseOrderId_ProductId",
            "RetailPurchaseOrderLines", new[] { "PurchaseOrderId", "ProductId" }, unique: true);

        migrationBuilder.CreateTable(
            name: "RetailReceivings",
            columns: table => new
            {
                Id = table.Column<long>(type: "bigint", nullable: false)
                    .Annotation("SqlServer:Identity", "1, 1"),
                BusinessId = table.Column<long>(type: "bigint", nullable: false),
                PurchaseOrderId = table.Column<long>(type: "bigint", nullable: false),
                CreatedByUserId = table.Column<long>(type: "bigint", nullable: false),
                PostedByUserId = table.Column<long>(type: "bigint", nullable: true),
                Status = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                CreatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                PostedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_RetailReceivings", x => x.Id);
                table.ForeignKey("FK_RetailReceivings_Businesses_BusinessId",
                    x => x.BusinessId, "Businesses", "Id", onDelete: ReferentialAction.Restrict);
                table.ForeignKey("FK_RetailReceivings_RetailPurchaseOrders_PurchaseOrderId",
                    x => x.PurchaseOrderId, "RetailPurchaseOrders", "Id", onDelete: ReferentialAction.Restrict);
                table.ForeignKey("FK_RetailReceivings_AspNetUsers_CreatedByUserId",
                    x => x.CreatedByUserId, "AspNetUsers", "Id", onDelete: ReferentialAction.Restrict);
                table.ForeignKey("FK_RetailReceivings_AspNetUsers_PostedByUserId",
                    x => x.PostedByUserId, "AspNetUsers", "Id", onDelete: ReferentialAction.Restrict);
            });

        migrationBuilder.CreateIndex("IX_RetailReceivings_CreatedByUserId",
            "RetailReceivings", "CreatedByUserId");
        migrationBuilder.CreateIndex("IX_RetailReceivings_PostedByUserId",
            "RetailReceivings", "PostedByUserId");
        migrationBuilder.CreateIndex("IX_RetailReceivings_PurchaseOrderId",
            "RetailReceivings", "PurchaseOrderId");
        migrationBuilder.CreateIndex("IX_RetailReceivings_BusinessId_PurchaseOrderId_Status",
            "RetailReceivings", new[] { "BusinessId", "PurchaseOrderId", "Status" });

        migrationBuilder.CreateTable(
            name: "RetailReceivingLines",
            columns: table => new
            {
                Id = table.Column<long>(type: "bigint", nullable: false)
                    .Annotation("SqlServer:Identity", "1, 1"),
                ReceivingId = table.Column<long>(type: "bigint", nullable: false),
                PurchaseOrderLineId = table.Column<long>(type: "bigint", nullable: false),
                Quantity = table.Column<decimal>(type: "decimal(18,3)", precision: 18, scale: 3, nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_RetailReceivingLines", x => x.Id);
                table.ForeignKey("FK_RetailReceivingLines_RetailReceivings_ReceivingId",
                    x => x.ReceivingId, "RetailReceivings", "Id", onDelete: ReferentialAction.Restrict);
                table.ForeignKey("FK_RetailReceivingLines_RetailPurchaseOrderLines_PurchaseOrderLineId",
                    x => x.PurchaseOrderLineId, "RetailPurchaseOrderLines", "Id", onDelete: ReferentialAction.Restrict);
            });

        migrationBuilder.CreateIndex("IX_RetailReceivingLines_PurchaseOrderLineId",
            "RetailReceivingLines", "PurchaseOrderLineId");
        migrationBuilder.CreateIndex("IX_RetailReceivingLines_ReceivingId_PurchaseOrderLineId",
            "RetailReceivingLines", new[] { "ReceivingId", "PurchaseOrderLineId" }, unique: true);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable("RetailReceivingLines");
        migrationBuilder.DropTable("RetailReceivings");
        migrationBuilder.DropTable("RetailPurchaseOrderLines");
        migrationBuilder.DropTable("RetailPurchaseOrders");
    }
}
