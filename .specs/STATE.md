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

## Handoff

- **Feature**: `.specs/features/plataforma-tenancy-acesso`
- **Phase / Task**: Execute — Fase 1 concluída (T1–T7); próxima é a Fase 2 (T8–T15)
- **Completed**: T1, T2, T3, T4, T5, T6, T7 (gate de Build da Fase 1 verde: 25 testes)
- **In-progress** (file:line): none
- **Next step**: Executar a Fase 2 começando pela T8 (`src/Jamsoft.DashBoards.Api/Seguranca/TenantAtual.cs`).
- **Blockers**: Validar a query de vendas (§11 do documento original) no banco real do ERP. Testes de integração rodam com `TESTES_SQLSERVER="Server=localhost;Integrated Security=true;TrustServerCertificate=true"` (Docker parado nesta máquina).
- **Uncommitted files**: none
- **Branch**: feat/plataforma-tenancy-acesso
