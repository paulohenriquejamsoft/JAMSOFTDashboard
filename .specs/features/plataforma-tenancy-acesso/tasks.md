# Plataforma — Tenancy, Usuários e Acesso — Tasks

## Execution Protocol (MANDATORY -- do not skip)

Implement these tasks with the `tlc-spec-driven` skill: **activate it by name and follow its Execute flow and Critical Rules.** Do not search for skill files by filesystem path. The skill is the source of truth for the full flow (per-task cycle, sub-agent delegation, adequacy review, Verifier, discrimination sensor).

**If the skill cannot be activated, STOP and tell the user - do not proceed without it.**

---

**Spec**: `.specs/features/plataforma-tenancy-acesso/spec.md`
**Design**: `.specs/features/plataforma-tenancy-acesso/design.md`
**Status**: In Progress

---

## Test Coverage Matrix

> Generated from project guidelines and spec - confirm before Execute. Guidelines found: none (repo has no code or tests yet) - strong defaults applied, using the test stack approved in `design.md` (xUnit + `WebApplicationFactory` + Testcontainers.MsSql; Angular with Vitest; Playwright for e2e).

| Code Layer | Required Test Type | Coverage Expectation | Location Pattern | Run Command |
| ---------- | ------------------ | -------------------- | ---------------- | ----------- |
| API domain rules (`TenantAtual`, `Cnpj`, `TokenService`, `PlanoExclusao` ordering) | unit | All branches; 1:1 to spec ACs; every listed edge case | `tests/Jamsoft.DashBoards.Api.Testes/Unidade/**/*Testes.cs` | `dotnet test tests/Jamsoft.DashBoards.Api.Testes --filter "Categoria=Unidade"` |
| API endpoints, handlers, middleware, policies | integration | Every route in scope: happy path + every listed edge case + error paths (status code **and** `codigo`) | `tests/Jamsoft.DashBoards.Api.Testes/Integracao/**/*Testes.cs` | `dotnet test tests/Jamsoft.DashBoards.Api.Testes --filter "Categoria=Integracao"` |
| EF infrastructure (DbContext, filtro, interceptor, serviços com SQL) | integration | Key query paths, isolamento, locks and error paths against real SQL Server | `tests/Jamsoft.DashBoards.Api.Testes/Integracao/Dados/*Testes.cs` | same as above |
| Schema / migrations | integration | Every unique, check and FK constraint that a spec AC depends on | `tests/Jamsoft.DashBoards.Api.Testes/Integracao/Esquema/*Testes.cs` | same as above |
| Architecture rules | unit | Every rule listed in design Risks & Concerns | `tests/Jamsoft.DashBoards.Api.Testes/Arquitetura/*Testes.cs` | `dotnet test tests/Jamsoft.DashBoards.Api.Testes --filter "Categoria=Arquitetura"` |
| Angular services, interceptors, guards | unit | All branches; every AC the class implements | `src/Jamsoft.DashBoards.Web/src/app/**/*.spec.ts` | `npm --prefix src/Jamsoft.DashBoards.Web test -- --watch=false` |
| Angular pages/components | unit (component, `HttpTestingController`) | Rendering per state (loading/empty/error/data) + every user action + every error `codigo` shown | `src/Jamsoft.DashBoards.Web/src/app/**/*.spec.ts` | same as above |
| Critical end-to-end flows | e2e | Login → tenant → Admin → user; SuperAdmin tenant context | `src/Jamsoft.DashBoards.Web/e2e/*.spec.ts` | `npm --prefix src/Jamsoft.DashBoards.Web run e2e` |
| Scaffolding, config, design tokens | none | - (build gate only) | - | build gate only |

## Gate Check Commands

> Generated from the approved design - confirm before Execute. Integration tests need Docker (or `TESTES_SQLSERVER` pointing to a SQL Server instance).

| Gate Level | When to Use | Command |
| ---------- | ----------- | ------- |
| Quick | API tasks with unit tests only | `dotnet test tests/Jamsoft.DashBoards.Api.Testes --filter "Categoria=Unidade\|Categoria=Arquitetura"` |
| Quick-Web | Angular tasks with unit/component tests | `npm --prefix src/Jamsoft.DashBoards.Web test -- --watch=false` |
| Full | API tasks with integration tests | `dotnet test tests/Jamsoft.DashBoards.Api.Testes` |
| Full-Web | e2e task | `npm --prefix src/Jamsoft.DashBoards.Web test -- --watch=false && npm --prefix src/Jamsoft.DashBoards.Web run e2e` |
| Build | End of each phase; config-only tasks | `dotnet build Jamsoft.DashBoards.slnx -warnaserror && dotnet format Jamsoft.DashBoards.slnx --verify-no-changes && dotnet test Jamsoft.DashBoards.slnx` (+ when `src/Jamsoft.DashBoards.Web` exists: `npm --prefix src/Jamsoft.DashBoards.Web run lint && npm --prefix src/Jamsoft.DashBoards.Web run build && npm --prefix src/Jamsoft.DashBoards.Web test -- --watch=false`) |

---

## Execution Plan

Phases are ordered and run sequentially - each phase completes before the next begins, and tasks within a phase execute in order. Arrows show dependencies inside a phase; dependencies on earlier phases are listed only in each task's `Depends on`.

### Phase 1: Fundação da API e esquema

```
T1 → T2
T1 → T3 → T4 → T5 → T6 → T7
```

### Phase 2: Segurança base e isolamento

```
T8 → T9 → T10
T8 → T11 → T15
T12 → T13 → T14
T8 → T13 → T15
```

### Phase 3: Autenticação

```
T16 → T17 → T18
T17 → T19 → T20
T17 → T21
```

### Phase 4: Tenants e auditoria

```
T22 → T23 → T24 → T25
T26 → T27
```

### Phase 5: Empresas e usuários

```
T28 → T29
T30 → T32
T31 → T32
T31 → T33
T34
```

### Phase 6: Exclusão de tenant

```
T35 → T37
T36 → T37 → T38
```

### Phase 7: Verificação transversal da API

```
T39
T40
```

### Phase 8: Frontend base

```
T41 → T42
T41 → T43 → T44 → T45
T41 → T46 → T45
T44 → T47
```

### Phase 9: Telas de conta e administração

```
T48 → T49
T50 → T51
T50 → T52
T50 → T53
T54
```

### Phase 10: Telas do cliente e e2e

```
T55 → T56 → T58
T57 → T58
```

---

## Task Breakdown

### Phase 1 tasks

### T1: Criar solução, projeto da API e projeto de testes

**What**: Solução `Jamsoft.DashBoards.slnx` com `src/Jamsoft.DashBoards.Api` (.NET 10, Minimal APIs, `GET /health` anônimo) e `tests/Jamsoft.DashBoards.Api.Testes` (xUnit, `WebApplicationFactory`, fixture `BancoDeTestes` com Testcontainers.MsSql ou `TESTES_SQLSERVER`).
**Where**: `Jamsoft.DashBoards.slnx`
**Depends on**: None
**Reuses**: `Jamsoft.Sge.Mcp/Program.cs` (estrutura do host, `/health`), `.csproj` (Nullable, ImplicitUsings, UserSecretsId)
**Requirement**: suporte (AD-009)

**Tools**:

- MCP: NONE
- Skill: NONE

**Done when**:

- [x] `dotnet build Jamsoft.DashBoards.slnx` sem warnings
- [x] Teste de integração `GET /health` → 200 `{ "status": "ok" }` passa
- [x] Fixture sobe SQL Server uma vez por coleção de testes
- [x] Gate check passes: Full
- [x] Test count: 1 test passes

**Tests**: integration
**Gate**: full

**Commit**: `chore(api): criar solucao, projeto da API e projeto de testes`

---

### T2: Padronizar erros com ProblemDetails

**What**: `ProblemasApi` (fábrica de ProblemDetails com extensão `codigo`, `title` pt-BR) + `TratadorExcecoes` (`IExceptionHandler`): exceção não tratada → 500 `ERRO_INTERNO` com `traceId`; falha de validação → 400 `VALIDACAO` com `erros`.
**Where**: `src/Jamsoft.DashBoards.Api/Infraestrutura/Erros/ProblemasApi.cs`
**Depends on**: T1
**Reuses**: formato de challenge de `ApiKeyAuthenticationHandler.cs` (reescrito como ProblemDetails)
**Requirement**: suporte (AD-011)

**Tools**:

- MCP: NONE
- Skill: NONE

**Done when**:

- [x] Rota de teste que lança exceção → 500, `application/problem+json`, `codigo = ERRO_INTERNO`, `traceId` presente, sem stack trace no corpo
- [x] Rota de teste com validação inválida → 400 `VALIDACAO` com `erros` por campo
- [x] Gate check passes: Full
- [x] Test count: 3 tests pass (no silent deletions)

**Tests**: integration
**Gate**: full

**Commit**: `feat(api): padronizar respostas de erro com ProblemDetails`

---

### T3: Convenção de nomes UPPER_SNAKE

**What**: `NomesUpperSnake.Aplicar(ModelBuilder)`: tabelas no singular em UPPER_SNAKE e colunas `PascalCase` → `UPPER_SNAKE` (`TenantId` → `TENANT_ID`, `IdPublico` → `ID_PUBLICO`).
**Where**: `src/Jamsoft.DashBoards.Api/Infraestrutura/Dados/Convencoes/NomesUpperSnake.cs`
**Depends on**: T1
**Reuses**: NONE
**Requirement**: suporte (AD-012)

**Tools**:

- MCP: NONE
- Skill: NONE

**Done when**:

- [x] Testes unitários cobrem: palavra simples, composta, sigla (`Cnpj` → `CNPJ`), sufixo `Id`, número
- [x] Gate check passes: Quick
- [x] Test count: 5 tests pass

**Tests**: unit
**Gate**: quick

**Commit**: `feat(api): convencao de nomes UPPER_SNAKE para o banco`

---

### T4: PlataformaDbContext, IPertenceAoTenant e tabela TENANT

**What**: `PlataformaDbContext` com convenção do T3, interface `IPertenceAoTenant`, entidade `Tenant` (`ID`, `ID_PUBLICO` com `NEWSEQUENTIALID()`, `NOME`, `CNPJ CHAR(14)` único, `ATIVO`, `CRIADO_EM`, `ALTERADO_EM`, `VERSAO`) e migração `Inicial`.
**Where**: `src/Jamsoft.DashBoards.Api/Infraestrutura/Dados/PlataformaDbContext.cs`
**Depends on**: T3
**Reuses**: NONE
**Requirement**: TEN-01, TEN-02, TEN-07

**Tools**:

- MCP: NONE
- Skill: NONE

**Done when**:

- [x] Migração aplicada no banco de testes cria `TENANT` com os nomes de colunas do design
- [x] Teste de esquema: CNPJ duplicado viola índice único; `ID_PUBLICO` é gerado pelo banco
- [x] Gate check passes: Full
- [x] Test count: 2 new tests pass

**Tests**: integration
**Gate**: full

**Commit**: `feat(api): criar DbContext da plataforma e tabela TENANT`

---

### T5: Tabela EMPRESA

**What**: Entidade `Empresa : IPertenceAoTenant` (`TENANT_ID`, `COD_EMPRESA`, `NOME`, `NOME_MANUAL`, `VERSAO`), índice único (`TENANT_ID`, `COD_EMPRESA`), FK `NO ACTION` e migração.
**Where**: `src/Jamsoft.DashBoards.Api/Modulos/Empresas/Empresa.cs`
**Depends on**: T4
**Reuses**: convenções do T3/T4
**Requirement**: EMP-01, ISO-05

**Tools**:

- MCP: NONE
- Skill: NONE

**Done when**:

- [x] Teste de esquema: mesmo `COD_EMPRESA` aceito em tenants diferentes e recusado no mesmo tenant
- [x] Índice tem `TENANT_ID` como primeira coluna
- [x] Gate check passes: Full
- [x] Test count: 2 new tests pass

**Tests**: integration
**Gate**: full

**Commit**: `feat(api): criar tabela EMPRESA`

---

### T6: Tabelas USUARIO e REFRESH_TOKEN

**What**: Entidades `Usuario` (e-mail único normalizado, `PERFIL`, `TENANT_ID` nulo só para SuperAdmin via CHECK, `FALHAS_LOGIN`, `BLOQUEADO_ATE`, `TROCAR_SENHA`, `VERSAO`) e `RefreshToken` (`HASH BINARY(32)` único, `FAMILIA`, datas, `TENANT_ID` cópia) e migração.
**Where**: `src/Jamsoft.DashBoards.Api/Modulos/Usuarios/Usuario.cs`
**Depends on**: T5
**Reuses**: convenções do T3/T4
**Requirement**: AUTH-01, USR-01, USR-03

**Tools**:

- MCP: NONE
- Skill: NONE

**Done when**:

- [x] Teste de esquema: e-mail duplicado recusado; SuperAdmin com tenant recusado; Admin sem tenant recusado
- [x] Teste de esquema: hash de refresh duplicado recusado
- [x] Gate check passes: Full
- [x] Test count: 4 new tests pass

**Tests**: integration
**Gate**: full

**Commit**: `feat(api): criar tabelas USUARIO e REFRESH_TOKEN`

---

### T7: Tabelas AUDITORIA e EXCLUSAO_TENANT

**What**: Entidades `RegistroAuditoria` (sem FK para tenant) e `ExclusaoTenant` (sem FK, índice único filtrado `WHERE SITUACAO IN ('Pendente','EmExecucao')`) e migração.
**Where**: `src/Jamsoft.DashBoards.Api/Modulos/Auditoria/RegistroAuditoria.cs`
**Depends on**: T6
**Reuses**: convenções do T3/T4
**Requirement**: AUD-02, DEL-09

**Tools**:

- MCP: NONE
- Skill: NONE

**Done when**:

- [x] Teste de esquema: segundo job `Pendente` para o mesmo tenant recusado; job `Concluida` + novo `Pendente` aceitos
- [x] Teste de esquema: registro de auditoria com `TENANT_ID_PUBLICO` inexistente é aceito (sem FK)
- [x] Gate check passes: Full
- [x] Test count: 3 new tests pass

**Tests**: integration
**Gate**: full

**Commit**: `feat(api): criar tabelas AUDITORIA e EXCLUSAO_TENANT`

---

### Phase 2 tasks

### T8: TenantAtual

**What**: `ITenantAtual`/`TenantAtual` (scoped): lê `perfil`, `tenant`, `sub`, `trocar_senha` das claims e `X-Tenant` do cabeçalho; SuperAdmin usa `X-Tenant`; Admin/Consulta usam o token; expõe `Divergente` quando `X-Tenant` ≠ token; resolve `ID_PUBLICO` → `ID` com cache de 5 min.
**Where**: `src/Jamsoft.DashBoards.Api/Seguranca/TenantAtual.cs`
**Depends on**: T4
**Reuses**: cache `IMemoryCache` de `RepositorioChaves.cs`
**Requirement**: ISO-01, CTX-01, CTX-03

**Tools**:

- MCP: NONE
- Skill: NONE

**Done when**:

- [x] Testes unitários: Admin sem cabeçalho; Admin com cabeçalho igual (ignorado); Admin com cabeçalho diferente (`Divergente`); Consulta idem; SuperAdmin com e sem `X-Tenant`; `X-Tenant` mal formatado; sem autenticação (`IdOuSemAcesso = -1`)
- [x] Gate check passes: Quick
- [x] Test count: 8 new tests pass

**Tests**: unit
**Gate**: quick

**Commit**: `feat(api): resolver tenant atual a partir do token`

---

### T9: Filtro global por tenant no DbContext

**What**: Filtro nomeado `"Tenant"` em todo `IPertenceAoTenant`: `e.TenantId == tenantAtual.IdOuSemAcesso`.
**Where**: `src/Jamsoft.DashBoards.Api/Infraestrutura/Dados/PlataformaDbContext.cs` (modify)
**Depends on**: T8
**Reuses**: T4
**Requirement**: ISO-02

**Tools**:

- MCP: NONE
- Skill: NONE

**Done when**:

- [x] Teste de integração: empresas de A e B semeadas; contexto como A lista só A; sem tenant lista zero
- [x] Gate check passes: Full
- [x] Test count: 2 new tests pass

**Tests**: integration
**Gate**: full

**Commit**: `feat(api): aplicar filtro global por tenant nas consultas`

---

### T10: Interceptor de escrita por tenant

**What**: `TenantSaveChangesInterceptor`: preenche `TenantId` em `Added` quando zero; lança `AcessoCruzadoException` se qualquer entidade adicionada/alterada/excluída tem `TenantId` ≠ tenant atual; `OperacaoGlobal()` libera o bloqueio de forma explícita.
**Where**: `src/Jamsoft.DashBoards.Api/Infraestrutura/Dados/TenantSaveChangesInterceptor.cs`
**Depends on**: T9
**Reuses**: T8
**Requirement**: ISO-02

**Tools**:

- MCP: NONE
- Skill: NONE

**Done when**:

- [x] Testes: inserção sem `TenantId` recebe o tenant atual; inserção, alteração e exclusão com tenant B estando em A lançam exceção e não gravam nada; dentro de `OperacaoGlobal()` gravam
- [x] AcessoCruzadoException → 403 + auditoria (corrigido em fix posterior)
- [x] Gate check passes: Full
- [x] Test count: 5 new tests pass

**Tests**: integration
**Gate**: full

**Commit**: `feat(api): bloquear escrita fora do tenant atual`

---

### T11: Serviço de auditoria

**What**: `IAuditoria` com `Registrar(...)` (entra na transação do `DbContext`) e `RegistrarAgoraAsync(...)` (grava na hora); preenche usuário, perfil, IP e data UTC; serializa `detalhes` removendo chaves `senha`, `token`, `secret`.
**Where**: `src/Jamsoft.DashBoards.Api/Modulos/Auditoria/Auditoria.cs`
**Depends on**: T8
**Reuses**: T7
**Requirement**: AUD-01, AUD-02

**Tools**:

- MCP: NONE
- Skill: NONE

**Done when**:

- [x] Testes: `Registrar` só persiste com `SaveChanges` (rollback descarta); `RegistrarAgoraAsync` persiste mesmo sem transação; detalhes com `senha` saem sem essa chave; todos os campos do AUD-02 preenchidos
- [x] Gate check passes: Full
- [x] Test count: 4 new tests pass

**Tests**: integration
**Gate**: full

**Commit**: `feat(api): servico de auditoria`

---

### T12: TokenService (emissão de JWT)

**What**: `TokenService.Emitir(usuario, tenant)` → JWT HS256 de 15 min com `sub`, `perfil`, `tenant`, `trocar_senha`, `jti`; `JwtOptions` com `ValidateOnStart` (chave ≥ 32 bytes) e suporte a duas chaves de validação.
**Where**: `src/Jamsoft.DashBoards.Api/Seguranca/TokenService.cs`
**Depends on**: T1
**Reuses**: `McpOptions.cs` (padrão de options com validação)
**Requirement**: AUTH-01

**Tools**:

- MCP: NONE
- Skill: NONE

