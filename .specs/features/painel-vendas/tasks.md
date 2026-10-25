# Painel de Vendas — Indicadores e PWA — Tasks

## Execution Protocol (MANDATORY -- do not skip)

Implement these tasks with the `tlc-spec-driven` skill: **activate it by name and follow its Execute flow and Critical Rules.** Do not search for skill files by filesystem path. The skill is the source of truth for the full flow (per-task cycle, sub-agent delegation, adequacy review, Verifier, discrimination sensor).

**If the skill cannot be activated, STOP and tell the user - do not proceed without it.**

---

**Spec**: `.specs/features/painel-vendas/spec.md`
**Design**: `.specs/features/painel-vendas/design.md`
**Status**: Draft
**Pré-requisito de execução**: a tabela `VENDA` (tarefa 4 de `sincronizacao-vendas`) precisa estar implementada antes da Fase 1 desta feature.

---

## Test Coverage Matrix

> Generated from the existing repo (xUnit + `WebApplicationFactory` + SQL Server real; Angular + Vitest; Playwright) and the approved design - confirm before Execute. Guidelines found: none beyond `.specs/STATE.md` conventions - strong defaults applied; existing test depth is the floor.

| Code Layer | Required Test Type | Coverage Expectation | Location Pattern | Run Command |
| ---------- | ------------------ | -------------------- | ---------------- | ----------- |
| API domain rules (`FiltroPainel`) | unit | All branches; 1:1 to spec ACs; limites (62 dias, 24 meses, período anterior) | `tests/Jamsoft.DashBoards.Api.Testes/Unidade/Painel/*Testes.cs` | `dotnet test tests/Jamsoft.DashBoards.Api.Testes --filter "Categoria=Unidade\|Categoria=Arquitetura"` |
| API endpoints e consultas do painel | integration | Every route: happy path + every listed edge case + error paths (status **e** `codigo`); valores conferidos contra um conjunto de vendas semeado e calculado à mão | `tests/Jamsoft.DashBoards.Api.Testes/Integracao/Painel/*Testes.cs` | `dotnet test tests/Jamsoft.DashBoards.Api.Testes --filter "Categoria=Integracao"` |
| Angular services | unit | All branches; query string; integração com o `authInterceptor` real | `src/Jamsoft.DashBoards.Web/src/app/**/*.spec.ts` | `npm --prefix src/Jamsoft.DashBoards.Web test -- --watch=false` |
| Angular pages/components (incl. gráficos) | unit (component) | Estados carregando/vazio/erro/dados + cada ação; gráficos: opções ECharts geradas (séries, tooltip, cores do DS) | `src/Jamsoft.DashBoards.Web/src/app/**/*.spec.ts` | same as above |
| Configuração PWA (manifest, `ngsw-config.json`) | unit | Cada regra do PWA-01/02 verificada lendo os arquivos | `src/Jamsoft.DashBoards.Web/src/app/**/*.spec.ts` | same as above |
| Fluxos do painel ponta a ponta | e2e | Painel com vendas semeadas; filtro; 360 px sem rolagem horizontal; smoke em Chromium, Firefox e WebKit | `src/Jamsoft.DashBoards.Web/e2e/painel.spec.ts` | `npm --prefix src/Jamsoft.DashBoards.Web run e2e` |
| Medição de desempenho | none | - (relatório; fora do gate) | - | - |

## Gate Check Commands

> Integration tests use `TESTES_SQLSERVER="Server=localhost;Integrated Security=true;TrustServerCertificate=true;Connect Timeout=60"`. Repetir uma vez só em falha de timeout/login de infraestrutura do SQL Server local.

| Gate Level | When to Use | Command |
| ---------- | ----------- | ------- |
| Quick | API tasks with unit tests only | `dotnet test tests/Jamsoft.DashBoards.Api.Testes --filter "Categoria=Unidade\|Categoria=Arquitetura"` |
| Full | API tasks with integration tests | `dotnet test tests/Jamsoft.DashBoards.Api.Testes` |
| Quick-Web | Angular tasks | `npm --prefix src/Jamsoft.DashBoards.Web test -- --watch=false` |
| Full-Web | e2e task | `npm --prefix src/Jamsoft.DashBoards.Web test -- --watch=false && npm --prefix src/Jamsoft.DashBoards.Web run e2e` |
| Build | End of each phase; tasks with no tests | `dotnet build Jamsoft.DashBoards.slnx -warnaserror && dotnet format Jamsoft.DashBoards.slnx --verify-no-changes && dotnet test Jamsoft.DashBoards.slnx && npm --prefix src/Jamsoft.DashBoards.Web run lint && npm --prefix src/Jamsoft.DashBoards.Web run build && npm --prefix src/Jamsoft.DashBoards.Web test -- --watch=false` |

