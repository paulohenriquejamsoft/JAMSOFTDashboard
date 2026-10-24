using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Jamsoft.DashBoards.Api.Infraestrutura.Dados.Migrations
{
    /// <inheritdoc />
    public partial class Empresa : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "EMPRESA",
                columns: table => new
                {
                    ID = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    ID_PUBLICO = table.Column<Guid>(type: "uniqueidentifier", nullable: false, defaultValueSql: "NEWSEQUENTIALID()"),
                    TENANT_ID = table.Column<int>(type: "int", nullable: false),
                    COD_EMPRESA = table.Column<int>(type: "int", nullable: false),
                    NOME = table.Column<string>(type: "nvarchar(80)", maxLength: 80, nullable: false),
                    NOME_MANUAL = table.Column<bool>(type: "bit", nullable: false, defaultValue: false),
                    CRIADO_EM = table.Column<DateTime>(type: "datetime2(0)", nullable: false, defaultValueSql: "SYSUTCDATETIME()"),
                    VERSAO = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_EMPRESA", x => x.ID);
                    table.ForeignKey(
                        name: "FK_EMPRESA_TENANT_TENANT_ID",
                        column: x => x.TENANT_ID,
                        principalTable: "TENANT",
                        principalColumn: "ID");
                });

            migrationBuilder.CreateIndex(
                name: "IX_EMPRESA_ID_PUBLICO",
                table: "EMPRESA",
                column: "ID_PUBLICO",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_EMPRESA_TENANT_ID_COD_EMPRESA",
                table: "EMPRESA",
                columns: new[] { "TENANT_ID", "COD_EMPRESA" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "EMPRESA");
        }
    }
}