**Done when**:

- [x] Testes unitários: claims corretas por perfil (SuperAdmin sem `tenant`); expiração 15 min; chave curta falha na validação de options; token assinado com a chave secundária é aceito pelos parâmetros de validação
- [x] Gate check passes: Quick
- [x] Test count: 5 new tests pass

**Tests**: unit
**Gate**: quick

**Commit**: `feat(api): emissao de JWT`

---

### T13: Autenticação JwtBearer e policies

**What**: Configuração do `JwtBearer` (401 como ProblemDetails `NAO_AUTENTICADO`), policies `SuperAdmin`, `AdminTenant`, `LeituraTenant`, `Coletor` e fallback (autenticado e sem troca de senha pendente; `.AllowTrocaSenhaPendente()` para exceções).
**Where**: `src/Jamsoft.DashBoards.Api/Seguranca/Policies.cs`
**Depends on**: T12, T8
**Reuses**: T2, T12
**Requirement**: AUTH-08, USR-08, TEN-08, USR-10

**Tools**:

- MCP: NONE
- Skill: NONE

**Done when**:

- [x] Testes com rotas de teste: sem token → 401; token expirado → 401; perfil errado → 403 `ACESSO_NEGADO`; `trocar_senha` → 403 `TROCA_SENHA_PENDENTE` exceto rota liberada; token `Coletor` → 403 em rota de usuário; SuperAdmin sem tenant em `AdminTenant` → 400 `TENANT_NAO_SELECIONADO`
- [x] Gate check passes: Full
- [x] Test count: 7 new tests pass

**Tests**: integration
**Gate**: full

**Commit**: `feat(api): autenticacao JWT e policies por perfil`

---

### T14: StatusAcessoCache

**What**: `StatusAcessoCache` (TTL 60 s) consultado em `OnTokenValidated`: usuário ou tenant inativo → falha de autenticação (401); método `Invalidar(usuarioId | tenantId)`.
**Where**: `src/Jamsoft.DashBoards.Api/Seguranca/StatusAcessoCache.cs`
**Depends on**: T13
**Reuses**: padrão de cache de `RepositorioChaves.cs`
**Requirement**: AUTH-07, USR-06, TEN-05

**Tools**:

- MCP: NONE
- Skill: NONE

**Done when**:

- [x] Testes: token válido de usuário inativado → 401 após `Invalidar`; tenant inativado → 401 para todos os usuários dele; usuário ativo segue 200; consulta ao banco no máximo 1 vez por TTL
- [x] Gate check passes: Full
- [x] Test count: 4 new tests pass

**Tests**: integration
**Gate**: full

**Commit**: `feat(api): revalidar usuario e tenant ativos a cada requisicao`

---

### T15: Filtro de contexto de tenant

**What**: `ValidacaoContextoTenantFilter` (`IEndpointFilter`) para rotas tenant-scoped: `TenantAtual.Divergente` → 403 `TENANT_DIVERGENTE` + auditoria `ACESSO_CRUZADO`; também compara `tenantId` presente no corpo/rota com o tenant efetivo.
**Where**: `src/Jamsoft.DashBoards.Api/Seguranca/ValidacaoContextoTenantFilter.cs`
**Depends on**: T13, T11
**Reuses**: T8, T11
**Requirement**: ISO-04, CTX-03

**Tools**:

- MCP: NONE
- Skill: NONE

**Done when**:

- [x] Testes: Admin com `X-Tenant` de outro tenant → 403 + 1 registro de auditoria; Admin com `X-Tenant` igual → 200; corpo com tenant divergente → 403; SuperAdmin com `X-Tenant` → 200
- [x] Gate check passes: Full
- [x] Test count: 4 new tests pass

**Tests**: integration
**Gate**: full

**Commit**: `feat(api): recusar contexto de tenant divergente do token`

---

### Phase 3 tasks

### T16: Comandos CLI criar-superadmin e migrar

**What**: `ComandosCli.TentarExecutar`: `migrar` aplica as migrações; `criar-superadmin --email --nome` cria SuperAdmin com senha temporária gerada, exibida uma vez, e `TROCAR_SENHA = 1`.
**Where**: `src/Jamsoft.DashBoards.Api/Cli/ComandosCli.cs`
**Depends on**: T6
**Reuses**: `Jamsoft.Sge.Mcp/Cli/ComandosCli.cs`, `ApiKey.Gerar` (geração aleatória)
**Requirement**: suporte (Assumption "SuperAdmin inicial")

**Tools**:

- MCP: NONE
- Skill: NONE

**Done when**:

- [x] Testes: `migrar` em banco vazio cria todas as tabelas; `criar-superadmin` cria usuário com perfil SuperAdmin, sem tenant e com troca pendente; e-mail repetido falha com mensagem clara
- [x] Gate check passes: Full
- [x] Test count: 3 new tests pass

**Tests**: integration
**Gate**: full

**Commit**: `feat(api): comandos CLI migrar e criar-superadmin`

---

### T17: POST /api/auth/login

**What**: Endpoint de login: verifica senha com `PasswordHasher<Usuario>` (hash fictício para e-mail inexistente), usuário/tenant inativo → 403 `ACESSO_DESATIVADO`, sucesso → access token + cookie `rt` + `ULTIMO_LOGIN_EM`; auditoria de login de SuperAdmin.
**Where**: `src/Jamsoft.DashBoards.Api/Modulos/Autenticacao/LoginEndpoint.cs`
**Depends on**: T16
**Reuses**: T12, T11
**Requirement**: AUTH-01, AUTH-02, AUTH-07, AUTH-09

**Tools**:

- MCP: NONE
- Skill: NONE

**Done when**:

- [x] Testes: login válido (Admin, Consulta, SuperAdmin) → 200 com campos do AUTH-01 e cookie `HttpOnly; Secure; SameSite=Strict; Path=/api/auth`; e-mail inexistente e senha errada → 401 com corpo idêntico; usuário inativo e tenant inativo → 403; logs capturados não contêm senha nem tokens
- [x] Gate check passes: Full
- [x] Test count: 7 new tests pass

**Tests**: integration
**Gate**: full

**Commit**: `feat(api): endpoint de login`

---

### T18: Bloqueio por tentativas e rate limit de login

**What**: `Bloqueio`: incrementa `FALHAS_LOGIN` de forma atômica; na 5ª falha grava `BLOQUEADO_ATE = agora + 15 min` → 423 `USUARIO_BLOQUEADO` com `bloqueadoAte`; sucesso zera; rate limiter de 20 req/min por IP no login → 429.
**Where**: `src/Jamsoft.DashBoards.Api/Modulos/Autenticacao/Bloqueio.cs`
**Depends on**: T17
**Reuses**: `AcessoOptions` (tentativas e minutos configuráveis)
**Requirement**: AUTH-03

**Tools**:

- MCP: NONE
- Skill: NONE

**Done when**:

- [x] Testes: 4 falhas + sucesso zera contador; 5 falhas → 423 até expirar (relógio falso via `TimeProvider`); login correto durante bloqueio → 423; após 15 min → 200; 21ª requisição no minuto → 429
- [x] Gate check passes: Full
- [x] Test count: 5 new tests pass

**Tests**: integration
**Gate**: full

**Commit**: `feat(api): bloqueio por tentativas e limite de requisicoes no login`

---

### T19: Refresh tokens e POST /api/auth/refresh

**What**: `RefreshTokens` (gera 48 caracteres, guarda SHA-256, família, rotação) e endpoint `/api/auth/refresh` exigindo cookie `rt` e `X-Requested-With: jamsoft`; reuso de token usado/revogado → 401 e revogação de todos os tokens do usuário.
**Where**: `src/Jamsoft.DashBoards.Api/Seguranca/RefreshTokens.cs`
**Depends on**: T17
**Reuses**: `ApiKey.Gerar`/`Hash`, `CryptographicOperations.FixedTimeEquals`
**Requirement**: AUTH-04, AUTH-05

**Tools**:

- MCP: NONE
- Skill: NONE

**Done when**:

- [x] Testes: refresh válido → novo par e antigo marcado usado; reuso do antigo → 401 e todos os tokens do usuário revogados; expirado → 401; sem `X-Requested-With` → 400; token de usuário inativo → 401
- [x] Gate check passes: Full
- [x] Test count: 5 new tests pass

**Tests**: integration
**Gate**: full

**Commit**: `feat(api): refresh token rotativo com deteccao de reuso`

---

### T20: POST /api/auth/logout

**What**: Revoga a família do refresh token do cookie e expira o cookie.
**Where**: `src/Jamsoft.DashBoards.Api/Modulos/Autenticacao/LogoutEndpoint.cs`
**Depends on**: T19
**Reuses**: T19
**Requirement**: AUTH-06

**Tools**:

- MCP: NONE
- Skill: NONE

**Done when**:

- [x] Testes: após logout, refresh com o cookie antigo → 401; resposta expira o cookie; logout sem cookie → 204 (idempotente)
- [x] Gate check passes: Full
- [x] Test count: 3 new tests pass

**Tests**: integration
**Gate**: full

**Commit**: `feat(api): endpoint de logout`

---

### T21: Troca de senha e GET /api/auth/eu

**What**: `POST /api/auth/trocar-senha` (liberado com troca pendente; valida política de senha; zera `TROCAR_SENHA`; emite novo access token) e `GET /api/auth/eu`.
**Where**: `src/Jamsoft.DashBoards.Api/Modulos/Autenticacao/TrocarSenhaEndpoint.cs`
**Depends on**: T17
**Reuses**: T13 (`AllowTrocaSenhaPendente`)
**Requirement**: USR-08

**Tools**:

- MCP: NONE
- Skill: NONE

**Done when**:

- [x] Testes: com troca pendente, outras rotas → 403 e trocar-senha → 200; senha atual errada → 400; nova senha fora da política (9 caracteres, sem dígito) → 400 `VALIDACAO`; depois da troca, rotas protegidas → 200; `/eu` devolve nome, e-mail, perfil, tenant
- [x] Gate check passes: Full
- [x] Test count: 6 new tests pass

**Tests**: integration
**Gate**: full

**Commit**: `feat(api): troca de senha e dados do usuario logado`

---

### Phase 4 tasks

### T22: Validação de CNPJ

**What**: `Cnpj.TentarNormalizar(string, out string)`: remove máscara, valida 14 dígitos e dígitos verificadores, recusa sequências repetidas.
**Where**: `src/Jamsoft.DashBoards.Api/Modulos/Tenants/Cnpj.cs`
**Depends on**: None
**Reuses**: NONE
**Requirement**: TEN-01

**Tools**:

- MCP: NONE
- Skill: NONE

**Done when**:

- [x] Testes: CNPJ válido com e sem máscara; dígito verificador errado; tamanho errado; `00000000000000`; letras
- [x] Gate check passes: Quick
- [x] Test count: 6 new tests pass

**Tests**: unit
**Gate**: quick

**Commit**: `feat(api): validacao de CNPJ`

---

### T23: POST /api/admin/tenants

**What**: Cria tenant (nome 3–120, CNPJ válido) com Admin inicial opcional, em uma transação; 201 com `idPublico`; CNPJ ou e-mail duplicado → 409; auditoria.
**Where**: `src/Jamsoft.DashBoards.Api/Modulos/Tenants/CriarTenantEndpoint.cs`
**Depends on**: T22
**Reuses**: T11, T13 (policy `SuperAdmin`)
**Requirement**: TEN-01, TEN-02, TEN-03, TEN-07, TEN-08

**Tools**:

- MCP: NONE
- Skill: NONE

**Done when**:

- [x] Testes: criação simples → 201 e resposta sem `ID INT`; com Admin inicial → Admin com troca pendente; CNPJ duplicado → 409 `CNPJ_DUPLICADO`; e-mail do Admin duplicado → 409 e tenant **não** criado (rollback); nome com 2 caracteres → 400; Admin chamando → 403; requisições simultâneas com o mesmo CNPJ → uma 201 e uma 409
- [x] Gate check passes: Full
- [x] Test count: 7 new tests pass

**Tests**: integration
**Gate**: full

**Commit**: `feat(api): cadastro de tenant com Admin inicial`

---

### T24: Consulta e edição de tenants

**What**: `GET /api/admin/tenants` (busca por nome/CNPJ, situação, paginação 20/100), `GET /api/admin/tenants/{idPublico}`, `PUT /api/admin/tenants/{idPublico}` com `versao` (409 `CONFLITO_VERSAO`).
**Where**: `src/Jamsoft.DashBoards.Api/Modulos/Tenants/TenantsEndpoints.cs`
**Depends on**: T23
**Reuses**: T2
**Requirement**: TEN-04, TEN-07

**Tools**:

- MCP: NONE
- Skill: NONE

**Done when**:

- [x] Testes: paginação padrão 20; `tamanho=500` → limitado a 100; filtro por nome e por CNPJ; filtro por situação; GUID inexistente → 404; GUID mal formatado → 400; edição com versão antiga → 409; lista vazia → 200 com `itens: []`
- [x] Gate check passes: Full
- [x] Test count: 8 new tests pass

**Tests**: integration
**Gate**: full

**Commit**: `feat(api): consulta e edicao de tenants`

---

### T25: Inativar e reativar tenant

**What**: `POST .../inativar` e `.../reativar`: alteram `ATIVO`, invalidam o `StatusAcessoCache` do tenant e auditam.
**Where**: `src/Jamsoft.DashBoards.Api/Modulos/Tenants/SituacaoTenantEndpoints.cs`
**Depends on**: T24
**Reuses**: T14, T11
**Requirement**: TEN-05, TEN-06

**Tools**:

- MCP: NONE
- Skill: NONE

**Done when**:

- [x] Testes: após inativar, login do Admin → 403 e token já emitido → 401; dados do tenant intactos; após reativar, login → 200; auditoria registra as duas operações
- [x] Gate check passes: Full
- [x] Test count: 4 new tests pass

**Tests**: integration
**Gate**: full

**Commit**: `feat(api): inativar e reativar tenant`

---

### T26: GET /api/admin/auditoria

**What**: Consulta paginada da auditoria com filtros por período, tenant, usuário e operação; somente SuperAdmin; nenhuma rota de escrita.
**Where**: `src/Jamsoft.DashBoards.Api/Modulos/Auditoria/AuditoriaEndpoints.cs`
**Depends on**: None
**Reuses**: T11
**Requirement**: AUD-03, AUD-04

**Tools**:

- MCP: NONE
- Skill: NONE

**Done when**:

- [x] Testes: cada filtro isolado e combinado; ordenação por `OCORRIDO_EM` desc; Admin → 403; `PUT`/`DELETE` em `/api/admin/auditoria/{id}` → 404/405
- [x] Gate check passes: Full
- [x] Test count: 6 new tests pass

**Tests**: integration
**Gate**: full

**Commit**: `feat(api): consulta de auditoria`

---

### T27: Script de permissões do banco

**What**: `sql/001_permissoes_aplicacao.sql`: role da aplicação com `SELECT/INSERT/UPDATE/DELETE` nas tabelas, `DENY UPDATE, DELETE ON AUDITORIA`.
**Where**: `sql/001_permissoes_aplicacao.sql`
**Depends on**: T26
**Reuses**: estilo de `Jamsoft.Sge.Mcp/sql/*.sql`
**Requirement**: AUD-04

**Tools**:

- MCP: NONE
- Skill: NONE

**Done when**:

- [x] Teste de integração aplica o script, conecta com um login da role e confirma que `UPDATE AUDITORIA` e `DELETE AUDITORIA` falham e `INSERT` funciona
- [x] Gate check passes: Full
- [x] Test count: 1 new test passes

**Tests**: integration
**Gate**: full

**Commit**: `feat(db): script de permissoes com auditoria somente insercao`

---

### Phase 5 tasks

### T28: IEmpresas.ObterOuCriarAsync

**What**: Serviço que obtém a empresa por (`TENANT_ID`, `COD_EMPRESA`) ou cria com o nome recebido; atualiza o nome só se `NOME_MANUAL = 0`; seguro sob concorrência (trata violação de índice único relendo).
**Where**: `src/Jamsoft.DashBoards.Api/Modulos/Empresas/Empresas.cs`
**Depends on**: None
**Reuses**: T5, T10
**Requirement**: EMP-01, EMP-04, EMP-05

**Tools**:

- MCP: NONE
- Skill: NONE

**Done when**:

- [x] Testes: cria quando não existe; reutiliza quando existe; atualiza nome automático; preserva nome manual; duas chamadas simultâneas criam uma única empresa
- [x] Gate check passes: Full
- [x] Test count: 5 new tests pass

**Tests**: integration
**Gate**: full

**Commit**: `feat(api): obter ou criar empresa do tenant`

---

### T29: Endpoints de empresas

**What**: `GET /api/empresas` (`LeituraTenant`), `POST /api/empresas` e `PUT /api/empresas/{idPublico}` (`AdminTenant`); renomear marca `NOME_MANUAL = 1`; auditoria.
**Where**: `src/Jamsoft.DashBoards.Api/Modulos/Empresas/EmpresasEndpoints.cs`
**Depends on**: T28
**Reuses**: T15 (filtro de contexto)
**Requirement**: EMP-02, EMP-03, EMP-05, EMP-06

**Tools**:

- MCP: NONE
- Skill: NONE

**Done when**:

- [x] Testes: Admin cria → 201; código duplicado → 409 `EMPRESA_DUPLICADA`; código 0 ou nome com 81 caracteres → 400; Consulta cria → 403; Consulta lista → só empresas do tenant; SuperAdmin com `X-Tenant` lista o tenant escolhido; renomear empresa de outro tenant → 404
- [x] Gate check passes: Full
- [x] Test count: 7 new tests pass

**Tests**: integration
**Gate**: full

**Commit**: `feat(api): endpoints de empresas`

---

### T30: Regra do último Admin

**What**: `RegrasUltimoAdmin.GarantirAsync(tenantId, usuarioAlvoId)`: conta Admins ativos exceto o alvo com `UPDLOCK, HOLDLOCK` dentro da transação; zero → `RegraNegocioException("ULTIMO_ADMIN")` (422).
**Where**: `src/Jamsoft.DashBoards.Api/Modulos/Usuarios/RegrasUltimoAdmin.cs`
**Depends on**: None
**Reuses**: T6
**Requirement**: USR-05

**Tools**:

- MCP: NONE
- Skill: NONE

**Done when**:

- [x] Testes: único Admin → exceção; dois Admins → permite; dois Admins se rebaixando ao mesmo tempo em transações paralelas → exatamente uma falha
- [x] Gate check passes: Full
- [x] Test count: 3 new tests pass

**Tests**: integration
**Gate**: full

**Commit**: `feat(api): garantir ao menos um Admin ativo por tenant`

---

### T31: Listar e criar usuários

**What**: `GET /api/usuarios` e `POST /api/usuarios` (`AdminTenant`): perfis aceitos `Admin`/`Consulta`, e-mail único (409 `EMAIL_EM_USO`, sem revelar o tenant), senha inicial válida, troca obrigatória; auditoria.
**Where**: `src/Jamsoft.DashBoards.Api/Modulos/Usuarios/UsuariosEndpoints.cs`
**Depends on**: None
**Reuses**: T13, T15, T11
**Requirement**: USR-01, USR-02, USR-03, USR-09, USR-10

