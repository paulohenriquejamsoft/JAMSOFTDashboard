using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Jamsoft.DashBoards.Api.Infraestrutura.Dados.Migrations
{
    /// <inheritdoc />
    public partial class ChaveMcp : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "CHAVE_MCP",
                columns: table => new
                {
                    ID = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    ID_PUBLICO = table.Column<Guid>(type: "uniqueidentifier", nullable: false, defaultValueSql: "NEWSEQUENTIALID()"),
                    TENANT_ID = table.Column<int>(type: "int", nullable: false),
                    PREFIXO = table.Column<string>(type: "char(8)", nullable: false),
                    HASH = table.Column<byte[]>(type: "varbinary(32)", nullable: false),
                    DESCRICAO = table.Column<string>(type: "nvarchar(80)", maxLength: 80, nullable: false),
                    ATIVO = table.Column<bool>(type: "bit", nullable: false, defaultValue: true),
                    CRIADO_EM = table.Column<DateTime>(type: "datetime2(0)", nullable: false, defaultValueSql: "SYSUTCDATETIME()"),
                    EXPIRA_EM = table.Column<DateTime>(type: "datetime2(0)", nullable: true),
                    REVOGADO_EM = table.Column<DateTime>(type: "datetime2(0)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CHAVE_MCP", x => x.ID);
                    table.CheckConstraint("CK_CHAVE_MCP_HASH", "DATALENGTH(HASH) = 32");
                    table.ForeignKey(
                        name: "FK_CHAVE_MCP_TENANT_TENANT_ID",
                        column: x => x.TENANT_ID,
                        principalTable: "TENANT",
                        principalColumn: "ID");
                });

            migrationBuilder.CreateIndex(
                name: "IX_CHAVE_MCP_ID_PUBLICO",
                table: "CHAVE_MCP",
                column: "ID_PUBLICO",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_CHAVE_MCP_PREFIXO",
                table: "CHAVE_MCP",
                column: "PREFIXO",
                unique: true)
                .Annotation("SqlServer:Include", new[] { "HASH", "TENANT_ID", "ATIVO", "EXPIRA_EM" });

            migrationBuilder.CreateIndex(
                name: "IX_CHAVE_MCP_TENANT_ID",
                table: "CHAVE_MCP",
                column: "TENANT_ID");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "CHAVE_MCP");
        }
    }
}