---

## Execution Plan

Phases run sequentially; tasks within a phase run in order. Arrows show dependencies inside a phase.

### Phase 1: API do painel

```
T1 → T2 → T3 → T7
T2 → T4 → T7
T2 → T5 → T7
T2 → T6 → T7
```

### Phase 2: Base do painel no Angular

```
T8
T9 → T10 → T11
```

### Phase 3: Blocos do painel

```
T12
T13
T14
T15
```

### Phase 4: Navegação por perfil e PWA

```
T16
T17
T18
T19
T20
```

### Phase 5: Ponta a ponta e medição

```
T21
T22
```

---

## Task Breakdown

### Phase 1 tasks

### T1: FiltroPainel

**What**: Cria o filtro validado a partir de `inicio`, `fim` e `empresas`: mês corrente por padrão (fuso de São Paulo, `TimeProvider`), faixa UTC `[inicio 00:00, fim+1 00:00)`, validação (fim < início; acima de 24 meses), período anterior equivalente, granularidade (≤ 62 dias → dia) e buckets para preencher zeros.
**Where**: `src/Jamsoft.DashBoards.Api/Modulos/Painel/FiltroPainel.cs`
**Depends on**: None
**Reuses**: `ProblemasApi.Validacao` (formato dos erros)
**Requirement**: DASH-01, DASH-05, DASH-09, TRD-01, TRD-02, TRD-03

**Tools**:

- MCP: NONE
- Skill: NONE

**Done when**:

- [x] Testes: sem datas → dia 1 do mês corrente até hoje (relógio falso, inclusive virada de mês em São Paulo × UTC); faixa UTC correta (00:00 −03:00); fim < início → erro em `fim`; exatamente 24 meses aceito e 1 dia a mais recusado; período anterior de 10 dias termina na véspera do início; 62 dias → `Dia` e 63 → `Mes`; buckets diários e mensais completos
- [x] Gate check passes: Quick
- [x] Test count: ≥ 9 tests pass

**Tests**: unit
**Gate**: quick

**Commit**: `feat(api): filtro do painel com periodo anterior e granularidade`

---

### T2: ConsultaVendas

**What**: Base de consulta do painel: `Vendas` (filtro global de tenant) na faixa do filtro e, quando informado, `EMPRESA_ID IN (...)`; `ResolverEmpresasAsync` converte IDs públicos via `Empresas` do tenant e lança 404 se algum não bater. Inclui o helper de testes `VendasSemeadas` (conjunto fixo de vendas em 2 empresas com valores calculados à mão).
**Where**: `src/Jamsoft.DashBoards.Api/Modulos/Painel/ConsultaVendas.cs`
**Depends on**: T1
**Reuses**: filtro global de tenant, `Empresas`; tabela `VENDA` (pré-requisito)
**Requirement**: DASH-04, DASH-07, DASH-08

**Tools**:

- MCP: NONE
- Skill: NONE

**Done when**:

- [x] Testes: só vendas do tenant do contexto e da faixa (limite superior exclusivo à meia-noite de São Paulo); filtro por empresa restringe; empresa de outro tenant → `ProblemaApiException` 404 `NAO_ENCONTRADO`; avaliação no cliente desligada (`ConfigureWarnings` lançando) e nenhuma exceção
- [x] Gate check passes: Full
- [x] Test count: 4 new tests pass

**Tests**: integration
**Gate**: full

**Commit**: `feat(api): base de consulta de vendas do painel`

---

### T3: GET /api/painel/kpis

**What**: KPIs do período e do anterior (faturamento, vendas, ticket médio, itens por venda e % canceladas, com efetivadas = Finalizada + Contingência), `semVendas` e variação % (nula quando o anterior é 0), arredondados a 2 casas.
**Where**: `src/Jamsoft.DashBoards.Api/Modulos/Painel/PainelEndpoints.cs`
**Depends on**: T2
**Reuses**: policies `LeituraTenant`, `.ValidarContextoTenant()`, `.AuditarContextoSuperAdmin()`
**Requirement**: DASH-01, DASH-02, DASH-03, DASH-06, DASH-09