**Tools**:

- MCP: NONE
- Skill: NONE

**Done when**:

- [x] Testes: Admin cria Consulta → 201 com troca pendente; perfil `SuperAdmin` ou `Coletor` → 403 `PERFIL_NAO_PERMITIDO`; e-mail de outro tenant → 409 com corpo sem dados do outro tenant; Consulta → 403; SuperAdmin com `X-Tenant` cria no tenant escolhido; lista não traz usuários de outro tenant nem `SENHA_HASH`
- [x] Gate check passes: Full
- [x] Test count: 7 new tests pass

**Tests**: integration
**Gate**: full

**Commit**: `feat(api): listar e criar usuarios do tenant`

---

### T32: Editar usuário

**What**: `PUT /api/usuarios/{idPublico}` (nome, perfil, ativo, versão): aplica `RegrasUltimoAdmin`; inativar revoga refresh tokens e invalida o `StatusAcessoCache`; auditoria.
**Where**: `src/Jamsoft.DashBoards.Api/Modulos/Usuarios/EditarUsuarioEndpoint.cs`
**Depends on**: T30, T31
**Reuses**: T14, T19
**Requirement**: USR-04, USR-05, USR-06

**Tools**:

- MCP: NONE
- Skill: NONE

**Done when**:

- [x] Testes: editar nome e perfil → 200 + auditoria; rebaixar o único Admin → 422 `ULTIMO_ADMIN` com a mensagem do spec; inativar usuário → refresh dele → 401; usuário de outro tenant → 404; versão antiga → 409
- [x] Gate check passes: Full
- [x] Test count: 5 new tests pass

**Tests**: integration
**Gate**: full

**Commit**: `feat(api): editar usuario do tenant`

---

### T33: Redefinir senha de usuário

**What**: `POST /api/usuarios/{idPublico}/redefinir-senha`: grava nova senha temporária, marca troca obrigatória, revoga refresh tokens, invalida cache; auditoria sem a senha.
**Where**: `src/Jamsoft.DashBoards.Api/Modulos/Usuarios/RedefinirSenhaEndpoint.cs`
**Depends on**: T31
**Reuses**: T19, T14
**Requirement**: USR-07

**Tools**:

- MCP: NONE
- Skill: NONE

**Done when**:

- [x] Testes: após redefinir, login com a nova senha exige troca; refresh antigo → 401; senha fora da política → 400; usuário de outro tenant → 404; detalhe da auditoria não contém a senha
- [x] Gate check passes: Full
- [x] Test count: 5 new tests pass

**Tests**: integration
**Gate**: full

**Commit**: `feat(api): redefinir senha de usuario`

---

### T34: Auditoria de acesso do SuperAdmin a tenant

**What**: Endpoint filter nas rotas tenant-scoped: SuperAdmin com `X-Tenant` gera auditoria `ACESSO_CONTEXTO_TENANT` no máximo 1 vez por (SuperAdmin, tenant, rota) a cada 10 min.
**Where**: `src/Jamsoft.DashBoards.Api/Seguranca/AuditoriaContextoSuperAdminFilter.cs`
**Depends on**: None
**Reuses**: T11, T8
**Requirement**: CTX-02

**Tools**:

- MCP: NONE
- Skill: NONE

**Done when**:

- [x] Testes: primeiro acesso gera 1 registro com SuperAdmin, tenant e rota; segundo acesso em 5 min não gera; após 10 min (relógio falso) gera; Admin não gera registro
- [x] Gate check passes: Full
- [x] Test count: 4 new tests pass

**Tests**: integration
**Gate**: full

**Commit**: `feat(api): auditar acesso de SuperAdmin ao contexto de tenant`

---

### Phase 6 tasks

### T35: PlanoExclusao

**What**: `PlanoExclusao` monta a lista de tabelas a partir do modelo EF (`IPertenceAoTenant` + `USUARIO` + `REFRESH_TOKEN`) em ordem topológica filhos → pais, com `TENANT` por último; teste de arquitetura garante que toda entidade com `TenantId` está no plano.
**Where**: `src/Jamsoft.DashBoards.Api/Modulos/Exclusao/PlanoExclusao.cs`
**Depends on**: None
**Reuses**: metadados do `PlataformaDbContext` (T4)
**Requirement**: DEL-04, DEL-07

**Tools**:

- MCP: NONE
- Skill: NONE

**Done when**:

- [x] Testes unitários: `REFRESH_TOKEN` antes de `USUARIO`; `USUARIO` e `EMPRESA` antes de `TENANT`; `AUDITORIA` e `EXCLUSAO_TENANT` fora do plano; entidade de teste com `TenantId` sem `IPertenceAoTenant` faz o teste de arquitetura falhar
- [x] Gate check passes: Quick
- [x] Test count: 4 new tests pass

**Tests**: unit
**Gate**: quick

**Commit**: `feat(api): plano de exclusao de tenant em ordem de dependencias`

---

### T36: POST /api/admin/tenants/{idPublico}/exclusao

**What**: Valida confirmação (nome exato, ordinal), inativa o tenant, cria job `Pendente` e responde 202 `{ jobId }`; job ativo existente → 409 `EXCLUSAO_EM_ANDAMENTO`; auditoria.
**Where**: `src/Jamsoft.DashBoards.Api/Modulos/Exclusao/SolicitarExclusaoEndpoint.cs`
**Depends on**: None
**Reuses**: T7, T14, T11
**Requirement**: DEL-01, DEL-02, DEL-03, DEL-08, DEL-09

**Tools**:

- MCP: NONE
- Skill: NONE

**Done when**:

- [x] Testes: Admin → 403; nome com caixa diferente ou espaço extra → 422 `CONFIRMACAO_INVALIDA` e nada criado; confirmação válida → 202, tenant inativo, job `Pendente`, auditoria; segunda solicitação → 409
- [x] Gate check passes: Full
- [x] Test count: 5 new tests pass

**Tests**: integration
**Gate**: full

**Commit**: `feat(api): solicitar exclusao de tenant`

---

### T37: ExclusaoTenantWorker

**What**: `BackgroundService` que reivindica jobs (`UPDLOCK, READPAST`; também `EmExecucao` sem heartbeat há 2 min), executa `DELETE TOP (5000) ... WHERE TENANT_ID = @t` por tabela do plano, atualiza etapa/linhas/heartbeat por lote, pausa 200 ms entre lotes, conclui removendo `TENANT` e marca `Falhou` com etapa e erro em caso de exceção; audita cada mudança de situação.
**Where**: `src/Jamsoft.DashBoards.Api/Modulos/Exclusao/ExclusaoTenantWorker.cs`
**Depends on**: T35, T36
**Reuses**: T35, T11, `OperacaoGlobal()` (T10)
**Requirement**: DEL-04, DEL-05, DEL-06, DEL-07, DEL-08

**Tools**:

- MCP: NONE
- Skill: NONE

**Done when**:

- [x] Testes: tenants A e B com 12.000 linhas cada; excluir A → 0 linhas de A em todas as tabelas, B inalterado, `LINHAS_REMOVIDAS` por tabela correto, job `Concluida`, tenant removido; falha injetada na 2ª tabela → `Falhou` com `ETAPA_ATUAL`, tenant segue inativo; job `EmExecucao` sem heartbeat é retomado; dois workers não processam o mesmo job
- [x] Gate check passes: Full
- [x] Test count: 5 new tests pass

**Tests**: integration
**Gate**: full

**Commit**: `feat(api): worker de exclusao de tenant em lotes`

---

### T38: Acompanhar e retomar exclusão

**What**: `GET /api/admin/exclusoes/{jobId}` (situação, etapa, linhas por tabela, erro) e `POST /api/admin/exclusoes/{jobId}/retomar` (só `Falhou` → `Pendente`; outros → 409).
**Where**: `src/Jamsoft.DashBoards.Api/Modulos/Exclusao/ExclusoesEndpoints.cs`
**Depends on**: T37
**Reuses**: T37
**Requirement**: DEL-06, DEL-08

**Tools**:

- MCP: NONE
- Skill: NONE

**Done when**:

- [ ] Testes: consulta de job existente e inexistente (404); retomar job `Falhou` → 202 e o worker conclui a partir da etapa salva; retomar `Concluida` → 409; Admin → 403
- [ ] Gate check passes: Full
- [ ] Test count: 5 new tests pass

**Tests**: integration
**Gate**: full

**Commit**: `feat(api): acompanhar e retomar exclusao de tenant`

---

### Phase 7 tasks

### T39: Suíte de isolamento entre tenants

**What**: Teste parametrizado que percorre todas as rotas tenant-scoped (via `EndpointDataSource`) e, autenticado como Admin e Consulta do tenant A, tenta ler, alterar e excluir recursos do tenant B; também verifica que toda tabela com `TENANT_ID` tem índice iniciando por ele.
**Where**: `tests/Jamsoft.DashBoards.Api.Testes/Integracao/IsolamentoTenantsTestes.cs`
**Depends on**: T38
**Reuses**: fixtures dos testes anteriores
**Requirement**: ISO-02, ISO-03, ISO-05

**Tools**:

- MCP: NONE
- Skill: NONE

**Done when**:

- [ ] Toda rota tenant-scoped responde 404 (ou 403 com `X-Tenant`) e o banco de B fica inalterado (checksum por tabela)
- [ ] Teste falha se uma rota nova tenant-scoped não tiver caso cadastrado
- [ ] Gate check passes: Full
- [ ] Test count: ≥ 1 caso por rota tenant-scoped, todos passando

