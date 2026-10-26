using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Jamsoft.DashBoards.Api.Infraestrutura.Dados.Migrations
{
    /// <inheritdoc />
    public partial class VendaTipoPedido : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "CK_VENDA_TIPO_DFE",
                table: "VENDA");

            migrationBuilder.AddCheckConstraint(
                name: "CK_VENDA_TIPO_DFE",
                table: "VENDA",
                sql: "TIPO_DFE IN ('NF-e','NFC-e','Pedido')");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "CK_VENDA_TIPO_DFE",
                table: "VENDA");

            migrationBuilder.AddCheckConstraint(
                name: "CK_VENDA_TIPO_DFE",
                table: "VENDA",
                sql: "TIPO_DFE IN ('NF-e','NFC-e')");
        }
    }
}