**Tools**:

- MCP: NONE
- Skill: NONE

**Done when**:

- [x] Testes com `VendasSemeadas`: cada KPI igual ao valor calculado à mão (Contingência conta; Inutilizada não; Cancelada só no %); sem datas → mês corrente; período alterado recalcula; período sem vendas → zeros e `semVendas = true`; variação com anterior 0 → `null`; período inválido → 400 `VALIDACAO`; Consulta e Admin → 200; SuperAdmin sem tenant → 400 `TENANT_NAO_SELECIONADO`
- [x] Gate check passes: Full
- [x] Test count: 8 new tests pass

**Tests**: integration
**Gate**: full

**Commit**: `feat(api): kpis do painel de vendas`

---

### T4: GET /api/painel/evolucao

**What**: Faturamento (efetivadas) por dia ou mês no fuso de São Paulo (`EF.Functions.AtTimeZone`), zeros preenchidos e série do período anterior alinhada ponto a ponto.
**Where**: `src/Jamsoft.DashBoards.Api/Modulos/Painel/PainelEndpoints.cs` (modify)
**Depends on**: T2
**Reuses**: T1 (buckets)
**Requirement**: TRD-01, TRD-02, TRD-03

**Tools**:

- MCP: NONE
- Skill: NONE

**Done when**:

- [x] Testes: 10 dias com vendas em 3 → 10 pontos e 7 zeros; venda às 23:30 de São Paulo cai no dia local, não no dia UTC; 90 dias → granularidade `mes` com meses vazios zerados; série anterior com o mesmo número de pontos e valores certos
- [x] Gate check passes: Full
- [x] Test count: 4 new tests pass

**Tests**: integration
**Gate**: full

**Commit**: `feat(api): evolucao do faturamento no painel`

---

### T5: GET /api/painel/rankings

**What**: Top 10 de vendedores, clientes e empresas por faturamento (efetivadas): agrupamento por código, nomes buscados só do top 10, "Não informado" para código nulo, desempate por nome (pt-BR), participação sobre o total.
**Where**: `src/Jamsoft.DashBoards.Api/Modulos/Painel/PainelEndpoints.cs` (modify)
**Depends on**: T2
**Reuses**: `Empresas` (nome manual tem prioridade)
**Requirement**: RNK-01, RNK-02, RNK-03, RNK-04

**Tools**:

- MCP: NONE
- Skill: NONE

**Done when**:

- [x] Testes: 12 vendedores → 10, em ordem decrescente; campos nome/faturamento/vendas/participação exatos; vendas sem vendedor e sem cliente → "Não informado"; empate de faturamento → ordem alfabética; empresa renomeada à mão aparece com o nome manual; soma das participações ≤ 100
- [x] Gate check passes: Full
- [x] Test count: 6 new tests pass

**Tests**: integration
**Gate**: full

**Commit**: `feat(api): rankings do painel de vendas`

---

### T6: GET /api/painel/dfe

**What**: Faturamento, nº de vendas e participação por tipo de DF-e, sempre com NF-e e NFC-e.
**Where**: `src/Jamsoft.DashBoards.Api/Modulos/Painel/PainelEndpoints.cs` (modify)
**Depends on**: T2
**Reuses**: T2
**Requirement**: DFE-01, DFE-02

**Tools**:

- MCP: NONE
- Skill: NONE

**Done when**:

- [x] Testes: 3 NF-e e 7 NFC-e → participação por faturamento conforme o conjunto e contagens 3/7; período só com NFC-e → NF-e com zeros
- [x] Gate check passes: Full
- [x] Test count: 2 new tests pass

**Tests**: integration
**Gate**: full

**Commit**: `feat(api): divisao nf-e e nfc-e no painel`

---

### T7: Isolamento e duração das consultas do painel

**What**: Casos das 4 rotas `/api/painel/*` na suíte de isolamento (Admin e Consulta de A contra B, incluindo `empresas` de B e `X-Tenant` divergente) e endpoint filter que loga a duração por bloco sem dados de venda.
**Where**: `src/Jamsoft.DashBoards.Api/Modulos/Painel/DuracaoPainelFilter.cs`
**Depends on**: T3, T4, T5, T6
**Reuses**: suíte de isolamento (`IsolamentoTenantsTestes`), `LogsCapturados`
**Requirement**: DASH-07, DASH-08

