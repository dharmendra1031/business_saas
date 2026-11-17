using BusinessSaas.Api.Data;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BusinessSaas.Api.Data.Migrations;

[DbContext(typeof(AppDbContext))]
[Migration("20261117160000_AddPlatformAuditEvents")]
public sealed class AddPlatformAuditEvents : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable(
            name: "PlatformAuditEvents",
            columns: table => new
            {
                Id = table.Column<long>(type: "bigint", nullable: false)
                    .Annotation("SqlServer:Identity", "1, 1"),
                ActorUserId = table.Column<long>(type: "bigint", nullable: false),
                Action = table.Column<string>(type: "nvarchar(60)", maxLength: 60, nullable: false),
                TargetType = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: false),
                TargetId = table.Column<long>(type: "bigint", nullable: true),
                Reason = table.Column<string>(type: "nvarchar(250)", maxLength: 250, nullable: true),
                CreatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_PlatformAuditEvents", x => x.Id);
                table.ForeignKey("FK_PlatformAuditEvents_AspNetUsers_ActorUserId",
                    x => x.ActorUserId, "AspNetUsers", "Id", onDelete: ReferentialAction.Restrict);
            });

        migrationBuilder.CreateIndex("IX_PlatformAuditEvents_ActorUserId",
            "PlatformAuditEvents", "ActorUserId");
        migrationBuilder.CreateIndex("IX_PlatformAuditEvents_CreatedAtUtc_Id",
            "PlatformAuditEvents", new[] { "CreatedAtUtc", "Id" });
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable("PlatformAuditEvents");
    }
}
