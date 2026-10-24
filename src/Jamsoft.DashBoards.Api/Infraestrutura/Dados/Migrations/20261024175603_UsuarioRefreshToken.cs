using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Jamsoft.DashBoards.Api.Infraestrutura.Dados.Migrations
{
    /// <inheritdoc />
    public partial class UsuarioRefreshToken : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "USUARIO",
                columns: table => new
                {
                    ID = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    ID_PUBLICO = table.Column<Guid>(type: "uniqueidentifier", nullable: false, defaultValueSql: "NEWSEQUENTIALID()"),
                    TENANT_ID = table.Column<int>(type: "int", nullable: true),
                    NOME = table.Column<string>(type: "nvarchar(120)", maxLength: 120, nullable: false),
                    EMAIL = table.Column<string>(type: "nvarchar(254)", maxLength: 254, nullable: false),
                    SENHA_HASH = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    PERFIL = table.Column<string>(type: "varchar(20)", nullable: false),
                    ATIVO = table.Column<bool>(type: "bit", nullable: false, defaultValue: true),
                    TROCAR_SENHA = table.Column<bool>(type: "bit", nullable: false, defaultValue: true),
                    FALHAS_LOGIN = table.Column<byte>(type: "tinyint", nullable: false, defaultValue: (byte)0),
                    BLOQUEADO_ATE = table.Column<DateTime>(type: "datetime2(0)", nullable: true),
                    ULTIMO_LOGIN_EM = table.Column<DateTime>(type: "datetime2(0)", nullable: true),
                    CRIADO_EM = table.Column<DateTime>(type: "datetime2(0)", nullable: false, defaultValueSql: "SYSUTCDATETIME()"),
                    VERSAO = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_USUARIO", x => x.ID);
                    table.CheckConstraint("CK_USUARIO_PERFIL", "PERFIL IN ('SuperAdmin','Admin','Consulta')");
                    table.CheckConstraint("CK_USUARIO_PERFIL_TENANT", "(PERFIL = 'SuperAdmin' AND TENANT_ID IS NULL) OR (PERFIL <> 'SuperAdmin' AND TENANT_ID IS NOT NULL)");
                    table.ForeignKey(
                        name: "FK_USUARIO_TENANT_TENANT_ID",
                        column: x => x.TENANT_ID,
                        principalTable: "TENANT",
                        principalColumn: "ID");
                });

            migrationBuilder.CreateTable(
                name: "REFRESH_TOKEN",
                columns: table => new
                {
                    ID = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    USUARIO_ID = table.Column<int>(type: "int", nullable: false),
                    TENANT_ID = table.Column<int>(type: "int", nullable: true),
                    HASH = table.Column<byte[]>(type: "binary(32)", nullable: false),
                    FAMILIA = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CRIADO_EM = table.Column<DateTime>(type: "datetime2(0)", nullable: false, defaultValueSql: "SYSUTCDATETIME()"),
                    EXPIRA_EM = table.Column<DateTime>(type: "datetime2(0)", nullable: false),
                    USADO_EM = table.Column<DateTime>(type: "datetime2(0)", nullable: true),
                    REVOGADO_EM = table.Column<DateTime>(type: "datetime2(0)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_REFRESH_TOKEN", x => x.ID);
                    table.ForeignKey(
                        name: "FK_REFRESH_TOKEN_USUARIO_USUARIO_ID",
                        column: x => x.USUARIO_ID,
                        principalTable: "USUARIO",
                        principalColumn: "ID");
                });

            migrationBuilder.CreateIndex(
                name: "IX_REFRESH_TOKEN_HASH",
                table: "REFRESH_TOKEN",
                column: "HASH",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_REFRESH_TOKEN_TENANT_ID",
                table: "REFRESH_TOKEN",
                column: "TENANT_ID");

            migrationBuilder.CreateIndex(
                name: "IX_REFRESH_TOKEN_USUARIO_ID",
                table: "REFRESH_TOKEN",
                column: "USUARIO_ID");

            migrationBuilder.CreateIndex(
                name: "IX_USUARIO_EMAIL",
                table: "USUARIO",
                column: "EMAIL",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_USUARIO_ID_PUBLICO",
                table: "USUARIO",
                column: "ID_PUBLICO",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_USUARIO_TENANT_ID_PERFIL_ATIVO",
                table: "USUARIO",
                columns: new[] { "TENANT_ID", "PERFIL", "ATIVO" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "REFRESH_TOKEN");

            migrationBuilder.DropTable(
                name: "USUARIO");
        }
    }
}