**Tools**:

- MCP: NONE
- Skill: NONE

**Done when**:

- [x] Suíte de isolamento cobre as 4 rotas novas e passa (valores de A nunca incluem vendas de B; `empresas` de B → 404; `X-Tenant` divergente → 403)
- [x] Testes: cada bloco gera um log com o nome do bloco e a duração; o log não contém valores nem nomes de clientes
- [x] Gate check passes: Full
- [x] Test count: 2 new tests pass (+ casos da suíte de isolamento)

**Tests**: integration
**Gate**: full

**Commit**: `test(api): isolamento e duracao das consultas do painel`

---

### Phase 2 tasks

### T8: ECharts, formatação pt-BR e tema dos gráficos

**What**: Instala `echarts` e `ngx-echarts` com import modular (`echarts/core` + Line, Pie, Grid, Tooltip, Legend, CanvasRenderer) só no chunk do painel; `registerLocaleData(pt)`, `LOCALE_ID`, `DEFAULT_CURRENCY_CODE = 'BRL'`, `DATE_PIPE_DEFAULT_OPTIONS` (São Paulo); `tema-graficos.ts` com cores e fontes lidas dos tokens do DS.
**Where**: `src/Jamsoft.DashBoards.Web/src/app/cliente/painel/graficos/tema-graficos.ts`
**Depends on**: None
**Reuses**: `styles/_tokens.scss`
**Requirement**: UI-06

**Tools**:

- MCP: NONE
- Skill: `dataviz`

**Done when**:

- [x] Testes: o tema usa `--primary` na série atual e `--text-muted` tracejado na anterior; fontes Inter/JetBrains Mono; `R$ 1.234,56` e `24/10/2026` formatados pelo pipe com a configuração aplicada
- [x] Build: chunk inicial sem ECharts (verificado nas estatísticas do build) e budget do bundle inicial inalterado
- [x] Gate check passes: Quick-Web
- [x] Test count: 3 new tests pass

**Tests**: unit
**Gate**: quick-web

**Commit**: `feat(web): echarts modular, formatacao pt-br e tema dos graficos`

---

### T9: PainelService

**What**: `kpis`, `evolucao`, `rankings`, `dfe(filtro)` montando a query string (`inicio`, `fim`, `empresas` repetido) e tipos das respostas.
**Where**: `src/Jamsoft.DashBoards.Web/src/app/cliente/painel/painel.service.ts`
**Depends on**: None
**Reuses**: `authInterceptor`, `tenant-contexto`
**Requirement**: UI-05

**Tools**:

- MCP: NONE
- Skill: NONE

**Done when**:

- [x] Testes: cada método chama a rota certa com a query string esperada (sem `empresas` quando vazio; repetido quando vários); com o `authInterceptor` real no TestBed, 401 em `/api/painel/kpis` renova e repete com o token novo; SuperAdmin com tenant envia `X-Tenant`
- [x] Gate check passes: Quick-Web
- [x] Test count: 6 new tests pass

**Tests**: unit
**Gate**: quick-web

**Commit**: `feat(web): servico do painel de vendas`

---

### T10: FiltroPainelComponent

**What**: Datas inicial/final, atalhos (Este mês, Últimos 7 dias, Últimos 30 dias, Mês anterior), multisseleção de empresas (só com mais de uma), "Atualizar", validação local (fim < início; > 24 meses) e estado na URL.
**Where**: `src/Jamsoft.DashBoards.Web/src/app/cliente/painel/filtro-painel.component.ts`
**Depends on**: T9
**Reuses**: `/api/empresas`, `shared/ui`
**Requirement**: DASH-03, DASH-04, DASH-05

**Tools**:

- MCP: NONE
- Skill: NONE

**Done when**:

- [x] Testes de componente: cada atalho gera as datas esperadas (relógio falso); mudar período emite o filtro e atualiza a URL; abrir com `?inicio&fim&empresas` restaura o filtro; fim < início e 25 meses mostram a mensagem e não emitem; seletor de empresas oculto com 1 empresa
- [x] Gate check passes: Quick-Web
- [x] Test count: 7 new tests pass

