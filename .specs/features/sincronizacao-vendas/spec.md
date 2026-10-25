# Sincronização de Vendas — Coletor e Ingestão — Specification

**Projeto:** JAMSOFT Dashboards · **Escopo:** Complex · **Data:** 2026-10-24
**Decisões de projeto aplicáveis:** AD-001, AD-002, AD-003, AD-005, AD-006, AD-007 (`.specs/STATE.md`)
**Depende de:** `plataforma-tenancy-acesso` (tenants, empresas, perfis)

## Problem Statement

Os dados de vendas estão nos bancos dos ERPs, dentro dos servidores dos clientes. A plataforma precisa recebê-los de forma contínua, incremental e sem duplicação, a partir de um coletor Windows que atende vários clientes numa única instalação. Falhas de rede, alterações tardias no ERP e reinícios não podem perder nem duplicar vendas.

## Goals

- [ ] Uma instalação do Coletor sincroniza ao menos 10 configurações (bancos/tenants) em paralelo, de forma independente.
- [ ] Carga inicial de 6 meses de vendas concluída sem intervenção manual.
- [ ] Zero vendas duplicadas e zero perdidas após quedas de rede simuladas durante a sincronização.
- [ ] Cancelamento feito no ERP aparece na plataforma em até 1 ciclo de sincronização (dentro da janela de reprocessamento).

## Out of Scope

| Feature | Reason |
| ------- | ------ |
| Rotinas além de vendas (produtos, financeiro, estoque) | Documento define vendas como primeira rotina; arquitetura deve permitir novas rotinas, mas elas não fazem parte desta spec |
| Itens de venda (detalhe de produtos) | Query traz apenas `QtdItens` |
| ERPs com banco diferente de SQL Server | Query e conexão do §11 são SQL Server |
| Exclusão automática de dados na plataforma ao reiniciar sincronização | §10.3 proíbe explicitamente |
| Atualização automática (auto-update) do Coletor | Não pedido |
| Execução do Coletor como Windows Service | Documento define Windows Forms; ver Assumptions |
| Detecção de vendas excluídas fisicamente no ERP | ERP não exclui notas (canceladas mudam de situação); ver Assumptions |

---

## Assumptions & Open Questions

