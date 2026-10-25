# Sincronização de Vendas — Coletor e Ingestão — Tasks

## Execution Protocol (MANDATORY -- do not skip)

Implement these tasks with the `tlc-spec-driven` skill: **activate it by name and follow its Execute flow and Critical Rules.** Do not search for skill files by filesystem path. The skill is the source of truth for the full flow (per-task cycle, sub-agent delegation, adequacy review, Verifier, discrimination sensor).

**If the skill cannot be activated, STOP and tell the user - do not proceed without it.**

---

**Spec**: `.specs/features/sincronizacao-vendas/spec.md`
**Design**: `.specs/features/sincronizacao-vendas/design.md`
**Status**: Draft

---

## Test Coverage Matrix

> Generated from the existing repo (`plataforma-tenancy-acesso`: xUnit + `WebApplicationFactory` + SQL Server real; Angular + Vitest; Playwright) and the approved design - confirm before Execute. Guidelines found: none beyond the conventions in `.specs/STATE.md` (AD-001..AD-016) - strong defaults applied; existing test depth is the floor.

| Code Layer | Required Test Type | Coverage Expectation | Location Pattern | Run Command |
| ---------- | ------------------ | -------------------- | ---------------- | ----------- |
| Contratos (DTOs + limites) | unit | Serialização JSON dos nomes do contrato (camelCase) e constantes de limite | `tests/Jamsoft.DashBoards.Coletor.Testes/Unidade/Contratos/*Testes.cs` | `dotnet test tests/Jamsoft.DashBoards.Coletor.Testes --filter "Categoria=Unidade"` |
| API domain rules (`SegredoColetor`, `ValidacaoVenda`) | unit | All branches; 1:1 to spec ACs; every listed edge case | `tests/Jamsoft.DashBoards.Api.Testes/Unidade/**/*Testes.cs` | `dotnet test tests/Jamsoft.DashBoards.Api.Testes --filter "Categoria=Unidade\|Categoria=Arquitetura"` |
| API endpoints, filtros, caches, `GravadorVendas` | integration | Every route: happy path + every listed edge case + error paths (status **e** `codigo`); concorrência onde o AC fala em duplicidade | `tests/Jamsoft.DashBoards.Api.Testes/Integracao/{Coletores,Ingestao}/*Testes.cs` | `dotnet test tests/Jamsoft.DashBoards.Api.Testes --filter "Categoria=Integracao"` |
| Schema / migrations | integration | Every unique, check and FK constraint an AC depends on | `tests/Jamsoft.DashBoards.Api.Testes/Integracao/Esquema/*Testes.cs` | same as above |
| Architecture rules | unit | Plano de exclusão inclui as tabelas novas na ordem certa; allowlist de bypass | `tests/Jamsoft.DashBoards.Api.Testes/Arquitetura/*Testes.cs` | `dotnet test tests/Jamsoft.DashBoards.Api.Testes --filter "Categoria=Arquitetura"` |
| Coletor.Nucleo (configuração, checkpoint, logs, agendador, comandos) | unit | All branches; 1:1 to spec ACs; relógio falso (`FakeTimeProvider`) | `tests/Jamsoft.DashBoards.Coletor.Testes/Unidade/**/*Testes.cs` | `dotnet test tests/Jamsoft.DashBoards.Coletor.Testes --filter "Categoria=Unidade"` |
| Coletor.Nucleo (extrator, conexões ERP, cliente API, executor) | integration | ERP falso real (tabela `NotaSaida` identity + view `c_NotaSaida` + `NotaSaidaProd`) no SQL Server de testes; API real via `WebApplicationFactory` | `tests/Jamsoft.DashBoards.Coletor.Testes/Integracao/**/*Testes.cs` | `dotnet test tests/Jamsoft.DashBoards.Coletor.Testes --filter "Categoria=Integracao"` |
| Ponta a ponta Coletor → API | integration | O Independent Test do spec (1.200 vendas, novas, cancelamento, reinício) | `tests/Jamsoft.DashBoards.Coletor.Testes/PontaAPonta/*Testes.cs` | `dotnet test tests/Jamsoft.DashBoards.Coletor.Testes` |
| Coletor WinForms (forms, bandeja, host) | none | - (build gate only; sem lógica — tudo no Nucleo) + roteiro de UAT manual (T41) | - | build gate only |
| Angular pages/components | unit (component) | Estados carregando/vazio/erro/dados + cada ação + cada `codigo` exibido | `src/Jamsoft.DashBoards.Web/src/app/**/*.spec.ts` | `npm --prefix src/Jamsoft.DashBoards.Web test -- --watch=false` |
| Scripts SQL do ERP | integration | Script aplica no ERP falso e o usuário criado lê só o necessário | `tests/Jamsoft.DashBoards.Coletor.Testes/Integracao/Erp/*Testes.cs` | `dotnet test tests/Jamsoft.DashBoards.Coletor.Testes --filter "Categoria=Integracao"` |

## Gate Check Commands

> Integration tests use `TESTES_SQLSERVER="Server=localhost;Integrated Security=true;TrustServerCertificate=true;Connect Timeout=60"` (Docker parado nesta máquina). SQL Server local sob pressão de memória: repetir uma vez só em falha de timeout/login de infraestrutura.

| Gate Level | When to Use | Command |
| ---------- | ----------- | ------- |
| Quick | API tasks with unit tests only | `dotnet test tests/Jamsoft.DashBoards.Api.Testes --filter "Categoria=Unidade\|Categoria=Arquitetura"` |
| Full | API tasks with integration tests | `dotnet test tests/Jamsoft.DashBoards.Api.Testes` |
| Quick-Coletor | Coletor/Contratos tasks with unit tests only | `dotnet test tests/Jamsoft.DashBoards.Coletor.Testes --filter "Categoria=Unidade"` |
| Full-Coletor | Coletor tasks with integration tests | `dotnet test tests/Jamsoft.DashBoards.Coletor.Testes` |
| Quick-Web | Angular tasks | `npm --prefix src/Jamsoft.DashBoards.Web test -- --watch=false` |
| Build | End of each phase; tasks with no tests | `dotnet build Jamsoft.DashBoards.slnx -warnaserror && dotnet format Jamsoft.DashBoards.slnx --verify-no-changes && dotnet test Jamsoft.DashBoards.slnx && npm --prefix src/Jamsoft.DashBoards.Web run lint && npm --prefix src/Jamsoft.DashBoards.Web run build && npm --prefix src/Jamsoft.DashBoards.Web test -- --watch=false` |

---

## Execution Plan

Phases run sequentially; tasks within a phase run in order. Arrows show dependencies inside a phase; dependencies on earlier phases (or on `plataforma-tenancy-acesso`) appear only in each task's `Depends on`.

### Phase 1: Contratos e esquema

```
T1
T2 → T3 → T4 → T5 → T6
```

### Phase 2: Credenciais do Coletor (API)

```
T7 → T8 → T9
T8 → T10 → T12
T7 → T11 → T12 → T13
```

### Phase 3: Ingestão e status (API)

```
T14 → T16
T15 → T16 → T17 → T21
T16 → T18
T19 → T20
```

### Phase 4: Coletor.Nucleo — base

```
T22 → T23 → T24
T22 → T25
T22 → T26
```

### Phase 5: Coletor.Nucleo — sincronização

```
T27 → T28
T27 → T29
T27 → T31
T30 → T31 → T32 → T33 → T34
```

### Phase 6: Coletor WinForms

```
T35 → T36 → T37 → T41
T35 → T38 → T39 → T41
T35 → T40 → T41
```

### Phase 7: Telas web

```
T42 → T43
T44
T45
```

---

## Task Breakdown

### Phase 1 tasks

### T1: Projeto Jamsoft.DashBoards.Contratos

