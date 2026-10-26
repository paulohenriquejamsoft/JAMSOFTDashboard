# Painel de Vendas — relatório de verificação (rodada 2)

## Validation: painel-vendas (rodada 2) - PASS ✅

**Date**: 2026-10-26
**Spec**: `.specs/features/painel-vendas/spec.md` (DFE-01 e RNK-04 precisados pelo usuário nesta rodada)
**Diff range**: `188b9e3..2e474c7` (29 commits). A rodada 1 cobriu `188b9e3..f846c2c` e reprovou. As correções estão em `f846c2c..2e474c7` (7 commits, 12 arquivos, +186/−12).
**Verifier**: sub-agente independente (autor ≠ verificador). Cobertura re-derivada da spec e do código. As citações da rodada 1 em arquivos que não mudaram foram conferidas de novo linha a linha. Os arquivos alterados tiveram as linhas recontadas no `2e474c7`.

Por que PASS: os 2 mutantes sobreviventes da rodada 1 (A9, W5) agora morrem. As 7 mutações novas também morrem, e os dois gates passam com `exit=0`. As 3 decisões do usuário (participação do DF-e por faturamento, desempate por código no corte do top 10, verde escurecido só no painel) têm teste que distingue a regra certa da errada. O que sobra são spec-precision gaps e desvios de documentação, nenhum de comportamento.

---

## Task Completion

| Task | Status | Notes |
| ---- | ------ | ----- |
| T1–T22 | ✅ Done | Igual à rodada 1. Todos os "Done when" estão marcados em `tasks.md` |
| Correções da rodada 1 | ✅ Done | `a62c253` (DASH-04 API), `924b713` (RNK-05 página), `d430d2a` (eixo pt-BR), `bcf8457` (contraste), `341c3ec` (DFE-01 base), `7827112` (RNK-04 corte), `2e474c7` (AD-019) |

As correções foram commitadas direto, sem tarefas `T23+` em `tasks.md`. Isso não afeta o veredito.

---

## Spec-Anchored Acceptance Criteria

Caminhos abreviados: `API.T/` = `tests/Jamsoft.DashBoards.Api.Testes/`, `WEB/` = `src/Jamsoft.DashBoards.Web/src/app/`, `E2E/` = `src/Jamsoft.DashBoards.Web/e2e/`. As linhas são do commit `2e474c7`. Os valores esperados foram recalculados à mão a partir de `API.T/Infraestrutura/VendasSemeadas.cs:13-25`.

### P1: KPIs do período

