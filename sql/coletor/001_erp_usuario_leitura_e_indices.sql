/* =====================================================================
   Jamsoft DashBoards - Coletor: preparo do banco do ERP (SGE)

   Rodar em CADA banco de ERP que o Coletor sincroniza, conectado
   nele (o script não tem USE). Idempotente: rodar de novo não falha
   nem duplica nada.

   Estrutura confirmada em 2026-10-25:
   - c_NotaSaida é uma VIEW sobre a tabela dbo.NotaSaida;
   - NumNotaSaida é a identity de dbo.NotaSaida;
   - QtdItens vem de dbo.NotaSaidaProd.

   Derivado de sql/002_banco_cliente_indices_e_usuario.sql do Sge.Mcp.
   ===================================================================== */

/* 0. Login (uma vez por servidor, no master), ANTES deste script.
      Escolha uma senha forte e guarde-a só na tela de instância do
      Coletor (ela vai cifrada com DPAPI). Nunca grave a senha aqui.

      CREATE LOGIN jd_coletor_leitura WITH PASSWORD = '<senha forte>', CHECK_POLICY = ON;

      Sem o login, o usuário abaixo é criado sem login (WITHOUT LOGIN).
      Depois de criar o login, rode este script de novo: o usuário é
      recriado ligado ao login, com as mesmas permissões. */

/* 1. Usuário só de leitura, com acesso apenas ao que a consulta usa. */
IF USER_ID(N'jd_coletor_leitura') IS NOT NULL
   AND SUSER_ID(N'jd_coletor_leitura') IS NOT NULL
   AND NOT EXISTS (SELECT 1
                     FROM sys.database_principals AS u
                     JOIN sys.server_principals   AS l ON l.sid = u.sid
                    WHERE u.name = N'jd_coletor_leitura')
    -- Criado antes do login: o usuário não possui objetos, então pode ser recriado.
    DROP USER jd_coletor_leitura;

IF USER_ID(N'jd_coletor_leitura') IS NULL
BEGIN
    IF SUSER_ID(N'jd_coletor_leitura') IS NOT NULL
        CREATE USER jd_coletor_leitura FOR LOGIN jd_coletor_leitura;
    ELSE
        CREATE USER jd_coletor_leitura WITHOUT LOGIN;
END
GO

/* SELECT só na view e nos itens. A view lê a tabela base pelo
   encadeamento de propriedade (mesmo dono, dbo); o DENY garante que
   o usuário nunca leia dbo.NotaSaida direto, mesmo se entrar num papel
   como db_datareader. */
GRANT SELECT ON dbo.c_NotaSaida   TO jd_coletor_leitura;
GRANT SELECT ON dbo.NotaSaidaProd TO jd_coletor_leitura;
DENY  SELECT ON dbo.NotaSaida     TO jd_coletor_leitura;
GO

/* 2. Índices de apoio. Confira o plano de execução da consulta do
      Coletor (ExtratorVendas) antes de criar: se já houver índice
      equivalente, não crie outro. */

-- Keyset da extração: WHERE datafatura ... ORDER BY datafatura, NumNotaSaida.
-- O índice vai na tabela base (índice em view não se aplica aqui).
IF NOT EXISTS (SELECT 1 FROM sys.indexes
                WHERE object_id = OBJECT_ID(N'dbo.NotaSaida')
                  AND name = N'IX_NotaSaida_DataFatura_NumNotaSaida')
    CREATE INDEX IX_NotaSaida_DataFatura_NumNotaSaida
        ON dbo.NotaSaida (datafatura, NumNotaSaida)
        INCLUDE (situacaonota, TotalCondNeutra, modelo, DataHoraFechamento, totnota,
                 codcliente, codvendedor, codEmpresa);

-- Contagem de itens por nota (subconsulta QtdItens).
IF NOT EXISTS (SELECT 1 FROM sys.indexes
                WHERE object_id = OBJECT_ID(N'dbo.NotaSaidaProd')
                  AND name = N'IX_NotaSaidaProd_NumNotaSaida')
    CREATE INDEX IX_NotaSaidaProd_NumNotaSaida
        ON dbo.NotaSaidaProd (numnotasaida);
GO
