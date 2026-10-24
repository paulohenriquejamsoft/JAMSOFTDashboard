using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Jamsoft.DashBoards.Api.Infraestrutura.Dados.Migrations
{
    /// <inheritdoc />
    public partial class AuditoriaExclusaoTenant : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "AUDITORIA",
                columns: table => new
                {
                    ID = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    OCORRIDO_EM = table.Column<DateTime>(type: "datetime2(3)", nullable: false),
                    USUARIO_ID_PUBLICO = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    USUARIO_EMAIL = table.Column<string>(type: "nvarchar(254)", maxLength: 254, nullable: true),
                    PERFIL = table.Column<string>(type: "varchar(20)", nullable: true),
                    OPERACAO = table.Column<string>(type: "varchar(60)", nullable: false),
                    RECURSO_ID_PUBLICO = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    TENANT_ID_PUBLICO = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    TENANT_NOME = table.Column<string>(type: "nvarchar(120)", maxLength: 120, nullable: true),
                    IP = table.Column<string>(type: "varchar(45)", nullable: true),
                    RESULTADO = table.Column<string>(type: "varchar(20)", nullable: false),
                    DETALHES = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AUDITORIA", x => x.ID);
                });

            migrationBuilder.CreateTable(
                name: "EXCLUSAO_TENANT",
                columns: table => new
                {
                    ID = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    ID_PUBLICO = table.Column<Guid>(type: "uniqueidentifier", nullable: false, defaultValueSql: "NEWSEQUENTIALID()"),
                    TENANT_ID = table.Column<int>(type: "int", nullable: false),
                    TENANT_ID_PUBLICO = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    TENANT_NOME = table.Column<string>(type: "nvarchar(120)", maxLength: 120, nullable: false),
                    SITUACAO = table.Column<string>(type: "varchar(20)", nullable: false),
                    ETAPA_ATUAL = table.Column<string>(type: "varchar(128)", nullable: true),
                    LINHAS_REMOVIDAS = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    ERRO = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: true),
                    SOLICITADO_POR_ID_PUBLICO = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CRIADO_EM = table.Column<DateTime>(type: "datetime2(0)", nullable: false, defaultValueSql: "SYSUTCDATETIME()"),
                    INICIADO_EM = table.Column<DateTime>(type: "datetime2(0)", nullable: true),
                    HEARTBEAT_EM = table.Column<DateTime>(type: "datetime2(0)", nullable: true),
                    CONCLUIDO_EM = table.Column<DateTime>(type: "datetime2(0)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_EXCLUSAO_TENANT", x => x.ID);
                });

            migrationBuilder.CreateIndex(
                name: "IX_AUDITORIA_OCORRIDO_EM",
                table: "AUDITORIA",
                column: "OCORRIDO_EM");

            migrationBuilder.CreateIndex(
                name: "IX_AUDITORIA_TENANT_ID_PUBLICO_OCORRIDO_EM",
                table: "AUDITORIA",
                columns: new[] { "TENANT_ID_PUBLICO", "OCORRIDO_EM" });

            migrationBuilder.CreateIndex(
                name: "IX_EXCLUSAO_TENANT_ID_PUBLICO",
                table: "EXCLUSAO_TENANT",
                column: "ID_PUBLICO",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_EXCLUSAO_TENANT_TENANT_ID",
                table: "EXCLUSAO_TENANT",
                column: "TENANT_ID",
                unique: true,
                filter: "[SITUACAO] IN ('Pendente','EmExecucao')");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "AUDITORIA");

            migrationBuilder.DropTable(
                name: "EXCLUSAO_TENANT");
        }
    }
}