**What**: Projeto `net10.0` sem dependências com os DTOs do design (`LoteVendas`, `VendaContrato`, `ResultadoLote`, `VendaRejeitada`, `RelatorioExecucao`, `TokenColetorRequisicao/Resposta`, `TenantAutorizado`) e `Limites` (500 vendas, 2 MB); referenciado pela API; cria também `tests/Jamsoft.DashBoards.Coletor.Testes` (xUnit, mesmo fixture de banco da API).
**Where**: `src/Jamsoft.DashBoards.Contratos/Ingestao/LoteVendas.cs`
**Depends on**: None
**Reuses**: convenções do `Jamsoft.DashBoards.Api.csproj` e `BancoDeTestes`
**Requirement**: suporte (AD-013)

**Tools**:

- MCP: NONE
- Skill: NONE

**Done when**:

- [x] Testes: um `LoteVendas` serializado com `JsonSerializerDefaults.Web` produz exatamente os nomes do contrato (`batchId`, `tenant`, `configuracaoId`, `vendas[].vendaId`, `dataHora`, ...) e volta igual; `Limites.VendasPorLote == 500` e `BytesPorLote == 2 MB`
- [x] Gate check passes: Quick-Coletor
- [x] Test count: 3 tests pass

**Tests**: unit
**Gate**: quick

**Commit**: `feat(contratos): contratos da sincronizacao de vendas`

---

### T2: Tabelas COLETOR e COLETOR_TENANT

**What**: Entidades `Coletor` (CLIENT_ID único, SECRET_HASH binary(32), ATIVO, VERSAO_CREDENCIAL, REVOGADO_EM, ULTIMO_TOKEN_EM, VERSAO) e `ColetorTenant : IPertenceAoTenant` (PK TENANT_ID+COLETOR_ID) + migração.
**Where**: `src/Jamsoft.DashBoards.Api/Modulos/Coletores/Coletor.cs`
**Depends on**: None
**Reuses**: convenções de entidade (T4–T7 de `plataforma-tenancy-acesso`)
**Requirement**: COL-02

**Tools**:

- MCP: NONE
- Skill: NONE

**Done when**:

- [x] Testes de esquema: `CLIENT_ID` duplicado recusado; mesmo coletor/tenant duplicado recusado; `SECRET_HASH` aceita só 32 bytes
- [x] Gate check passes: Full
- [x] Test count: 3 new tests pass

**Tests**: integration
**Gate**: full

**Commit**: `feat(api): criar tabelas COLETOR e COLETOR_TENANT`

---

### T3: Tabela ORIGEM_SINCRONIZACAO e dona da empresa

**What**: Entidade `OrigemSincronizacao : IPertenceAoTenant` (UQ TENANT_ID+CONFIGURACAO_ID+ROTINA, FK COLETOR) e coluna `EMPRESA.ORIGEM_ID` (FK opcional) + migração.
**Where**: `src/Jamsoft.DashBoards.Api/Modulos/Ingestao/OrigemSincronizacao.cs`
**Depends on**: T2
**Reuses**: T2
**Requirement**: ING-09, ING-10

**Tools**:

- MCP: NONE
- Skill: NONE

**Done when**:

- [x] Testes de esquema: mesma (configuração, rotina) duplicada no tenant recusada e aceita em outro tenant; empresa existente aceita `ORIGEM_ID` nulo
- [x] Gate check passes: Full
- [x] Test count: 2 new tests pass

**Tests**: integration
**Gate**: full

**Commit**: `feat(api): criar tabela ORIGEM_SINCRONIZACAO`

---

### T4: Tabela VENDA

**What**: Entidade `Venda : IPertenceAoTenant` com UQ (TENANT_ID, COD_EMPRESA, VENDA_ID), IX (TENANT_ID, DATA_HORA) INCLUDE do design, CKs de `SITUACAO` (Finalizada, Cancelada, Inutilizada, Contingência), `TIPO_DFE`, `VALOR >= 0`, `QTD_ITENS >= 0` + migração.
**Where**: `src/Jamsoft.DashBoards.Api/Modulos/Ingestao/Venda.cs`
**Depends on**: T3
**Reuses**: T3
**Requirement**: ING-04

**Tools**:

- MCP: NONE
- Skill: NONE

**Done when**:

- [x] Testes de esquema: chave de negócio duplicada recusada (2601); situação fora do domínio recusada (547); `Contingência` aceita (com acento); valor negativo recusado; índice `(TENANT_ID, DATA_HORA)` começa por `TENANT_ID`
- [x] Gate check passes: Full
- [x] Test count: 5 new tests pass

**Tests**: integration
**Gate**: full

**Commit**: `feat(api): criar tabela VENDA`

---

### T5: Tabela LOTE_INGESTAO

**What**: Entidade `LoteIngestao : IPertenceAoTenant` (UQ TENANT_ID+BATCH_ID, RESPOSTA nvarchar(max), contagens, IX RECEBIDO_EM) + migração.
**Where**: `src/Jamsoft.DashBoards.Api/Modulos/Ingestao/LoteIngestao.cs`
**Depends on**: T4
**Reuses**: T4
**Requirement**: ING-05

**Tools**:

- MCP: NONE
- Skill: NONE

**Done when**:

- [x] Teste de esquema: mesmo `BATCH_ID` no mesmo tenant recusado e aceito em outro tenant
- [x] Gate check passes: Full
- [x] Test count: 1 new test passes

**Tests**: integration
**Gate**: full

**Commit**: `feat(api): criar tabela LOTE_INGESTAO`

---

### T6: Plano de exclusão e regras de arquitetura

**What**: Confirmar que `PlanoExclusao` inclui `VENDA`, `LOTE_INGESTAO`, `ORIGEM_SINCRONIZACAO` e `COLETOR_TENANT` em ordem válida (VENDA e LOTE antes de EMPRESA; EMPRESA antes de ORIGEM) e `COLETOR` fora; acrescentar `Modulos.Coletores` à allowlist de bypass em `RegrasArquiteturaTestes`.
**Where**: `tests/Jamsoft.DashBoards.Api.Testes/Arquitetura/RegrasArquiteturaTestes.cs`
**Depends on**: T5
**Reuses**: `PlanoExclusao` (T35 da feature anterior)
**Requirement**: suporte (AD-001, DEL-04/DEL-07 da feature `plataforma-tenancy-acesso`)

**Tools**:

- MCP: NONE
- Skill: NONE

**Done when**:

- [x] Testes: ordem do plano (VENDA < EMPRESA < ORIGEM_SINCRONIZACAO; LOTE_INGESTAO < ORIGEM_SINCRONIZACAO; COLETOR_TENANT antes de TENANT; COLETOR ausente); exclusão de um tenant com vendas, lotes e origens apaga tudo dele e nada de outro tenant (reaproveitando o teste do processador)
- [x] Gate check passes: Full
- [x] Test count: 3 new tests pass

**Tests**: integration
**Gate**: full

**Commit**: `test(api): incluir tabelas de sincronizacao no plano de exclusao`

---

### Phase 2 tasks

### T7: SegredoColetor

**What**: Geração de `client_secret` (32 bytes aleatórios, Base64Url), SHA-256 e comparação em tempo constante.
**Where**: `src/Jamsoft.DashBoards.Api/Modulos/Coletores/SegredoColetor.cs`
**Depends on**: None
**Reuses**: padrão de `RefreshTokens.Hash` / `FixedTimeEquals`
**Requirement**: COL-01, COL-02

**Tools**:

- MCP: NONE
- Skill: NONE

**Done when**:

- [x] Testes: segredo gerado tem 43 caracteres Base64Url e decodifica para 32 bytes; dois segredos seguidos diferem; hash tem 32 bytes; comparação aceita o segredo certo e recusa um com 1 caractere trocado
- [x] Gate check passes: Quick
- [x] Test count: 4 new tests pass

**Tests**: unit
**Gate**: quick

**Commit**: `feat(api): gerar e verificar segredo do coletor`

---

### T8: POST /api/admin/coletores

