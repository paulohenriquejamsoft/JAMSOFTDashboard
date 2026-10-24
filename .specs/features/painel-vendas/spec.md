# Painel de Vendas — Indicadores e PWA — Specification

**Projeto:** JAMSOFT Dashboards · **Escopo:** Large · **Data:** 2026-10-24
**Decisões de projeto aplicáveis:** AD-001, AD-003, AD-004, AD-008 (`.specs/STATE.md`)
**Depende de:** `plataforma-tenancy-acesso` (login, perfis, empresas) e `sincronizacao-vendas` (dados)

## Problem Statement

Os clientes da JAMSOFT não têm uma visão consolidada e acessível das próprias vendas fora do ERP. Com as vendas centralizadas na plataforma, é preciso apresentá-las num painel claro, responsivo e instalável (PWA), mostrando a cada usuário apenas o que seu perfil e tenant permitem.

## Goals

- [ ] Usuário vê os KPIs do mês corrente em até 2 segundos após o login (p95, tenant com 1 milhão de vendas).
- [ ] Painel utilizável em celular (largura ≥ 360 px) e instalável como PWA.
- [ ] 100% dos números do painel conferem com consulta SQL de referência sobre os mesmos dados.

## Out of Scope

| Feature | Reason |
| ------- | ------ |
| Exportação (PDF/Excel/CSV) | Não pedido |
| Indicadores de outras rotinas (produtos, financeiro, estoque) | Dependem de novas rotinas de sincronização |
| Modo offline com dados em cache | PWA no MVP cobre instalação e shell; dados exigem conexão |
| Painéis personalizáveis pelo usuário (arrastar widgets) | Não pedido |
| Metas de vendas e alertas por e-mail/push | Não pedido |
| Visão consolidada de vários tenants ao mesmo tempo | SuperAdmin vê um tenant por vez (CTX-01) |

---

## Assumptions & Open Questions

| Assumption / decision | Chosen default | Rationale | Confirmed? |
| --------------------- | -------------- | --------- | ---------- |
| Indicadores do MVP | KPIs gerais, evolução temporal, rankings, NF-e × NFC-e | Escolha do usuário | y |
| Base do faturamento | Soma de `valor` apenas de vendas "Finalizada" | Canceladas e inutilizadas não são receita | n |
| Ticket médio | Faturamento ÷ nº de vendas "Finalizada"; 0 quando não há vendas | Definição usual | n |
| Itens por venda | Soma de `qtd_itens` ÷ nº de vendas "Finalizada" | Coerente com ticket médio | n |
| % canceladas | Nº "Cancelada" ÷ (nº "Finalizada" + nº "Cancelada") × 100 | Inutilizadas não são vendas emitidas | n |
| Data de referência dos indicadores | `data_hora` da venda (campo `dataentrega` do ERP) | É a data trafegada no contrato | n |
| Fuso horário | America/Sao_Paulo para agrupar por dia/mês | Clientes no Brasil | n |
| Período padrão | Mês corrente (dia 1 até hoje) | Uso mais comum | n |
| Período máximo por consulta | 24 meses | Limita custo de consulta | n |
| Granularidade da série | Diária quando o período tem ≤ 62 dias; mensal acima disso | Legibilidade do gráfico | n |
| Período anterior para comparação | Mesmo número de dias imediatamente antes do início do período | Comparação simples e previsível | n |
| Tamanho dos rankings | Top 10 | Cabe em tela de celular | n |
| Vendas sem vendedor/cliente | Agrupadas como "Não informado" nos rankings | Não descartar receita | n |
| Formato | Moeda BRL (R$ 1.234,56), datas dd/MM/yyyy, pt-BR | Público brasileiro | n |
| Estratégia de consulta | Consultas agregadas sobre a tabela de vendas com índices (`TENANT_ID`, `DATA_HORA`); tabelas de agregados pré-calculados só se a meta de 2 s não for atingida (decisão de Design) | Evita complexidade prematura | n |
| Atualização dos dados na tela | Sob demanda (ao abrir/alterar filtro) e botão "Atualizar"; sem atualização automática | Dados mudam no ritmo do Coletor (≥ 5 min) | n |

**Open questions:** none - all resolved or logged above.

---

## User Stories

### P1: KPIs do período ⭐ MVP

**User Story**: Como usuário de um tenant, quero ver faturamento, número de vendas, ticket médio, itens por venda e percentual de cancelamento no período, para acompanhar o desempenho da empresa.

**Why P1**: É o núcleo do produto.

