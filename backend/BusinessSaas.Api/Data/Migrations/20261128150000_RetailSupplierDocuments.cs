using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace BusinessSaas.Api.Data.Migrations;

[DbContext(typeof(AppDbContext))]
[Migration("20261128150000_RetailSupplierDocuments")]
public sealed class RetailSupplierDocuments : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable(
            name: "RetailSupplierDocuments",
            columns: table => new
            {
                Id = table.Column<long>(type: "bigint", nullable: false).Annotation("SqlServer:Identity", "1, 1"),
                BusinessId = table.Column<long>(type: "bigint", nullable: false),
                SupplierId = table.Column<long>(type: "bigint", nullable: false),
                ReceivingId = table.Column<long>(type: "bigint", nullable: false),
                ActorUserId = table.Column<long>(type: "bigint", nullable: false),
                PurchaseReturnId = table.Column<long>(type: "bigint", nullable: true),
                Kind = table.Column<string>(type: "nvarchar(12)", maxLength: 12, nullable: false),
                CurrencyCode = table.Column<string>(type: "nvarchar(3)", maxLength: 3, nullable: false),
                Amount = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                Reference = table.Column<string>(type: "nvarchar(120)", maxLength: 120, nullable: false),
                RequestKey = table.Column<string>(type: "nvarchar(80)", maxLength: 80, nullable: false),
                PostedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_RetailSupplierDocuments", x => x.Id);
                table.ForeignKey("FK_RetailSupplierDocuments_Businesses_BusinessId", x => x.BusinessId, "Businesses", "Id", onDelete: ReferentialAction.Restrict);
                table.ForeignKey("FK_RetailSupplierDocuments_RetailSuppliers_SupplierId", x => x.SupplierId, "RetailSuppliers", "Id", onDelete: ReferentialAction.Restrict);
                table.ForeignKey("FK_RetailSupplierDocuments_RetailReceivings_ReceivingId", x => x.ReceivingId, "RetailReceivings", "Id", onDelete: ReferentialAction.Restrict);
                table.ForeignKey("FK_RetailSupplierDocuments_AspNetUsers_ActorUserId", x => x.ActorUserId, "AspNetUsers", "Id", onDelete: ReferentialAction.Restrict);
                table.ForeignKey("FK_RetailSupplierDocuments_RetailPurchaseReturns_PurchaseReturnId", x => x.PurchaseReturnId, "RetailPurchaseReturns", "Id", onDelete: ReferentialAction.Restrict);
                table.CheckConstraint("CK_RetailSupplierDocuments_KindAmount", "[Amount] > 0 AND (([Kind] = 'CreditNote' AND [PurchaseReturnId] IS NOT NULL) OR ([Kind] IN ('Invoice', 'Payment') AND [PurchaseReturnId] IS NULL))");
            });
        migrationBuilder.CreateIndex("IX_RetailSupplierDocuments_BusinessId_RequestKey", "RetailSupplierDocuments", new[] { "BusinessId", "RequestKey" }, unique: true);
        migrationBuilder.CreateIndex("IX_RetailSupplierDocuments_BusinessId_SupplierId_Kind_Reference", "RetailSupplierDocuments", new[] { "BusinessId", "SupplierId", "Kind", "Reference" }, unique: true);
        migrationBuilder.CreateIndex("IX_RetailSupplierDocuments_ReceivingId", "RetailSupplierDocuments", "ReceivingId", unique: true, filter: "[Kind] = 'Invoice'");
        migrationBuilder.CreateIndex("IX_RetailSupplierDocuments_PurchaseReturnId", "RetailSupplierDocuments", "PurchaseReturnId", unique: true, filter: "[PurchaseReturnId] IS NOT NULL");
        migrationBuilder.CreateIndex("IX_RetailSupplierDocuments_SupplierId", "RetailSupplierDocuments", "SupplierId");
        migrationBuilder.CreateIndex("IX_RetailSupplierDocuments_ActorUserId", "RetailSupplierDocuments", "ActorUserId");
        migrationBuilder.CreateIndex("IX_RetailSupplierDocuments_ReceivingId_Kind", "RetailSupplierDocuments", new[] { "ReceivingId", "Kind" });
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("IF EXISTS (SELECT 1 FROM RetailSupplierDocuments) THROW 51000, 'Posted supplier documents prevent downgrade.', 1;");
        migrationBuilder.DropTable("RetailSupplierDocuments");
    }
}
