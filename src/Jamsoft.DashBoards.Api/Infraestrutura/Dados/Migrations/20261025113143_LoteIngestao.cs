using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Jamsoft.DashBoards.Api.Infraestrutura.Dados.Migrations
{
    /// <inheritdoc />
    public partial class LoteIngestao : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "LOTE_INGESTAO",
                columns: table => new
                {
                    ID = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    TENANT_ID = table.Column<int>(type: "int", nullable: false),
                    BATCH_ID = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ORIGEM_ID = table.Column<int>(type: "int", nullable: false),
                    RECEBIDO_EM = table.Column<DateTime>(type: "datetime2(0)", nullable: false, defaultValueSql: "SYSUTCDATETIME()"),
                    INSERIDOS = table.Column<int>(type: "int", nullable: false),
                    ATUALIZADOS = table.Column<int>(type: "int", nullable: false),
                    REJEITADOS = table.Column<int>(type: "int", nullable: false),
                    RESPOSTA = table.Column<string>(type: "nvarchar(max)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_LOTE_INGESTAO", x => x.ID);
                    table.ForeignKey(
                        name: "FK_LOTE_INGESTAO_ORIGEM_SINCRONIZACAO_ORIGEM_ID",
                        column: x => x.ORIGEM_ID,
                        principalTable: "ORIGEM_SINCRONIZACAO",
                        principalColumn: "ID");
                    table.ForeignKey(
                        name: "FK_LOTE_INGESTAO_TENANT_TENANT_ID",
                        column: x => x.TENANT_ID,
                        principalTable: "TENANT",
                        principalColumn: "ID");
                });

            migrationBuilder.CreateIndex(
                name: "IX_LOTE_INGESTAO_ORIGEM_ID",
                table: "LOTE_INGESTAO",
                column: "ORIGEM_ID");

            migrationBuilder.CreateIndex(
                name: "IX_LOTE_INGESTAO_RECEBIDO_EM",
                table: "LOTE_INGESTAO",
                column: "RECEBIDO_EM");

            migrationBuilder.CreateIndex(
                name: "IX_LOTE_INGESTAO_TENANT_ID_BATCH_ID",
                table: "LOTE_INGESTAO",
                columns: new[] { "TENANT_ID", "BATCH_ID" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "LOTE_INGESTAO");
        }
    }
}