**Acceptance Criteria**:

1. WHEN um usuário autenticado abre o painel THEN o sistema SHALL exibir os KPIs do mês corrente para todas as empresas do tenant. (DASH-01)
2. The API SHALL calcular faturamento, nº de vendas, ticket médio, itens por venda e % canceladas conforme as fórmulas da seção Assumptions. (DASH-02)
3. WHEN o usuário altera o período (data inicial e final, inclusivas) THEN o sistema SHALL recalcular todos os indicadores da tela para o novo período. (DASH-03)
4. WHEN o usuário seleciona uma ou mais empresas do tenant THEN o sistema SHALL restringir todos os indicadores da tela a essas empresas. (DASH-04)
5. IF a data final é anterior à inicial ou o período excede 24 meses THEN a API SHALL responder 400 e o frontend SHALL exibir a mensagem de validação sem consultar. (DASH-05)
6. IF não há vendas no período THEN o sistema SHALL exibir os KPIs com valor zero e a mensagem "Nenhuma venda no período". (DASH-06)
7. The API SHALL calcular os indicadores apenas com vendas do tenant efetivo do token (ou do tenant selecionado pelo SuperAdmin). (DASH-07)
8. IF a seleção de empresas contém empresa de outro tenant THEN a API SHALL responder 404. (DASH-08)
9. WHEN a API responde os KPIs THEN cada KPI SHALL trazer também o valor do período anterior equivalente e a variação percentual (nula quando o anterior é zero). (DASH-09)

**Independent Test**: Com um conjunto fixo de vendas semeadas (finalizadas, canceladas, inutilizadas em 2 empresas), conferir cada KPI contra valores calculados à mão.

---

### P1: Evolução temporal ⭐ MVP

**User Story**: Como usuário, quero ver o faturamento ao longo do tempo comparado ao período anterior, para identificar tendências.

**Why P1**: Escolhido pelo usuário como parte do MVP.

**Acceptance Criteria**:

1. WHEN o período selecionado tem até 62 dias THEN a API SHALL retornar o faturamento agrupado por dia (fuso America/Sao_Paulo), incluindo dias sem vendas com valor zero. (TRD-01)
2. WHEN o período selecionado tem mais de 62 dias THEN a API SHALL retornar o faturamento agrupado por mês, incluindo meses sem vendas com valor zero. (TRD-02)
3. The API SHALL retornar junto a série do período anterior equivalente, alinhada ponto a ponto. (TRD-03)
4. WHEN o usuário passa o cursor ou toca num ponto do gráfico THEN o frontend SHALL exibir data, faturamento do período atual e do anterior. (TRD-04)

**Independent Test**: Período de 10 dias com vendas em 3 dias: série tem 10 pontos, 7 com zero.

---

### P1: Interface por perfil ⭐ MVP

**User Story**: Como usuário, quero ver apenas os menus e ações permitidos ao meu perfil, para uma experiência simples e segura.

**Why P1**: §6.3 exige UI adaptada ao nível de acesso.

**Acceptance Criteria**:

1. WHEN um `Consulta` faz login THEN o frontend SHALL exibir apenas o painel e o perfil do usuário. (UI-01)
2. WHEN um `Admin` faz login THEN o frontend SHALL exibir painel, usuários, empresas e status de sincronização do próprio tenant. (UI-02)
3. WHEN um `SuperAdmin` faz login THEN o frontend SHALL exibir a área administrativa (tenants, coletores, auditoria) e um seletor de tenant para abrir os painéis. (UI-03)
4. IF o usuário navega diretamente para uma rota não permitida ao seu perfil THEN o frontend SHALL redirecionar para a página inicial do perfil com a mensagem "Acesso não permitido". (UI-04)
5. IF a API responde 401 THEN o frontend SHALL tentar renovar o token uma vez e, falhando, SHALL redirecionar para o login. (UI-05)
6. The frontend SHALL seguir os componentes, cores, tipografia e espaçamentos do Design System do projeto em todas as telas. (UI-06)
7. WHILE uma consulta está em andamento o frontend SHALL exibir estado de carregamento nos blocos afetados. (UI-07)
8. IF uma consulta falha THEN o frontend SHALL exibir mensagem de erro no bloco afetado com ação "Tentar novamente", sem derrubar os demais blocos. (UI-08)

**Independent Test**: Logar com os três perfis e verificar menus; acessar URL de admin como Consulta e ser redirecionado.

---

### P2: Rankings