| Assumption / decision | Chosen default | Rationale | Confirmed? |
| --------------------- | -------------- | --------- | ---------- |
| Autenticação do Coletor | `client_id` + `client_secret` por instalação, emitidos por SuperAdmin, com lista de tenants autorizados | Decisão do usuário (AD-005) | y |
| Alterações/cancelamentos | Upsert por (`TENANT_ID`, `COD_EMPRESA`, `VENDA_ID`) + janela de reprocessamento de 7 dias, configurável de 0 a 90 | Decisão do usuário (AD-006) | y |
| Unicidade de `venda_id` | `NumNotaSaida` é a identity da tabela `NotaSaida`: único em todo o banco do ERP. A chave de negócio continua (`TENANT_ID`, `COD_EMPRESA`, `VENDA_ID`) | Confirmado pelo usuário em 2026-10-25; manter `Cod_Empresa` na chave não custa nada e protege contra dois bancos no mesmo tenant | y |
| Origem da query | `c_NotaSaida` é uma **view** sobre a tabela `NotaSaida`; o usuário só de leitura recebe `SELECT` na view e em `NotaSaidaProd`; índices de apoio são criados na tabela base `NotaSaida` | Confirmado pelo usuário em 2026-10-25; índice em view não se aplica aqui | y |
| Dois bancos de ERP no mesmo tenant com mesmo `Cod_Empresa` | Proibido: API rejeita lote (409) se a empresa já recebe dados de outra configuração | Evita colisão de `venda_id` entre bancos | n |
| Coluna de data | Filtro de período e checkpoint por `datafatura` (só data); `data_hora` enviada = `DataHoraFechamento` (data e hora), usada pelo painel | Confirmado pelo usuário em 2026-10-25: `datafatura` não tem hora | y |
| Vendas excluídas fisicamente no ERP | Não tratadas; permanecem na plataforma | ERP cancela (situação 9) em vez de excluir; validar | n |
| Classificação de situação | `situacaonota` 5 e 6 → `Finalizada`; 9 → `Cancelada`; 11 → `Inutilizada`; 12 → `Contingência`. **Somente esses códigos** entram na extração (`situacaonota IN (5,6,9,11,12)`) | Confirmado pelo usuário em 2026-10-25 | y |
| Valor neutro | Somente vendas sem valor neutro: `TotalCondNeutra = 0` | Confirmado pelo usuário em 2026-10-25 | y |
| `QtdItens` | Subconsulta `(SELECT COUNT(*) FROM NotaSaidaProd WHERE NotaSaidaProd.numnotasaida = ns.numnotasaida)` | Confirmado pelo usuário em 2026-10-25 | y |
| `DataHoraFechamento` nula | Se vier nula, o Coletor envia `datafatura` às 00:00 (hora local) e registra aviso no log | Evita rejeitar a venda; o ERP pode não fechar notas antigas ou inutilizadas | n |
| Hospedagem do Coletor | Aplicação Windows Forms que agenda em background enquanto aberta; minimiza para a bandeja e inicia com o Windows | Documento define WinForms; Windows Service fica para evolução | n |
| Armazenamento local das configurações | Arquivo JSON local; senhas de banco e `client_secret` cifrados com DPAPI (escopo máquina) | §9.2 exige armazenamento seguro sem expor em arquivos | n |
| Configurações são locais ou na API? | Locais ao Coletor; a API guarda só o estado de sincronização reportado (última execução, situação) | Credenciais de banco do cliente não saem do servidor do cliente | n |
| Tamanho do lote | 500 vendas por lote | Payload pequeno (~150 KB), confirmação frequente | n |
| Limite de payload da API | 2 MB por requisição; acima disso 413 | Proteção da API | n |
| Intervalo de sincronização | Mínimo 5 min, máximo 24 h, padrão 15 min | Evita carga excessiva no ERP | n |
| Retry de comunicação | 5 tentativas com backoff exponencial (2 s, 4 s, 8 s, 16 s, 32 s); depois a execução termina como "Falha" e aguarda o próximo ciclo | Tolera instabilidade sem travar outras configurações | n |
| Timeout da consulta no ERP | 120 s por lote | Evita locks longos no ERP (§12.1) | n |
| Nível de isolamento da query no ERP | `READ COMMITTED` com consulta paginada por faixas | Evita bloqueios prolongados sem ler dados sujos | n |
| Retenção de logs do Coletor | 30 dias; arquivos mais antigos removidos automaticamente | Diagnóstico suficiente sem encher disco | n |
| Formato dos logs | Um arquivo texto estruturado (JSON por linha) por dia | Filtrável por tenant/banco/rotina na tela de logs | n |
| Fuso horário | Datas do ERP tratadas como horário local America/Sao_Paulo e enviadas com offset | ERP grava hora local | n |
| Situação "Inutilizada" | Enviada e armazenada; excluída dos indicadores de faturamento | Preserva fidelidade ao ERP | n |

**Open questions:** none - all resolved or logged above.

---

## Modelo de dados da venda (contrato)

| Campo | Tipo | Regra |
| ----- | ---- | ----- |
| `venda_id` | INT | Obrigatório, > 0 |
| `tipo_dfe` | VARCHAR(20) | `NF-e` ou `NFC-e` |
| `data_hora` | DATETIMEOFFSET | Obrigatório; vem de `DataHoraFechamento` |
| `valor` | DECIMAL(19,4) (MONEY no ERP) | ≥ 0 |
| `cod_cliente` | INT | Opcional |
| `nome_cliente` | VARCHAR(120) | Opcional, truncado em 120 |
| `vendedor_codigo` | INT | Opcional |
| `vendedor_nome` | VARCHAR(80) | Opcional, truncado em 80 |
| `situacao` | VARCHAR(20) | `Finalizada`, `Cancelada`, `Inutilizada` ou `Contingência` |
| `nome_empresa` | VARCHAR(80) | Obrigatório (o §11.2 indica VARCHAR(20); ampliado para não truncar nomes) |
| `cod_empresa` | INT | Obrigatório, > 0 |
| `qtd_itens` | INT | ≥ 0 |

