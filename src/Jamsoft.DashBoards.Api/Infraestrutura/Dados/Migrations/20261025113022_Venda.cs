using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Jamsoft.DashBoards.Api.Infraestrutura.Dados.Migrations
{
    /// <inheritdoc />
    public partial class Venda : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "VENDA",
                columns: table => new
                {
                    ID = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    TENANT_ID = table.Column<int>(type: "int", nullable: false),
                    EMPRESA_ID = table.Column<int>(type: "int", nullable: false),
                    COD_EMPRESA = table.Column<int>(type: "int", nullable: false),
                    VENDA_ID = table.Column<int>(type: "int", nullable: false),
                    TIPO_DFE = table.Column<string>(type: "varchar(6)", nullable: false),
                    DATA_HORA = table.Column<DateTimeOffset>(type: "datetimeoffset(0)", nullable: false),
                    VALOR = table.Column<decimal>(type: "decimal(19,4)", nullable: false),
                    COD_CLIENTE = table.Column<int>(type: "int", nullable: true),
                    NOME_CLIENTE = table.Column<string>(type: "nvarchar(120)", maxLength: 120, nullable: true),
                    VENDEDOR_CODIGO = table.Column<int>(type: "int", nullable: true),
                    VENDEDOR_NOME = table.Column<string>(type: "nvarchar(80)", maxLength: 80, nullable: true),
                    SITUACAO = table.Column<string>(type: "nvarchar(12)", maxLength: 12, nullable: false),
                    QTD_ITENS = table.Column<int>(type: "int", nullable: false),
                    ORIGEM_ID = table.Column<int>(type: "int", nullable: false),
                    RECEBIDO_EM = table.Column<DateTime>(type: "datetime2(0)", nullable: false, defaultValueSql: "SYSUTCDATETIME()"),
                    ALTERADO_EM = table.Column<DateTime>(type: "datetime2(0)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_VENDA", x => x.ID);
                    table.CheckConstraint("CK_VENDA_QTD_ITENS", "QTD_ITENS >= 0");
                    table.CheckConstraint("CK_VENDA_SITUACAO", "SITUACAO IN (N'Finalizada',N'Cancelada',N'Inutilizada',N'Contingência')");
                    table.CheckConstraint("CK_VENDA_TIPO_DFE", "TIPO_DFE IN ('NF-e','NFC-e')");
                    table.CheckConstraint("CK_VENDA_VALOR", "VALOR >= 0");
                    table.ForeignKey(
                        name: "FK_VENDA_EMPRESA_EMPRESA_ID",
                        column: x => x.EMPRESA_ID,
                        principalTable: "EMPRESA",
                        principalColumn: "ID");
                    table.ForeignKey(
                        name: "FK_VENDA_ORIGEM_SINCRONIZACAO_ORIGEM_ID",
                        column: x => x.ORIGEM_ID,
                        principalTable: "ORIGEM_SINCRONIZACAO",
                        principalColumn: "ID");
                    table.ForeignKey(
                        name: "FK_VENDA_TENANT_TENANT_ID",
                        column: x => x.TENANT_ID,
                        principalTable: "TENANT",
                        principalColumn: "ID");
                });

            migrationBuilder.CreateIndex(
                name: "IX_VENDA_EMPRESA_ID",
                table: "VENDA",
                column: "EMPRESA_ID");

            migrationBuilder.CreateIndex(
                name: "IX_VENDA_ORIGEM_ID",
                table: "VENDA",
                column: "ORIGEM_ID");

            migrationBuilder.CreateIndex(
                name: "IX_VENDA_TENANT_ID_COD_EMPRESA_VENDA_ID",
                table: "VENDA",
                columns: new[] { "TENANT_ID", "COD_EMPRESA", "VENDA_ID" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_VENDA_TENANT_ID_DATA_HORA",
                table: "VENDA",
                columns: new[] { "TENANT_ID", "DATA_HORA" })
                .Annotation("SqlServer:Include", new[] { "EMPRESA_ID", "VALOR", "SITUACAO", "TIPO_DFE", "QTD_ITENS", "VENDEDOR_CODIGO", "COD_CLIENTE" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "VENDA");
        }
    }
}
