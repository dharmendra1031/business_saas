using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace BusinessSaas.Api.Data.Migrations;

[DbContext(typeof(AppDbContext))]
[Migration("20261128170000_RetailStockTransfers")]
public sealed class RetailStockTransfers : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable(
            name: "RetailStockTransfers",
            columns: table => new
            {
                Id = table.Column<long>(type: "bigint", nullable: false).Annotation("SqlServer:Identity", "1, 1"),
                BusinessId = table.Column<long>(type: "bigint", nullable: false),
                FromLocationId = table.Column<long>(type: "bigint", nullable: false),
                ToLocationId = table.Column<long>(type: "bigint", nullable: false),
                RequestedByUserId = table.Column<long>(type: "bigint", nullable: false),
                ApprovedByUserId = table.Column<long>(type: "bigint", nullable: true),
                DispatchedByUserId = table.Column<long>(type: "bigint", nullable: true),
                RequestKey = table.Column<string>(type: "nvarchar(80)", maxLength: 80, nullable: false),
                Status = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                Reason = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                RequestedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                ApprovedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                DispatchedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_RetailStockTransfers", x => x.Id);
                table.ForeignKey("FK_RetailStockTransfers_Businesses_BusinessId", x => x.BusinessId, "Businesses", "Id", onDelete: ReferentialAction.Restrict);
                table.ForeignKey("FK_RetailStockTransfers_BusinessLocations_FromLocationId", x => x.FromLocationId, "BusinessLocations", "Id", onDelete: ReferentialAction.Restrict);
                table.ForeignKey("FK_RetailStockTransfers_BusinessLocations_ToLocationId", x => x.ToLocationId, "BusinessLocations", "Id", onDelete: ReferentialAction.Restrict);
                table.ForeignKey("FK_RetailStockTransfers_AspNetUsers_RequestedByUserId", x => x.RequestedByUserId, "AspNetUsers", "Id", onDelete: ReferentialAction.Restrict);
                table.ForeignKey("FK_RetailStockTransfers_AspNetUsers_ApprovedByUserId", x => x.ApprovedByUserId, "AspNetUsers", "Id", onDelete: ReferentialAction.Restrict);
                table.ForeignKey("FK_RetailStockTransfers_AspNetUsers_DispatchedByUserId", x => x.DispatchedByUserId, "AspNetUsers", "Id", onDelete: ReferentialAction.Restrict);
                table.CheckConstraint("CK_RetailStockTransfers_Valid", "[FromLocationId] <> [ToLocationId] AND [Status] IN ('Requested','Approved','InTransit','Completed','Cancelled')");
            });
        migrationBuilder.CreateIndex("IX_RetailStockTransfers_BusinessId_RequestKey", "RetailStockTransfers", new[] { "BusinessId", "RequestKey" }, unique: true);
        migrationBuilder.CreateIndex("IX_RetailStockTransfers_BusinessId_FromLocationId_Id", "RetailStockTransfers", new[] { "BusinessId", "FromLocationId", "Id" });
        migrationBuilder.CreateIndex("IX_RetailStockTransfers_BusinessId_ToLocationId_Id", "RetailStockTransfers", new[] { "BusinessId", "ToLocationId", "Id" });
        migrationBuilder.CreateIndex("IX_RetailStockTransfers_FromLocationId", "RetailStockTransfers", "FromLocationId");
        migrationBuilder.CreateIndex("IX_RetailStockTransfers_ToLocationId", "RetailStockTransfers", "ToLocationId");
        migrationBuilder.CreateIndex("IX_RetailStockTransfers_RequestedByUserId", "RetailStockTransfers", "RequestedByUserId");
        migrationBuilder.CreateIndex("IX_RetailStockTransfers_ApprovedByUserId", "RetailStockTransfers", "ApprovedByUserId");
        migrationBuilder.CreateIndex("IX_RetailStockTransfers_DispatchedByUserId", "RetailStockTransfers", "DispatchedByUserId");
        migrationBuilder.CreateTable(
            name: "RetailStockTransferLines",
            columns: table => new
            {
                Id = table.Column<long>(type: "bigint", nullable: false).Annotation("SqlServer:Identity", "1, 1"),
                TransferId = table.Column<long>(type: "bigint", nullable: false),
                ProductId = table.Column<long>(type: "bigint", nullable: false),
                Quantity = table.Column<decimal>(type: "decimal(18,3)", precision: 18, scale: 3, nullable: false),
                UnitCode = table.Column<string>(type: "nvarchar(12)", maxLength: 12, nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_RetailStockTransferLines", x => x.Id);
                table.ForeignKey("FK_RetailStockTransferLines_RetailStockTransfers_TransferId", x => x.TransferId, "RetailStockTransfers", "Id", onDelete: ReferentialAction.Restrict);
                table.ForeignKey("FK_RetailStockTransferLines_RetailProducts_ProductId", x => x.ProductId, "RetailProducts", "Id", onDelete: ReferentialAction.Restrict);
                table.CheckConstraint("CK_RetailStockTransferLines_Valid", "[Quantity] > 0");
            });
        migrationBuilder.CreateIndex("IX_RetailStockTransferLines_TransferId_ProductId", "RetailStockTransferLines", new[] { "TransferId", "ProductId" }, unique: true);
        migrationBuilder.CreateIndex("IX_RetailStockTransferLines_ProductId", "RetailStockTransferLines", "ProductId");
        migrationBuilder.CreateTable(
            name: "RetailStockTransferReceipts",
            columns: table => new
            {
                Id = table.Column<long>(type: "bigint", nullable: false).Annotation("SqlServer:Identity", "1, 1"),
                TransferLineId = table.Column<long>(type: "bigint", nullable: false),
                BusinessId = table.Column<long>(type: "bigint", nullable: false),
                ActorUserId = table.Column<long>(type: "bigint", nullable: false),
                Quantity = table.Column<decimal>(type: "decimal(18,3)", precision: 18, scale: 3, nullable: false),
                Kind = table.Column<string>(type: "nvarchar(12)", maxLength: 12, nullable: false),
                RequestKey = table.Column<string>(type: "nvarchar(80)", maxLength: 80, nullable: false),
                Reason = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                PostedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_RetailStockTransferReceipts", x => x.Id);
                table.ForeignKey("FK_RetailStockTransferReceipts_RetailStockTransferLines_TransferLineId", x => x.TransferLineId, "RetailStockTransferLines", "Id", onDelete: ReferentialAction.Restrict);
                table.ForeignKey("FK_RetailStockTransferReceipts_Businesses_BusinessId", x => x.BusinessId, "Businesses", "Id", onDelete: ReferentialAction.Restrict);
                table.ForeignKey("FK_RetailStockTransferReceipts_AspNetUsers_ActorUserId", x => x.ActorUserId, "AspNetUsers", "Id", onDelete: ReferentialAction.Restrict);
                table.CheckConstraint("CK_RetailStockTransferReceipts_Valid", "[Quantity] > 0 AND [Kind] IN ('Received','Damaged','Missing')");
            });
        migrationBuilder.CreateIndex("IX_RetailStockTransferReceipts_BusinessId_RequestKey", "RetailStockTransferReceipts", new[] { "BusinessId", "RequestKey" }, unique: true);
        migrationBuilder.CreateIndex("IX_RetailStockTransferReceipts_TransferLineId", "RetailStockTransferReceipts", "TransferLineId");
        migrationBuilder.CreateIndex("IX_RetailStockTransferReceipts_ActorUserId", "RetailStockTransferReceipts", "ActorUserId");
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("IF EXISTS (SELECT 1 FROM RetailStockTransfers) THROW 51000, 'Transfer history prevents downgrade.', 1;");
        migrationBuilder.DropTable("RetailStockTransferReceipts");
        migrationBuilder.DropTable("RetailStockTransferLines");
        migrationBuilder.DropTable("RetailStockTransfers");
    }
}