**Tests**: integration
**Gate**: full

**Commit**: `test(api): suite de isolamento entre tenants`

---

### T40: Testes de arquitetura da API

**What**: Regras: toda rota tem policy explícita ou `AllowAnonymous` declarado; `IgnoreQueryFilters`/`OperacaoGlobal` só nos namespaces `Tenants`, `Auditoria`, `Exclusao`, `Autenticacao`; toda entidade com `TenantId` implementa `IPertenceAoTenant`; rotas `/api/admin/**` exigem `SuperAdmin`.
**Where**: `tests/Jamsoft.DashBoards.Api.Testes/Arquitetura/RegrasArquiteturaTestes.cs`
**Depends on**: T38
**Reuses**: NONE
**Requirement**: AUTH-08, ISO-02, TEN-08

**Tools**:

- MCP: NONE
- Skill: NONE

**Done when**:

- [ ] As 4 regras passam no código atual
- [ ] Cada regra falha quando uma violação de exemplo é introduzida (verificado localmente e descartado)
- [ ] Gate check passes: Quick
- [ ] Test count: 4 new tests pass

**Tests**: unit
**Gate**: quick

**Commit**: `test(api): regras de arquitetura de seguranca`

---

### Phase 8 tasks

### T41: Criar projeto Angular 22

**What**: `src/Jamsoft.DashBoards.Web` com Angular 22 (standalone, zoneless, Vitest), ESLint, rotas lazy `conta`, `admin`, `cliente`, proxy de desenvolvimento `/api` → API, e `@angular/pwa`.
**Where**: `src/Jamsoft.DashBoards.Web/angular.json`
**Depends on**: None
**Reuses**: NONE
**Requirement**: suporte (AD-008)

**Tools**:

- MCP: NONE
- Skill: NONE

**Done when**:

- [ ] `npm --prefix src/Jamsoft.DashBoards.Web run lint`, `run build` e `test -- --watch=false` passam
- [ ] Gate check passes: Build

**Tests**: none
**Gate**: build

**Commit**: `chore(web): criar projeto Angular 22`

---

### T42: Tokens do Design System

**What**: Ler o Design System do artifact do §3.1 e traduzir cores, tipografia, espaçamentos, raios e sombras para variáveis CSS em `styles/_tokens.scss` (tema claro e escuro).
**Where**: `src/Jamsoft.DashBoards.Web/src/styles/_tokens.scss`
**Depends on**: T41
**Reuses**: Design System `https://claude.ai/artifact/7H4jYEbfVCQPwkWNfTeP54`
**Requirement**: suporte (UI-06 de `painel-vendas`)

**Tools**:

- MCP: NONE
- Skill: Artifact (action `read`) para ler o Design System

**Done when**:

- [ ] Todos os tokens do Design System existem como variáveis CSS com os mesmos nomes/valores
- [ ] Build passa
- [ ] Gate check passes: Build

**Tests**: none
**Gate**: build

**Commit**: `feat(web): tokens do design system`

---

### T43: Tradução de ProblemDetails

**What**: `problema.ts`: converte `HttpErrorResponse` com `application/problem+json` em `{ codigo, mensagem, erros }` e mapeia cada `codigo` do design para a mensagem pt-BR.
**Where**: `src/Jamsoft.DashBoards.Web/src/app/core/erros/problema.ts`
**Depends on**: T41
**Reuses**: tabela de erros do `design.md`
**Requirement**: AUTH-02, AUTH-03, USR-05

**Tools**:

- MCP: NONE
- Skill: NONE

**Done when**:

- [ ] Testes: cada `codigo` da tabela do design gera a mensagem esperada; erro sem ProblemDetails → "Erro inesperado"; `USUARIO_BLOQUEADO` formata o horário; `VALIDACAO` expõe erros por campo
- [ ] Gate check passes: Quick-Web
- [ ] Test count: ≥ 18 tests pass

**Tests**: unit
**Gate**: quick-web

**Commit**: `feat(web): traduzir erros da API`

---

### T44: AuthService

**What**: `AuthService`: `login`, `logout`, `renovar` (chamadas concorrentes compartilham uma requisição), `restaurarSessao` no bootstrap via refresh, `usuario` como signal; access token só em memória.
**Where**: `src/Jamsoft.DashBoards.Web/src/app/core/auth/auth.service.ts`
**Depends on**: T43
**Reuses**: T43
**Requirement**: AUTH-01, AUTH-04, AUTH-06

**Tools**:

- MCP: NONE
- Skill: NONE

**Done when**:

- [ ] Testes: login preenche `usuario`; logout limpa estado e chama a API; 3 `renovar()` simultâneos fazem 1 requisição; `restaurarSessao` com refresh 401 deixa `usuario = null`; nada é gravado em `localStorage`/`sessionStorage`
- [ ] Gate check passes: Quick-Web
- [ ] Test count: 5 new tests pass

**Tests**: unit
**Gate**: quick-web

**Commit**: `feat(web): servico de autenticacao`

---

### T45: authInterceptor

**What**: Interceptor que adiciona `Authorization` e `X-Tenant`; em 401 renova uma vez e repete; falhando, faz logout e navega para `/conta/login`; em 403 `TROCA_SENHA_PENDENTE` navega para `/conta/trocar-senha`.
**Where**: `src/Jamsoft.DashBoards.Web/src/app/core/auth/auth.interceptor.ts`
**Depends on**: T44, T46
**Reuses**: T44, T46
**Requirement**: AUTH-04, USR-08, CTX-01

**Tools**:

- MCP: NONE
- Skill: NONE

**Done when**:

- [ ] Testes: cabeçalhos presentes; sem tenant selecionado não envia `X-Tenant`; 401 → renova e repete com sucesso; 401 após renovar → login; rotas `/api/auth/*` não disparam renovação; 403 troca pendente → redireciona
- [ ] Gate check passes: Quick-Web
- [ ] Test count: 6 new tests pass

**Tests**: unit
**Gate**: quick-web

**Commit**: `feat(web): interceptor de autenticacao e contexto de tenant`

---

### T46: TenantContextoService

**What**: Guarda o tenant selecionado pelo SuperAdmin (`idPublico`, `nome`) em signal + `sessionStorage`; limpa no logout; ignorado para outros perfis.
**Where**: `src/Jamsoft.DashBoards.Web/src/app/core/auth/tenant-contexto.service.ts`
**Depends on**: T41
**Reuses**: NONE
**Requirement**: CTX-01

**Tools**:

- MCP: NONE
- Skill: NONE

**Done when**:

- [ ] Testes: selecionar/limpar; restaura após recarregar a página; `sessionStorage` indisponível não quebra; logout limpa
- [ ] Gate check passes: Quick-Web
- [ ] Test count: 4 new tests pass

**Tests**: unit
**Gate**: quick-web

**Commit**: `feat(web): contexto de tenant do SuperAdmin`

---

### T47: perfilGuard e rotas por perfil

**What**: `perfilGuard` (`canMatch`) e configuração de rotas: `admin/**` só SuperAdmin; `cliente/usuarios` e `cliente/empresas` para Admin e SuperAdmin com tenant; sem permissão → home do perfil com aviso "Acesso não permitido"; sem sessão → login.
**Where**: `src/Jamsoft.DashBoards.Web/src/app/core/auth/perfil.guard.ts`
**Depends on**: T44
**Reuses**: T44
**Requirement**: AUTH-08, TEN-08, USR-10

**Tools**:

- MCP: NONE
- Skill: NONE

**Done when**:

- [ ] Testes: matriz perfil × rota (SuperAdmin, Admin, Consulta, anônimo) com o resultado esperado de cada combinação
- [ ] Gate check passes: Quick-Web
- [ ] Test count: ≥ 12 tests pass

**Tests**: unit
**Gate**: quick-web

**Commit**: `feat(web): guard de rotas por perfil`

---

### Phase 9 tasks

### T48: Tela de login

**What**: Página `/conta/login` com e-mail e senha, validação de campos, mensagens de erro por `codigo` (credenciais inválidas, bloqueado até HH:mm, acesso desativado, muitas requisições) e redirecionamento por perfil.
**Where**: `src/Jamsoft.DashBoards.Web/src/app/conta/login/login.page.ts`
**Depends on**: None
**Reuses**: T44, T43, T42
**Requirement**: AUTH-01, AUTH-02, AUTH-03, AUTH-07

**Tools**:

- MCP: NONE
- Skill: NONE

**Done when**:

- [ ] Testes de componente: campos obrigatórios; botão desabilitado enquanto envia; cada erro mostra a mensagem certa; sucesso redireciona SuperAdmin → `/admin/tenants`, Admin/Consulta → home do cliente, troca pendente → `/conta/trocar-senha`
- [ ] Gate check passes: Quick-Web
- [ ] Test count: 7 new tests pass

**Tests**: unit
**Gate**: quick-web

**Commit**: `feat(web): tela de login`

---

### T49: Tela de troca de senha

**What**: Página `/conta/trocar-senha` com senha atual, nova e confirmação; valida a política (10+ caracteres, letra e dígito) antes de enviar.
**Where**: `src/Jamsoft.DashBoards.Web/src/app/conta/trocar-senha/trocar-senha.page.ts`
**Depends on**: T48
**Reuses**: T44
**Requirement**: USR-08

**Tools**:

- MCP: NONE
- Skill: NONE

**Done when**:

- [ ] Testes: confirmação diferente bloqueia envio; política violada mostra regra; senha atual errada mostra erro da API; sucesso leva à home do perfil
- [ ] Gate check passes: Quick-Web
- [ ] Test count: 4 new tests pass