---

## User Stories

### P1: Credencial do Coletor ⭐ MVP

**User Story**: Como SuperAdmin, quero emitir e revogar credenciais para cada instalação do Coletor, definindo para quais tenants ela pode enviar dados.

**Why P1**: Sem credencial o Coletor não autentica nem envia dados.

**Acceptance Criteria**:

1. WHEN o SuperAdmin cria um Coletor com nome (3–80 caracteres) e ao menos um tenant ativo autorizado THEN a API SHALL gerar `client_id` (GUID) e `client_secret` (mínimo 32 bytes aleatórios) e exibir o secret uma única vez. (COL-01)
2. The API SHALL armazenar apenas o hash do `client_secret`. (COL-02)
3. WHEN o Coletor envia `client_id` e `client_secret` válidos THEN a API SHALL emitir access token com perfil `Coletor` e a lista de `ID_PUBLICO` dos tenants autorizados. (COL-03)
4. IF a credencial é inválida ou foi revogada THEN a API SHALL responder 401. (COL-04)
5. WHEN o SuperAdmin revoga a credencial THEN a API SHALL recusar novos tokens e toda requisição de ingestão com token emitido para ela em no máximo 15 minutos. (COL-05)
6. WHEN o SuperAdmin rotaciona o secret THEN a API SHALL gerar novo secret, exibi-lo uma vez e invalidar o anterior. (COL-06)
7. WHEN o SuperAdmin altera a lista de tenants autorizados THEN a API SHALL aplicar a nova lista no próximo token emitido. (COL-07)
8. IF um usuário de perfil diferente de `SuperAdmin` acessa a gestão de Coletores THEN a API SHALL responder 403. (COL-08)
9. IF um token de perfil `Coletor` chama rota que não seja de coletor THEN a API SHALL responder 403. (COL-09)

**Independent Test**: Criar Coletor autorizado ao tenant A, autenticar, receber lista [A]; revogar e autenticar de novo (401).

---

### P1: Login e configuração no Coletor ⭐ MVP

**User Story**: Como técnico de implantação, quero cadastrar conexões e bancos dos ERPs e vinculá-los aos tenants, para que uma única instalação sincronize vários clientes.

**Why P1**: Pré-requisito para qualquer sincronização.

**Acceptance Criteria**:

1. WHILE o Coletor não está autenticado na API a interface SHALL bloquear cadastro, alteração e exclusão de configurações, permitindo apenas a tela de login e a visualização de logs. (CFG-01)
2. WHEN o técnico informa URL da API, `client_id` e `client_secret` válidos THEN o Coletor SHALL autenticar, guardar a credencial cifrada com DPAPI e carregar a lista de tenants autorizados. (CFG-02)
3. WHEN o técnico cadastra uma instância de conexão (servidor, porta, autenticação Windows ou SQL, usuário, senha) THEN o Coletor SHALL testar a conexão e listar os bancos disponíveis. (CFG-03)
4. IF o teste de conexão falha THEN o Coletor SHALL exibir o tipo de erro (rede, autenticação, timeout) sem exibir a senha. (CFG-04)
5. WHEN o técnico cria uma configuração de sincronização THEN o Coletor SHALL exigir: tenant (da lista autorizada), instância, banco de origem, rotina ("Vendas"), intervalo, período inicial e situação ativa/inativa. (CFG-05)
6. IF o banco escolhido já está vinculado a outro tenant em outra configuração da mesma rotina THEN o Coletor SHALL impedir o cadastro com a mensagem "Banco já vinculado ao tenant X". (CFG-06)
7. The Coletor SHALL usar como período inicial padrão 6 meses antes da data de execução, calculado no momento da carga inicial. (CFG-07)
8. The Coletor SHALL armazenar senhas de banco e o `client_secret` cifrados com DPAPI, nunca em texto puro em arquivo, log ou mensagem de erro. (CFG-08)
9. WHEN o técnico exclui uma configuração THEN o Coletor SHALL pedir confirmação e remover a configuração e seu checkpoint, sem enviar exclusão à API. (CFG-09)
10. The tela principal do Coletor SHALL listar cada configuração com tenant, banco, situação, intervalo, data/hora da última sincronização bem-sucedida e resultado da última execução. (CFG-10)

