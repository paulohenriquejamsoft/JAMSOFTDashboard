/* =====================================================================
   Banco central do servidor MCP (ex.: SGE_MCP)
   Guarda as api-keys e, para cada uma, a connection string do cliente
   cifrada com AES-256-GCM (a chave fica só no servidor MCP).
   ===================================================================== */

CREATE TABLE dbo.McpApiKey
(
    Id                       INT IDENTITY(1,1) NOT NULL CONSTRAINT PK_McpApiKey PRIMARY KEY,
    Prefixo                  CHAR(8)           NOT NULL,  -- parte visível da chave (sge_PREFIXO_...)
    HashChave                VARBINARY(32)     NOT NULL,  -- SHA-256 da chave inteira
    Cliente                  NVARCHAR(120)     NOT NULL,
    ConnectionStringCifrada  VARCHAR(2000)     NOT NULL,  -- Base64 (nonce|tag|cifrado)
    Ativo                    BIT               NOT NULL CONSTRAINT DF_McpApiKey_Ativo DEFAULT (1),
    ExpiraEm                 DATETIME2(0)      NULL,      -- UTC; NULL = não expira
    CriadoEm                 DATETIME2(0)      NOT NULL CONSTRAINT DF_McpApiKey_CriadoEm DEFAULT (SYSUTCDATETIME()),
    Observacao               NVARCHAR(400)     NULL
);

CREATE UNIQUE INDEX UX_McpApiKey_Prefixo ON dbo.McpApiKey (Prefixo)
    INCLUDE (HashChave, Cliente, ConnectionStringCifrada, Ativo, ExpiraEm);
GO

/* Usuário só de leitura para o servidor MCP ler as chaves */
-- CREATE LOGIN mcp_leitura WITH PASSWORD = 'TROQUE';
-- CREATE USER  mcp_leitura FOR LOGIN mcp_leitura;
-- GRANT SELECT ON dbo.McpApiKey TO mcp_leitura;

/* Revogar uma chave (vale em até Mcp:CacheMinutos):
   UPDATE dbo.McpApiKey SET Ativo = 0 WHERE Prefixo = 'XXXXXXXX'; */