**What**: Cria coletor (nome 3–80, ≥ 1 tenant ativo) com `client_id` GUID e secret exibido só na resposta; grava só o hash; auditoria `COLETOR_CRIADO`.
**Where**: `src/Jamsoft.DashBoards.Api/Modulos/Coletores/ColetoresEndpoints.cs`
**Depends on**: T7
**Reuses**: `ProblemasApi`, `ServicoAuditoria`, policy `SuperAdmin`
**Requirement**: COL-01, COL-02, COL-08

**Tools**:

- MCP: NONE
- Skill: NONE

**Done when**:

- [x] Testes: 201 com `idPublico`, `clientId`, `clientSecret`; banco guarda `SHA256(secret)` e nunca o secret; sem tenant → 400 `VALIDACAO`; tenant inativo → 400 `VALIDACAO`; nome com 2 caracteres → 400; Admin → 403 `ACESSO_NEGADO`; auditoria sem o secret
- [x] Gate check passes: Full
- [x] Test count: 7 new tests pass

**Tests**: integration
**Gate**: full

**Commit**: `feat(api): cadastrar coletor`

---

### T9: Listar coletores e alterar tenants

**What**: `GET /api/admin/coletores` (paginado, sem secret/hash) e `PUT /api/admin/coletores/{idPublico}/tenants` com `versao`, incrementando `VERSAO_CREDENCIAL`; auditoria `COLETOR_TENANTS_ALTERADOS`.
**Where**: `src/Jamsoft.DashBoards.Api/Modulos/Coletores/ColetoresEndpoints.cs` (modify)
**Depends on**: T8
**Reuses**: `ResultadoPaginado<T>`, versão em Base64
**Requirement**: COL-07, COL-08

**Tools**:

- MCP: NONE
- Skill: NONE

**Done when**:

- [ ] Testes: lista traz tenants (id, nome) e nunca `secret`/`hash`; PUT troca a lista e incrementa `VERSAO_CREDENCIAL`; versão antiga → 409 `CONFLITO_VERSAO`; lista vazia → 400; Admin → 403
- [ ] Gate check passes: Full
- [ ] Test count: 5 new tests pass

**Tests**: integration
**Gate**: full

**Commit**: `feat(api): listar coletores e alterar tenants autorizados`

---

### T10: Revogar e rotacionar credencial

**What**: `POST .../revogar` (ATIVO=0, REVOGADO_EM, incrementa versão) e `POST .../rotacionar` (novo secret exibido uma vez, hash novo, incrementa versão); auditoria.
**Where**: `src/Jamsoft.DashBoards.Api/Modulos/Coletores/ColetoresEndpoints.cs` (modify)
**Depends on**: T8
**Reuses**: T7
**Requirement**: COL-05, COL-06

**Tools**:

- MCP: NONE
- Skill: NONE

**Done when**:

- [ ] Testes: revogar → credencial inativa e versão +1; rotacionar → resposta com secret novo, hash trocado, versão +1; rotacionar coletor revogado → 409; auditoria das duas sem segredo
- [ ] Gate check passes: Full
- [ ] Test count: 4 new tests pass

**Tests**: integration
**Gate**: full

**Commit**: `feat(api): revogar e rotacionar credencial do coletor`

---

### T11: POST /api/coletor/token

**What**: Troca `clientId`/`clientSecret` por JWT (`perfil=Coletor`, `sub`, `tenants`, `cred_ver`, 15 min) via `TokenService.EmitirColetor`, com tenants ativos e nomes na resposta; rate limit 20/min por IP; grava `ULTIMO_TOKEN_EM`.
**Where**: `src/Jamsoft.DashBoards.Api/Modulos/Coletores/TokenColetorEndpoint.cs`
**Depends on**: T7
**Reuses**: `TokenService`, rate limiter de `AddAutenticacao`
**Requirement**: COL-03, COL-04

**Tools**:

- MCP: NONE
- Skill: NONE

**Done when**:

- [ ] Testes: credencial válida → 200 com token cujas claims são `perfil=Coletor`, `tenants` = ID públicos autorizados e ativos, `cred_ver` atual, validade 15 min; secret errado, `clientId` inexistente e credencial revogada → 401 `CREDENCIAL_INVALIDA` com corpo idêntico; 21ª requisição no minuto → 429
- [ ] Gate check passes: Full
- [ ] Test count: 6 new tests pass

**Tests**: integration
**Gate**: full

**Commit**: `feat(api): emitir token do coletor por client credentials`

---

### T12: Revalidação do token do Coletor

**What**: `StatusColetorCache` (60 s) chamado em `RevalidarAcessoAsync` para tokens de Coletor (credencial ativa e `cred_ver` igual); `TenantAtual.TenantsAutorizadosColetor` lido da claim `tenants`.
**Where**: `src/Jamsoft.DashBoards.Api/Modulos/Coletores/StatusColetorCache.cs`
**Depends on**: T11, T10
**Reuses**: padrão do `StatusAcessoCache`, `Policies.cs`, `TenantAtual.cs`
**Requirement**: COL-05, COL-09

**Tools**:

- MCP: NONE
- Skill: NONE

**Done when**:

- [ ] Testes: token válido acessa rota de Coletor; após revogar, token antigo → 401 em até 60 s (relógio falso) e imediatamente após invalidar o cache; após rotacionar ou trocar tenants, token antigo → 401; token de Coletor em rota de usuário → 403 (regressão COL-09); `TenantsAutorizadosColetor` = conjunto da claim
- [ ] Gate check passes: Full
- [ ] Test count: 5 new tests pass

**Tests**: integration
**Gate**: full

**Commit**: `feat(api): revalidar credencial do coletor a cada requisicao`

---

### T13: GET /api/coletor/tenants

**What**: Lista os tenants autorizados ao token (ID público e nome, só ativos) para a configuração no Coletor.
**Where**: `src/Jamsoft.DashBoards.Api/Modulos/Coletores/TokenColetorEndpoint.cs` (modify)
**Depends on**: T12
**Reuses**: T12
**Requirement**: CFG-02, CFG-05

**Tools**:

- MCP: NONE
- Skill: NONE

**Done when**:

- [ ] Testes: retorna exatamente os tenants autorizados e ativos, ordenados por nome; token de usuário → 403
- [ ] Gate check passes: Full
- [ ] Test count: 2 new tests pass

**Tests**: integration
**Gate**: full

**Commit**: `feat(api): listar tenants autorizados ao coletor`

---

### Phase 3 tasks

### T14: ValidacaoVenda

**What**: Valida cada `VendaContrato` contra o contrato do spec (obrigatórios, `vendaId > 0`, `codEmpresa > 0`, `valor >= 0`, `qtdItens >= 0`, domínio de `situacao` com Contingência e de `tipoDfe`) e trunca `nomeCliente` (120), `vendedorNome` (80), `nomeEmpresa` (80); devolve válidas + rejeitadas com motivo.
**Where**: `src/Jamsoft.DashBoards.Api/Modulos/Ingestao/ValidacaoVenda.cs`
**Depends on**: None
**Reuses**: T1
**Requirement**: ING-06

**Tools**:

- MCP: NONE
- Skill: NONE

**Done when**:

- [ ] Testes: um caso por regra (cada violação gera o motivo esperado); as 4 situações aceitas; `tipoDfe` fora do domínio; truncamentos nos 3 campos; venda válida passa intacta
- [ ] Gate check passes: Quick
- [ ] Test count: ≥ 12 tests pass

**Tests**: unit
**Gate**: quick

**Commit**: `feat(api): validar vendas do lote`

---

### T15: GravadorVendas (MERGE via OPENJSON)

**What**: `GravarAsync(contexto, tenantId, origemId, vendas)` executa o `MERGE ... WITH (HOLDLOCK)` do design com `OPENJSON` e devolve `(inseridos, atualizados)` a partir de `OUTPUT $action`.
**Where**: `src/Jamsoft.DashBoards.Api/Modulos/Ingestao/GravadorVendas.cs`
**Depends on**: None
**Reuses**: T4
**Requirement**: ING-04

