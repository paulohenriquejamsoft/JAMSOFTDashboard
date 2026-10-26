/* =====================================================================
   Rodar em CADA banco de cliente (banco do SGE).
   Confira os planos de execução antes: os nomes de colunas e se
   c_NotaSaida é tabela ou view podem mudar o que vale a pena criar.
   ===================================================================== */

/* 1. Usuário só de leitura, com acesso apenas ao que a consulta usa.
      Se c_NotaSaida for uma VIEW, conceda SELECT na view. */
-- CREATE LOGIN mcp_sge_leitura WITH PASSWORD = 'TROQUE';
-- CREATE USER  mcp_sge_leitura FOR LOGIN mcp_sge_leitura;
-- GRANT SELECT ON dbo.c_NotaSaida    TO mcp_sge_leitura;
-- GRANT SELECT ON dbo.NotaSaidaProd  TO mcp_sge_leitura;

/* 2. Índices sugeridos (só se ainda não existirem equivalentes) */

-- Filtro por período de faturamento (se c_NotaSaida for view, crie na tabela base):
-- CREATE INDEX IX_NotaSaida_DataFatura
--     ON dbo.NotaSaida (datafatura)
--     INCLUDE (situacaonota, TotalCondNeutra, modelo, dataentrega, totnota, codcliente,
--              codvendedor, codEmpresa);

-- Contagem de itens por nota (subconsulta QtdItens):
-- CREATE INDEX IX_NotaSaidaProd_NumNotaSaida ON dbo.NotaSaidaProd (numnotasaida);