**Tests**: unit
**Gate**: quick-web

**Commit**: `feat(web): filtro do painel com estado na url`

---

### T11: PainelPage

**What**: Página `/cliente` (substitui `cliente-inicio.page.ts`): grid responsivo, um estado por bloco (carregando/erro com "Tentar novamente"/dados), mensagens "Nenhuma venda no período" e "Nenhuma venda sincronizada ainda", SuperAdmin sem tenant → seletor.
**Where**: `src/Jamsoft.DashBoards.Web/src/app/cliente/painel/painel.page.ts`
**Depends on**: T10
**Reuses**: `estado-carga.component`, `tenant-efetivo.ts`, `seletor-tenant`
**Requirement**: DASH-01, DASH-06, UI-07, UI-08

**Tools**:

- MCP: NONE
- Skill: NONE

**Done when**:

- [x] Testes de componente: ao abrir, dispara os 4 blocos com o mês corrente; cada bloco mostra carregando enquanto pendente; erro em `rankings` mostra erro e "Tentar novamente" só nele e os outros 3 renderizam; "Tentar novamente" refaz só aquele bloco; `semVendas` → mensagem; tenant sem sincronização → mensagem própria; SuperAdmin sem tenant → seletor e nenhuma chamada
- [x] Gate check passes: Quick-Web
- [x] Test count: 7 new tests pass

**Tests**: unit
**Gate**: quick-web

**Commit**: `feat(web): pagina do painel de vendas`

---

### Phase 3 tasks

### T12: KpisComponent

**What**: 5 blocos de KPI no padrão "Relatório Painel" com valor formatado e variação (seta + texto + cor de estado; "—" quando nula).
**Where**: `src/Jamsoft.DashBoards.Web/src/app/cliente/painel/kpis.component.ts`
**Depends on**: None
**Reuses**: tokens do DS
**Requirement**: DASH-02, DASH-09

**Tools**:

- MCP: NONE
- Skill: NONE

**Done when**:

- [x] Testes de componente: faturamento e ticket em BRL, vendas inteiro, itens com 2 casas, % canceladas com "%"; variação positiva com seta para cima e texto "+12,30%"; negativa com seta para baixo; nula → "—"; cada bloco com rótulo acessível
- [x] Gate check passes: Quick-Web
- [x] Test count: 5 new tests pass

**Tests**: unit
**Gate**: quick-web

**Commit**: `feat(web): blocos de kpi do painel`

---

### T13: EvolucaoComponent

**What**: Gráfico de linha ECharts: atual (`--primary`) e anterior (tracejado), rótulos dd/MM ou MMM/yy, tooltip com data, atual e anterior em BRL, toque habilitado.
**Where**: `src/Jamsoft.DashBoards.Web/src/app/cliente/painel/evolucao.component.ts`
**Depends on**: None
**Reuses**: `tema-graficos.ts`
**Requirement**: TRD-04

**Tools**:

- MCP: NONE
- Skill: `dataviz`

**Done when**:

- [x] Testes de componente: as opções geradas têm 2 séries com os valores da resposta, eixo com os rótulos da granularidade e `tooltip.trigger = 'axis'`; o formatter do tooltip devolve data, "Atual R$ …" e "Anterior R$ …"; `triggerOn` cobre toque
- [x] Gate check passes: Quick-Web
- [x] Test count: 4 new tests pass

**Tests**: unit
**Gate**: quick-web

**Commit**: `feat(web): grafico de evolucao do faturamento`

---

### T14: RankingsComponent

**What**: Tabelas Vendedores, Clientes e Empresas (nome, faturamento, vendas, participação + barra inline), Empresas oculta com tenant de uma empresa, rolagem interna em telas estreitas.
**Where**: `src/Jamsoft.DashBoards.Web/src/app/cliente/painel/rankings.component.ts`
**Depends on**: None
**Reuses**: estilos de tabela do DS (`_componentes.scss`)
**Requirement**: RNK-02, RNK-05

**Tools**:

- MCP: NONE
- Skill: NONE

**Done when**:

- [x] Testes de componente: cada linha mostra os 4 campos formatados; barra com largura = participação; tenant com 1 empresa → sem tabela de Empresas; lista vazia → estado vazio; tabela com `<caption>`/cabeçalhos acessíveis
- [x] Gate check passes: Quick-Web
- [x] Test count: 5 new tests pass