**Tools**:

- MCP: NONE
- Skill: NONE

**Done when**:

- [ ] Testes: lote novo → todas inseridas; mesmo lote com situação alterada → todas atualizadas com os campos novos e `ALTERADO_EM`; mistura → contagens exatas; dois lotes sobrepostos em paralelo → nenhuma duplicata e soma coerente; tenant vem do parâmetro (venda de outro tenant com mesma chave não é tocada)
- [ ] Gate check passes: Full
- [ ] Test count: 5 new tests pass

**Tests**: integration
**Gate**: full

**Commit**: `feat(api): gravar vendas por merge em lote`

---

### T16: POST /api/coletor/vendas/lotes

**What**: Endpoint de ingestão (policy `Coletor`): limite de corpo 2 MB e de 500 vendas → 413 `LOTE_GRANDE_DEMAIS`; tenant fora do token → 403 `TENANT_NAO_AUTORIZADO`; tenant inativo → 403 `TENANT_INATIVO`; escopo com `TenantFixo`; validação (T14) e gravação (T15); resposta `ResultadoLote`.
**Where**: `src/Jamsoft.DashBoards.Api/Modulos/Ingestao/LotesEndpoint.cs`
**Depends on**: T14, T15
**Reuses**: T12, `TenantFixo`/escopo de DI, `StatusAcessoCache`
**Requirement**: ING-01, ING-02, ING-03, ING-06, ING-08

**Tools**:

- MCP: NONE
- Skill: NONE

**Done when**:

- [ ] Testes: lote válido → 200 com contagens; 1 inválida entre 10 → 9 gravadas e 1 rejeitada com motivo; tenant não autorizado → 403 sem gravar; tenant inativo → 403 `TENANT_INATIVO`; 501 vendas → 413; corpo > 2 MB → 413; token de usuário → 403
- [ ] Gate check passes: Full
- [ ] Test count: 7 new tests pass

**Tests**: integration
**Gate**: full

**Commit**: `feat(api): receber lotes de vendas do coletor`

---

### T17: Idempotência por lote

**What**: Gravar `LOTE_INGESTAO` com a resposta na mesma transação; lote repetido devolve a resposta original sem regravar; corrida do mesmo lote resolve pela violação do índice (relê e devolve).
**Where**: `src/Jamsoft.DashBoards.Api/Modulos/Ingestao/LotesEndpoint.cs` (modify)
**Depends on**: T16
**Reuses**: T5
**Requirement**: ING-05

**Tools**:

- MCP: NONE
- Skill: NONE

**Done when**:

- [ ] Testes: segundo envio do mesmo `batchId` → 200 com corpo idêntico ao primeiro e contagem de vendas no banco inalterada; dois envios simultâneos → mesma resposta e uma única linha em `LOTE_INGESTAO`; mesmo `batchId` em outro tenant é independente
- [ ] Gate check passes: Full
- [ ] Test count: 3 new tests pass

**Tests**: integration
**Gate**: full

**Commit**: `feat(api): responder lote repetido sem regravar`

---

### T18: Empresas do lote, dona da empresa e transação única

**What**: Resolver/criar `ORIGEM_SINCRONIZACAO`; `ServicoEmpresas.ObterOuCriarAsync` por `codEmpresa`; empresa de outra origem → 409 `EMPRESA_VINCULADA_A_OUTRA_ORIGEM` sem gravar nada; empresa sem origem é adotada; tudo numa transação.
**Where**: `src/Jamsoft.DashBoards.Api/Modulos/Ingestao/LotesEndpoint.cs` (modify)
**Depends on**: T16
**Reuses**: `ServicoEmpresas`, T3
**Requirement**: ING-07, ING-10

**Tools**:

- MCP: NONE
- Skill: NONE

**Done when**:

- [ ] Testes: primeira venda de empresa nova cria a empresa com o nome recebido e `ORIGEM_ID` da origem; empresa criada à mão é adotada; segunda configuração enviando a mesma empresa → 409 e nenhuma venda gravada; falha injetada no MERGE desfaz origem/empresa/lote (nada persistido)
- [ ] Gate check passes: Full
- [ ] Test count: 4 new tests pass

**Tests**: integration
**Gate**: full

**Commit**: `feat(api): vincular empresas a origem e gravar lote numa transacao`

---

### T19: POST /api/coletor/execucoes

**What**: Registra o fim de uma execução na `ORIGEM_SINCRONIZACAO` (início, fim, situação, registros, mensagem ≤ 500); `ULTIMO_SUCESSO_EM` só em Sucesso.
**Where**: `src/Jamsoft.DashBoards.Api/Modulos/Ingestao/ExecucoesEndpoint.cs`
**Depends on**: None
**Reuses**: T12, T3
**Requirement**: ING-09

**Tools**:

- MCP: NONE
- Skill: NONE

**Done when**:

- [ ] Testes: Sucesso grava tudo e `ULTIMO_SUCESSO_EM`; Falha grava situação e mensagem e preserva o último sucesso anterior; tenant não autorizado → 403; mensagem longa truncada em 500
- [ ] Gate check passes: Full
- [ ] Test count: 4 new tests pass

**Tests**: integration
**Gate**: full

**Commit**: `feat(api): registrar execucoes do coletor`

---

### T20: Status de sincronização para o painel

**What**: `GET /api/sincronizacao` (AdminTenant, contexto de tenant) e `GET /api/sincronizacao/resumo` (LeituraTenant); casos novos na suíte de isolamento.
**Where**: `src/Jamsoft.DashBoards.Api/Modulos/Ingestao/SincronizacaoEndpoints.cs`
**Depends on**: T19
**Reuses**: `.ValidarContextoTenant()`, `.AuditarContextoSuperAdmin()`, suíte T39 da feature anterior
**Requirement**: STS-01, STS-02

**Tools**:

- MCP: NONE
- Skill: NONE

**Done when**:

- [ ] Testes: lista traz as origens do tenant com os campos do STS-01; Consulta na lista → 403; resumo para Consulta → menor `ULTIMO_SUCESSO_EM` das origens; sem sincronização → `ultimoSucessoEm: null`; suíte de isolamento cobre as 2 rotas (Admin/Consulta de A contra B)
- [ ] Gate check passes: Full
- [ ] Test count: 4 new tests pass (+ casos da suíte de isolamento)

**Tests**: integration
**Gate**: full

**Commit**: `feat(api): status de sincronizacao do tenant`

---

### T21: Retenção de LOTE_INGESTAO

**What**: `BackgroundService` diário que apaga `LOTE_INGESTAO` com mais de 30 dias em lotes de 5.000 (configurável, desligado nos testes de API).
**Where**: `src/Jamsoft.DashBoards.Api/Modulos/Ingestao/LimpezaLotes.cs`
**Depends on**: T17
**Reuses**: padrão do `ProcessadorExclusao` (lógica chamável + loop fino)
**Requirement**: suporte (design Risks: crescimento de `LOTE_INGESTAO`)

**Tools**:

- MCP: NONE
- Skill: NONE

**Done when**:

- [ ] Testes: lotes de 31 dias apagados, de 29 dias mantidos (relógio falso); várias passadas quando passa do tamanho do lote
- [ ] Gate check passes: Full
- [ ] Test count: 2 new tests pass

**Tests**: integration
**Gate**: full

**Commit**: `feat(api): limpar lotes de ingestao antigos`

---

### Phase 4 tasks

### T22: Projetos do Coletor

**What**: `src/Jamsoft.DashBoards.Coletor.Nucleo` (`net10.0-windows`, Dapper, SqlClient, Http.Resilience, ProtectedData) e `src/Jamsoft.DashBoards.Coletor` (`net10.0-windows`, WinForms, Generic Host) na solução; `Coletor.Testes` referencia Nucleo, Contratos e API.
**Where**: `src/Jamsoft.DashBoards.Coletor.Nucleo/Jamsoft.DashBoards.Coletor.Nucleo.csproj`
**Depends on**: None
**Reuses**: `Directory.Build.props`
**Requirement**: suporte (AD-013)

