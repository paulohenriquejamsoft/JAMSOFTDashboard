using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Jamsoft.DashBoards.Api.Infraestrutura.Dados.Migrations
{
    /// <inheritdoc />
    public partial class Coletor : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "COLETOR",
                columns: table => new
                {
                    ID = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    ID_PUBLICO = table.Column<Guid>(type: "uniqueidentifier", nullable: false, defaultValueSql: "NEWSEQUENTIALID()"),
                    NOME = table.Column<string>(type: "nvarchar(80)", maxLength: 80, nullable: false),
                    CLIENT_ID = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    SECRET_HASH = table.Column<byte[]>(type: "varbinary(32)", nullable: false),
                    ATIVO = table.Column<bool>(type: "bit", nullable: false, defaultValue: true),
                    VERSAO_CREDENCIAL = table.Column<int>(type: "int", nullable: false, defaultValue: 1),
                    CRIADO_EM = table.Column<DateTime>(type: "datetime2(0)", nullable: false, defaultValueSql: "SYSUTCDATETIME()"),
                    REVOGADO_EM = table.Column<DateTime>(type: "datetime2(0)", nullable: true),
                    ULTIMO_TOKEN_EM = table.Column<DateTime>(type: "datetime2(0)", nullable: true),
                    VERSAO = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_COLETOR", x => x.ID);
                    table.CheckConstraint("CK_COLETOR_SECRET_HASH", "DATALENGTH(SECRET_HASH) = 32");
                });

            migrationBuilder.CreateTable(
                name: "COLETOR_TENANT",
                columns: table => new
                {
                    TENANT_ID = table.Column<int>(type: "int", nullable: false),
                    COLETOR_ID = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_COLETOR_TENANT", x => new { x.TENANT_ID, x.COLETOR_ID });
                    table.ForeignKey(
                        name: "FK_COLETOR_TENANT_COLETOR_COLETOR_ID",
                        column: x => x.COLETOR_ID,
                        principalTable: "COLETOR",
                        principalColumn: "ID");
                    table.ForeignKey(
                        name: "FK_COLETOR_TENANT_TENANT_TENANT_ID",
                        column: x => x.TENANT_ID,
                        principalTable: "TENANT",
                        principalColumn: "ID");
                });

            migrationBuilder.CreateIndex(
                name: "IX_COLETOR_CLIENT_ID",
                table: "COLETOR",
                column: "CLIENT_ID",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_COLETOR_ID_PUBLICO",
                table: "COLETOR",
                column: "ID_PUBLICO",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_COLETOR_TENANT_COLETOR_ID",
                table: "COLETOR_TENANT",
                column: "COLETOR_ID");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "COLETOR_TENANT");

            migrationBuilder.DropTable(
                name: "COLETOR");
        }
    }
}
