/* =====================================================================
   Banco da plataforma (JAMSOFT Dashboards): permissões do servidor MCP.
   O Jamsoft.Sge.Mcp só lê: valida a api-key (CHAVE_MCP + TENANT) e
   consulta as vendas do tenant (VENDA + EMPRESA). Nada além disso.
   Rodar depois da migração que cria CHAVE_MCP (dotnet run -- migrar na API).
   Idempotente. Um único lote, sem GO.
   ===================================================================== */

IF DATABASE_PRINCIPAL_ID(N'jamsoft_dashboards_mcp') IS NULL
    CREATE ROLE jamsoft_dashboards_mcp AUTHORIZATION dbo;

GRANT SELECT ON OBJECT::dbo.CHAVE_MCP TO jamsoft_dashboards_mcp;
GRANT SELECT ON OBJECT::dbo.TENANT    TO jamsoft_dashboards_mcp;
GRANT SELECT ON OBJECT::dbo.VENDA     TO jamsoft_dashboards_mcp;
GRANT SELECT ON OBJECT::dbo.EMPRESA   TO jamsoft_dashboards_mcp;

/* Vincular o login do MCP (senha fora do código):
-- CREATE LOGIN jamsoft_dashboards_mcp_leitura WITH PASSWORD = 'TROQUE';
-- CREATE USER  jamsoft_dashboards_mcp_leitura FOR LOGIN jamsoft_dashboards_mcp_leitura;
-- ALTER ROLE   jamsoft_dashboards_mcp ADD MEMBER jamsoft_dashboards_mcp_leitura;
*/
