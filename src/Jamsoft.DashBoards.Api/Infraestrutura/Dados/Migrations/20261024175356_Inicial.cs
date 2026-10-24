using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Jamsoft.DashBoards.Api.Infraestrutura.Dados.Migrations
{
    /// <inheritdoc />
    public partial class Inicial : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "TENANT",
                columns: table => new
                {
                    ID = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    ID_PUBLICO = table.Column<Guid>(type: "uniqueidentifier", nullable: false, defaultValueSql: "NEWSEQUENTIALID()"),
                    NOME = table.Column<string>(type: "nvarchar(120)", maxLength: 120, nullable: false),
                    CNPJ = table.Column<string>(type: "char(14)", nullable: false),
                    ATIVO = table.Column<bool>(type: "bit", nullable: false, defaultValue: true),
                    CRIADO_EM = table.Column<DateTime>(type: "datetime2(0)", nullable: false, defaultValueSql: "SYSUTCDATETIME()"),
                    ALTERADO_EM = table.Column<DateTime>(type: "datetime2(0)", nullable: true),
                    VERSAO = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TENANT", x => x.ID);
                });

            migrationBuilder.CreateIndex(
                name: "IX_TENANT_CNPJ",
                table: "TENANT",
                column: "CNPJ",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_TENANT_ID_PUBLICO",
                table: "TENANT",
                column: "ID_PUBLICO",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "TENANT");
        }
    }
}
