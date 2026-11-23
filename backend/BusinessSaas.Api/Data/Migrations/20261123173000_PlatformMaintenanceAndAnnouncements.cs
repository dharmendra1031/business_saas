using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace BusinessSaas.Api.Data.Migrations;

[DbContext(typeof(AppDbContext))]
[Migration("20261123173000_PlatformMaintenanceAndAnnouncements")]
public sealed class PlatformMaintenanceAndAnnouncements : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable(
            name: "PlatformMaintenanceWindows",
            columns: table => new
            {
                Id = table.Column<long>(type: "bigint", nullable: false).Annotation("SqlServer:Identity", "1, 1"),
                Mode = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                Title = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                Message = table.Column<string>(type: "nvarchar(300)", maxLength: 300, nullable: false),
                StartUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                EndUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                IsCancelled = table.Column<bool>(type: "bit", nullable: false),
                Reason = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                CreatedByUserId = table.Column<long>(type: "bigint", nullable: false),
                CreatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false)
            },
            constraints: table => table.PrimaryKey("PK_PlatformMaintenanceWindows", x => x.Id));
        migrationBuilder.CreateIndex("IX_PlatformMaintenanceWindows_StartUtc_EndUtc",
            "PlatformMaintenanceWindows", new[] { "StartUtc", "EndUtc" });
        migrationBuilder.CreateTable(
            name: "PlatformAnnouncements",
            columns: table => new
            {
                Id = table.Column<long>(type: "bigint", nullable: false).Annotation("SqlServer:Identity", "1, 1"),
                Level = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                Title = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                Message = table.Column<string>(type: "nvarchar(300)", maxLength: 300, nullable: false),
                StartUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                EndUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                IsArchived = table.Column<bool>(type: "bit", nullable: false),
                Reason = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                CreatedByUserId = table.Column<long>(type: "bigint", nullable: false),
                CreatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false)
            },
            constraints: table => table.PrimaryKey("PK_PlatformAnnouncements", x => x.Id));
        migrationBuilder.CreateIndex("IX_PlatformAnnouncements_StartUtc_EndUtc",
            "PlatformAnnouncements", new[] { "StartUtc", "EndUtc" });
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable("PlatformAnnouncements");
        migrationBuilder.DropTable("PlatformMaintenanceWindows");
    }
}
