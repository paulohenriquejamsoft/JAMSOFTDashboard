using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Jamsoft.DashBoards.Api.Infraestrutura.Dados.Migrations
{
    /// <inheritdoc />
    public partial class OrigemSincronizacao : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "ORIGEM_ID",
                table: "EMPRESA",
                type: "int",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "ORIGEM_SINCRONIZACAO",
                columns: table => new
                {
                    ID = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    ID_PUBLICO = table.Column<Guid>(type: "uniqueidentifier", nullable: false, defaultValueSql: "NEWSEQUENTIALID()"),
                    TENANT_ID = table.Column<int>(type: "int", nullable: false),
                    COLETOR_ID = table.Column<int>(type: "int", nullable: false),
                    CONFIGURACAO_ID = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ROTINA = table.Column<string>(type: "varchar(30)", nullable: false),
                    BANCO_ORIGEM = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: true),
                    ULTIMA_EXECUCAO_INICIO = table.Column<DateTime>(type: "datetime2(0)", nullable: true),
                    ULTIMA_EXECUCAO_FIM = table.Column<DateTime>(type: "datetime2(0)", nullable: true),
                    ULTIMA_SITUACAO = table.Column<string>(type: "varchar(20)", nullable: true),
                    ULTIMA_MENSAGEM = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    REGISTROS_ULTIMA = table.Column<int>(type: "int", nullable: true),
                    ULTIMO_SUCESSO_EM = table.Column<DateTime>(type: "datetime2(0)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ORIGEM_SINCRONIZACAO", x => x.ID);
                    table.ForeignKey(
                        name: "FK_ORIGEM_SINCRONIZACAO_COLETOR_COLETOR_ID",
                        column: x => x.COLETOR_ID,
                        principalTable: "COLETOR",
                        principalColumn: "ID");
                    table.ForeignKey(
                        name: "FK_ORIGEM_SINCRONIZACAO_TENANT_TENANT_ID",
                        column: x => x.TENANT_ID,
                        principalTable: "TENANT",
                        principalColumn: "ID");
                });

            migrationBuilder.CreateIndex(
                name: "IX_EMPRESA_ORIGEM_ID",
                table: "EMPRESA",
                column: "ORIGEM_ID");

            migrationBuilder.CreateIndex(
                name: "IX_ORIGEM_SINCRONIZACAO_COLETOR_ID",
                table: "ORIGEM_SINCRONIZACAO",
                column: "COLETOR_ID");

            migrationBuilder.CreateIndex(
                name: "IX_ORIGEM_SINCRONIZACAO_ID_PUBLICO",
                table: "ORIGEM_SINCRONIZACAO",
                column: "ID_PUBLICO",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ORIGEM_SINCRONIZACAO_TENANT_ID_CONFIGURACAO_ID_ROTINA",
                table: "ORIGEM_SINCRONIZACAO",
                columns: new[] { "TENANT_ID", "CONFIGURACAO_ID", "ROTINA" },
                unique: true);

            migrationBuilder.AddForeignKey(
                name: "FK_EMPRESA_ORIGEM_SINCRONIZACAO_ORIGEM_ID",
                table: "EMPRESA",
                column: "ORIGEM_ID",
                principalTable: "ORIGEM_SINCRONIZACAO",
                principalColumn: "ID");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_EMPRESA_ORIGEM_SINCRONIZACAO_ORIGEM_ID",
                table: "EMPRESA");

            migrationBuilder.DropTable(
                name: "ORIGEM_SINCRONIZACAO");

            migrationBuilder.DropIndex(
                name: "IX_EMPRESA_ORIGEM_ID",
                table: "EMPRESA");

            migrationBuilder.DropColumn(
                name: "ORIGEM_ID",
                table: "EMPRESA");
        }
    }
}