**Independent Test**: Instalar o Coletor, autenticar, cadastrar uma instância apontando para um SQL Server de teste com dois bancos e vincular cada banco a um tenant diferente.

---

### P1: Carga inicial e incremental de vendas ⭐ MVP

**User Story**: Como cliente, quero que as vendas do meu ERP cheguem à plataforma automaticamente e sem duplicidade, para que o painel reflita a realidade.

**Why P1**: É o dado que alimenta todo o painel.

**Acceptance Criteria**:

1. WHEN uma configuração ativa executa pela primeira vez THEN o Coletor SHALL consultar vendas com `datafatura` entre o período inicial (inclusivo) e o início da execução (exclusivo), usando a query do §11 com parâmetros `@DataInicial`/`@DataFinal`. (SYNC-01)
2. The Coletor SHALL ler e enviar vendas em lotes de 500, ordenados por (`datafatura`, `NumNotaSaida`) crescente. (SYNC-02)
3. WHEN a API confirma um lote THEN o Coletor SHALL persistir o checkpoint da configuração (última `datafatura` e `NumNotaSaida` confirmados) antes de enviar o próximo lote. (SYNC-03)
4. IF a API não confirma um lote THEN o Coletor SHALL manter o checkpoint anterior e reenviar o mesmo lote com o mesmo `batch_id` na tentativa seguinte. (SYNC-04)
5. WHEN uma execução incremental começa THEN o Coletor SHALL consultar desde (data do checkpoint − janela de reprocessamento) até o início da execução (exclusivo). (SYNC-05)
6. WHEN a carga termina sem erro THEN o Coletor SHALL registrar data/hora da última sincronização bem-sucedida e situação "Sucesso". (SYNC-06)
7. The Coletor SHALL usar apenas parâmetros (nunca concatenação de strings) nas consultas ao ERP. (SYNC-07)
8. IF a consulta ao ERP excede 120 s THEN o Coletor SHALL cancelar a consulta, registrar erro de tipo "Consulta" e encerrar a execução como "Falha" mantendo o checkpoint. (SYNC-08)
9. The Coletor SHALL manter checkpoint independente por (configuração, rotina). (SYNC-09)

**Independent Test**: Banco ERP de teste com 1.200 vendas em 6 meses: primeira execução envia 3 lotes e a plataforma tem 1.200 vendas; inserir 10 vendas novas e cancelar 1 antiga dentro da janela; segunda execução resulta em 1.210 vendas e a antiga com situação "Cancelada".

---

### P1: Ingestão de vendas na API ⭐ MVP

**User Story**: Como plataforma, quero receber lotes do Coletor de forma segura e idempotente, para manter os dados consistentes mesmo com reenvios.

**Why P1**: Lado servidor da sincronização.

**Acceptance Criteria**:

1. WHEN o Coletor envia um lote com `batch_id` (GUID), `ID_PUBLICO` do tenant, identificador da configuração e até 500 vendas THEN a API SHALL validar, gravar e responder 200 com contagens de inseridos, atualizados e rejeitados. (ING-01)
2. IF o tenant do lote não está na lista autorizada do token do Coletor THEN a API SHALL responder 403 sem gravar nada. (ING-02)
3. IF o tenant do lote está inativo THEN a API SHALL responder 403 com código "TENANT_INATIVO". (ING-03)
4. The API SHALL gravar vendas por upsert na chave (`TENANT_ID`, `COD_EMPRESA`, `VENDA_ID`), atualizando todos os campos quando a venda já existe. (ING-04)
5. WHEN um lote com `batch_id` já processado com sucesso é reenviado THEN a API SHALL responder 200 com o mesmo resultado original sem regravar dados. (ING-05)
6. IF uma venda do lote viola o contrato (tipo, obrigatoriedade, domínio de `situacao`/`tipo_dfe`, valor negativo) THEN a API SHALL rejeitar só aquela venda, gravar as demais e listar `venda_id` + motivo de cada rejeição na resposta. (ING-06)
7. The API SHALL processar cada lote em uma única transação: ou todas as vendas válidas são gravadas, ou nenhuma. (ING-07)
8. IF o lote tem mais de 500 vendas ou o payload excede 2 MB THEN a API SHALL responder 413 sem gravar. (ING-08)
9. WHEN o Coletor reporta o fim de uma execução THEN a API SHALL registrar por configuração: início, fim, situação, registros enviados e mensagem de erro resumida. (ING-09)
10. IF duas configurações diferentes enviam vendas do mesmo (`TENANT_ID`, `COD_EMPRESA`) THEN a API SHALL rejeitar o lote da configuração que não é a dona da empresa com 409 "EMPRESA_VINCULADA_A_OUTRA_ORIGEM". (ING-10)

**Independent Test**: Enviar o mesmo lote duas vezes (segunda resposta igual, contagem no banco inalterada); enviar lote com 1 venda inválida entre 10 (9 gravadas, 1 rejeitada com motivo).

---

### P1: Agendamento e concorrência ⭐ MVP

**User Story**: Como técnico, quero que cada configuração rode no seu próprio intervalo sem uma atrapalhar a outra, para atender clientes com criticidades diferentes.

**Why P1**: Uma instância atende vários clientes (§9.1, §10.4).

**Acceptance Criteria**:

1. WHILE uma configuração está ativa o Coletor SHALL iniciar uma execução a cada intervalo configurado (5 min a 24 h), contado a partir do fim da execução anterior. (SCH-01)
2. WHILE uma execução de uma configuração está em andamento o Coletor SHALL não iniciar outra execução da mesma configuração. (SCH-02)
3. The Coletor SHALL executar configurações diferentes em paralelo, com no máximo 4 execuções simultâneas por instalação. (SCH-03)
4. IF uma execução falha THEN o Coletor SHALL manter as demais configurações executando normalmente. (SCH-04)
5. WHEN o Coletor é reiniciado THEN o Coletor SHALL retomar cada configuração ativa a partir do último checkpoint confirmado. (SCH-05)

**Independent Test**: Duas configurações com intervalos de 5 e 10 min, uma com banco inacessível: a outra sincroniza normalmente e nenhuma executa em duplicidade.

---

### P2: Pausar, retomar e reiniciar sincronização

**User Story**: Como técnico, quero pausar, retomar ou reiniciar a sincronização de uma configuração, para lidar com manutenções e recargas.

**Why P2**: Operação de suporte; o fluxo automático funciona sem isso.

**Acceptance Criteria**:

1. WHEN o técnico pausa uma configuração THEN o Coletor SHALL concluir o lote em andamento, não iniciar novas execuções e exibir situação "Pausada". (CTL-01)
2. WHEN o técnico retoma uma configuração pausada THEN o Coletor SHALL voltar a agendar execuções a partir do checkpoint preservado. (CTL-02)
3. WHEN o técnico solicita reiniciar THEN o Coletor SHALL pedir confirmação e permitir informar novo período inicial (padrão 6 meses). (CTL-03)
4. WHEN o reinício é confirmado THEN o Coletor SHALL zerar o checkpoint daquela configuração apenas e executar uma nova carga inicial. (CTL-04)
5. The reinício SHALL não excluir nenhuma venda já gravada na plataforma; vendas reenviadas SHALL ser tratadas por upsert. (CTL-05)
6. IF o técnico solicita reiniciar durante uma execução em andamento THEN o Coletor SHALL aguardar o fim do lote atual antes de zerar o checkpoint. (CTL-06)

