using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace BusinessSaas.Api.Data.Migrations;

[DbContext(typeof(AppDbContext))]
[Migration("20261123170000_BusinessTypeReleaseStatus")]
public sealed class BusinessTypeReleaseStatus : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<string>("ReleaseState", "BusinessTypes",
            type: "nvarchar(24)", maxLength: 24, nullable: false, defaultValue: "Available");
        migrationBuilder.AddColumn<int>("ReleaseRevision", "BusinessTypes",
            type: "int", nullable: false, defaultValue: 0);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropColumn("ReleaseRevision", "BusinessTypes");
        migrationBuilder.DropColumn("ReleaseState", "BusinessTypes");
    }
}