**User Story**: Como gestor, quero ver os principais vendedores, clientes e empresas por faturamento, para reconhecer destaques e oportunidades.

**Why P2**: Escolhido para o MVP, mas depende dos KPIs e não os bloqueia.

**Acceptance Criteria**:

1. WHEN o usuário abre os rankings THEN a API SHALL retornar o top 10 de vendedores, de clientes e de empresas por faturamento no período e filtros selecionados. (RNK-01)
2. The cada item do ranking SHALL conter nome, faturamento, nº de vendas e participação % no faturamento total do período. (RNK-02)
3. WHEN vendas não têm vendedor ou cliente informado THEN a API SHALL agrupá-las num item "Não informado". (RNK-03)
4. WHEN dois itens têm o mesmo faturamento THEN a API SHALL ordená-los por nome em ordem alfabética. (RNK-04)
5. WHERE o tenant tem uma única empresa o frontend SHALL ocultar o ranking de empresas. (RNK-05)

**Independent Test**: Dados semeados com 12 vendedores: ranking retorna 10, ordenados, com participação somando ≤ 100%.

---

### P2: NF-e × NFC-e

**User Story**: Como gestor, quero ver a divisão do faturamento entre NF-e e NFC-e, para entender o peso do varejo e do atacado.

**Why P2**: Escolhido para o MVP; bloco independente.

**Acceptance Criteria**:

1. WHEN o usuário abre o painel THEN a API SHALL retornar, por tipo de DF-e, faturamento, nº de vendas e participação % no período e filtros selecionados. (DFE-01)
2. IF um dos tipos não tem vendas no período THEN a API SHALL retorná-lo com valores zero. (DFE-02)

**Independent Test**: 3 NF-e e 7 NFC-e semeadas: participação por quantidade 30%/70%.

---

### P2: PWA e responsividade

**User Story**: Como usuário móvel, quero instalar o painel no celular e usá-lo em qualquer tamanho de tela.

**Why P2**: §2 e §6.3 pedem PWA; o painel funciona no navegador sem isso.

**Acceptance Criteria**:

1. The frontend SHALL fornecer web app manifest (nome "JAMSOFT Dashboards", ícones 192 e 512 px, `display: standalone`) e service worker, atendendo aos critérios de instalabilidade dos navegadores Chromium. (PWA-01)
2. The service worker SHALL armazenar em cache apenas os arquivos estáticos da aplicação, nunca respostas da API com dados de tenant. (PWA-02)
3. IF o dispositivo está sem conexão THEN o frontend SHALL exibir a mensagem "Sem conexão" em vez dos dados. (PWA-03)
4. WHEN uma nova versão é publicada THEN o frontend SHALL avisar "Nova versão disponível" com ação para recarregar. (PWA-04)
5. The layout SHALL funcionar sem rolagem horizontal em larguras de 360 px a 1920 px. (PWA-05)
6. The frontend SHALL funcionar nas duas últimas versões de Chrome, Edge, Firefox e Safari. (PWA-06)
7. WHEN o usuário faz logout THEN o frontend SHALL remover tokens e qualquer dado de tenant do armazenamento local. (PWA-07)

**Independent Test**: Lighthouse marca o app como instalável; instalar no Android, abrir em modo avião e ver "Sem conexão".

---

## Edge Cases

- IF o tenant ainda não recebeu nenhuma sincronização THEN o painel SHALL exibir "Nenhuma venda sincronizada ainda" em vez de KPIs zerados. (DASH-06)
- WHEN o período inclui o dia de hoje THEN os indicadores SHALL refletir os dados recebidos até a última sincronização (exibida via STS-02).
- IF o SuperAdmin não selecionou tenant THEN o frontend SHALL exibir o seletor de tenant em vez do painel. (UI-03)
- IF uma empresa selecionada no filtro é renomeada THEN o filtro SHALL continuar válido, pois usa o `ID_PUBLICO` da empresa. (DASH-04)
- WHEN o período anterior equivalente começa antes da primeira venda do tenant THEN a variação SHALL ser nula quando o valor anterior for zero. (DASH-09)
- IF a sessão expira com o app aberto em segundo plano THEN, ao voltar, o frontend SHALL seguir UI-05.

---

## Implicit-Requirement Dimensions Sweep

