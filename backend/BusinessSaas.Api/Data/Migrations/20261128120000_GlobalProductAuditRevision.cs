using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace BusinessSaas.Api.Data.Migrations;

[DbContext(typeof(AppDbContext))]
[Migration("20261128120000_GlobalProductAuditRevision")]
public sealed class GlobalProductAuditRevision : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<int>("Revision", "GlobalProducts", type: "int", nullable: false, defaultValue: 0);
        migrationBuilder.AlterColumn<string>("PreviousValue", "PlatformAuditEvents", type: "nvarchar(3000)",
            maxLength: 3000, nullable: true, oldClrType: typeof(string), oldType: "nvarchar(100)",
            oldMaxLength: 100, oldNullable: true);
        migrationBuilder.AlterColumn<string>("NewValue", "PlatformAuditEvents", type: "nvarchar(3000)",
            maxLength: 3000, nullable: true, oldClrType: typeof(string), oldType: "nvarchar(100)",
            oldMaxLength: 100, oldNullable: true);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("IF EXISTS (SELECT 1 FROM PlatformAuditEvents WHERE DATALENGTH(PreviousValue) > 200 OR DATALENGTH(NewValue) > 200 OR TargetType = 'GlobalProduct') THROW 51000, 'Catalogue audit history prevents downgrade.', 1;");
        migrationBuilder.AlterColumn<string>("PreviousValue", "PlatformAuditEvents", type: "nvarchar(100)",
            maxLength: 100, nullable: true, oldClrType: typeof(string), oldType: "nvarchar(3000)",
            oldMaxLength: 3000, oldNullable: true);
        migrationBuilder.AlterColumn<string>("NewValue", "PlatformAuditEvents", type: "nvarchar(100)",
            maxLength: 100, nullable: true, oldClrType: typeof(string), oldType: "nvarchar(3000)",
            oldMaxLength: 3000, oldNullable: true);
        migrationBuilder.DropColumn("Revision", "GlobalProducts");
    }
}
