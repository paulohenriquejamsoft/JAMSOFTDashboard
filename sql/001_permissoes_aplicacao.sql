/* =====================================================================
   Banco da plataforma (JAMSOFT Dashboards): permissões da aplicação.
   A API usa um login membro da role jamsoft_dashboards_app: lê e grava
   todas as tabelas de dbo, mas a AUDITORIA é somente inserção (AUD-04).
   Migrações (dotnet run -- migrar) rodam com outro login, com DDL.
   Idempotente: pode ser executado de novo depois de cada migração.
   Um único lote, sem GO.
   ===================================================================== */

IF DATABASE_PRINCIPAL_ID(N'jamsoft_dashboards_app') IS NULL
    CREATE ROLE jamsoft_dashboards_app AUTHORIZATION dbo;

GRANT SELECT, INSERT, UPDATE, DELETE ON SCHEMA::dbo TO jamsoft_dashboards_app;

/* DENY prevalece sobre o GRANT do schema: registros de auditoria não mudam nem somem. */
DENY UPDATE, DELETE ON OBJECT::dbo.AUDITORIA TO jamsoft_dashboards_app;

/* Vincular o login da aplicação (senha fora do código, como no Jamsoft.Sge.Mcp):
-- CREATE LOGIN jamsoft_dashboards WITH PASSWORD = 'TROQUE';
-- CREATE USER  jamsoft_dashboards FOR LOGIN jamsoft_dashboards;
-- ALTER ROLE   jamsoft_dashboards_app ADD MEMBER jamsoft_dashboards;
*/
