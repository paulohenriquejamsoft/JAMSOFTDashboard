using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Jamsoft.DashBoards.Api.Infraestrutura.Dados.Migrations
{
    /// <inheritdoc />
    public partial class ColetorAcesso : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "COLETOR_ACESSO",
                columns: table => new
                {
                    COLETOR_ID = table.Column<int>(type: "int", nullable: false),
                    ULTIMO_TOKEN_EM = table.Column<DateTime>(type: "datetime2(0)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_COLETOR_ACESSO", x => x.COLETOR_ID);
                    table.ForeignKey(
                        name: "FK_COLETOR_ACESSO_COLETOR_COLETOR_ID",
                        column: x => x.COLETOR_ID,
                        principalTable: "COLETOR",
                        principalColumn: "ID");
                });

            // Preserva o último token já registrado antes de tirar a coluna do COLETOR.
            migrationBuilder.Sql("""
                INSERT INTO COLETOR_ACESSO (COLETOR_ID, ULTIMO_TOKEN_EM)
                SELECT ID, ULTIMO_TOKEN_EM FROM COLETOR WHERE ULTIMO_TOKEN_EM IS NOT NULL;
                """);

            migrationBuilder.DropColumn(
                name: "ULTIMO_TOKEN_EM",
                table: "COLETOR");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "ULTIMO_TOKEN_EM",
                table: "COLETOR",
                type: "datetime2(0)",
                nullable: true);

            migrationBuilder.Sql("""
                UPDATE c SET ULTIMO_TOKEN_EM = a.ULTIMO_TOKEN_EM
                FROM COLETOR c JOIN COLETOR_ACESSO a ON a.COLETOR_ID = c.ID;
                """);

            migrationBuilder.DropTable(
                name: "COLETOR_ACESSO");
        }
    }
}