**Tests**: unit
**Gate**: quick-web

**Commit**: `feat(web): rankings do painel`

---

### T15: DfeComponent

**What**: Rosca ECharts NF-e × NFC-e com legenda (valor, quantidade, %).
**Where**: `src/Jamsoft.DashBoards.Web/src/app/cliente/painel/dfe.component.ts`
**Depends on**: None
**Reuses**: `tema-graficos.ts`
**Requirement**: DFE-01

**Tools**:

- MCP: NONE
- Skill: `dataviz`

**Done when**:

- [ ] Testes de componente: série de pizza com 2 itens e valores da resposta; legenda com faturamento, vendas e participação formatados; tipo com zero aparece na legenda com "R$ 0,00"
- [ ] Gate check passes: Quick-Web
- [ ] Test count: 3 new tests pass

**Tests**: unit
**Gate**: quick-web

**Commit**: `feat(web): grafico nf-e e nfc-e`

---

### Phase 4 tasks

### T16: Menu por perfil e "Meu perfil"

**What**: Itens do menu do shell por perfil (Consulta: Painel e Meu perfil; Admin: + Usuários, Empresas, Sincronização; SuperAdmin: Tenants, Coletores, Auditoria e, com tenant, as telas do tenant), página `conta/perfil` (nome, e-mail, perfil, tenant, link para trocar senha) e matriz do guard com as rotas novas.
**Where**: `src/Jamsoft.DashBoards.Web/src/app/conta/perfil/perfil.page.ts`
**Depends on**: None
**Reuses**: `shell.component.ts`, `perfil.guard.ts`, `/api/auth/eu`
**Requirement**: UI-01, UI-02, UI-03, UI-04

**Tools**:

- MCP: NONE
- Skill: NONE

**Done when**:

- [ ] Testes: menu de cada perfil contém exatamente os itens esperados; "Meu perfil" mostra os dados do `/api/auth/eu`; matriz do guard inclui `/cliente`, `/cliente/sincronizacao`, `/admin/coletores`, `/conta/perfil` com o resultado esperado por perfil (incl. aviso "Acesso não permitido")
- [ ] Gate check passes: Quick-Web
- [ ] Test count: ≥ 8 new tests pass

**Tests**: unit
**Gate**: quick-web

**Commit**: `feat(web): menu por perfil e pagina meu perfil`

---

### T17: Aviso "Sem conexão"

**What**: `conexao.service.ts` (`navigator.onLine` + eventos) com faixa "Sem conexão" no shell; blocos do painel mostram "Sem conexão" e não consultam enquanto offline.
**Where**: `src/Jamsoft.DashBoards.Web/src/app/core/pwa/conexao.service.ts`
**Depends on**: None
**Reuses**: shell, `painel.page.ts`
**Requirement**: PWA-03

**Tools**:

- MCP: NONE
- Skill: NONE

**Done when**:

- [ ] Testes: evento `offline` → faixa visível e blocos com "Sem conexão" sem chamadas HTTP; `online` → faixa some e o painel recarrega
- [ ] Gate check passes: Quick-Web
- [ ] Test count: 3 new tests pass

**Tests**: unit
**Gate**: quick-web

**Commit**: `feat(web): aviso de sem conexao`

---

### T18: Aviso "Nova versão disponível"

**What**: `atualizacao.service.ts` com `SwUpdate.versionUpdates` (`VERSION_READY`) → faixa "Nova versão disponível" com "Recarregar" (`activateUpdate` + reload).
**Where**: `src/Jamsoft.DashBoards.Web/src/app/core/pwa/atualizacao.service.ts`
**Depends on**: None
**Reuses**: `provideServiceWorker` existente
**Requirement**: PWA-04

**Tools**:

- MCP: NONE
- Skill: NONE

**Done when**:

- [ ] Testes (SwUpdate falso): `VERSION_READY` mostra a faixa; "Recarregar" chama `activateUpdate` e recarrega; outros eventos não mostram; service worker desabilitado não quebra
- [ ] Gate check passes: Quick-Web
- [ ] Test count: 4 new tests pass

**Tests**: unit
**Gate**: quick-web

**Commit**: `feat(web): aviso de nova versao disponivel`

---

### T19: Manifest e política de cache

