using BusinessSaas.Api.Data;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BusinessSaas.Api.Data.Migrations;

[DbContext(typeof(AppDbContext))]
[Migration("20261117190000_AddPlatformAuditChangeValues")]
public sealed class AddPlatformAuditChangeValues : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<string>("PreviousValue", "PlatformAuditEvents",
            type: "nvarchar(100)", maxLength: 100, nullable: true);
        migrationBuilder.AddColumn<string>("NewValue", "PlatformAuditEvents",
            type: "nvarchar(100)", maxLength: 100, nullable: true);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropColumn("PreviousValue", "PlatformAuditEvents");
        migrationBuilder.DropColumn("NewValue", "PlatformAuditEvents");
    }
}