**Independent Test**: Reiniciar configuração com 1.200 vendas sincronizadas: após nova carga, plataforma continua com 1.200 vendas (sem duplicar).

---

### P2: Logs diários do Coletor

**User Story**: Como técnico de suporte, quero logs diários detalhados por execução, para diagnosticar falhas de sincronização.

**Why P2**: Essencial para operação, mas a sincronização funciona sem a tela de logs.

**Acceptance Criteria**:

1. The Coletor SHALL gravar um arquivo de log por dia com uma linha por evento contendo: data/hora, tenant, instância, banco de origem, rotina, `id` da execução e nível. (LOG-01)
2. WHEN uma execução termina THEN o Coletor SHALL registrar início, término, duração, registros lidos, enviados, rejeitados e resultado (Sucesso/Falha/Cancelada). (LOG-02)
3. IF ocorre erro THEN o Coletor SHALL classificá-lo como Conexão, Consulta, Comunicação ou Processamento e registrar a mensagem técnica. (LOG-03)
4. The Coletor SHALL não registrar senhas, connection strings completas, `client_secret` ou tokens em logs. (LOG-04)
5. WHEN o técnico abre a tela de logs THEN o Coletor SHALL permitir filtrar por data, tenant, banco, rotina e resultado. (LOG-05)
6. The Coletor SHALL remover automaticamente arquivos de log com mais de 30 dias. (LOG-06)

**Independent Test**: Forçar erro de conexão em uma configuração; filtrar logs por esse tenant e ver a execução com tipo "Conexão", duração e resultado "Falha", sem senha no arquivo.

---

### P3: Status de sincronização no painel web

**User Story**: Como Admin do cliente ou SuperAdmin, quero ver quando os dados foram sincronizados pela última vez, para saber se o painel está atualizado.

**Why P3**: Melhora a confiança no dado; não bloqueia o MVP.

**Acceptance Criteria**:

1. WHEN um `Admin` ou `SuperAdmin` abre a tela de sincronização THEN a API SHALL retornar por configuração do tenant: rotina, última sincronização bem-sucedida, situação e mensagem da última execução. (STS-01)
2. WHEN qualquer usuário abre o painel THEN o frontend SHALL exibir "Dados atualizados em <data/hora>" com a última sincronização bem-sucedida do tenant. (STS-02)
3. IF a última sincronização bem-sucedida tem mais de 24 h THEN o frontend SHALL exibir um alerta "Dados desatualizados" para `Admin` e `SuperAdmin`. (STS-03)

**Independent Test**: Após uma execução com falha, a tela mostra a falha e mantém a data da última sincronização bem-sucedida.

---

## Edge Cases

- IF a API fica indisponível no meio da carga THEN o Coletor SHALL aplicar o retry com backoff e, esgotado, encerrar como "Falha" com o checkpoint do último lote confirmado. (SYNC-04)
- IF uma venda com `NumNotaSaida` menor que o checkpoint é inserida no ERP com `datafatura` dentro da janela THEN a próxima execução SHALL enviá-la. (SYNC-05)
- IF uma venda é inserida com `datafatura` anterior à janela THEN ela SHALL ser capturada apenas por reinício (limitação documentada). (CTL-04)
- WHEN o período consultado não tem vendas THEN o Coletor SHALL registrar execução "Sucesso" com 0 registros e avançar a data da última sincronização. (SYNC-06)
- IF o token do Coletor expira durante uma execução THEN o Coletor SHALL renovar o token e reenviar o lote atual uma vez. (COL-03)
- IF o tenant vinculado é removido da lista autorizada THEN o Coletor SHALL marcar a configuração como "Não autorizada" e não executá-la. (ING-02)
- IF o relógio do servidor do ERP está adiantado/atrasado THEN o limite superior SHALL usar a hora do servidor do ERP (`SYSDATETIMEOFFSET()`), não a do Coletor. (SYNC-01)
- WHEN `nome_cliente` ou `vendedor_nome` excedem o tamanho THEN o Coletor SHALL truncar antes do envio. (ING-06)