**What**: Ajustar `manifest.webmanifest` (nome, `short_name`, ícones 192/512 gerados da logo sobre o vermelho do DS, `standalone`, cores) e garantir `ngsw-config.json` sem `dataGroups` nem URLs `/api`; testes que leem os dois arquivos.
**Where**: `src/Jamsoft.DashBoards.Web/public/manifest.webmanifest`
**Depends on**: None
**Reuses**: logos em `public/marca/`
**Requirement**: PWA-01, PWA-02

**Tools**:

- MCP: NONE
- Skill: NONE

**Done when**:

- [ ] Testes: manifest com `name = "JAMSOFT Dashboards"`, `display = standalone`, ícones 192 e 512 existentes no disco; `ngsw-config.json` sem `dataGroups` e sem nenhuma URL começando por `/api`
- [ ] Gate check passes: Quick-Web
- [ ] Test count: 4 new tests pass

**Tests**: unit
**Gate**: quick-web

**Commit**: `feat(web): manifest e politica de cache da pwa`

---

### T20: Logout limpa o armazenamento local

**What**: Garantir e testar que o logout remove tokens, tenant selecionado e qualquer chave da aplicação de `sessionStorage`/`localStorage`.
**Where**: `src/Jamsoft.DashBoards.Web/src/app/core/auth/auth.service.ts` (modify)
**Depends on**: None
**Reuses**: `TenantContextoService.limpar()`
**Requirement**: PWA-07

**Tools**:

- MCP: NONE
- Skill: NONE

**Done when**:

- [ ] Testes: após logout, `sessionStorage` e `localStorage` sem chaves da aplicação (prefixo do app) e `usuario()`/token nulos; uma chave de outro app no storage é preservada
- [ ] Gate check passes: Quick-Web
- [ ] Test count: 2 new tests pass

**Tests**: unit
**Gate**: quick-web

**Commit**: `feat(web): logout limpa dados locais da aplicacao`

---

### Phase 5 tasks

### T21: e2e do painel

**What**: Playwright com vendas semeadas por SQL no banco do e2e: Admin abre o painel e vê os KPIs esperados; muda o período e os valores mudam; Consulta vê o painel e não acessa usuários; 360 × 740 e 1920 × 1080 sem rolagem horizontal; smoke do painel também em Firefox e WebKit.
**Where**: `src/Jamsoft.DashBoards.Web/e2e/painel.spec.ts`
**Depends on**: None
**Reuses**: `e2e/global-setup.ts` da feature anterior
**Requirement**: DASH-01, DASH-03, PWA-05, PWA-06

**Tools**:

- MCP: NONE
- Skill: NONE

**Done when**:

- [ ] Cenários passam em Chromium; smoke passa em Firefox e WebKit; `scrollWidth <= clientWidth` nas duas larguras
- [ ] Gate check passes: Full-Web
- [ ] Test count: ≥ 5 e2e tests pass

**Tests**: e2e
**Gate**: full-web

**Commit**: `test(web): e2e do painel de vendas`

---

### T22: Medição de desempenho e revisão visual

**What**: Script para semear 1 milhão de vendas num banco de medição e medir p95 de cada bloco (log de duração do T7) e Lighthouse (PWA instalável, Acessibilidade ≥ 90); relatório com resultado, ambiente e revisão visual contra o Design System. Fora do gate (ambiente local com pouca memória).
**Where**: `.specs/features/painel-vendas/medicao.md`
**Depends on**: None
**Reuses**: log de duração (T7)
**Requirement**: UI-06

**Tools**:

- MCP: NONE
- Skill: NONE

**Done when**:

- [ ] Relatório com p95 por bloco, ambiente, resultado do Lighthouse e checklist visual do DS; se p95 > 2 s, recomendação registrada (agregados pré-calculados) para decisão
- [ ] Gate check passes: Build

**Tests**: none
**Gate**: build

**Commit**: `docs(specs): medicao de desempenho do painel`

---

## Phase Execution Map

```
Phase 1 → Phase 2 → Phase 3 → Phase 4 → Phase 5

Phase 1 (7):  T1 T2 T3 T4 T5 T6 T7
Phase 2 (4):  T8 T9 T10 T11
Phase 3 (4):  T12 T13 T14 T15
Phase 4 (5):  T16 T17 T18 T19 T20
Phase 5 (2):  T21 T22
```