**Tools**:

- MCP: NONE
- Skill: NONE

**Done when**:

- [ ] `dotnet build Jamsoft.DashBoards.slnx -warnaserror` sem avisos
- [ ] Gate check passes: Build

**Tests**: none
**Gate**: build

**Commit**: `chore(coletor): criar projetos do coletor`

---

### T23: ProtetorSegredos (DPAPI)

**What**: `Cifrar/Decifrar` com `ProtectedData` escopo `LocalMachine` e entropia fixa do produto.
**Where**: `src/Jamsoft.DashBoards.Coletor.Nucleo/Configuracao/ProtetorSegredos.cs`
**Depends on**: T22
**Reuses**: NONE
**Requirement**: CFG-08

**Tools**:

- MCP: NONE
- Skill: NONE

**Done when**:

- [ ] Testes: ida e volta preserva o texto; texto cifrado não contém o original; cifrado adulterado lança `CryptographicException`
- [ ] Gate check passes: Quick-Coletor
- [ ] Test count: 3 new tests pass

**Tests**: unit
**Gate**: quick

**Commit**: `feat(coletor): proteger segredos com DPAPI`

---

### T24: ArmazenamentoConfiguracao

**What**: Lê/grava `config.json` (URL, credencial, instâncias, configurações) com segredos cifrados, gravação atômica, validações (intervalo 5 min–24 h, janela 0–90 d, período inicial padrão 6 meses, banco já vinculado a outro tenant na mesma rotina) e exclusão de configuração com seu checkpoint.
**Where**: `src/Jamsoft.DashBoards.Coletor.Nucleo/Configuracao/ArmazenamentoConfiguracao.cs`
**Depends on**: T23
**Reuses**: T23
**Requirement**: CFG-02, CFG-05, CFG-06, CFG-07, CFG-08, CFG-09

**Tools**:

- MCP: NONE
- Skill: NONE

**Done when**:

- [ ] Testes: o arquivo gravado não contém senha nem secret em texto; recarrega igual; intervalo 4 min e 25 h recusados; banco já vinculado a outro tenant → erro "Banco já vinculado ao tenant X"; período inicial padrão = hoje − 6 meses (relógio falso); excluir remove configuração e checkpoint; gravação interrompida não corrompe o arquivo anterior
- [ ] Gate check passes: Quick-Coletor
- [ ] Test count: 7 new tests pass

**Tests**: unit
**Gate**: quick

**Commit**: `feat(coletor): armazenar configuracoes do coletor`

---

### T25: Checkpoints

**What**: `checkpoints.json` por (configuração, rotina) com gravação atômica; `Avancar`, `Obter`, `Zerar`.
**Where**: `src/Jamsoft.DashBoards.Coletor.Nucleo/Sincronizacao/Checkpoints.cs`
**Depends on**: T22
**Reuses**: gravação atômica de T24
**Requirement**: SYNC-03, SYNC-09

**Tools**:

- MCP: NONE
- Skill: NONE

**Done when**:

- [ ] Testes: avançar e reler; configurações diferentes independentes; zerar uma não afeta outra; arquivo ausente → sem checkpoint
- [ ] Gate check passes: Quick-Coletor
- [ ] Test count: 4 new tests pass

**Tests**: unit
**Gate**: quick

**Commit**: `feat(coletor): checkpoint por configuracao e rotina`

---

### T26: LogExecucoes e LeitorLogs

**What**: Log diário JSON lines com os campos do LOG-01/02/03, sanitização de `Password=`/`Pwd=`, retenção de 30 dias e leitor com filtros.
**Where**: `src/Jamsoft.DashBoards.Coletor.Nucleo/Logs/LogExecucoes.cs`
**Depends on**: T22
**Reuses**: NONE
**Requirement**: LOG-01, LOG-02, LOG-03, LOG-04, LOG-05, LOG-06

**Tools**:

- MCP: NONE
- Skill: NONE

**Done when**:

- [ ] Testes: um arquivo por dia; evento de fim com todos os campos do LOG-02; tipo de erro em uma das 4 categorias; mensagem com `Password=x` sai sem a senha; filtros por data, tenant, banco, rotina e resultado; arquivos de 31 dias apagados e de 29 mantidos
- [ ] Gate check passes: Quick-Coletor
- [ ] Test count: 7 new tests pass

**Tests**: unit
**Gate**: quick

**Commit**: `feat(coletor): logs diarios de execucao`

---

### Phase 5 tasks

### T27: ExtratorVendas e ERP falso

**What**: Fixture `ErpFalso` (banco de testes com tabela `NotaSaida` identity, view `c_NotaSaida`, `NotaSaidaProd` e semeadura) e `ExtratorVendas` com a query final do design (situações 5/6/9/11/12, `TotalCondNeutra = 0`, `DataHoraFechamento`, `QtdItens`, keyset, `TOP 500`, timeout 120 s) e `AgoraDoServidorAsync`.
**Where**: `src/Jamsoft.DashBoards.Coletor.Nucleo/Erp/ExtratorVendas.cs`
**Depends on**: None
**Reuses**: `BancoDeTestes`; query do `VendasRepositorio` do Sge.Mcp
**Requirement**: SYNC-01, SYNC-02, SYNC-07, SYNC-08

**Tools**:

- MCP: NONE
- Skill: NONE

**Done when**:

- [ ] Testes: só situações 5/6/9/11/12 voltam e com o texto certo (incl. `Contingência`); venda com valor neutro fica fora; `data_hora` = `DataHoraFechamento`; nula → `datafatura` 00:00; `qtd_itens` = linhas em `NotaSaidaProd`; 1.200 vendas em lotes de 500/500/200 em ordem (`datafatura`, `NumNotaSaida`) sem repetição; limite superior exclusivo; consulta acima do timeout (configurável no teste) cancela com erro de Consulta
- [ ] Gate check passes: Full-Coletor
- [ ] Test count: 8 new tests pass

**Tests**: integration
**Gate**: full

**Commit**: `feat(coletor): extrair vendas do ERP em lotes`

---

### T28: Script do ERP (usuário de leitura e índices)

**What**: `sql/coletor/001_erp_usuario_leitura_e_indices.sql`: usuário só de leitura com `SELECT` na view `c_NotaSaida` e em `NotaSaidaProd` (sem acesso à tabela base) e índices de apoio na tabela base `NotaSaida` e em `NotaSaidaProd`, idempotente e comentado.
**Where**: `sql/coletor/001_erp_usuario_leitura_e_indices.sql`
**Depends on**: T27
**Reuses**: `sql/002_banco_cliente_indices_e_usuario.sql` do Sge.Mcp
**Requirement**: suporte (design: origem confirmada da query)

**Tools**:

- MCP: NONE
- Skill: NONE

**Done when**:

- [ ] Teste: aplica duas vezes no ERP falso; com `EXECUTE AS USER` do usuário criado (sem login de servidor), o `ExtratorVendas` lê; `SELECT` direto em `NotaSaida` falha; índices existem
- [ ] Gate check passes: Full-Coletor
- [ ] Test count: 1 new test passes

**Tests**: integration
**Gate**: full

**Commit**: `feat(db): script do erp com usuario de leitura e indices`

---

### T29: ConexoesErp

**What**: Testar conexão, listar bancos e classificar falhas (rede, autenticação, timeout) sem expor a senha.
**Where**: `src/Jamsoft.DashBoards.Coletor.Nucleo/Erp/ConexoesErp.cs`
**Depends on**: T27
**Reuses**: T27
**Requirement**: CFG-03, CFG-04

**Tools**:

- MCP: NONE
- Skill: NONE

**Done when**:

- [ ] Testes: conexão válida lista bancos incluindo o ERP falso; senha errada → "autenticação"; servidor inexistente → "rede" ou "timeout"; mensagem nunca contém a senha
- [ ] Gate check passes: Full-Coletor
- [ ] Test count: 4 new tests pass

**Tests**: integration
**Gate**: full

**Commit**: `feat(coletor): testar conexao e listar bancos do erp`

---

### T30: ClienteApi

**What**: Cliente HTTP do Coletor: token com cache e renovação 1x em 401; retry 2/4/8/16/32 s só em falhas transitórias; tradução de 403 `TENANT_NAO_AUTORIZADO`/`TENANT_INATIVO` e 401 persistente em resultados tipados.
**Where**: `src/Jamsoft.DashBoards.Coletor.Nucleo/Api/ClienteApi.cs`
**Depends on**: None
**Reuses**: T1 (contratos), `Microsoft.Extensions.Http.Resilience`
**Requirement**: COL-03, SYNC-04

**Tools**:

- MCP: NONE
- Skill: NONE

**Done when**:

- [ ] Testes contra a API real (`WebApplicationFactory`): autentica e envia lote; token expirado (relógio falso) renova uma vez e reenvia; credencial revogada → resultado "credencial inválida"; tenant inativo → "não autorizada"; com handler que falha 3x e depois responde, chega ao sucesso com os 3 intervalos esperados; 400 não é repetido
- [ ] Gate check passes: Full-Coletor
- [ ] Test count: 6 new tests pass

**Tests**: integration
**Gate**: full

**Commit**: `feat(coletor): cliente da api com token e retry`

---

### T31: ExecutorSincronizacao

**What**: Executa uma configuração: janela (inicial/incremental com reprocessamento), lotes, envio, checkpoint por ACK, relatório de execução e log; para entre lotes se pedido.
**Where**: `src/Jamsoft.DashBoards.Coletor.Nucleo/Sincronizacao/ExecutorSincronizacao.cs`
**Depends on**: T27, T30
**Reuses**: T24, T25, T26
**Requirement**: SYNC-01, SYNC-03, SYNC-04, SYNC-05, SYNC-06, SYNC-08, CFG-07, LOG-02, LOG-03

**Tools**:

- MCP: NONE
- Skill: NONE

**Done when**:

- [ ] Testes (ERP falso + cliente falso controlável): carga inicial desde hoje − 6 meses; checkpoint só avança após ACK; falha no 2º lote mantém checkpoint do 1º e o próximo ciclo reenvia a partir dele; incremental começa em `checkpoint − janela`; período vazio → Sucesso com 0 registros; erro de consulta → Falha classificada e checkpoint intacto; evento de fim no log com contagens
- [ ] Gate check passes: Full-Coletor
- [ ] Test count: 7 new tests pass

**Tests**: integration
**Gate**: full

**Commit**: `feat(coletor): executar sincronizacao de vendas`

---

### T32: Agendador

**What**: `BackgroundService` com um loop por configuração ativa (próxima = fim + intervalo), `SemaphoreSlim(4)` global, isolamento de falhas e retomada a partir do checkpoint.
**Where**: `src/Jamsoft.DashBoards.Coletor.Nucleo/Sincronizacao/Agendador.cs`
**Depends on**: T31
**Reuses**: T31
**Requirement**: SCH-01, SCH-02, SCH-03, SCH-04, SCH-05

**Tools**:

- MCP: NONE
- Skill: NONE

**Done when**:

- [ ] Testes (executor falso + `FakeTimeProvider`): intervalo contado do fim da anterior; nunca duas execuções da mesma configuração; 6 configurações → no máximo 4 simultâneas; falha de uma não afeta outra; reinício do agendador retoma do checkpoint
- [ ] Gate check passes: Quick-Coletor
- [ ] Test count: 5 new tests pass

**Tests**: unit
**Gate**: quick

**Commit**: `feat(coletor): agendar sincronizacoes por configuracao`

---

### T33: Pausar, retomar e reiniciar

**What**: Comandos via `Channel`: pausar (termina lote atual, situação "Pausada"), retomar (do checkpoint), reiniciar (aguarda fim do lote, zera só aquele checkpoint, novo período inicial, nova carga).
**Where**: `src/Jamsoft.DashBoards.Coletor.Nucleo/Sincronizacao/ComandosConfiguracao.cs`
**Depends on**: T32
**Reuses**: T25, T32
**Requirement**: CTL-01, CTL-02, CTL-03, CTL-04, CTL-06

**Tools**:

- MCP: NONE
- Skill: NONE

**Done when**:

- [ ] Testes: pausa durante execução conclui o lote e não agenda mais; retomar volta a agendar com checkpoint preservado; reiniciar durante execução só zera após o lote; reiniciar zera apenas a configuração alvo e aplica o novo período
- [ ] Gate check passes: Quick-Coletor
- [ ] Test count: 4 new tests pass

**Tests**: unit
**Gate**: quick

**Commit**: `feat(coletor): pausar, retomar e reiniciar sincronizacao`

---

### T34: Ponta a ponta Coletor → API

**What**: Teste do Independent Test do spec com Nucleo real + ERP falso + API real: carga inicial, incremental com vendas novas e cancelamento dentro da janela, reinício sem duplicar, queda da API no meio.
**Where**: `tests/Jamsoft.DashBoards.Coletor.Testes/PontaAPonta/SincronizacaoVendasTestes.cs`
**Depends on**: T33
**Reuses**: T27, T30, `ApiFactory`
**Requirement**: SYNC-05, SCH-05, CTL-05, ING-04, ING-05

**Tools**:

- MCP: NONE
- Skill: NONE

**Done when**:

- [ ] 1.200 vendas em 6 meses → plataforma com 1.200; +10 novas e 1 cancelada na janela → 1.210 e a antiga "Cancelada"; reiniciar → continua 1.210; API derrubada 2x durante carga de 3.000 → contagem exata, sem duplicatas
- [ ] Gate check passes: Full-Coletor
- [ ] Test count: 4 new tests pass

**Tests**: integration
**Gate**: full

**Commit**: `test(coletor): ponta a ponta da sincronizacao de vendas`

---

### Phase 6 tasks

### T35: Host do Coletor e bandeja

**What**: `Program.cs` com Generic Host (DI do Nucleo, Agendador), ícone na bandeja (abrir/sair), fechar janela minimiza, opção "iniciar com o Windows" (HKCU Run, só quando marcada).
**Where**: `src/Jamsoft.DashBoards.Coletor/Program.cs`
**Depends on**: None
**Reuses**: T22
**Requirement**: suporte (design: hospedagem do agendador)

**Tools**:

- MCP: NONE
- Skill: NONE

**Done when**:

- [ ] Build sem avisos; app abre, minimiza para a bandeja e encerra o host ao sair (verificado no roteiro T41)
- [ ] Gate check passes: Build

**Tests**: none
**Gate**: build

**Commit**: `feat(coletor): host do coletor e icone na bandeja`

---

### T36: FormLogin

**What**: URL da API, `clientId`, `clientSecret`; autentica via `ClienteApi`, grava credencial cifrada, carrega tenants; até o login, só login e logs ficam disponíveis.
**Where**: `src/Jamsoft.DashBoards.Coletor/FormLogin.cs`
**Depends on**: T35
**Reuses**: T24, T30, T13
**Requirement**: CFG-01, CFG-02

**Tools**:

- MCP: NONE
- Skill: NONE

**Done when**:

- [ ] Build sem avisos; comportamento coberto pelo roteiro T41 (lógica no Nucleo, já testada)
- [ ] Gate check passes: Build

**Tests**: none
**Gate**: build

**Commit**: `feat(coletor): tela de login do coletor`

---

### T37: FormPrincipal

**What**: Grade de configurações (tenant, banco, situação, intervalo, último sucesso, último resultado) atualizada por eventos do Agendador, com ações editar, excluir (confirmação), pausar, retomar e reiniciar (confirmação + novo período).
**Where**: `src/Jamsoft.DashBoards.Coletor/FormPrincipal.cs`
**Depends on**: T36
**Reuses**: T33
**Requirement**: CFG-09, CFG-10, CTL-01, CTL-02, CTL-03