---

## Implicit-Requirement Dimensions Sweep

| Dimension | Coverage |
| --------- | -------- |
| Input validation & bounds | Contrato da venda, ING-06, ING-08, CFG-05, SCH-01 |
| Failure / partial-failure states | SYNC-04, SYNC-08, ING-07, SCH-04, edge case de API indisponível |
| Idempotency / retry / duplicate handling | ING-04, ING-05, SYNC-04, CTL-05, ING-10 |
| Auth boundaries & rate limits | COL-01..COL-09, ING-02, ING-03, CFG-01; limite de 4 execuções simultâneas (SCH-03) e de payload (ING-08) |
| Concurrency / ordering | SYNC-02, SCH-02, SCH-03, CTL-06 |
| Data lifecycle / expiry | LOG-06, COL-05 (expiração de token), CTL-05 (reinício não apaga) |
| Observability | LOG-01..LOG-06, ING-09, STS-01..STS-03 |
| External-dependency failure | SYNC-08 (ERP), retry/backoff da API (Assumptions), CFG-04 |
| State-transition integrity | Configuração Ativa↔Pausada↔Inativa (CTL-01/02), checkpoint só após ACK (SYNC-03), reinício (CTL-04/06) |

---

## Requirement Traceability

| Requirement ID | Story | Phase | Status |
| -------------- | ----- | ----- | ------ |
| COL-01 | P1: Credencial do Coletor | Tasks | In Tasks |
| COL-02 | P1: Credencial do Coletor | Tasks | In Tasks |
| COL-03 | P1: Credencial do Coletor | Tasks | In Tasks |
| COL-04 | P1: Credencial do Coletor | Tasks | In Tasks |
| COL-05 | P1: Credencial do Coletor | Tasks | In Tasks |
| COL-06 | P1: Credencial do Coletor | Tasks | In Tasks |
| COL-07 | P1: Credencial do Coletor | Tasks | In Tasks |
| COL-08 | P1: Credencial do Coletor | Tasks | In Tasks |
| COL-09 | P1: Credencial do Coletor | Tasks | In Tasks |
| CFG-01 | P1: Login e configuração no Coletor | Tasks | In Tasks |
| CFG-02 | P1: Login e configuração no Coletor | Tasks | In Tasks |
| CFG-03 | P1: Login e configuração no Coletor | Tasks | In Tasks |
| CFG-04 | P1: Login e configuração no Coletor | Tasks | In Tasks |
| CFG-05 | P1: Login e configuração no Coletor | Tasks | In Tasks |
| CFG-06 | P1: Login e configuração no Coletor | Tasks | In Tasks |
| CFG-07 | P1: Login e configuração no Coletor | Tasks | In Tasks |
| CFG-08 | P1: Login e configuração no Coletor | Tasks | In Tasks |
| CFG-09 | P1: Login e configuração no Coletor | Tasks | In Tasks |
| CFG-10 | P1: Login e configuração no Coletor | Tasks | In Tasks |
| SYNC-01 | P1: Carga inicial e incremental | Tasks | In Tasks |
| SYNC-02 | P1: Carga inicial e incremental | Tasks | In Tasks |
| SYNC-03 | P1: Carga inicial e incremental | Tasks | In Tasks |
| SYNC-04 | P1: Carga inicial e incremental | Tasks | In Tasks |
| SYNC-05 | P1: Carga inicial e incremental | Tasks | In Tasks |
| SYNC-06 | P1: Carga inicial e incremental | Tasks | In Tasks |
| SYNC-07 | P1: Carga inicial e incremental | Tasks | In Tasks |
| SYNC-08 | P1: Carga inicial e incremental | Tasks | In Tasks |
| SYNC-09 | P1: Carga inicial e incremental | Tasks | In Tasks |
| ING-01 | P1: Ingestão na API | Tasks | In Tasks |
| ING-02 | P1: Ingestão na API | Tasks | In Tasks |
| ING-03 | P1: Ingestão na API | Tasks | In Tasks |
| ING-04 | P1: Ingestão na API | Tasks | In Tasks |
| ING-05 | P1: Ingestão na API | Tasks | In Tasks |
| ING-06 | P1: Ingestão na API | Tasks | In Tasks |
| ING-07 | P1: Ingestão na API | Tasks | In Tasks |
| ING-08 | P1: Ingestão na API | Tasks | In Tasks |
| ING-09 | P1: Ingestão na API | Tasks | In Tasks |
| ING-10 | P1: Ingestão na API | Tasks | In Tasks |
| SCH-01 | P1: Agendamento e concorrência | Tasks | In Tasks |
| SCH-02 | P1: Agendamento e concorrência | Tasks | In Tasks |
| SCH-03 | P1: Agendamento e concorrência | Tasks | In Tasks |
| SCH-04 | P1: Agendamento e concorrência | Tasks | In Tasks |
| SCH-05 | P1: Agendamento e concorrência | Tasks | In Tasks |
| CTL-01 | P2: Pausar, retomar e reiniciar | Tasks | In Tasks |
| CTL-02 | P2: Pausar, retomar e reiniciar | Tasks | In Tasks |
| CTL-03 | P2: Pausar, retomar e reiniciar | Tasks | In Tasks |
| CTL-04 | P2: Pausar, retomar e reiniciar | Tasks | In Tasks |
| CTL-05 | P2: Pausar, retomar e reiniciar | Tasks | In Tasks |
| CTL-06 | P2: Pausar, retomar e reiniciar | Tasks | In Tasks |
| LOG-01 | P2: Logs diários do Coletor | Tasks | In Tasks |
| LOG-02 | P2: Logs diários do Coletor | Tasks | In Tasks |
| LOG-03 | P2: Logs diários do Coletor | Tasks | In Tasks |
| LOG-04 | P2: Logs diários do Coletor | Tasks | In Tasks |
| LOG-05 | P2: Logs diários do Coletor | Tasks | In Tasks |
| LOG-06 | P2: Logs diários do Coletor | Tasks | In Tasks |
| STS-01 | P3: Status de sincronização | Tasks | In Tasks |
| STS-02 | P3: Status de sincronização | Tasks | In Tasks |
| STS-03 | P3: Status de sincronização | Tasks | In Tasks |