**Tests**: unit
**Gate**: quick-web

**Commit**: `feat(web): tela de troca de senha`

---

### T50: Lista de tenants

**What**: Página `/admin/tenants` com busca, filtro de situação, paginação, estados de carregamento, vazio ("Cadastrar tenant") e erro, e ações inativar/reativar.
**Where**: `src/Jamsoft.DashBoards.Web/src/app/admin/tenants/tenants-lista.page.ts`
**Depends on**: None
**Reuses**: T42, T43
**Requirement**: TEN-04, TEN-05, TEN-06

**Tools**:

- MCP: NONE
- Skill: NONE

**Done when**:

- [ ] Testes de componente: busca dispara consulta com debounce; paginação; estado vazio com ação; erro com "Tentar novamente"; inativar pede confirmação e atualiza a linha
- [ ] Gate check passes: Quick-Web
- [ ] Test count: 6 new tests pass

**Tests**: unit
**Gate**: quick-web

**Commit**: `feat(web): lista de tenants`

---

### T51: Formulário de tenant

**What**: Página `/admin/tenants/novo` e `/admin/tenants/:id` com nome, CNPJ (máscara e validação), seção opcional de Admin inicial; trata 409 de CNPJ/e-mail e de versão.
**Where**: `src/Jamsoft.DashBoards.Web/src/app/admin/tenants/tenant-form.page.ts`
**Depends on**: T50
**Reuses**: T43
**Requirement**: TEN-01, TEN-02, TEN-03

**Tools**:

- MCP: NONE
- Skill: NONE

**Done when**:

- [ ] Testes de componente: CNPJ inválido bloqueia envio; criação com e sem Admin inicial envia o corpo esperado; `CNPJ_DUPLICADO` e `EMAIL_EM_USO` aparecem no campo; `CONFLITO_VERSAO` mostra aviso de recarregar
- [ ] Gate check passes: Quick-Web
- [ ] Test count: 5 new tests pass

**Tests**: unit
**Gate**: quick-web

**Commit**: `feat(web): formulario de tenant`

---

### T52: Seletor de tenant do SuperAdmin

**What**: Componente no cabeçalho (só SuperAdmin) para buscar e escolher tenant; grava no `TenantContextoService`; rotas tenant-scoped sem tenant mostram o seletor.
**Where**: `src/Jamsoft.DashBoards.Web/src/app/admin/seletor-tenant/seletor-tenant.component.ts`
**Depends on**: T50
**Reuses**: T46
**Requirement**: CTX-01

**Tools**:

- MCP: NONE
- Skill: NONE

**Done when**:

- [ ] Testes de componente: invisível para Admin/Consulta; busca lista tenants; escolher grava contexto e mostra o nome; "Limpar" remove
- [ ] Gate check passes: Quick-Web
- [ ] Test count: 4 new tests pass

**Tests**: unit
**Gate**: quick-web

**Commit**: `feat(web): seletor de tenant do SuperAdmin`

---

### T53: Diálogo de exclusão de tenant

**What**: Diálogo que explica a exclusão, exige digitar o nome exato do tenant, envia a solicitação e acompanha o job a cada 5 s (situação, etapa, linhas), com "Retomar" quando `Falhou`.
**Where**: `src/Jamsoft.DashBoards.Web/src/app/admin/exclusao/exclusao-tenant.dialog.ts`
**Depends on**: T50
**Reuses**: T43
**Requirement**: DEL-02, DEL-03, DEL-06

**Tools**:

- MCP: NONE
- Skill: NONE

**Done when**:

- [ ] Testes de componente: botão confirmar só habilita com nome idêntico; 202 inicia polling; `Concluida` encerra polling; `Falhou` mostra erro e "Retomar"; `EXCLUSAO_EM_ANDAMENTO` mostra o job atual
- [ ] Gate check passes: Quick-Web
- [ ] Test count: 5 new tests pass

**Tests**: unit
**Gate**: quick-web

**Commit**: `feat(web): dialogo de exclusao de tenant`

---

### T54: Tela de auditoria

**What**: Página `/admin/auditoria` com filtros (período, tenant, usuário, operação), tabela paginada e detalhes expandíveis.
**Where**: `src/Jamsoft.DashBoards.Web/src/app/admin/auditoria/auditoria.page.ts`
**Depends on**: None
**Reuses**: T42, T43
**Requirement**: AUD-03

**Tools**:

- MCP: NONE
- Skill: NONE

**Done when**:

- [ ] Testes de componente: filtros viram parâmetros da consulta; paginação; estado vazio; erro com "Tentar novamente"
- [ ] Gate check passes: Quick-Web
- [ ] Test count: 4 new tests pass

**Tests**: unit
**Gate**: quick-web

**Commit**: `feat(web): tela de auditoria`

---

### Phase 10 tasks

### T55: Lista de usuários do tenant

**What**: Página `/cliente/usuarios` com lista (nome, e-mail, perfil, situação), estados de carregamento/vazio/erro e ações editar, inativar e redefinir senha.
**Where**: `src/Jamsoft.DashBoards.Web/src/app/cliente/usuarios/usuarios-lista.page.ts`
**Depends on**: None
**Reuses**: T42, T43
**Requirement**: USR-04, USR-09

**Tools**:

- MCP: NONE
- Skill: NONE

**Done when**:

- [ ] Testes de componente: renderiza lista; inativar pede confirmação; `ULTIMO_ADMIN` mostra a mensagem do spec; SuperAdmin sem tenant vê o seletor
- [ ] Gate check passes: Quick-Web
- [ ] Test count: 4 new tests pass

**Tests**: unit
**Gate**: quick-web

**Commit**: `feat(web): lista de usuarios do tenant`

---

### T56: Formulário de usuário e redefinição de senha

**What**: Formulário de criação/edição (nome, e-mail, perfil `Admin`/`Consulta`, senha inicial) e diálogo de redefinir senha.
**Where**: `src/Jamsoft.DashBoards.Web/src/app/cliente/usuarios/usuario-form.page.ts`
**Depends on**: T55
**Reuses**: T43
**Requirement**: USR-01, USR-02, USR-03, USR-05, USR-07

**Tools**:

- MCP: NONE
- Skill: NONE

**Done when**:

- [ ] Testes de componente: só oferece perfis Admin e Consulta; `EMAIL_EM_USO` no campo; senha fora da política bloqueada; `ULTIMO_ADMIN` ao rebaixar mostra a mensagem; redefinir senha envia e confirma
- [ ] Gate check passes: Quick-Web
- [ ] Test count: 5 new tests pass

**Tests**: unit
**Gate**: quick-web

**Commit**: `feat(web): formulario de usuario e redefinicao de senha`

---

### T57: Tela de empresas

**What**: Página `/cliente/empresas` com lista, cadastro (código e nome) e renomear; Consulta não chega aqui (guard).
**Where**: `src/Jamsoft.DashBoards.Web/src/app/cliente/empresas/empresas.page.ts`
**Depends on**: None
**Reuses**: T42, T43
**Requirement**: EMP-02, EMP-03, EMP-05, EMP-06

**Tools**:

- MCP: NONE
- Skill: NONE

**Done when**:

- [ ] Testes de componente: lista; cadastro válido; `EMPRESA_DUPLICADA` no campo código; renomear atualiza a linha; estados vazio e erro
- [ ] Gate check passes: Quick-Web
- [ ] Test count: 5 new tests pass

**Tests**: unit
**Gate**: quick-web

**Commit**: `feat(web): tela de empresas`

---

### T58: Testes e2e dos fluxos críticos

**What**: Playwright contra API + Web reais (banco de testes semeado com `criar-superadmin`): (1) SuperAdmin troca senha, cria tenant com Admin; (2) Admin entra, troca senha, cria usuário Consulta; (3) Consulta não acessa `/cliente/usuarios`; (4) SuperAdmin seleciona o tenant e vê os usuários.
**Where**: `src/Jamsoft.DashBoards.Web/e2e/tenancy.spec.ts`
**Depends on**: T56, T57
**Reuses**: T16
**Requirement**: TEN-03, USR-01, USR-08, CTX-01

**Tools**:

- MCP: NONE
- Skill: NONE

**Done when**:

- [ ] Os 4 cenários passam em Chromium
- [ ] Gate check passes: Full-Web
- [ ] Test count: 4 e2e tests pass

**Tests**: e2e
**Gate**: full-web

**Commit**: `test(web): e2e de tenancy e acesso`

---

## Phase Execution Map

```
Phase 1 → Phase 2 → Phase 3 → Phase 4 → Phase 5 → Phase 6 → Phase 7 → Phase 8 → Phase 9 → Phase 10

Phase 1  (7):  T1 T2 T3 T4 T5 T6 T7
Phase 2  (8):  T8 T9 T10 T11 T12 T13 T14 T15
Phase 3  (6):  T16 T17 T18 T19 T20 T21
Phase 4  (6):  T22 T23 T24 T25 T26 T27
Phase 5  (7):  T28 T29 T30 T31 T32 T33 T34
Phase 6  (4):  T35 T36 T37 T38
Phase 7  (2):  T39 T40
Phase 8  (7):  T41 T42 T43 T44 T45 T46 T47
Phase 9  (7):  T48 T49 T50 T51 T52 T53 T54
Phase 10 (4):  T55 T56 T57 T58
```

Total: 58 tasks. Execução sequencial, tarefas em ordem numérica dentro de cada fase. Empacotamento sugerido em lotes de ~7 tarefas (fases inteiras): [1] · [2] · [3] · [4] · [5] · [6+7] · [8] · [9] · [10] → 9 lotes.

---

## Task Granularity Check

