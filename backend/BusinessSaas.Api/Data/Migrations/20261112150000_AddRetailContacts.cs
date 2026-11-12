using BusinessSaas.Api.Data;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BusinessSaas.Api.Data.Migrations;

[DbContext(typeof(AppDbContext))]
[Migration("20261112150000_AddRetailContacts")]
public sealed class AddRetailContacts : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        CreateContacts(migrationBuilder, "RetailSuppliers", true);
        CreateContacts(migrationBuilder, "RetailCustomers", false);
    }

    private static void CreateContacts(MigrationBuilder migrationBuilder, string tableName, bool unique)
    {
        migrationBuilder.CreateTable(
            name: tableName,
            columns: table => new
            {
                Id = table.Column<long>(type: "bigint", nullable: false)
                    .Annotation("SqlServer:Identity", "1, 1"),
                BusinessId = table.Column<long>(type: "bigint", nullable: false),
                Name = table.Column<string>(type: "nvarchar(120)", maxLength: 120, nullable: false),
                NameKey = table.Column<string>(type: "nvarchar(120)", maxLength: 120, nullable: false),
                Phone = table.Column<string>(type: "nvarchar(25)", maxLength: 25, nullable: false),
                Email = table.Column<string>(type: "nvarchar(160)", maxLength: 160, nullable: false),
                IsActive = table.Column<bool>(type: "bit", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_" + tableName, x => x.Id);
                table.ForeignKey("FK_" + tableName + "_Businesses_BusinessId",
                    x => x.BusinessId, "Businesses", "Id", onDelete: ReferentialAction.Cascade);
            });

        migrationBuilder.CreateIndex(
            name: "IX_" + tableName + "_BusinessId_NameKey",
            table: tableName,
            columns: new[] { "BusinessId", "NameKey" },
            unique: unique);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable("RetailCustomers");
        migrationBuilder.DropTable("RetailSuppliers");
    }
}
