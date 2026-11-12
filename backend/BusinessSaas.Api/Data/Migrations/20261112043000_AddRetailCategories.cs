using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BusinessSaas.Api.Data.Migrations;

public partial class AddRetailCategories : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable(
            name: "ProductCategories",
            columns: table => new
            {
                Id = table.Column<long>(type: "bigint", nullable: false)
                    .Annotation("SqlServer:Identity", "1, 1"),
                BusinessId = table.Column<long>(type: "bigint", nullable: false),
                Name = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                NameKey = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_ProductCategories", x => x.Id);
                table.ForeignKey(
                    name: "FK_ProductCategories_Businesses_BusinessId",
                    column: x => x.BusinessId,
                    principalTable: "Businesses",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Cascade);
            });

        migrationBuilder.CreateIndex(
            name: "IX_ProductCategories_BusinessId_NameKey",
            table: "ProductCategories",
            columns: new[] { "BusinessId", "NameKey" },
            unique: true);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(name: "ProductCategories");
    }
}
