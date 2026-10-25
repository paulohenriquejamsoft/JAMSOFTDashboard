# STATE

## Decisions

### AD-001
- **Decision**: Multi-tenant com banco compartilhado (SQL Server 2019); toda tabela com dado de cliente carrega `TENANT_ID INT`; entidades expostas têm `ID INT` (PK interna) + `ID_PUBLICO UNIQUEIDENTIFIER` (único, usado em URLs e integrações).
- **Reason**: Custo operacional baixo com muitos tenants pequenos; `INT` mantém índices e joins enxutos; GUID evita expor sequenciais.
- **Trade-off**: Isolamento depende de disciplina de código (filtros), não de fronteira física; exclusão de tenant exige rotina dedicada.
- **Scope**: API, banco da plataforma, todas as features.
- **Date**: 2026-10-24
- **Status**: active

### AD-002
- **Decision**: A API usa Entity Framework Core com *global query filter* por tenant; o Coletor usa Dapper contra os bancos dos ERPs.
- **Reason**: O filtro global do EF reduz o risco de esquecer `WHERE TENANT_ID` na API; no Coletor, Dapper dá controle total do SQL legado do ERP. Resolve a contradição do documento original (§3 cita EF na API, §4.2 cita Dapper).
- **Trade-off**: Duas tecnologias de acesso a dados; consultas analíticas pesadas da API podem precisar de SQL bruto via EF.
- **Scope**: API, Coletor.
- **Date**: 2026-10-24
- **Status**: active

### AD-003
- **Decision**: O tenant efetivo de cada requisição vem exclusivamente das *claims* do token autenticado; qualquer identificador de tenant em rota/corpo é validado contra o token e nunca o substitui.
- **Reason**: Impede acesso cruzado por manipulação de parâmetros no frontend.
- **Trade-off**: SuperAdmin precisa de um mecanismo explícito (e auditado) para operar no contexto de outro tenant.
- **Scope**: API (todas as rotas com dado de tenant).
- **Date**: 2026-10-24
- **Status**: active

### AD-004
- **Decision**: Autenticação de usuários por JWT de curta duração (15 min) + refresh token rotativo (7 dias) armazenado com hash; autorização por *Policies* do ASP.NET Core com os perfis `SuperAdmin`, `Admin`, `Consulta`, `Coletor`.
- **Reason**: Padrão nativo do .NET, funciona bem com SPA/PWA e com o coletor; revogação via refresh token.
- **Trade-off**: Access token não é revogável antes de expirar (janela de 15 min).
- **Scope**: API, Frontend, Coletor.
- **Date**: 2026-10-24
- **Status**: active

### AD-005
- **Decision**: Cada instalação do Coletor tem credencial própria (`client_id` + `client_secret`) emitida por um SuperAdmin, com lista explícita de tenants autorizados; o Coletor só envia dados para esses tenants.
- **Reason**: Uma instância atende vários clientes; uma credencial por instalação simplifica operação e revogação.
- **Trade-off**: Comprometimento da credencial afeta todos os tenants autorizados naquela instalação.
- **Scope**: Coletor, API (ingestão), área administrativa.
- **Date**: 2026-10-24
- **Status**: active

### AD-006
- **Decision**: Ingestão idempotente por upsert na chave de negócio (`TENANT_ID`, `COD_EMPRESA`, `VENDA_ID`) + janela de reprocessamento de N dias (padrão 7) em toda execução incremental; lotes carregam `batch_id` para deduplicação.
- **Reason**: `NumNotaSaida` máximo não basta como checkpoint (inserções tardias, cancelamentos); upsert + janela captura alterações sem duplicar.
- **Trade-off**: Cada execução reenvia a janela inteira (mais tráfego e escrita).
- **Scope**: Coletor, API (ingestão), modelo de dados de vendas.
- **Date**: 2026-10-24
- **Status**: active

### AD-007
- **Decision**: O checkpoint de cada configuração+rotina só avança depois que a API confirma o processamento do lote.
- **Reason**: Garante que falhas de comunicação nunca percam dados.
- **Trade-off**: Lotes podem ser reenviados após falha (absorvido pela idempotência do AD-006).
- **Scope**: Coletor.
- **Date**: 2026-10-24
- **Status**: active