**Tools**:

- MCP: NONE
- Skill: NONE

**Done when**:

- [ ] Build sem avisos; roteiro T41
- [ ] Gate check passes: Build

**Tests**: none
**Gate**: build

**Commit**: `feat(coletor): tela principal do coletor`

---

### T38: FormInstancia

**What**: Cadastro de instância (servidor, porta, autenticação Windows/SQL, usuário, senha), "Testar conexão" e lista de bancos, mensagens por tipo de erro sem senha.
**Where**: `src/Jamsoft.DashBoards.Coletor/FormInstancia.cs`
**Depends on**: T35
**Reuses**: T29
**Requirement**: CFG-03, CFG-04

**Tools**:

- MCP: NONE
- Skill: NONE

**Done when**:

- [ ] Build sem avisos; roteiro T41
- [ ] Gate check passes: Build

**Tests**: none
**Gate**: build

**Commit**: `feat(coletor): tela de instancias de conexao`

---

### T39: FormConfiguracao

**What**: Configuração de sincronização: tenant (lista da API), instância, banco, rotina "Vendas", intervalo, período inicial, janela, ativa/inativa; validações do Nucleo exibidas nos campos.
**Where**: `src/Jamsoft.DashBoards.Coletor/FormConfiguracao.cs`
**Depends on**: T38
**Reuses**: T24
**Requirement**: CFG-05, CFG-06, CFG-07

**Tools**:

- MCP: NONE
- Skill: NONE

**Done when**:

- [ ] Build sem avisos; roteiro T41
- [ ] Gate check passes: Build

**Tests**: none
**Gate**: build

**Commit**: `feat(coletor): tela de configuracao de sincronizacao`

---

### T40: FormLogs

**What**: Visualização dos logs com filtros por data, tenant, banco, rotina e resultado.
**Where**: `src/Jamsoft.DashBoards.Coletor/FormLogs.cs`
**Depends on**: T35
**Reuses**: T26
**Requirement**: LOG-05

**Tools**:

- MCP: NONE
- Skill: NONE

**Done when**:

- [ ] Build sem avisos; roteiro T41
- [ ] Gate check passes: Build

**Tests**: none
**Gate**: build

**Commit**: `feat(coletor): tela de logs do coletor`

---

### T41: Roteiro de UAT do Coletor

**What**: Documento com o roteiro manual (passos e resultado esperado) cobrindo CFG-01..CFG-10, CTL-01..CTL-06 e LOG-05 contra um SQL Server de teste com o ERP falso e a API local.
**Where**: `.specs/features/sincronizacao-vendas/uat-coletor.md`
**Depends on**: T37, T39, T40
**Reuses**: Independent Tests do spec
**Requirement**: CFG-01, CFG-03, CFG-04, CFG-10, CTL-01, CTL-02, CTL-03, LOG-05

**Tools**:

- MCP: NONE
- Skill: NONE

**Done when**:

- [ ] Roteiro cobre cada AC listado com passo e resultado esperado
- [ ] Gate check passes: Build

**Tests**: none
**Gate**: build

**Commit**: `docs(specs): roteiro de uat do coletor`

---

### Phase 7 tasks

### T42: Lista de coletores (admin web)

**What**: `/admin/coletores` (SuperAdmin): lista paginada com nome, `clientId`, situação, tenants e último token; revogar (confirmação) e rotacionar (abre o diálogo de segredo).
**Where**: `src/Jamsoft.DashBoards.Web/src/app/admin/coletores/coletores-lista.page.ts`
**Depends on**: None
**Reuses**: shell, `shared/ui`, `problema.ts`
**Requirement**: COL-05, COL-06, COL-08

**Tools**:

- MCP: NONE
- Skill: NONE

**Done when**:

- [ ] Testes de componente: estados carregando/vazio/erro; revogar pede confirmação e atualiza a linha; rotacionar abre o diálogo com o novo secret; rota protegida por perfil (guard)
- [ ] Gate check passes: Quick-Web
- [ ] Test count: 5 new tests pass

**Tests**: unit
**Gate**: quick-web

**Commit**: `feat(web): lista de coletores`

---

### T43: Formulário de coletor e diálogo do segredo

**What**: `/admin/coletores/novo` e `/:id` (nome, tenants ativos em multisseleção, versão) e `segredo-coletor.dialog.ts` (mostra `clientId`/`clientSecret` uma vez, copiar, aviso).
**Where**: `src/Jamsoft.DashBoards.Web/src/app/admin/coletores/coletor-form.page.ts`
**Depends on**: T42
**Reuses**: T42
**Requirement**: COL-01, COL-06, COL-07

**Tools**:

- MCP: NONE
- Skill: NONE

**Done when**:

- [ ] Testes de componente: criar envia o corpo esperado e abre o diálogo com o segredo; fechar o diálogo não deixa o segredo em lugar nenhum da tela; sem tenant bloqueia envio; editar tenants trata `CONFLITO_VERSAO`
- [ ] Gate check passes: Quick-Web
- [ ] Test count: 5 new tests pass

**Tests**: unit
**Gate**: quick-web

**Commit**: `feat(web): formulario de coletor e exibicao unica do segredo`

---

### T44: Tela de sincronização do tenant

**What**: `/cliente/sincronizacao` (Admin ou SuperAdmin com tenant): tabela das origens com rotina, banco, último sucesso, situação e mensagem.
**Where**: `src/Jamsoft.DashBoards.Web/src/app/cliente/sincronizacao/sincronizacao.page.ts`
**Depends on**: None
**Reuses**: shell, `shared/ui`, `tenant-efetivo.ts`
**Requirement**: STS-01

**Tools**:

- MCP: NONE
- Skill: NONE

**Done when**:

- [ ] Testes de componente: renderiza as origens; falha mostra a mensagem; estado vazio; SuperAdmin sem tenant vê o seletor
- [ ] Gate check passes: Quick-Web
- [ ] Test count: 4 new tests pass

**Tests**: unit
**Gate**: quick-web

**Commit**: `feat(web): tela de sincronizacao do tenant`

---

### T45: Status dos dados no header

**What**: `status-dados.component.ts` no shell: "Dados atualizados em dd/MM/yyyy HH:mm" para todos; alerta "Dados desatualizados" (> 24 h) só para Admin e SuperAdmin; "Nenhuma sincronização ainda" sem dados.
**Where**: `src/Jamsoft.DashBoards.Web/src/app/shared/layout/status-dados.component.ts`
**Depends on**: None
**Reuses**: shell
**Requirement**: STS-02, STS-03

**Tools**:

- MCP: NONE
- Skill: NONE

**Done when**:

- [ ] Testes de componente: texto com a data formatada; 25 h → alerta para Admin e não para Consulta; 23 h → sem alerta; `null` → "Nenhuma sincronização ainda"
- [ ] Gate check passes: Quick-Web
- [ ] Test count: 5 new tests pass

**Tests**: unit
**Gate**: quick-web

**Commit**: `feat(web): status dos dados no cabecalho`

---

## Phase Execution Map

```
Phase 1 → Phase 2 → Phase 3 → Phase 4 → Phase 5 → Phase 6 → Phase 7

Phase 1 (6):  T1 T2 T3 T4 T5 T6
Phase 2 (7):  T7 T8 T9 T10 T11 T12 T13
Phase 3 (8):  T14 T15 T16 T17 T18 T19 T20 T21
Phase 4 (5):  T22 T23 T24 T25 T26
Phase 5 (8):  T27 T28 T29 T30 T31 T32 T33 T34
Phase 6 (7):  T35 T36 T37 T38 T39 T40 T41
Phase 7 (4):  T42 T43 T44 T45
```