**Coverage:** 58 total, 58 mapped to tasks (`tasks.md`), 0 unmapped

---

## Cobertura do documento original

| Seção original | Requisitos |
| -------------- | ---------- |
| §4.3 Coletor (responsabilidades) | CFG-*, SYNC-*, SCH-*, CTL-*, LOG-* |
| §7 Perfil Coletor | COL-03, COL-09 |
| §8.2 Comunicação com o coletor | COL-*, ING-* |
| §8.3 Duplicidade, validação, segredos | ING-04..ING-06, COL-02, CFG-08, LOG-04 |
| §9.1–9.3 Conexões, autenticação, configuração | CFG-01..CFG-10 |
| §9.4 Logs | LOG-01..LOG-06 |
| §9.5 Rotina de vendas | SYNC-01..SYNC-09 |
| §10.1–10.2 Carga inicial e incremental | SYNC-01..SYNC-05, AD-006, AD-007 |
| §10.3 Reinicialização | CTL-03..CTL-06 |
| §10.4 Intervalo / concorrência | SCH-01..SCH-03 |
| §11 Query e campos | Contrato da venda, SYNC-01, Assumptions |
| §12.1–12.2 Desempenho e confiabilidade | SYNC-02, SYNC-08, SCH-03, ING-05, ING-07 |

---

## Success Criteria

- [ ] Teste de caos (derrubar a API 3 vezes durante uma carga de 50 mil vendas) termina com contagem exata, sem duplicatas.
- [ ] Carga inicial de 6 meses (~100 mil vendas) concluída em menos de 30 minutos.
- [ ] Consulta ao ERP nunca mantém lock por mais de 120 s.
- [ ] Nenhum segredo encontrado em logs ou arquivos de configuração em texto puro (varredura automatizada).