### AD-008
- **Decision**: Um único frontend Angular 22 (PWA) atende área administrativa e área do cliente, seguindo o Design System do projeto (https://claude.ai/artifact/7H4jYEbfVCQPwkWNfTeP54); menus e rotas variam por perfil, mas a autorização é sempre revalidada na API.
- **Reason**: Uma base de código, visual consistente.
- **Trade-off**: Bundle inclui telas administrativas (mitigado com lazy loading por rota).
- **Scope**: Frontend.
- **Date**: 2026-10-24
- **Status**: active

### AD-009
- **Decision**: A API é um monolito modular: um projeto `Jamsoft.DashBoards.Api` com Minimal APIs e pastas por módulo (`Modulos/Autenticacao`, `Modulos/Tenants`, ...), mais `Infraestrutura/` e `Seguranca/`; nomes em português, seguindo o estilo do Jamsoft.Sge.Mcp.
- **Reason**: Escolha do usuário; menos cerimônia que Clean Architecture para o tamanho atual.
- **Trade-off**: Fronteiras entre módulos dependem de convenção e de testes de arquitetura, não de projetos separados.
- **Scope**: API (todas as features).
- **Date**: 2026-10-24
- **Status**: active

### AD-010
- **Decision**: Usuários em tabela própria (`USUARIO`) com `PasswordHasher<T>` do ASP.NET Core Identity só para hash; JWT e refresh tokens próprios; refresh token em cookie HttpOnly `SameSite=Strict` restrito a `/api/auth`, access token só em memória no Angular.
- **Reason**: Escolha do usuário; esquema multi-tenant sob controle; refresh fora do alcance de XSS.
- **Trade-off**: Lockout, rotação e revogação implementados à mão; SPA e API precisam ficar no mesmo site.
- **Scope**: API, Frontend.
- **Date**: 2026-10-24
- **Status**: active

### AD-011
- **Decision**: Toda resposta de erro da API usa ProblemDetails (`application/problem+json`) com a extensão `codigo` (string estável em UPPER_SNAKE) e `title` em pt-BR.
- **Reason**: Contrato único para Angular e Coletor, nativo do ASP.NET Core (§8.3, §12.3).
- **Trade-off**: Diferente do formato `{ erro, mensagem }` do Jamsoft.Sge.Mcp.
- **Scope**: API, Frontend, Coletor.
- **Date**: 2026-10-24
- **Status**: active

### AD-012
- **Decision**: Tabelas e colunas do banco da plataforma em UPPER_SNAKE_CASE no singular (`TENANT`, `USUARIO`, `TENANT_ID`, `ID_PUBLICO`), aplicadas por convenção no `OnModelCreating`; entidades C# em PascalCase; FKs sem cascade (`NO ACTION`).
- **Reason**: Segue os nomes do documento original; exclusão sempre explícita e controlada (§5.4).
- **Trade-off**: Convenção customizada para manter no EF.
- **Scope**: Banco da plataforma, API.
- **Date**: 2026-10-24
- **Status**: active

### AD-013
- **Decision**: O Coletor é dividido em três projetos: `Jamsoft.DashBoards.Contratos` (DTOs compartilhados com a API, sem dependências), `Jamsoft.DashBoards.Coletor.Nucleo` (agendamento, extração Dapper, cliente HTTP, checkpoint, logs) e `Jamsoft.DashBoards.Coletor` (casca Windows Forms + Generic Host).
- **Reason**: Escolha do usuário; lógica testável sem UI e contrato único entre API e Coletor.
- **Trade-off**: Mais projetos na solução; mudanças de contrato exigem atualizar API e Coletor juntos.
- **Scope**: Coletor, API (ingestão), futuras rotinas de sincronização.
- **Date**: 2026-10-25
- **Status**: active

### AD-014
- **Decision**: A ingestão grava cada lote com um único `MERGE ... WITH (HOLDLOCK)` alimentado por `OPENJSON` (SQL fixo e parametrizado), na mesma transação que registra o lote em `LOTE_INGESTAO (TENANT_ID, BATCH_ID)` com a resposta, usada para responder reenvios de forma idêntica.
- **Reason**: Escolha do usuário; uma ida ao banco por lote, atômico e com contagens via `OUTPUT $action`.
- **Trade-off**: SQL específico do SQL Server fora do LINQ; `LOTE_INGESTAO` precisa de retenção.
- **Scope**: API (ingestão de todas as rotinas futuras).
- **Date**: 2026-10-25
- **Status**: active

### AD-015
- **Decision**: O Coletor se autentica por client credentials (`POST /api/coletor/token`) e recebe um JWT de 15 min com perfil `Coletor`, claim `tenants` e `cred_ver`; um cache de 60 s revalida credencial ativa e versão a cada requisição.
- **Reason**: Reaproveita `TokenService` e policies; revogação, rotação e troca de tenants passam a valer em até 60 s.
- **Trade-off**: Mudança na lista de tenants só vale no próximo token (COL-07) e a revogação tem até 60 s de atraso.
- **Scope**: API, Coletor.
- **Date**: 2026-10-25
- **Status**: active

### AD-016
- **Decision**: O estado local do Coletor (configurações, checkpoints, logs) fica em `%ProgramData%\Jamsoft\DashBoards.Coletor\` em JSON com gravação atômica; segredos (senhas de banco e `client_secret`) cifrados com DPAPI escopo `LocalMachine`.
- **Reason**: Sem banco local; credenciais do ERP nunca saem do servidor do cliente; sobrevive a reinício.
- **Trade-off**: Qualquer processo local com privilégio pode decifrar; mitigado por ACL e usuário de ERP só de leitura.
- **Scope**: Coletor.
- **Date**: 2026-10-25
- **Status**: active

## Handoff

- **Feature**: `.specs/features/plataforma-tenancy-acesso` — **concluída e verificada** (Verifier iteração 3: PASS; 54/54 ACs, 13/13 mutações mortas)
- **Phase / Task**: Execute concluído (T1–T58 + correções da validação)
- **Completed**: T1–T58; 238 testes API, 121 Web, 4 e2e
- **In-progress** (file:line): none
- **Next step**: Aprovar `.specs/features/sincronizacao-vendas/design.md` (rascunho) e gerar o `tasks.md`; depois `painel-vendas`.
- **Blockers**: Validar a query de vendas (§11 do documento original) no banco real do ERP. Testes de integração: `TESTES_SQLSERVER="Server=localhost;Integrated Security=true;TrustServerCertificate=true;Connect Timeout=60"` (Docker parado). SQL Server local sofre com pouca RAM livre: se a suíte oscilar por timeout, liberar memória ou reiniciar o serviço como administrador.
- **Uncommitted files**: none
- **Branch**: feat/plataforma-tenancy-acesso (sem remoto configurado)