| Task | Scope | Status |
| ---- | ----- | ------ |
| T1 | Scaffolding da solução (um entregável: solução compilando com teste de saúde) | ⚠️ Coeso (scaffold) |
| T2–T3 | 1 componente cada | ✅ Granular |
| T4–T7 | 1 grupo de entidades + 1 migração cada | ✅ Granular |
| T8–T15 | 1 classe/componente cada | ✅ Granular |
| T16 | 1 arquivo de CLI (2 comandos coesos) | ⚠️ Coeso |
| T17–T21 | 1 endpoint/serviço cada (T21: 2 endpoints pequenos do mesmo módulo) | ✅ Granular |
| T22–T27 | 1 função/endpoint/script cada (T24: consultas + edição do mesmo recurso) | ✅ Granular |
| T28–T34 | 1 serviço/endpoint/filtro cada | ✅ Granular |
| T35–T38 | 1 componente cada | ✅ Granular |
| T39–T40 | 1 arquivo de testes cada | ✅ Granular |
| T41 | Scaffold Angular | ⚠️ Coeso (scaffold) |
| T42–T47 | 1 arquivo cada | ✅ Granular |
| T48–T57 | 1 página/componente cada | ✅ Granular |
| T58 | 1 arquivo e2e | ✅ Granular |

---

## Diagram-Definition Cross-Check

| Task | Depends On (task body) | Diagram Shows (same phase) | Status |
| ---- | ---------------------- | -------------------------- | ------ |
| T1 | None | — | ✅ Match |
| T2 | T1 | T1 → T2 | ✅ Match |
| T3 | T1 | T1 → T3 | ✅ Match |
| T4 | T3 | T3 → T4 | ✅ Match |
| T5 | T4 | T4 → T5 | ✅ Match |
| T6 | T5 | T5 → T6 | ✅ Match |
| T7 | T6 | T6 → T7 | ✅ Match |
| T8 | T4 (fase 1) | — | ✅ Match |
| T9 | T8 | T8 → T9 | ✅ Match |
| T10 | T9 | T9 → T10 | ✅ Match |
| T11 | T8 | T8 → T11 | ✅ Match |
| T12 | T1 (fase 1) | — | ✅ Match |
| T13 | T12, T8 | T12 → T13, T8 → T13 | ✅ Match |
| T14 | T13 | T13 → T14 | ✅ Match |
| T15 | T13, T11 | T13 → T15, T11 → T15 | ✅ Match |
| T16 | T6 (fase 1) | — | ✅ Match |
| T17 | T16 | T16 → T17 | ✅ Match |
| T18 | T17 | T17 → T18 | ✅ Match |
| T19 | T17 | T17 → T19 | ✅ Match |
| T20 | T19 | T19 → T20 | ✅ Match |
| T21 | T17 | T17 → T21 | ✅ Match |
| T22 | None | — | ✅ Match |
| T23 | T22 | T22 → T23 | ✅ Match |
| T24 | T23 | T23 → T24 | ✅ Match |
| T25 | T24 | T24 → T25 | ✅ Match |
| T26 | None | — | ✅ Match |
| T27 | T26 | T26 → T27 | ✅ Match |
| T28 | None | — | ✅ Match |
| T29 | T28 | T28 → T29 | ✅ Match |
| T30 | None | — | ✅ Match |
| T31 | None | — | ✅ Match |
| T32 | T30, T31 | T30 → T32, T31 → T32 | ✅ Match |
| T33 | T31 | T31 → T33 | ✅ Match |
| T34 | None | — | ✅ Match |
| T35 | None | — | ✅ Match |
| T36 | None | — | ✅ Match |
| T37 | T35, T36 | T35 → T37, T36 → T37 | ✅ Match |
| T38 | T37 | T37 → T38 | ✅ Match |
| T39 | T38 (fase 6) | — | ✅ Match |
| T40 | T38 (fase 6) | — | ✅ Match |
| T41 | None | — | ✅ Match |
| T42 | T41 | T41 → T42 | ✅ Match |
| T43 | T41 | T41 → T43 | ✅ Match |
| T44 | T43 | T43 → T44 | ✅ Match |
| T45 | T44, T46 | T44 → T45, T46 → T45 | ✅ Match |
| T46 | T41 | T41 → T46 | ✅ Match |
| T47 | T44 | T44 → T47 | ✅ Match |
| T48 | None | — | ✅ Match |
| T49 | T48 | T48 → T49 | ✅ Match |
| T50 | None | — | ✅ Match |
| T51 | T50 | T50 → T51 | ✅ Match |
| T52 | T50 | T50 → T52 | ✅ Match |
| T53 | T50 | T50 → T53 | ✅ Match |
| T54 | None | — | ✅ Match |
| T55 | None | — | ✅ Match |
| T56 | T55 | T55 → T56 | ✅ Match |
| T57 | None | — | ✅ Match |
| T58 | T56, T57 | T56 → T58, T57 → T58 | ✅ Match |

Nenhuma dependência aponta para fase posterior. Dependências de fases anteriores (ex.: T23 usa T11/T13) estão cobertas pela ordem das fases.

---

## Test Co-location Validation

| Task | Code Layer Created/Modified | Matrix Requires | Task Says | Status |
| ---- | --------------------------- | --------------- | --------- | ------ |
| T1 | Endpoint `/health` | integration | integration | ✅ OK |
| T2 | Middleware de erro | integration | integration | ✅ OK |
| T3 | Regra de domínio (convenção) | unit | unit | ✅ OK |
| T4–T7 | Schema / migrations | integration | integration | ✅ OK |
| T8 | Regra de domínio | unit | unit | ✅ OK |
| T9–T10 | Infra EF | integration | integration | ✅ OK |
| T11 | Infra EF (serviço) | integration | integration | ✅ OK |
| T12 | Regra de domínio | unit | unit | ✅ OK |
| T13–T15 | Middleware / policies | integration | integration | ✅ OK |
| T16 | CLI com SQL | integration | integration | ✅ OK |
| T17–T21 | Endpoints | integration | integration | ✅ OK |
| T22 | Regra de domínio | unit | unit | ✅ OK |
| T23–T26 | Endpoints | integration | integration | ✅ OK |
| T27 | Schema (permissões) | integration | integration | ✅ OK |
| T28 | Infra EF (serviço) | integration | integration | ✅ OK |
| T29 | Endpoints | integration | integration | ✅ OK |
| T30 | Infra EF (lock) | integration | integration | ✅ OK |
| T31–T34 | Endpoints / filtro | integration | integration | ✅ OK |
| T35 | Regra de domínio + arquitetura | unit | unit | ✅ OK |
| T36–T38 | Endpoints / worker | integration | integration | ✅ OK |
| T39 | Suíte de rotas | integration | integration | ✅ OK |
| T40 | Arquitetura | unit | unit | ✅ OK |
| T41 | Scaffolding | none | none | ✅ OK |
| T42 | Design tokens | none | none | ✅ OK |
| T43–T47 | Serviços / interceptor / guard Angular | unit | unit | ✅ OK |
| T48–T57 | Páginas / componentes Angular | unit (component) | unit | ✅ OK |
| T58 | Fluxos críticos | e2e | e2e | ✅ OK |

---

## Requirement Coverage

| Requirement | Tasks |
| ----------- | ----- |
| AUTH-01 | T6, T12, T17, T44, T48 |
| AUTH-02 | T17, T43, T48 |
| AUTH-03 | T18, T43, T48 |
| AUTH-04 | T19, T44, T45 |
| AUTH-05 | T19 |
| AUTH-06 | T20, T44 |
| AUTH-07 | T14, T17, T48 |
| AUTH-08 | T13, T40, T47 |
| AUTH-09 | T17 |
| TEN-01 | T4, T22, T23, T51 |
| TEN-02 | T4, T23, T51 |
| TEN-03 | T23, T51, T58 |
| TEN-04 | T24, T50 |
| TEN-05 | T14, T25, T50 |
| TEN-06 | T25, T50 |
| TEN-07 | T4, T23, T24 |
| TEN-08 | T13, T23, T40, T47 |
| EMP-01 | T5, T28 |
| EMP-02 | T29, T57 |
| EMP-03 | T29, T57 |
| EMP-04 | T28 |
| EMP-05 | T28, T29, T57 |
| EMP-06 | T29, T57 |
| ISO-01 | T8 |
| ISO-02 | T9, T10, T39, T40 |
| ISO-03 | T39 |
| ISO-04 | T15 |
| ISO-05 | T5, T39 |
| USR-01 | T6, T31, T56, T58 |
| USR-02 | T31, T56 |
| USR-03 | T6, T31, T56 |
| USR-04 | T32, T55 |
| USR-05 | T30, T32, T43, T56 |
| USR-06 | T14, T32 |
| USR-07 | T33, T56 |
| USR-08 | T13, T21, T45, T49, T58 |
| USR-09 | T31, T55 |
| USR-10 | T13, T31, T47 |
| CTX-01 | T8, T45, T46, T52, T58 |
| CTX-02 | T34 |
| CTX-03 | T8, T15 |
| DEL-01 | T36 |
| DEL-02 | T36, T53 |
| DEL-03 | T36, T53 |
| DEL-04 | T35, T37 |
| DEL-05 | T37 |
| DEL-06 | T37, T38, T53 |
| DEL-07 | T35, T37 |
| DEL-08 | T36, T37, T38 |
| DEL-09 | T7, T36 |
| AUD-01 | T11 |
| AUD-02 | T7, T11 |
| AUD-03 | T26, T54 |
| AUD-04 | T26, T27 |

**Coverage:** 54 requisitos, 54 mapeados, 0 sem tarefa.
