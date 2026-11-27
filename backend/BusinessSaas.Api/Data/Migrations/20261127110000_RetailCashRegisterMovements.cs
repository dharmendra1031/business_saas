using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace BusinessSaas.Api.Data.Migrations;

[DbContext(typeof(AppDbContext))]
[Migration("20261127110000_RetailCashRegisterMovements")]
public sealed class RetailCashRegisterMovements : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable("RetailCashRegisterMovements", table => new
        {
            Id = table.Column<long>(type: "bigint", nullable: false).Annotation("SqlServer:Identity", "1, 1"),
            RegisterSessionId = table.Column<long>(type: "bigint", nullable: false),
            ActorUserId = table.Column<long>(type: "bigint", nullable: false),
            RequestKey = table.Column<string>(type: "nvarchar(80)", maxLength: 80, nullable: false),
            IsCashIn = table.Column<bool>(type: "bit", nullable: false),
            Amount = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
            Reason = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
            PostedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false)
        }, constraints: table =>
        {
            table.PrimaryKey("PK_RetailCashRegisterMovements", x => x.Id);
            table.ForeignKey("FK_RetailCashRegisterMovements_RetailCashRegisterSessions_RegisterSessionId",
                x => x.RegisterSessionId, "RetailCashRegisterSessions", "Id", onDelete: ReferentialAction.Restrict);
            table.ForeignKey("FK_RetailCashRegisterMovements_AspNetUsers_ActorUserId",
                x => x.ActorUserId, "AspNetUsers", "Id", onDelete: ReferentialAction.Restrict);
            table.CheckConstraint("CK_RetailCashRegisterMovements_Amount", "[Amount] > 0");
        });
        migrationBuilder.CreateIndex("IX_RetailCashRegisterMovements_ActorUserId", "RetailCashRegisterMovements", "ActorUserId");
        migrationBuilder.CreateIndex("IX_RetailCashRegisterMovements_RegisterSessionId_RequestKey",
            "RetailCashRegisterMovements", new[] { "RegisterSessionId", "RequestKey" }, unique: true);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("IF EXISTS (SELECT 1 FROM RetailCashRegisterMovements) THROW 51000, 'Posted register movements cannot be discarded by downgrade.', 1;");
        migrationBuilder.DropTable("RetailCashRegisterMovements");
    }
}
