using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.EntityFrameworkCore.Infrastructure;

#nullable disable

namespace BusinessSaas.Api.Data.Migrations
{
    /// <inheritdoc />
    [DbContext(typeof(AppDbContext))]
    [Migration("20261118210000_RetailCashBasket")]
    public sealed class RetailCashBasket : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "RetailCashSaleLine",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    SaleId = table.Column<long>(type: "bigint", nullable: false),
                    ProductId = table.Column<long>(type: "bigint", nullable: false),
                    ProductName = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    UnitPrice = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                    Quantity = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RetailCashSaleLine", x => x.Id);
                    table.ForeignKey(
                        name: "FK_RetailCashSaleLine_RetailCashSales_SaleId",
                        column: x => x.SaleId,
                        principalTable: "RetailCashSales",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_RetailCashSaleLine_RetailProducts_ProductId",
                        column: x => x.ProductId,
                        principalTable: "RetailProducts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_RetailCashSaleLine_ProductId",
                table: "RetailCashSaleLine",
                column: "ProductId");

            migrationBuilder.CreateIndex(
                name: "IX_RetailCashSaleLine_SaleId",
                table: "RetailCashSaleLine",
                column: "SaleId");
            migrationBuilder.Sql("""
                INSERT INTO RetailCashSaleLine (SaleId, ProductId, ProductName, Quantity, UnitPrice)
                SELECT Id, ProductId, ProductName, Quantity, UnitPrice FROM RetailCashSales;
                """);
            migrationBuilder.DropForeignKey(
                name: "FK_RetailCashSales_RetailProducts_ProductId",
                table: "RetailCashSales");

            migrationBuilder.DropIndex(
                name: "IX_RetailCashSales_ProductId",
                table: "RetailCashSales");

            migrationBuilder.DropColumn(
                name: "ProductId",
                table: "RetailCashSales");

            migrationBuilder.DropColumn(
                name: "ProductName",
                table: "RetailCashSales");

            migrationBuilder.DropColumn(
                name: "Quantity",
                table: "RetailCashSales");

            migrationBuilder.DropColumn(
                name: "UnitPrice",
                table: "RetailCashSales");

        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                IF EXISTS (SELECT SaleId FROM RetailCashSaleLine GROUP BY SaleId HAVING COUNT(*) > 1)
                    THROW 51000, 'A receipt contains multiple products and cannot be downgraded.', 1;
                """);

            migrationBuilder.AddColumn<long>(
                name: "ProductId",
                table: "RetailCashSales",
                type: "bigint",
                nullable: false,
                defaultValue: 0L);

            migrationBuilder.AddColumn<string>(
                name: "ProductName",
                table: "RetailCashSales",
                type: "nvarchar(200)",
                maxLength: 200,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<int>(
                name: "Quantity",
                table: "RetailCashSales",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<decimal>(
                name: "UnitPrice",
                table: "RetailCashSales",
                type: "decimal(18,2)",
                precision: 18,
                scale: 2,
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.Sql("""
                UPDATE s SET ProductId = l.ProductId, ProductName = l.ProductName,
                    Quantity = l.Quantity, UnitPrice = l.UnitPrice
                FROM RetailCashSales s JOIN RetailCashSaleLine l ON l.SaleId = s.Id;
                """);
            migrationBuilder.DropTable(name: "RetailCashSaleLine");

            migrationBuilder.CreateIndex(
                name: "IX_RetailCashSales_ProductId",
                table: "RetailCashSales",
                column: "ProductId");

            migrationBuilder.AddForeignKey(
                name: "FK_RetailCashSales_RetailProducts_ProductId",
                table: "RetailCashSales",
                column: "ProductId",
                principalTable: "RetailProducts",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }
    }
}