| Dimension | Coverage |
| --------- | -------- |
| Input validation & bounds | DASH-05 (período), DASH-08 (empresas), top 10 (RNK-01) |
| Failure / partial-failure states | UI-08 (falha por bloco), PWA-03 |
| Idempotency / retry / duplicate handling | N/A because o painel é somente leitura; UI-08 oferece nova tentativa sem efeito colateral |
| Auth boundaries & rate limits | DASH-07, DASH-08, UI-01..UI-05; rate limit N/A because consultas são autenticadas e de baixo volume por usuário no MVP |
| Concurrency / ordering | RNK-04 (desempate determinístico); demais N/A because leituras não concorrem entre si |
| Data lifecycle / expiry | PWA-02, PWA-07 (nada de tenant persiste no dispositivo) |
| Observability | Tempo de resposta das consultas de painel registrado na API para medir a meta de 2 s (Success Criteria) |
| External-dependency failure | PWA-03 (sem conexão), UI-05 (falha de autenticação) |
| State-transition integrity | N/A because o painel não altera estado de domínio |

---

## Requirement Traceability

| Requirement ID | Story | Phase | Status |
| -------------- | ----- | ----- | ------ |
| DASH-01 | P1: KPIs do período | - | Pending |
| DASH-02 | P1: KPIs do período | - | Pending |
| DASH-03 | P1: KPIs do período | - | Pending |
| DASH-04 | P1: KPIs do período | - | Pending |
| DASH-05 | P1: KPIs do período | - | Pending |
| DASH-06 | P1: KPIs do período | - | Pending |
| DASH-07 | P1: KPIs do período | - | Pending |
| DASH-08 | P1: KPIs do período | - | Pending |
| DASH-09 | P1: KPIs do período | - | Pending |
| TRD-01 | P1: Evolução temporal | - | Pending |
| TRD-02 | P1: Evolução temporal | - | Pending |
| TRD-03 | P1: Evolução temporal | - | Pending |
| TRD-04 | P1: Evolução temporal | - | Pending |
| UI-01 | P1: Interface por perfil | - | Pending |
| UI-02 | P1: Interface por perfil | - | Pending |
| UI-03 | P1: Interface por perfil | - | Pending |
| UI-04 | P1: Interface por perfil | - | Pending |
| UI-05 | P1: Interface por perfil | - | Pending |
| UI-06 | P1: Interface por perfil | - | Pending |
| UI-07 | P1: Interface por perfil | - | Pending |
| UI-08 | P1: Interface por perfil | - | Pending |
| RNK-01 | P2: Rankings | - | Pending |
| RNK-02 | P2: Rankings | - | Pending |
| RNK-03 | P2: Rankings | - | Pending |
| RNK-04 | P2: Rankings | - | Pending |
| RNK-05 | P2: Rankings | - | Pending |
| DFE-01 | P2: NF-e × NFC-e | - | Pending |
| DFE-02 | P2: NF-e × NFC-e | - | Pending |
| PWA-01 | P2: PWA e responsividade | - | Pending |
| PWA-02 | P2: PWA e responsividade | - | Pending |
| PWA-03 | P2: PWA e responsividade | - | Pending |
| PWA-04 | P2: PWA e responsividade | - | Pending |
| PWA-05 | P2: PWA e responsividade | - | Pending |
| PWA-06 | P2: PWA e responsividade | - | Pending |
| PWA-07 | P2: PWA e responsividade | - | Pending |

**Coverage:** 35 total, 0 mapped to tasks, 35 unmapped ⚠️ (esperado antes de Tasks)

---

## Cobertura do documento original

| Seção original | Requisitos |
| -------------- | ---------- |
| §2 Objetivos (painel, PWA) | DASH-*, PWA-* |
| §3.1 Design System | UI-06, AD-008 |
| §4.1 Frontend (responsabilidades) | UI-*, PWA-*, DASH-* |
| §6.2 Área do cliente (visualizar métricas) | DASH-*, TRD-*, RNK-*, DFE-* |
| §6.3 Experiência de uso | PWA-05, PWA-06, UI-01..UI-04 |
| §8.1 Consulta de indicadores | DASH-*, TRD-*, RNK-*, DFE-* |
| §12.1 Desempenho (evitar consultas desnecessárias) | Assumption de estratégia de consulta, Success Criteria |

---

## Success Criteria

- [ ] p95 de resposta das consultas do painel ≤ 2 s com 1 milhão de vendas no tenant.
- [ ] Todos os indicadores batem com a consulta SQL de referência em um conjunto de dados semeado.
- [ ] Lighthouse PWA: instalável; Acessibilidade ≥ 90.
- [ ] Nenhum dado de tenant encontrado em cache/armazenamento local após logout.
