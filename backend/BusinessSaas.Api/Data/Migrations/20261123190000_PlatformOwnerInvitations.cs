using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace BusinessSaas.Api.Data.Migrations;

[DbContext(typeof(AppDbContext))]
[Migration("20261123190000_PlatformOwnerInvitations")]
public sealed class PlatformOwnerInvitations : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable(
            name: "PlatformOwnerInvitations",
            columns: table => new
            {
                Id = table.Column<long>(type: "bigint", nullable: false).Annotation("SqlServer:Identity", "1, 1"),
                Email = table.Column<string>(type: "nvarchar(254)", maxLength: 254, nullable: false),
                NormalizedEmail = table.Column<string>(type: "nvarchar(254)", maxLength: 254, nullable: false),
                TokenHash = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                CreatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                ExpiresAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                AcceptedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                RevokedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                CreatedByUserId = table.Column<long>(type: "bigint", nullable: false),
                AcceptedUserId = table.Column<long>(type: "bigint", nullable: true),
                Reason = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_PlatformOwnerInvitations", x => x.Id);
                table.ForeignKey("FK_PlatformOwnerInvitations_AspNetUsers_CreatedByUserId",
                    x => x.CreatedByUserId, "AspNetUsers", "Id", onDelete: ReferentialAction.Restrict);
            });
        migrationBuilder.CreateIndex(
            name: "IX_PlatformOwnerInvitations_TokenHash",
            table: "PlatformOwnerInvitations", column: "TokenHash", unique: true);
        migrationBuilder.CreateIndex(
            name: "IX_PlatformOwnerInvitations_NormalizedEmail_CreatedAtUtc",
            table: "PlatformOwnerInvitations", columns: new[] { "NormalizedEmail", "CreatedAtUtc" });
        migrationBuilder.CreateIndex(
            name: "IX_PlatformOwnerInvitations_CreatedByUserId",
            table: "PlatformOwnerInvitations", column: "CreatedByUserId");
    }

    protected override void Down(MigrationBuilder migrationBuilder) =>
        migrationBuilder.DropTable("PlatformOwnerInvitations");
}