Total: 45 tasks. Lotes sugeridos (~7, fases inteiras): [1] · [2] · [3] · [4] · [5] · [6] · [7] → 7 lotes (a Fase 4 pode ir junto da 7 se convier, mas fica sozinha para não misturar Coletor e Web).

---

## Task Granularity Check

| Task | Scope | Status |
| ---- | ----- | ------ |
| T1 | Projeto de contratos + projeto de testes (scaffold coeso) | ⚠️ Coeso (scaffold) |
| T2–T5 | 1 grupo de entidades + 1 migração cada | ✅ Granular |
| T6 | 1 arquivo de testes de arquitetura | ✅ Granular |
| T7–T13 | 1 componente/endpoint cada (T9/T10: 2 endpoints pequenos do mesmo recurso) | ✅ Granular |
| T14–T21 | 1 componente/endpoint cada (T16–T18 evoluem o mesmo endpoint em passos testáveis) | ✅ Granular |
| T22 | Scaffold dos projetos do Coletor | ⚠️ Coeso (scaffold) |
| T23–T34 | 1 componente/arquivo de testes cada | ✅ Granular |
| T35–T40 | 1 form cada | ✅ Granular |
| T41 | 1 documento | ✅ Granular |
| T42–T45 | 1 página/componente cada | ✅ Granular |

---

## Diagram-Definition Cross-Check

| Task | Depends On (task body) | Diagram Shows (same phase) | Status |
| ---- | ---------------------- | -------------------------- | ------ |
| T1 | None | — | ✅ Match |
| T2 | None | — | ✅ Match |
| T3 | T2 | T2 → T3 | ✅ Match |
| T4 | T3 | T3 → T4 | ✅ Match |
| T5 | T4 | T4 → T5 | ✅ Match |
| T6 | T5 | T5 → T6 | ✅ Match |
| T7 | None | — | ✅ Match |
| T8 | T7 | T7 → T8 | ✅ Match |
| T9 | T8 | T8 → T9 | ✅ Match |
| T10 | T8 | T8 → T10 | ✅ Match |
| T11 | T7 | T7 → T11 | ✅ Match |
| T12 | T11, T10 | T11 → T12, T10 → T12 | ✅ Match |
| T13 | T12 | T12 → T13 | ✅ Match |
| T14 | None | — | ✅ Match |
| T15 | None | — | ✅ Match |
| T16 | T14, T15 | T14 → T16, T15 → T16 | ✅ Match |
| T17 | T16 | T16 → T17 | ✅ Match |
| T18 | T16 | T16 → T18 | ✅ Match |
| T19 | None | — | ✅ Match |
| T20 | T19 | T19 → T20 | ✅ Match |
| T21 | T17 | T17 → T21 | ✅ Match |
| T22 | None | — | ✅ Match |
| T23 | T22 | T22 → T23 | ✅ Match |
| T24 | T23 | T23 → T24 | ✅ Match |
| T25 | T22 | T22 → T25 | ✅ Match |
| T26 | T22 | T22 → T26 | ✅ Match |
| T27 | None | — | ✅ Match |
| T28 | T27 | T27 → T28 | ✅ Match |
| T29 | T27 | T27 → T29 | ✅ Match |
| T30 | None | — | ✅ Match |
| T31 | T27, T30 | T27 → T31, T30 → T31 | ✅ Match |
| T32 | T31 | T31 → T32 | ✅ Match |
| T33 | T32 | T32 → T33 | ✅ Match |
| T34 | T33 | T33 → T34 | ✅ Match |
| T35 | None | — | ✅ Match |
| T36 | T35 | T35 → T36 | ✅ Match |
| T37 | T36 | T36 → T37 | ✅ Match |
| T38 | T35 | T35 → T38 | ✅ Match |
| T39 | T38 | T38 → T39 | ✅ Match |
| T40 | T35 | T35 → T40 | ✅ Match |
| T41 | T37, T39, T40 | T37 → T41, T39 → T41, T40 → T41 | ✅ Match |
| T42 | None | — | ✅ Match |
| T43 | T42 | T42 → T43 | ✅ Match |
| T44 | None | — | ✅ Match |
| T45 | None | — | ✅ Match |

Nenhuma dependência aponta para fase posterior.

---

## Test Co-location Validation

| Task | Code Layer Created/Modified | Matrix Requires | Task Says | Status |
| ---- | --------------------------- | --------------- | --------- | ------ |
| T1 | Contratos | unit | unit | ✅ OK |
| T2–T5 | Schema / migrations | integration | integration | ✅ OK |
| T6 | Architecture rules + exclusão (integração) | integration | integration | ✅ OK |
| T7 | API domain rule | unit | unit | ✅ OK |
| T8–T13 | API endpoints / cache | integration | integration | ✅ OK |
| T14 | API domain rule | unit | unit | ✅ OK |
| T15–T21 | API endpoints / gravador / limpeza | integration | integration | ✅ OK |
| T22 | Scaffolding | none | none | ✅ OK |
| T23–T26 | Coletor.Nucleo (config, checkpoint, logs) | unit | unit | ✅ OK |
| T27–T31 | Coletor.Nucleo (ERP, API, executor) + script SQL | integration | integration | ✅ OK |
| T32–T33 | Coletor.Nucleo (agendador, comandos) | unit | unit | ✅ OK |
| T34 | Ponta a ponta | integration | integration | ✅ OK |
| T35–T40 | Coletor WinForms | none (+ UAT) | none | ✅ OK |
| T41 | Documento de UAT | none | none | ✅ OK |
| T42–T45 | Angular pages/components | unit (component) | unit | ✅ OK |

---

## Requirement Coverage

| Requirement | Tasks |
| ----------- | ----- |
| COL-01 | T7, T8, T43 |
| COL-02 | T2, T7, T8 |
| COL-03 | T11, T30 |
| COL-04 | T11 |
| COL-05 | T10, T12, T42 |
| COL-06 | T10, T42, T43 |
| COL-07 | T9, T43 |
| COL-08 | T8, T9, T42 |
| COL-09 | T12 |
| CFG-01 | T36, T41 |
| CFG-02 | T13, T24, T36 |
| CFG-03 | T29, T38, T41 |
| CFG-04 | T29, T38, T41 |
| CFG-05 | T13, T24, T39 |
| CFG-06 | T24, T39 |
| CFG-07 | T24, T31, T39 |
| CFG-08 | T23, T24 |
| CFG-09 | T24, T37 |
| CFG-10 | T37, T41 |
| SYNC-01 | T27, T31 |
| SYNC-02 | T27 |
| SYNC-03 | T25, T31 |
| SYNC-04 | T30, T31 |
| SYNC-05 | T31, T34 |
| SYNC-06 | T31 |
| SYNC-07 | T27 |
| SYNC-08 | T27, T31 |
| SYNC-09 | T25 |
| ING-01 | T16 |
| ING-02 | T16 |
| ING-03 | T16 |
| ING-04 | T4, T15, T34 |
| ING-05 | T5, T17, T34 |
| ING-06 | T14, T16 |
| ING-07 | T18 |
| ING-08 | T16 |
| ING-09 | T3, T19 |
| ING-10 | T3, T18 |
| SCH-01 | T32 |
| SCH-02 | T32 |
| SCH-03 | T32 |
| SCH-04 | T32 |
| SCH-05 | T32, T34 |
| CTL-01 | T33, T37, T41 |
| CTL-02 | T33, T37, T41 |
| CTL-03 | T33, T37, T41 |
| CTL-04 | T33 |
| CTL-05 | T34 |
| CTL-06 | T33 |
| LOG-01 | T26 |
| LOG-02 | T26, T31 |
| LOG-03 | T26, T31 |
| LOG-04 | T26 |
| LOG-05 | T26, T40, T41 |
| LOG-06 | T26 |
| STS-01 | T20, T44 |
| STS-02 | T20, T45 |
| STS-03 | T45 |

**Coverage:** 58 requisitos, 58 mapeados, 0 sem tarefa.
