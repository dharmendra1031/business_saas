using BusinessSaas.Api.Data;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BusinessSaas.Api.Data.Migrations;

[DbContext(typeof(AppDbContext))]
[Migration("20261118190000_AddBillingTaxDocumentDrafts")]
public sealed class AddBillingTaxDocumentDrafts : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable(
            name: "BillingTaxProfiles",
            columns: table => new
            {
                Id = table.Column<long>(type: "bigint", nullable: false)
                    .Annotation("SqlServer:Identity", "1, 1"),
                BusinessId = table.Column<long>(type: "bigint", nullable: false),
                Revision = table.Column<int>(type: "int", nullable: false),
                IssuerName = table.Column<string>(type: "nvarchar(160)", maxLength: 160, nullable: false),
                IssuerAddress = table.Column<string>(type: "nvarchar(300)", maxLength: 300, nullable: false),
                IssuerGstin = table.Column<string>(type: "nvarchar(15)", maxLength: 15, nullable: false),
                IssuerStateCode = table.Column<string>(type: "nvarchar(2)", maxLength: 2, nullable: false),
                RecipientName = table.Column<string>(type: "nvarchar(160)", maxLength: 160, nullable: false),
                RecipientAddress = table.Column<string>(type: "nvarchar(300)", maxLength: 300, nullable: false),
                RecipientGstin = table.Column<string>(type: "nvarchar(15)", maxLength: 15, nullable: true),
                RecipientStateCode = table.Column<string>(type: "nvarchar(2)", maxLength: 2, nullable: false),
                ServiceCode = table.Column<string>(type: "nvarchar(8)", maxLength: 8, nullable: false),
                ServiceDescription = table.Column<string>(type: "nvarchar(160)", maxLength: 160, nullable: false),
                InvoiceSeries = table.Column<string>(type: "nvarchar(5)", maxLength: 5, nullable: false),
                GstRate = table.Column<decimal>(type: "decimal(5,2)", nullable: false),
                PricesIncludeTax = table.Column<bool>(type: "bit", nullable: false),
                UpdatedByUserId = table.Column<long>(type: "bigint", nullable: false),
                UpdatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_BillingTaxProfiles", x => x.Id);
                table.ForeignKey("FK_BillingTaxProfiles_Businesses_BusinessId",
                    x => x.BusinessId, "Businesses", "Id", onDelete: ReferentialAction.Restrict);
                table.ForeignKey("FK_BillingTaxProfiles_AspNetUsers_UpdatedByUserId",
                    x => x.UpdatedByUserId, "AspNetUsers", "Id", onDelete: ReferentialAction.Restrict);
            });
        migrationBuilder.CreateIndex("IX_BillingTaxProfiles_BusinessId",
            "BillingTaxProfiles", "BusinessId", unique: true);
        migrationBuilder.CreateIndex("IX_BillingTaxProfiles_UpdatedByUserId", "BillingTaxProfiles", "UpdatedByUserId");

        migrationBuilder.CreateTable(
            name: "BillingDocumentDrafts",
            columns: table => new
            {
                Id = table.Column<long>(type: "bigint", nullable: false)
                    .Annotation("SqlServer:Identity", "1, 1"),
                BusinessId = table.Column<long>(type: "bigint", nullable: false),
                SubscriptionPaymentId = table.Column<long>(type: "bigint", nullable: false),
                SubscriptionRefundId = table.Column<long?>(type: "bigint", nullable: true),
                OriginalDocumentId = table.Column<long?>(type: "bigint", nullable: true),
                BillingTaxProfileId = table.Column<long>(type: "bigint", nullable: false),
                TaxProfileRevision = table.Column<int>(type: "int", nullable: false),
                DocumentType = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                Status = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                DocumentNumber = table.Column<string>(type: "nvarchar(16)", maxLength: 16, nullable: true),
                TaxSnapshotJson = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: false),
                CurrencyCode = table.Column<string>(type: "nvarchar(3)", maxLength: 3, nullable: false),
                FinancialYearStart = table.Column<int>(type: "int", nullable: false),
                GrossAmount = table.Column<decimal>(type: "decimal(18,2)", nullable: false),
                TaxableAmount = table.Column<decimal>(type: "decimal(18,2)", nullable: false),
                CgstAmount = table.Column<decimal>(type: "decimal(18,2)", nullable: false),
                SgstAmount = table.Column<decimal>(type: "decimal(18,2)", nullable: false),
                IgstAmount = table.Column<decimal>(type: "decimal(18,2)", nullable: false),
                CreatedByUserId = table.Column<long>(type: "bigint", nullable: false),
                CreatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_BillingDocumentDrafts", x => x.Id);
                table.ForeignKey("FK_BillingDocumentDrafts_Businesses_BusinessId",
                    x => x.BusinessId, "Businesses", "Id", onDelete: ReferentialAction.Restrict);
                table.ForeignKey("FK_BillingDocumentDrafts_SubscriptionPayments_SubscriptionPaymentId",
                    x => x.SubscriptionPaymentId, "SubscriptionPayments", "Id", onDelete: ReferentialAction.Restrict);
                table.ForeignKey("FK_BillingDocumentDrafts_SubscriptionRefunds_SubscriptionRefundId",
                    x => x.SubscriptionRefundId, "SubscriptionRefunds", "Id", onDelete: ReferentialAction.Restrict);
                table.ForeignKey("FK_BillingDocumentDrafts_BillingDocumentDrafts_OriginalDocumentId",
                    x => x.OriginalDocumentId, "BillingDocumentDrafts", "Id", onDelete: ReferentialAction.Restrict);
                table.ForeignKey("FK_BillingDocumentDrafts_BillingTaxProfiles_BillingTaxProfileId",
                    x => x.BillingTaxProfileId, "BillingTaxProfiles", "Id", onDelete: ReferentialAction.Restrict);
                table.ForeignKey("FK_BillingDocumentDrafts_AspNetUsers_CreatedByUserId",
                    x => x.CreatedByUserId, "AspNetUsers", "Id", onDelete: ReferentialAction.Restrict);
            });
        migrationBuilder.CreateIndex("IX_BillingDocumentDrafts_BusinessId",
            "BillingDocumentDrafts", "BusinessId");
        migrationBuilder.CreateIndex("IX_BillingDocumentDrafts_SubscriptionPaymentId_DocumentType",
            "BillingDocumentDrafts", new[] { "SubscriptionPaymentId", "DocumentType" }, unique: true, filter: "[DocumentType] = 'Invoice'");
        migrationBuilder.CreateIndex("IX_BillingDocumentDrafts_SubscriptionRefundId",
            "BillingDocumentDrafts", "SubscriptionRefundId", unique: true, filter: "[SubscriptionRefundId] IS NOT NULL");
        migrationBuilder.CreateIndex("IX_BillingDocumentDrafts_OriginalDocumentId",
            "BillingDocumentDrafts", "OriginalDocumentId");
        migrationBuilder.CreateIndex("IX_BillingDocumentDrafts_BillingTaxProfileId", "BillingDocumentDrafts", "BillingTaxProfileId");
        migrationBuilder.CreateIndex("IX_BillingDocumentDrafts_CreatedByUserId", "BillingDocumentDrafts", "CreatedByUserId");
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable("BillingDocumentDrafts");
        migrationBuilder.DropTable("BillingTaxProfiles");
    }
}