| Criterion | Spec-defined outcome | `file:line` + assertion | Result |
| --------- | -------------------- | ----------------------- | ------ |
| DASH-01 ao abrir, KPIs do mês corrente de todas as empresas | período do dia 1 do mês até hoje (SP), sem `empresas` | `API.T/Unidade/Painel/FiltroPainelTestes.cs:31-32`: `Assert.Equal(new DateOnly(2026,10,1), filtro.Inicio)`, `Fim == 2026-10-24`. `API.T/Integracao/Painel/KpisEndpointTestes.cs:113-116`: `periodo.inicio == "2026-10-01"`, faturamento `475.50` (1+2+5+6+8+9), anterior `500.00` (#10), variação `-4.90`. `WEB/cliente/painel/painel.page.spec.ts:91`: `urlWithParams == /api/painel/{bloco}?inicio=2026-10-01&fim=2026-10-24`. `E2E/painel.spec.ts:162-166` | ✅ |
| DASH-02 fórmulas | faturamento = Σ efetivadas (Finalizada + Contingência). Ticket = fat ÷ nº. Itens = Σitens ÷ nº. % canceladas = canc ÷ (efet + canc) × 100. Sem vendas, 0 | `API.T/Integracao/Painel/KpisEndpointTestes.cs:75-80`: `375.50`, `4`, `93.88`, `2.00`, `20.00` (Contingência conta, Inutilizada fica fora). `:134`: `percentualCanceladas 50.00` (1 efet + 1 canc). `:148`: zeros. `WEB/cliente/painel/kpis.component.spec.ts:33-37`: formatação | ✅ |
| DASH-03 alterar o período recalcula tudo | novos valores | `API.T/Integracao/Painel/KpisEndpointTestes.cs:130-134`: só 15/10 dá `200.00`, `1`, `4.00`, `50.00`. `WEB/cliente/painel/filtro-painel.component.spec.ts:95-96`. `E2E/painel.spec.ts:175-177` | ✅ |
| DASH-04 empresas restringem todos os indicadores | só as vendas da empresa escolhida, nos 4 blocos | **Agora nas 4 rotas**, com `empresas=<Filial>`. Na Filial, o período tem #5 (200, 4 itens) e #6 (25,50, 1 item); o anterior tem #9 (60). **KPIs** `API.T/Integracao/Painel/KpisEndpointTestes.cs:97-101`: `225.50/60.00/275.83`, `2/1/100.00`, `112.75/60.00/87.92`, `2.50/1.00/150.00`, `0/0/null`. **Evolução** `API.T/Integracao/Painel/EvolucaoEndpointTestes.cs:120-121`: atual `[0,0,0,0,200,0,0,0,0,25.50]`, anterior `[0,0,0,0,60,0,0,0,0,0]`. **Rankings** `API.T/Integracao/Painel/RankingsEndpointTestes.cs:175-179`: total `225.50`, vendedores `Ana 200/88.69` e `Não informado 25.50/11.31`, cliente `Cliente Y 225.50/100`, empresa `Filial`. **DF-e** `API.T/Integracao/Painel/DfeEndpointTestes.cs:96`: `NF-e 200/1/88.69`, `NFC-e 25.50/1/11.31`. Recalculei todos à mão e conferem. Base: `API.T/Integracao/Painel/ConsultaVendasTestes.cs:65-66`. Front: `WEB/cliente/painel/filtro-painel.component.spec.ts:157-158`, `WEB/cliente/painel/painel.service.spec.ts:81-84`. O mutante A9 morreu nos 4 testes | ✅ (gap 1 fechado) |
| DASH-05 fim < início ou > 24 meses: 400, e o front não consulta | 400 `VALIDACAO` com erro em `fim`; mensagem; nada emitido | `API.T/Unidade/Painel/FiltroPainelTestes.cs:64,72,80`: `Assert.Equal(["fim"], erros.Keys)`, 24 meses exatos aceitos. `API.T/Integracao/Painel/KpisEndpointTestes.cs:160-163`: `BadRequest`, `codigo == "VALIDACAO"`, `erros.fim`. `WEB/cliente/painel/filtro-painel.component.spec.ts:119-120` e `:131-132`: mensagem, e `emitidos.length` não muda | ✅ (⚠️ a spec não define o texto da mensagem) |
| DASH-06 sem vendas: zeros + "Nenhuma venda no período"; nunca sincronizado: "Nenhuma venda sincronizada ainda" | textos exatos da spec | `API.T/Integracao/Painel/KpisEndpointTestes.cs:146-148`: `semVendas == true`, todos `0`. `WEB/cliente/painel/painel.page.spec.ts:156-157`: `toContain('Nenhuma venda no período')`, e os KPIs aparecem. `:164-166`: `toContain('Nenhuma venda sincronizada ainda')`, sem blocos | ✅ |
| DASH-07 só o tenant efetivo | valores de A nunca incluem B | `API.T/Integracao/Painel/ConsultaVendasTestes.cs:48`: `Assert.All(vendas, v => Assert.Equal(a.Id, v.TenantId))`. `API.T/Integracao/IsolamentoTenantsTestes.cs:199`: `faturamento.atual == 375.50` mesmo com uma venda de 9.999 em B | ✅ |
| DASH-08 empresa de outro tenant: 404 | 404 `NAO_ENCONTRADO` | `API.T/Integracao/Painel/ConsultaVendasTestes.cs:81-82`: `Status == 404`, `Codigo == "NAO_ENCONTRADO"`. Casos `?empresas=<B>` das 4 rotas na suíte de isolamento | ✅ |
| DASH-09 anterior + variação (nula se o anterior é 0) | mesmo nº de dias até a véspera; (a−b) ÷ b × 100; `null` se b = 0 | `API.T/Unidade/Painel/FiltroPainelTestes.cs:88-92`: `01/10..10/10`. `API.T/Integracao/Painel/KpisEndpointTestes.cs:75-80`: `275.50`, `100.00`, `87.75`, `33.33`, `null`. `:130-134`: `null`. `WEB/cliente/painel/kpis.component.spec.ts:46,67,76`: `+12,30%`, `-5,00%`, `—` | ✅ |

### P1: Evolução temporal

| Criterion | Spec-defined outcome | `file:line` + assertion | Result |
| --------- | -------------------- | ----------------------- | ------ |
| TRD-01 até 62 dias: por dia (SP), com zeros | 10 dias com vendas em 3: 10 pontos, 7 zeros | `API.T/Integracao/Painel/EvolucaoEndpointTestes.cs:62-65`: `"dia"`, 10 datas, `[15.25,0,0,20,0,0,0,0,0,30]`, `7` zeros. Fuso em `:76-78`. Limite em `API.T/Unidade/Painel/FiltroPainelTestes.cs:96-105` (62 → Dia) | ✅ |
| TRD-02 mais de 62 dias: por mês, meses vazios zerados | mensal | `API.T/Integracao/Painel/EvolucaoEndpointTestes.cs:94-95`: `"mes"`, agosto `0`. `API.T/Unidade/Painel/FiltroPainelTestes.cs:97` (63 → Mes) | ✅ |
| TRD-03 série anterior alinhada ponto a ponto | mesmo nº de pontos, por posição | `API.T/Integracao/Painel/EvolucaoEndpointTestes.cs:107-109`: `10` pontos, `anterior == [0,0,0,0,60,0,0,0,0,40]` | ✅ |
| TRD-04 cursor ou toque: data, atual e anterior | tooltip com os 3 | `WEB/cliente/painel/evolucao.component.spec.ts:73-75`: `'02/10/2026<br/>Atual R$ 0,00<br/>Anterior R$ 1.234,56'`. `:87`: `click` + `mousemove`. W9 (atual e anterior trocados) morreu | ✅ |

### P1: Interface por perfil

| Criterion | Spec-defined outcome | `file:line` + assertion | Result |
| --------- | -------------------- | ----------------------- | ------ |
| UI-01 Consulta | Painel, Meu perfil | `WEB/shared/layout/shell.component.spec.ts:62-65`: `toEqual([{Painel,/cliente},{Meu perfil,/conta/perfil}])`. `E2E/painel.spec.ts:183` | ✅ |
| UI-02 Admin | painel, usuários, empresas, sincronização | `WEB/shared/layout/shell.component.spec.ts:71-77` | ✅ |
| UI-03 SuperAdmin | Tenants, Coletores, Auditoria + seletor | `WEB/shared/layout/shell.component.spec.ts:83-89` e `:95-103`. Edge case (sem tenant, nada consultado): `WEB/cliente/painel/painel.page.spec.ts:197-202` | ✅ |
| UI-04 rota não permitida: página inicial + "Acesso não permitido" | texto exato | `WEB/core/auth/perfil.guard.spec.ts:39-40`: `{ url: '/cliente', aviso: NEGADO }`. `WEB/shared/layout/shell.component.spec.ts:114`. `E2E/painel.spec.ts:187-188` | ✅ |
| UI-05 401: renova uma vez; se falhar, login | uma renovação, depois `/conta/login` | `WEB/cliente/painel/painel.service.spec.ts:107-108`: `Bearer token-novo`. `WEB/core/auth/auth.interceptor.spec.ts:108`: `navigateByUrl('/conta/login')` | ✅ |
| UI-06 Design System | componentes, cores, tipografia e espaçamentos do DS | `WEB/cliente/painel/graficos/tema-graficos.spec.ts:21-35`: tokens e fontes. **Eixo Y pt-BR** em `WEB/cliente/painel/evolucao.component.spec.ts:81`: `[0,2500,1234567,1500.5].map(formatter)` dá `['0','2.500','1.234.567','1.500,5']` (W7 morreu). **Contraste** em `WEB/cliente/painel/kpis.component.spec.ts:57-59`: `--kpi-sucesso-texto == '#2e7d32'`, `.variacao.sucesso` usa esse token, e `contraste >= 4.5` (recalculei: 5,13:1; W8 morreu). `--sucesso` (`src/Jamsoft.DashBoards.Web/src/styles/_tokens.scss:35`, `#388e3c`) não mudou, como o usuário decidiu | ✅ nas 2 divergências da rodada 1 · ⚠️ "seguir o DS" segue sem critério verificável completo |
| UI-07 carregando por bloco | só no bloco pendente | `WEB/cliente/painel/painel.page.spec.ts:100-108`: `'Carregando...'` em cada bloco, que some só no bloco que respondeu | ✅ |
| UI-08 falha: erro + "Tentar novamente" só no bloco | os outros blocos seguem | `WEB/cliente/painel/painel.page.spec.ts:121-126` e `:142-143`: `'Tentar novamente'` só em rankings, `expectNone` nos outros 3 | ✅ |

### P2: Rankings

| Criterion | Spec-defined outcome | `file:line` + assertion | Result |
| --------- | -------------------- | ----------------------- | ------ |
| RNK-01 top 10 | 10 itens, ordem decrescente | `API.T/Integracao/Painel/RankingsEndpointTestes.cs:64-65`: `Vendedor 12..03`, `120..30`. `:135`: `Count == 10` | ✅ |
| RNK-02 nome, faturamento, nº e participação | campos exatos | `API.T/Integracao/Painel/RankingsEndpointTestes.cs:77-84`: `Item("Ana",300.00,2,79.89)`, `Item("Filial",225.50,2,60.05)` etc. `WEB/cliente/painel/rankings.component.spec.ts:36-40` | ✅ |
| RNK-03 sem vendedor ou cliente: "Não informado" | item único | `API.T/Integracao/Painel/RankingsEndpointTestes.cs:98-99`: `[Item("Não informado",50.00,2,100.00)]` | ✅ |
| RNK-04 empate: por nome; no corte do top 10, menor código entra | nome crescente; corte por código crescente | Ordem: `API.T/Integracao/Painel/RankingsEndpointTestes.cs:114`: `["Zélia","Álvaro","Bruna","Carlos"]` (A6 morreu na rodada 1). **Corte**: `:135-138`, com 10º e 11º empatados em 50. Código 20 "Zeca" e código 21 "Aaron" (pelo nome, Aaron entraria). Espera-se `vendedores[^1] == Item("Zeca",50.00,1,3.25)` (50 ÷ 1540 = 3,2467) e `DoesNotContain "Aaron"`. Recalculei: 200+…+120 = 1440. A11 (`ThenByDescending`) morreu | ✅ (gap 5 fechado) |
| RNK-05 tenant com 1 empresa oculta o ranking de empresas | sem tabela Empresas | **Página**: `WEB/cliente/painel/painel.page.spec.ts:182-183`. `/api/empresas` devolve 1 empresa e a resposta traz `empresas` preenchido; `.bloco-conteudo` existe e `[data-ranking=empresas]` é `null`. `:190`: com 2 empresas, existe. Componente: `WEB/cliente/painel/rankings.component.spec.ts:50-54`. W5 morreu | ✅ (gap 2 fechado) |

### P2: NF-e × NFC-e

| Criterion | Spec-defined outcome | `file:line` + assertion | Result |
| --------- | -------------------- | ----------------------- | ------ |
| DFE-01 por tipo: faturamento, nº e participação **por faturamento** | 300 + 100 dá 75%/25% (por quantidade seriam 50/50); 3 + 7 de mesmo valor dá 30/70 | `API.T/Integracao/Painel/DfeEndpointTestes.cs:72`: `[Tipo("NF-e",300.00,1,75.00), Tipo("NFC-e",100.00,1,25.00)]`, exatamente o Independent Test da spec. `:58`: 30/70. `WEB/cliente/painel/dfe.component.spec.ts:49-50`. A10 (base por quantidade) morreu em `:72` e `:96` | ✅ (gap 4 fechado) |
| DFE-02 tipo sem vendas: zeros | NF-e `0/0/0` | `API.T/Integracao/Painel/DfeEndpointTestes.cs:85`: `Tipo("NF-e",0m,0,0m)`. `WEB/cliente/painel/dfe.component.spec.ts:61` | ✅ |

### P2: PWA e responsividade

| Criterion | Spec-defined outcome | `file:line` + assertion | Result |
| --------- | -------------------- | ----------------------- | ------ |
| PWA-01 manifest + SW | "JAMSOFT Dashboards", ícones 192 e 512, `standalone` | `WEB/core/pwa/configuracao-pwa.spec.ts:38-40` e `:49-51` (dimensão real do PNG) | ✅ |
| PWA-02 SW só com estáticos | sem `dataGroups` e sem `/api` | `WEB/core/pwa/configuracao-pwa.spec.ts:57` e `:64` | ✅ |
| PWA-03 sem conexão | "Sem conexão", sem consultas | `WEB/shared/layout/shell.component.spec.ts:123`. `WEB/cliente/painel/painel.page.spec.ts:217-223` | ✅ |
| PWA-04 nova versão | "Nova versão disponível" + recarregar | `WEB/core/pwa/atualizacao.service.spec.ts:45-46` e `:57-58`: `activateUpdate` antes de `recarregar` | ✅ |
| PWA-05 sem rolagem horizontal de 360 a 1920 px | `scrollWidth <= clientWidth` | `E2E/painel.spec.ts:120` (chamado nas linhas `:196` e `:203`) | ✅ (e2e não rodado nesta rodada; ver Gates) |
| PWA-06 duas últimas versões de Chrome, Edge, Firefox e Safari | 4 navegadores × 2 versões | `E2E/painel.spec.ts:209-212`: smoke em Chromium, Firefox e WebKit, só na versão empacotada de cada um | ⚠️ Spec-precision gap (aguarda decisão do usuário) |
| PWA-07 logout limpa | nenhuma chave `jamsoft.` | `WEB/core/auth/auth.service.spec.ts:126-128`: `toEqual([])`, `selecionado()` nulo | ✅ |

### Edge cases da spec

- [x] Nunca sincronizado: `WEB/cliente/painel/painel.page.spec.ts:164`
- [x] SuperAdmin sem tenant vê o seletor: `WEB/cliente/painel/painel.page.spec.ts:197-202`
- [x] Empresa renomeada: o filtro usa `IdPublico` (`src/Jamsoft.DashBoards.Api/Modulos/Painel/ConsultaVendas.cs:34`). O nome manual está em `API.T/Integracao/Painel/RankingsEndpointTestes.cs:152`
- [x] Anterior antes da primeira venda dá variação nula: `API.T/Integracao/Painel/KpisEndpointTestes.cs:130-134`
- [x] Sessão expirada em segundo plano segue UI-05: `WEB/core/auth/auth.interceptor.spec.ts:108`
- [ ] Período que inclui hoje mostra dados até a última sincronização (STS-02): sem teste nesta feature, porque depende do status de `sincronizacao-vendas`. Igual à rodada 1

**Status**: 35/35 ACs com evidência `file:line`. 32 conferem com o resultado preciso da spec. 3 são spec-precision gaps (UI-06 no sentido amplo, PWA-06, texto de DASH-05).

---

## Gate Check

| Gate | Comando | Retorno | Resultado |
| ---- | ------- | ------- | --------- |
| API (Full) | `TESTES_SQLSERVER="Server=localhost;Integrated Security=true;TrustServerCertificate=true;Connect Timeout=60" dotnet test tests/Jamsoft.DashBoards.Api.Testes` | `exit=0` | 403 aprovados, 0 falhas, 0 ignorados, em 1 min 47 s. Nenhum timeout de SQL Server, sem repetição |
| Web (Quick-Web) | `npm --prefix src/Jamsoft.DashBoards.Web test -- --watch=false` | `exit=0` | 34 arquivos, 226 aprovados, 0 falhas |
| Lint web (parte do Build) | `npm --prefix src/Jamsoft.DashBoards.Web run lint` | `exit=0` | "All files pass linting." |

- Integridade das contagens: API foi de 397 para 403 (+6: 4 testes de filtro de empresa, 1 de base do DF-e, 1 de corte do top 10). Web foi de 222 para 226 (+4: eixo, contraste e 2 de RNK-05). Nenhum teste foi removido nem enfraquecido; o diff dos testes só adiciona casos e um parâmetro opcional `extra`.
- Não rodados nesta rodada: o e2e (Full-Web) e o resto do gate Build (`dotnet build -warnaserror`, `dotnet format`, build Angular). As correções só mexem em testes, em um `formatter` e em uma variável CSS; o e2e passou 8/8 na rodada 1.

---

## Discrimination Sensor

Tudo rodou num worktree descartável, `git worktree add --detach <scratchpad>/verif-wt2 2e474c7`, com `npm ci` próprio. Cada mutação foi aplicada com `sed`, testada e revertida com `git checkout --`, e o `git status --porcelain` do worktree voltou vazio depois de cada uma. As mutações de API rodaram com `--filter "FullyQualifiedName~Painel|FullyQualifiedName~Isolamento"` (82 testes) e as da web com a suíte inteira (226).

| # | Arquivo:linha | Mutação | Testes que falharam | Status |
| - | ------------- | ------- | ------------------- | ------ |
| A9 (reaplicado) | `src/Jamsoft.DashBoards.Api/Modulos/Painel/PainelEndpoints.cs:53` | Resolve as empresas (o 404 continua), mas passa `EmpresaIds = null` para as consultas | 4: `Filtro_com_uma_empresa_restringe_*` de KPIs, Evolução, Rankings e DF-e | ✅ Morto |
| A10 | `src/Jamsoft.DashBoards.Api/Modulos/Painel/PainelEndpoints.cs:236` | Participação do DF-e por quantidade: `vendas / Σvendas × 100` | 2: `Participacao_e_por_faturamento_e_nao_por_quantidade_de_vendas`, `Filtro_com_uma_empresa_restringe_o_DFe_as_vendas_dela` | ✅ Morto |
| A11 | `src/Jamsoft.DashBoards.Api/Modulos/Painel/PainelEndpoints.cs:200` | Desempate do corte `ThenBy(g => g.Codigo)` trocado por `ThenByDescending` | 1: `Empate_no_corte_do_top_10_entra_o_de_menor_codigo` | ✅ Morto |
| A12 | `src/Jamsoft.DashBoards.Api/Modulos/Painel/PainelEndpoints.cs:82` | % canceladas com o denominador errado: `canc × 100 ÷ efetivadas` | 2: `Cada_KPI_e_igual_ao_valor_calculado_a_mao_*`, `Periodo_alterado_recalcula_*` | ✅ Morto |
| A13 | `src/Jamsoft.DashBoards.Api/Modulos/Painel/PainelEndpoints.cs:227` | DF-e deixa Contingência de fora das efetivadas | 3: `Tres_NFe_e_sete_NFCe_*`, `Valores_do_painel_de_A_nunca_incluem_vendas_de_B` (Admin e Consulta) | ✅ Morto |
| W5 (reaplicado) | `src/Jamsoft.DashBoards.Web/src/app/cliente/painel/painel.page.ts:155` | `[mostrarEmpresas]="true"` | 1: `tenant com uma única empresa não mostra o ranking de empresas (RNK-05)` | ✅ Morto |
| W7 | `src/Jamsoft.DashBoards.Web/src/app/cliente/painel/evolucao.component.ts:73` | `formatter` do eixo Y removido (volta ao padrão do ECharts) | 1: `eixo de valores usa o separador de milhar pt-BR (2.500)` | ✅ Morto |
| W8 | `src/Jamsoft.DashBoards.Web/src/app/cliente/painel/kpis.component.ts:82` | `.sucesso { color: var(--sucesso) }` (cor anterior) | 1: `variação positiva usa o verde de texto do painel, com contraste ≥ 4,5:1 …` | ✅ Morto |
| W9 | `src/Jamsoft.DashBoards.Web/src/app/cliente/painel/evolucao.component.ts:85` | Tooltip com atual e anterior trocados | 1: `tooltip por eixo devolve a data, "Atual R$ …" e "Anterior R$ …"` | ✅ Morto |

**Sensor depth**: expandido (≥ 5 mutações; o caminho de isolamento e integridade dos números é crítico).
**Sensor total**: 9 injetadas, 9 mortas, 0 sobreviventes.

Limpeza: `git worktree remove --force` falhou com "Filename too long" (por causa do `node_modules`). A pasta foi apagada com o prefixo `\\?\` e depois rodei `git worktree prune`. A pasta `verif-wt2` não existe mais. O `git status --porcelain` da árvore real ficou igual à linha de base: `?? .specs/features/painel-vendas/validation.md` e `?? src/Jamsoft.DashBoards.Coletor/FormLogin.resx`. Observação: `git worktree list` ainda mostra um worktree de outra sessão (`…/7bb5943e-…/scratchpad/verif-wt`, em `188b9e3`). Ele não foi criado por esta verificação e não foi mexido.

---

## Gaps da rodada 1: status

| # | Gap | Status | Evidência própria |
| - | --- | ------ | ----------------- |
| 1 | DASH-04 sem teste nas 4 rotas (A9 sobrevivia) | ✅ Fechado | 4 testes novos com valores conferidos à mão (tabela DASH-04). A9 morreu nos 4 |
| 2 | RNK-05 sem teste da ligação na página (W5 sobrevivia) | ✅ Fechado | `WEB/cliente/painel/painel.page.spec.ts:178-190`, com 1 e com 2 empresas. W5 morreu |
| 3 | UI-06: eixo en-US e contraste de 4,11:1 | ✅ Fechado | Formatter pt-BR (`evolucao.component.ts:73`, teste `:81`, W7 morreu). Verde `#2e7d32` só no painel (`kpis.component.ts:43,82`, teste `:57-59`, 5,13:1, W8 morreu). `--sucesso` sem mudança, como o usuário decidiu |
| 4 | DFE-01: base da participação | ✅ Fechado | A spec agora diz "por faturamento". O teste com 300/100 dá 75/25 (`DfeEndpointTestes.cs:72`). A10 morreu |
| 5 | RNK-04: empate no corte do top 10 | ✅ Fechado | A spec agora diz "código crescente". O teste Zeca(20) × Aaron(21) distingue nome de código (`RankingsEndpointTestes.cs:137-138`). A11 morreu |
| 6 | PWA-06 "duas últimas versões" | ⚠️ Aberto como spec-precision gap | Nenhuma piora: o smoke cobre os 3 motores (`E2E/painel.spec.ts:209-212`, `playwright.config.ts:24-26`). Falta a decisão do usuário |
| 7 | p95 em 12 e 24 meses acima de 2 s | ⏭️ Fora do veredito | AD-019 (`.specs/STATE.md:149-155`) adia a otimização para outra feature. O Goal do mês corrente (p95 de 115 ms) é atendido |

---

## Spec-precision gaps restantes

1. **PWA-06**: falta definir como "duas últimas versões de Chrome, Edge, Firefox e Safari" é verificado. Hoje há um smoke por motor (Chromium, Firefox e WebKit empacotados).
2. **UI-06** (sentido amplo): "seguir o DS em todas as telas" não tem critério verificável completo. As duas divergências concretas da rodada 1 foram corrigidas e testadas.
3. **DASH-05**: a spec não define o texto da mensagem de validação. O teste confere o texto da implementação.

## Desvios de documentação (não bloqueiam)

- `.specs/features/painel-vendas/medicao.md:89,105,107` ainda listam o eixo en-US e o contraste de 4,11:1 como ❌ e "a decidir", mas `d430d2a` e `bcf8457` já corrigiram os dois.
- A decisão "verde escurecido só no painel, `--sucesso` sem mudança" está registrada só no comentário de `kpis.component.ts:41-42`, sem AD no `.specs/STATE.md`. A cor `#2e7d32` é um literal local, não um token do DS.
- A tabela de rastreabilidade do `spec.md` continua com "In Tasks" em todos os requisitos (atualização proposta abaixo; o verificador não altera a spec).

---

## Code Quality

| Principle | Status |
| --------- | ------ |
| Minimum code / surgical changes | ✅ As correções mexem só no formatter, em uma variável CSS e em testes |
| No scope creep | ✅ |
| Matches patterns | ✅ Mesmos helpers (`VendasSemeadas`, `abrir()`, `AssertIndicador`) |
| Spec-anchored outcome check | ✅ 32/35 precisos, ⚠️ 3 spec-precision gaps |
| Per-layer coverage (rotas com happy, edge e erro) | ✅ As 4 rotas agora testam valores com `empresas=`. Observação menor: o 400 de validação é testado só em `/kpis` (as 4 rotas usam o mesmo `PrepararAsync`) |
| Every test maps to a requirement | ✅ |
| Documented guidelines | none - strong defaults applied |

---

## Requirement Traceability Update (proposta; o verificador não altera `spec.md`)

| Requirement | New Status |
| ----------- | ---------- |
| DASH-01..09 | ✅ Verified |
| TRD-01..04 | ✅ Verified |
| UI-01..05, UI-07, UI-08 | ✅ Verified |
| UI-06 | ✅ Verified (⚠️ critério amplo do DS) |
| RNK-01..05 | ✅ Verified |
| DFE-01, DFE-02 | ✅ Verified |
| PWA-01..05, PWA-07 | ✅ Verified |
| PWA-06 | ⚠️ Verified parcialmente (Chromium, Firefox e WebKit atuais; falta a decisão do usuário) |

---

## Summary

**Overall**: ✅ Ready

**Spec-anchored check**: 35/35 ACs com evidência, 32 com resultado preciso da spec, 3 spec-precision gaps (PWA-06, UI-06 amplo, texto de DASH-05).
**Sensor**: 9 injetadas, 9 mortas, 0 sobreviventes (A9 e W5, os sobreviventes da rodada 1, agora morrem).
**Gate**: API 403/403 (`exit=0`), Web 226/226 (`exit=0`), lint `exit=0`.

**Next steps**: o usuário decide PWA-06. Atualizar `medicao.md:89,105,107` e registrar a decisão do verde num AD. O desempenho de períodos longos segue para a feature da AD-019.
