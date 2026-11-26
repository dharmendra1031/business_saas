using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace BusinessSaas.Api.Data.Migrations;

[DbContext(typeof(AppDbContext))]
[Migration("20261126190000_RetailCashRegisterSessions")]
public sealed class RetailCashRegisterSessions : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable("RetailCashRegisterSessions", table => new
        {
            Id = table.Column<long>(type: "bigint", nullable: false).Annotation("SqlServer:Identity", "1, 1"),
            BusinessId = table.Column<long>(type: "bigint", nullable: false),
            LocationId = table.Column<long>(type: "bigint", nullable: false),
            ActorUserId = table.Column<long>(type: "bigint", nullable: false),
            CurrencyCode = table.Column<string>(type: "nvarchar(3)", maxLength: 3, nullable: false),
            RequestKey = table.Column<string>(type: "nvarchar(80)", maxLength: 80, nullable: false),
            OpeningCash = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
            OpenedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
            ClosedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true)
        }, constraints: table =>
        {
            table.PrimaryKey("PK_RetailCashRegisterSessions", x => x.Id);
            table.ForeignKey("FK_RetailCashRegisterSessions_Businesses_BusinessId", x => x.BusinessId, "Businesses", "Id", onDelete: ReferentialAction.Restrict);
            table.ForeignKey("FK_RetailCashRegisterSessions_BusinessLocations_LocationId", x => x.LocationId, "BusinessLocations", "Id", onDelete: ReferentialAction.Restrict);
            table.ForeignKey("FK_RetailCashRegisterSessions_AspNetUsers_ActorUserId", x => x.ActorUserId, "AspNetUsers", "Id", onDelete: ReferentialAction.Restrict);
            table.CheckConstraint("CK_RetailCashRegisterSessions_AmountsAndDates", "[OpeningCash] >= 0 AND ([ClosedAtUtc] IS NULL OR [ClosedAtUtc] >= [OpenedAtUtc])");
        });
        migrationBuilder.CreateIndex("IX_RetailCashRegisterSessions_ActorUserId", "RetailCashRegisterSessions", "ActorUserId");
        migrationBuilder.CreateIndex("IX_RetailCashRegisterSessions_LocationId", "RetailCashRegisterSessions", "LocationId");
        migrationBuilder.CreateIndex("IX_RetailCashRegisterSessions_BusinessId_RequestKey", "RetailCashRegisterSessions",
            new[] { "BusinessId", "RequestKey" }, unique: true);
        migrationBuilder.CreateIndex("IX_RetailCashRegisterSessions_BusinessId_LocationId_ActorUserId", "RetailCashRegisterSessions",
            new[] { "BusinessId", "LocationId", "ActorUserId" }, unique: true, filter: "[ClosedAtUtc] IS NULL");
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("IF EXISTS (SELECT 1 FROM RetailCashRegisterSessions) THROW 51000, 'Cash register sessions cannot be discarded by downgrade.', 1;");
        migrationBuilder.DropTable("RetailCashRegisterSessions");
    }
}