Total: 22 tasks. Lotes sugeridos (~7, fases inteiras): [1] · [2+3] · [4+5] → 3 lotes.

---

## Task Granularity Check

| Task | Scope | Status |
| ---- | ----- | ------ |
| T1–T2 | 1 componente cada | ✅ Granular |
| T3–T6 | 1 endpoint cada (mesmo arquivo de rotas) | ✅ Granular |
| T7 | 1 filtro + casos na suíte existente | ✅ Granular |
| T8 | Setup de biblioteca + tema (coeso) | ⚠️ Coeso (setup) |
| T9–T15 | 1 serviço/componente cada | ✅ Granular |
| T16 | Menu + 1 página pequena (mesma regra de perfis) | ⚠️ Coeso |
| T17–T20 | 1 serviço/arquivo cada | ✅ Granular |
| T21 | 1 arquivo e2e | ✅ Granular |
| T22 | 1 relatório | ✅ Granular |

---

## Diagram-Definition Cross-Check

| Task | Depends On (task body) | Diagram Shows (same phase) | Status |
| ---- | ---------------------- | -------------------------- | ------ |
| T1 | None | — | ✅ Match |
| T2 | T1 | T1 → T2 | ✅ Match |
| T3 | T2 | T2 → T3 | ✅ Match |
| T4 | T2 | T2 → T4 | ✅ Match |
| T5 | T2 | T2 → T5 | ✅ Match |
| T6 | T2 | T2 → T6 | ✅ Match |
| T7 | T3, T4, T5, T6 | T3 → T7, T4 → T7, T5 → T7, T6 → T7 | ✅ Match |
| T8 | None | — | ✅ Match |
| T9 | None | — | ✅ Match |
| T10 | T9 | T9 → T10 | ✅ Match |
| T11 | T10 | T10 → T11 | ✅ Match |
| T12–T15 | None | — | ✅ Match |
| T16–T20 | None | — | ✅ Match |
| T21–T22 | None | — | ✅ Match |

Nenhuma dependência aponta para fase posterior. Os blocos da Fase 3 usam o serviço e o tema da Fase 2 (fase anterior).

---

## Test Co-location Validation

| Task | Code Layer Created/Modified | Matrix Requires | Task Says | Status |
| ---- | --------------------------- | --------------- | --------- | ------ |
| T1 | API domain rule | unit | unit | ✅ OK |
| T2–T7 | API consultas/endpoints/filtro | integration | integration | ✅ OK |
| T8–T11 | Angular services/pages | unit | unit | ✅ OK |
| T12–T15 | Angular components (gráficos) | unit (component) | unit | ✅ OK |
| T16–T18, T20 | Angular pages/services | unit | unit | ✅ OK |
| T19 | Configuração PWA | unit | unit | ✅ OK |
| T21 | Fluxos ponta a ponta | e2e | e2e | ✅ OK |
| T22 | Medição | none | none | ✅ OK |

---

## Requirement Coverage

| Requirement | Tasks |
| ----------- | ----- |
| DASH-01 | T1, T3, T11, T21 |
| DASH-02 | T3, T12 |
| DASH-03 | T3, T10, T21 |
| DASH-04 | T2, T10 |
| DASH-05 | T1, T3, T10 |
| DASH-06 | T3, T11 |
| DASH-07 | T2, T7 |
| DASH-08 | T2, T7 |
| DASH-09 | T1, T3, T12 |
| TRD-01 | T1, T4 |
| TRD-02 | T1, T4 |
| TRD-03 | T1, T4 |
| TRD-04 | T13 |
| UI-01 | T16 |
| UI-02 | T16 |
| UI-03 | T16 |
| UI-04 | T16 |
| UI-05 | T9 |
| UI-06 | T8, T22 |
| UI-07 | T11 |
| UI-08 | T11 |
| RNK-01 | T5 |
| RNK-02 | T5, T14 |
| RNK-03 | T5 |
| RNK-04 | T5 |
| RNK-05 | T14 |
| DFE-01 | T6, T15 |
| DFE-02 | T6 |
| PWA-01 | T19 |
| PWA-02 | T19 |
| PWA-03 | T17 |
| PWA-04 | T18 |
| PWA-05 | T21 |
| PWA-06 | T21 |
| PWA-07 | T20 |

**Coverage:** 35 requisitos, 35 mapeados, 0 sem tarefa.
