# Plataforma — Tenancy, Usuários e Acesso — Validação (iteração 2 de 3)

**Data**: 2026-10-25
**Spec**: `.specs/features/plataforma-tenancy-acesso/spec.md`
**Faixa do diff**: `8ae509d..301bd50` (feature inteira, branch `feat/plataforma-tenancy-acesso`); correções desde a iteração 1: `7c3c576..301bd50` (9 commits)
**Verifier**: sub-agente independente (autor ≠ verificador), regra evidence-or-zero. Cobertura re-derivada da spec; as conclusões da iteração 1 não foram herdadas.

## Veredito: FAIL ❌

Os gates estão todos verdes, inclusive o e2e. Os 6 gaps da iteração 1 e a divergência do edge case de TEN-05 foram fechados. Das 15 mutações distintas, 14 morreram.

O veredito é FAIL por dois motivos, ambos pequenos:

1. **USR-05, inativação do último Admin.** O mutante M11 sobreviveu: nenhum teste HTTP inativa o único Admin mantendo o perfil Admin. O próprio Independent Test da spec pede esse caso: "tentar inativar a si mesmo sendo o único Admin (422)".
2. **DEL-09, ramo "Em execução".** A spec diz "Pendente ou Em execução". Só o ramo Pendente tem evidência.

---

## Iteração 1 → 2

| Gap da iteração 1 | Situação | Evidência nova |
| ----------------- | -------- | -------------- |
| Edge TEN-05: tenant inativado durante a sessão recebia 401 (spec pede 403) | ✅ Fechado (commit `301bd50`, decisão do usuário; design.md atualizado) | `Integracao/Tenants/SituacaoTenantTestes.cs:62-63`; `Integracao/Seguranca/StatusAcessoCacheTestes.cs:86-87`; renovação: `Integracao/Autenticacao/RefreshTestes.cs:147-150`; Web: `auth.interceptor.spec.ts:139-173`, `login.page.spec.ts:55-63` |
| USR-02: promoção via edição sem teste (M10 sobreviveu) | ✅ Fechado. O mutante equivalente (M2) agora morre | `Integracao/Usuarios/EditarUsuarioTestes.cs:75-92` |
| DEL-04: limite de 5.000 sem teste (M9 sobreviveu) | ✅ Fechado. O mutante equivalente (M1) agora morre | `Unidade/Exclusao/ExclusaoOptionsTestes.cs:20-23,38-41`; `Integracao/Exclusao/ProcessadorExclusaoTestes.cs:159-196` |
| CTX-01: tenant inativo | ✅ Fechado | `Integracao/Seguranca/ContextoTenantInativoTestes.cs:55-58` |
| USR-09: só a criação pelo SuperAdmin | ✅ Fechado | listar `UsuariosEndpointsTestes.cs:161-173`; editar `EditarUsuarioTestes.cs:96-123`; redefinir `RedefinirSenhaTestes.cs:69-88` |
| EMP-02: SuperAdmin e limites do nome | ✅ Fechado | `EmpresasEndpointsTestes.cs:122-139`, `:145-156`, `:162-171` |
| TEN-01: limites do nome (3/120/2/121) | ✅ Fechado | `CriarTenantTestes.cs:161-186` |
| M5 dependente de tempo (pausa de 500 ms) | ✅ Fechado. Sincronização determinística por `sys.dm_exec_requests`; o mutante morreu em 6 de 6 execuções | `RegrasUltimoAdminTestes.cs:61-99` |
| Tempo limite do fixture | ✅ `CommandTimeout = 120` em `BancoDeTestes.cs` (infra de teste) | — |

Gaps novos nesta iteração, encontrados pelo sensor ampliado: USR-05 (inativação) e DEL-09 (Em execução). Os dois já existiam no código da iteração 1; nenhum veio das correções.

---

## Conclusão das tarefas

| Tarefa | Situação | Observações |
| ------ | -------- | ----------- |
| T1–T58 | ✅ Concluídas | `tasks.md`: 183 `[x]`, 0 `[ ]` |
| Correções da iteração 1 | ✅ Commitadas | `7203131`, `b6bfd22`, `f2e54b9`, `eb02371`, `1d8b8e7`, `2e9d69e`, `ae15228`, `6b40446`, `301bd50` |

---

## Critérios de aceite ancorados na spec

Caminhos relativos a `tests/Jamsoft.DashBoards.Api.Testes/`, salvo indicação. `Web:` = `src/Jamsoft.DashBoards.Web/src/app/`.

### P1: Login e sessão

| AC | Resultado definido na spec | `file:line` + asserção | Resultado |
| -- | -------------------------- | ---------------------- | --------- |
| AUTH-01 | 200 + access de 15 min + refresh de 7 dias + perfil + `ID_PUBLICO` do tenant (nulo para SuperAdmin) | `Integracao/Autenticacao/LoginTestes.cs:50` `Assert.Equal(HttpStatusCode.OK, …)`; `:56` `InRange(expiraEm, antes.AddMinutes(15)…)`; `:57` perfil; `:60` `JsonValueKind.Null` (SuperAdmin); `:63` `tenant.idPublico`; `:82` `InRange(gravado.ExpiraEm, +7 dias)`; `Unidade/Seguranca/TokenServiceTestes.cs:62-63` | ✅ |
| AUTH-02 | 401 + a mesma mensagem "Credenciais inválidas" nos dois casos | `LoginTestes.cs:106-107` 401 ×2; `:110` `"CREDENCIAIS_INVALIDAS"`; `:111` `title == "Credenciais inválidas"`; `:115` corpos iguais | ✅ |
| AUTH-03 | 5 falhas → bloqueio de 15 min, 423 durante o bloqueio | `BloqueioTestes.cs:89` `(HttpStatusCode)423`; `:91` `USUARIO_BLOQUEADO`; `:93` +15 min; `:108` senha correta → 423; `:121` 200 depois de 15 min; `:69-78` sucesso zera o contador | ✅ |
| AUTH-04 | novo par de tokens e o usado invalidado | `RefreshTestes.cs:60` 200; `:65` `NotEqual(antigo, novo)`; `:71` `NotNull(registroAntigo.UsadoEm)` | ✅ |
| AUTH-05 | 401 + revogação de todos os refresh ativos | `RefreshTestes.cs:88-89` 401 `NAO_AUTENTICADO`; `:91-92` 3 tokens, todos `RevogadoEm != null`; `:95-96` | ✅ |
| AUTH-06 | API revoga o refresh da sessão; frontend descarta os tokens | `LogoutTestes.cs:46` 204; `:53` refresh antigo 401; `:58` `NotNull(revogado.RevogadoEm)`; `Web: core/auth/auth.service.spec.ts:57-58` `usuario()` e `accessToken` nulos | ✅ |
| AUTH-07 | 403 "Acesso desativado" (usuário ou tenant inativo) | `LoginTestes.cs:128-131` 403 + `ACESSO_DESATIVADO` + `title == "Acesso desativado"`; `:143-144` tenant inativo 403 + `ACESSO_DESATIVADO` (mesma linha de produção `LoginEndpoint.cs:67`); mutante M12 morto | ✅ |
| AUTH-08 | 401 sem token válido; 403 com perfil fora da policy | `Integracao/Seguranca/PoliciesTestes.cs:57` `(401, "NAO_AUTENTICADO")`; `:66` token expirado 401; `:75` `(403, "ACESSO_NEGADO")`; `:103-104` Coletor; `Arquitetura/RegrasArquiteturaTestes.cs:35` | ✅ |
| AUTH-09 | sem senhas ou tokens nos logs | `LoginTestes.cs:161-166` `DoesNotContain(senha/senhaErrada/accessToken/refresh)` nas linhas capturadas | ✅ (obs.: só o fluxo de login é varrido) |

### P1: SuperAdmin gerencia tenants

| AC | Resultado definido na spec | `file:line` + asserção | Resultado |
| -- | -------------------------- | ---------------------- | --------- |
| TEN-01 | 201 + `ID_PUBLICO`, Ativo; nome de 3–120; CNPJ válido | `CriarTenantTestes.cs:51` 201; `:54` `NotEqual(Guid.Empty, idPublico)`; `:55` sem `id`; `:58` `ativo`; `:161-170` 3/120 → 201; `:176-186` 2/121 → 400 `VALIDACAO` `erros.nome`; `Unidade/Tenants/CnpjTestes.cs` | ✅ |
| TEN-02 | 409 sem criar registro | `CriarTenantTestes.cs:116-118` 409 `CNPJ_DUPLICADO`, contagem == 1 | ✅ |
| TEN-03 | primeiro Admin no mesmo fluxo, com troca de senha | `CriarTenantTestes.cs:92-96` Admin, `TrocarSenha`; `:103-104` login `trocarSenha == true` | ✅ |
| TEN-04 | paginada (20 por padrão, máximo 100); filtro por nome/CNPJ e situação | `TenantsEndpointsTestes.cs:66-68`; `:84-85` `tamanho=500` → 100; `:101-103`; `:122-125` | ✅ |
| TEN-05 | bloqueia o login (e a ingestão do Coletor); dados preservados | `SituacaoTenantTestes.cs:62-66` token 403 `ACESSO_DESATIVADO` e login 403 `ACESSO_DESATIVADO`; `:84-90` dados intactos. Ingestão do Coletor: **fora do escopo** (`sincronizacao-vendas`) | ✅ (parte do Coletor fora do escopo) |
| TEN-06 | restaura o login (e a ingestão) sem alterar dados | `SituacaoTenantTestes.cs:103-108` reativar 200, login 200, `/eu` 200. Ingestão: **fora do escopo** | ✅ (parte do Coletor fora do escopo) |
| TEN-07 | só `ID_PUBLICO` exposto | `CriarTenantTestes.cs:55`, `TenantsEndpointsTestes.cs:73` `TryGetProperty("id") == false` | ✅ |
| TEN-08 | quem não é SuperAdmin → 403 | `CriarTenantTestes.cs:198-200` 403 `ACESSO_NEGADO`; `TenantsEndpointsTestes.cs:202-203`; `RegrasArquiteturaTestes.cs:47-48` | ✅ |

### P1: Empresas do tenant

| AC | Resultado definido na spec | `file:line` + asserção | Resultado |
| -- | -------------------------- | ---------------------- | --------- |
| EMP-01 | única por (`TENANT_ID`, `COD_EMPRESA`) | `Integracao/Esquema/EmpresaEsquemaTestes.cs:30-31` `ErroIndiceUnico`, `IX_EMPRESA_TENANT_ID_COD_EMPRESA` | ✅ |
| EMP-02 | Admin ou SuperAdmin; código inteiro positivo; nome de 1–80 → 201 | Admin `EmpresasEndpointsTestes.cs:63-75`; SuperAdmin `:131-139` 201 no tenant escolhido, outro com 0; nome 1/80 `:169-171`; vazio/espaços `:151-156` 400; código 0 e nome de 81 `:109-116` | ✅ |
| EMP-03 | 409 duplicada | `EmpresasEndpointsTestes.cs:95-96` 409 `EMPRESA_DUPLICADA` | ✅ |
| EMP-04 | criação automática com o `Nome_Empresa` recebido | `Integracao/Empresas/ServicoEmpresasTestes.cs:43-47` (serviço; a rota de ingestão é de `sincronizacao-vendas`) | ✅ (serviço) |
| EMP-05 | o nome editado sobrevive às ingestões | `EmpresasEndpointsTestes.cs:228-233` `NomeManual`; `ServicoEmpresasTestes.cs:85-88` | ✅ |
| EMP-06 | só o tenant do token ou o selecionado | `EmpresasEndpointsTestes.cs:199` `["Própria 1","Própria 2"]`; `:214` `["Do escolhido"]` | ✅ |

### P1: Isolamento entre tenants

| AC | Resultado definido na spec | `file:line` + asserção | Resultado |
| -- | -------------------------- | ---------------------- | --------- |
| ISO-01 | tenant tirado das claims | `Unidade/Seguranca/TenantAtualTestes.cs:49-52`, `:70-72`; mutante M7 morto | ✅ |
| ISO-02 | filtro por `TENANT_ID` em todo CRUD | `Integracao/Dados/FiltroTenantTestes.cs:38-39,49`; `TenantSaveChangesInterceptorTestes.cs:65-69,82-85,98-101`; `IsolamentoTenantsTestes.cs:149`; M6 e M8 mortos | ✅ |
| ISO-03 | 404 para recurso de outro tenant | `IsolamentoTenantsTestes.cs:136-146` `(NotFound, NAO_ENCONTRADO)` em toda rota com `{idPublico}`, cobertura de rotas provada em `:120-121` | ✅ |
| ISO-04 | tenant divergente → 403 + auditoria | `ValidacaoContextoTenantFilterTestes.cs:68-74` `(403, TENANT_DIVERGENTE)` + `ACESSO_CRUZADO`/`Negado`; `:98-99` corpo; M9 morto | ✅ |
| ISO-05 | índice iniciado por `TENANT_ID` | `IsolamentoTenantsTestes.cs:168-171` `Assert.Empty(semIndice)` | ✅ |

### P2: Admin do cliente gerencia usuários

| AC | Resultado definido na spec | `file:line` + asserção | Resultado |
| -- | -------------------------- | ---------------------- | --------- |
| USR-01 | 201, tenant do Admin, troca obrigatória | `UsuariosEndpointsTestes.cs:68-80` | ✅ |
| USR-02 | criar **ou promover** a SuperAdmin/Coletor → 403 | criar `UsuariosEndpointsTestes.cs:102-104` 403 `PERFIL_NAO_PERMITIDO`; promover `EditarUsuarioTestes.cs:84-92` 403 `PERFIL_NAO_PERMITIDO`, nome/perfil/ativo/versão inalterados, sem auditoria; M2 morto | ✅ |
| USR-03 | 409 sem revelar o tenant | `UsuariosEndpointsTestes.cs:116-121` | ✅ |
| USR-04 | aplica nome, perfil e situação + auditoria | `EditarUsuarioTestes.cs:58-71`; situação `:158-159` | ✅ |
| USR-05 | 422 "O tenant precisa de ao menos um Admin ativo" para **qualquer** alteração que deixe o tenant sem Admin ativo (rebaixamento **ou inativação**) | rebaixamento: `EditarUsuarioTestes.cs:134-140` 422 `ULTIMO_ADMIN` + título exato; regra: `RegrasUltimoAdminTestes.cs:37-39`; concorrência `:86-99`. Inativação do único Admin via HTTP: **sem evidência**; mutante M11 (`|| !ativo` removido em `EditarUsuarioEndpoint.cs:38`) **sobreviveu** | ❌ GAP (inativação) |
| USR-06 | inativação revoga todos os refresh | `EditarUsuarioTestes.cs:162` refresh 401; `:164-165` access 403 `ACESSO_DESATIVADO`; `:168-169` todos `RevogadoEm != null` | ✅ |
| USR-07 | exige troca + revoga refresh | `RedefinirSenhaTestes.cs:59-63`; `:103-106` | ✅ |
| USR-08 | com troca pendente, só troca e logout | `TrocarSenhaTestes.cs:62-67`; `PoliciesTestes.cs:84-85,93`; `LogoutTestes.cs:86-88` | ✅ |
| USR-09 | SuperAdmin: as mesmas operações em qualquer tenant | criar `UsuariosEndpointsTestes.cs:153-157`; listar `:172-173`; editar `EditarUsuarioTestes.cs:113-123`; redefinir `RedefinirSenhaTestes.cs:79-88` | ✅ |
| USR-10 | Consulta → 403 | `UsuariosEndpointsTestes.cs:137-140`; `IsolamentoTenantsTestes.cs:136-146` (PUT/redefinir com Consulta → `ACESSO_NEGADO`) | ✅ |

### P2: SuperAdmin acessa o contexto de um tenant

| AC | Resultado definido na spec | `file:line` + asserção | Resultado |
| -- | -------------------------- | ---------------------- | --------- |
| CTX-01 | aceita o `ID_PUBLICO` de tenant ativo **ou inativo** | ativo `EmpresasEndpointsTestes.cs:213-214`; inativo `Integracao/Seguranca/ContextoTenantInativoTestes.cs:55-58` 200 com os dados só do inativo | ✅ |
| CTX-02 | auditoria com SuperAdmin, tenant e rota | `AuditoriaContextoSuperAdminFilterTestes.cs:66-71` | ✅ |
| CTX-03 | outro perfil: cabeçalho igual é ignorado; diferente → 403 | `TenantAtualTestes.cs:60-61,70`; `ValidacaoContextoTenantFilterTestes.cs:68,84-85` | ✅ |

### P2: Exclusão total de um tenant

| AC | Resultado definido na spec | `file:line` + asserção | Resultado |
| -- | -------------------------- | ---------------------- | --------- |
| DEL-01 | quem não é SuperAdmin → 403 | `SolicitarExclusaoTestes.cs:51-55`; `ExclusoesEndpointsTestes.cs:167-170` | ✅ |
| DEL-02 | nome diferente → 422, nada iniciado | `SolicitarExclusaoTestes.cs:67-71` | ✅ |
| DEL-03 | inativa, job "Pendente", 202 + id | `SolicitarExclusaoTestes.cs:87-101` (inclui token 403 `ACESSO_DESATIVADO`) | ✅ |
| DEL-04 | lotes de **no máximo 5.000** por transação, filhos → pais, filtro por `TENANT_ID` | limite: `Unidade/Exclusao/ExclusaoOptionsTestes.cs:20-22` padrão = máximo = 5000, `:38-41` 5001/50000 recusados; `ProcessadorExclusaoTestes.cs:190` `[5_000, 5_000, 2_000, 0]` por transação, `:192` transações distintas; ordem `Unidade/Exclusao/PlanoExclusaoTestes.cs:22,31-35`; filtro `ProcessadorExclusaoTestes.cs:126-127`; M1 morto | ✅ |
| DEL-05 | "Concluída" + linhas por tabela + tenant removido | `ProcessadorExclusaoTestes.cs:126,133-136` | ✅ |
| DEL-06 | "Falhou" com etapa e erro, tenant inativo, retomada | `ProcessadorExclusaoTestes.cs:220-232`; `ExclusoesEndpointsTestes.cs:111-132` | ✅ |
| DEL-07 | não altera outros tenants | `ProcessadorExclusaoTestes.cs:127` impressão de B idêntica | ✅ |
| DEL-08 | auditoria: solicitante, `ID_PUBLICO`, nome, situação | `SolicitarExclusaoTestes.cs:106-111`; `ProcessadorExclusaoTestes.cs:141-150`, `:235-237` | ✅ |
| DEL-09 | job "Pendente" **ou "Em execução"** → 409 | Pendente: `SolicitarExclusaoTestes.cs:124-125` 409 `EXCLUSAO_EM_ANDAMENTO`, `:141-144`; `Esquema/AuditoriaExclusaoEsquemaTestes.cs:38-39`. Em execução: **sem evidência** (depende do `HasFilter("[SITUACAO] IN ('Pendente','EmExecucao')")` em `ExclusaoTenant.cs:46`, que nenhum teste exercita com job `EmExecucao`) | ⚠️ Parcial (GAP no ramo Em execução) |

### P3: Auditoria consultável

| AC | Resultado definido na spec | `file:line` + asserção | Resultado |
| -- | -------------------------- | ---------------------- | --------- |
| AUD-01 | eventos auditados | login SuperAdmin `LoginTestes.cs:90`; tenant `CriarTenantTestes.cs:67`, `SituacaoTenantTestes.cs:124`; usuário `UsuariosEndpointsTestes.cs:86`, `EditarUsuarioTestes.cs:68`; empresa `EmpresasEndpointsTestes.cs:77,235`; senha `RedefinirSenhaTestes.cs:151`; contexto `AuditoriaContextoSuperAdminFilterTestes.cs:66`; exclusão `ProcessadorExclusaoTestes.cs:141`, `SolicitarExclusaoTestes.cs:106`. Credencial de Coletor: **fora do escopo** | ✅ (Coletor fora do escopo) |
| AUD-02 | UTC, autor, perfil, operação, recurso, tenant, IP, resultado, sem segredos | `Integracao/Auditoria/AuditoriaTestes.cs:125-133`; `:110-114` | ✅ |
| AUD-03 | paginada; filtros de período, tenant, usuário e operação | `AuditoriaEndpointsTestes.cs:80,95,113-114,127,151-155` | ✅ |
| AUD-04 | sem rota de alteração ou exclusão | `AuditoriaEndpointsTestes.cs:181-186`; `Esquema/PermissoesAplicacaoTestes.cs:59-61` | ✅ |

**Situação**: ❌ há gaps. Dos 54 ACs: 52 ✅ (3 deles com parte do Coletor fora do escopo: TEN-05, TEN-06, AUD-01), 1 ❌ (USR-05, ramo da inativação), 1 ⚠️ parcial (DEL-09, ramo Em execução). Nenhum spec-precision gap: todos os ACs com status ou código definido são afirmados pelo valor exato.

---

## Edge cases

- [x] CNPJ concorrente → um 201 e um 409: `CriarTenantTestes.cs:212-215`
- [x] Usuário inativado com access token válido: renovação recusada (`RefreshTestes.cs:133-135`, 403 `ACESSO_DESATIVADO`; `EditarUsuarioTestes.cs:162`, 401 por token revogado); token válido por no máximo 15 min (`TokenServiceTestes.cs:62-63`); revalidação a cada requisição (`StatusAcessoCacheTestes.cs:70`, `:118`)
- [x] **Tenant inativado durante a sessão → 403** (corrigido): validação do token `SituacaoTenantTestes.cs:62-63` e `StatusAcessoCacheTestes.cs:86-87` `(Forbidden, "ACESSO_DESATIVADO")`; renovação `RefreshTestes.cs:147-150` (403, sem consumir o token); frontend `Web: core/auth/auth.interceptor.spec.ts:139-155` (sem renovar, login com o aviso), `:157-173` (renovação com 403), `Web: conta/login/login.page.spec.ts:55-63` ("Acesso desativado" no `role=alert`). Mutantes M3, M4, M10, W1, W2 e W3 mortos.
- [x] Lista de tenants vazia → "Cadastrar tenant": `Web: admin/tenants/tenants-lista.page.spec.ts:110-115`
- [x] `ID_PUBLICO` mal formatado → 400: `TenantsEndpointsTestes.cs:158-159`; `X-Tenant` mal formatado → `ValidacaoContextoTenantFilterTestes.cs:121`
- [x] API reinicia durante a exclusão → retomada: `ProcessadorExclusaoTestes.cs:257-266`

---

## Sensor de discriminação

**Isolamento**: `git worktree add --detach C:/Users/PauloHenrique/AppData/Local/Temp/vw2 HEAD` (caminho curto). `node_modules` do Web ligado por junção, removida com `rmdir` antes do `worktree remove`. Cada mutação foi aplicada com perl, seguida da suíte (`dotnet test Jamsoft.DashBoards.slnx` completo ou Vitest) e de `git checkout -- .`. Nenhuma execução teve erro de compilação nem de infraestrutura (SNI, login ou timeout). Contagem por mutante = `comp=0 infra=0`.
**Linha de base sem mutação**: API 236/236 ✅, Web 121/121 ✅.

| # | Arquivo de produção | Descrição | Morto? | Testes que mataram |
| - | ------------------- | --------- | ------ | ------------------ |
| M1 (M9) | `src/Jamsoft.DashBoards.Api/Configuracao/ExclusaoOptions.cs:15,23` | `TamanhoLote` e `LoteMaximo` 5000 → 50000 | ✅ (4) | `ExclusaoOptionsTestes` ×3, `ProcessadorExclusaoTestes.Lote_padrao_apaga_no_maximo_5000_linhas_por_transacao` |
| M2 (M10) | `…/Modulos/Usuarios/EditarUsuarioEndpoint.cs:25` | checagem `PerfilPermitido` removida da edição | ✅ (2) | `EditarUsuarioTestes.Promover_a_perfil_nao_permitido…` (SuperAdmin, Coletor) |
| M3 | `…/Seguranca/Policies.cs:42` | `OnChallenge` removido → volta 401 | ✅ (6) | `StatusAcessoCacheTestes` ×3, `SituacaoTenantTestes`, `SolicitarExclusaoTestes`, `EditarUsuarioTestes.Inativar…` |
| M4 | `…/Seguranca/RefreshTokens.cs:73` | renovação deixa passar tenant inativo | ✅ (1) | `RefreshTestes.Refresh_com_tenant_inativado_durante_a_sessao…` |
| M5 | `…/Modulos/Usuarios/RegrasUltimoAdmin.cs:27` | `WITH (UPDLOCK, HOLDLOCK)` removido (suíte completa) | ✅ (1) | `RegrasUltimoAdminTestes.Dois_Admins_se_rebaixando…` |
| M5b–f | mesmo | mesma mutação, só o teste de concorrência, 5 repetições | ✅ 5/5 (determinístico: log "Sessão B bloqueada pela A: False; regra de B já terminou: True") | idem |
| M6 | `…/Infraestrutura/Dados/PlataformaDbContext.cs:98` | filtro global de tenant (`HasQueryFilter`) removido | ✅ (17) | `FiltroTenantTestes`, `EmpresasEndpointsTestes`, `ServicoEmpresasTestes`… |
| M7 | `…/Seguranca/TenantAtual.cs:115` | `X-Tenant` de Admin/Consulta substitui o tenant do token | ✅ (11) | suíte de isolamento ×8, `ValidacaoContextoTenantFilterTestes`, `TenantAtualTestes` ×2 |
| M8 | `…/Infraestrutura/Dados/TenantSaveChangesInterceptor.cs:60` | interceptor nunca acusa acesso cruzado | ✅ (4) | `TenantSaveChangesInterceptorTestes` ×3, `AcessoCruzadoTestes` |
| M9 | `…/Seguranca/ValidacaoContextoTenantFilter.cs:38` | 403 sem gravar a auditoria (ISO-04) | ✅ (2) | `ValidacaoContextoTenantFilterTestes` ×2 |
| M10 | `…/Seguranca/Policies.cs:92` | revalidação por requisição ignora a situação do tenant | ✅ (3) | `StatusAcessoCacheTestes.Tenant_inativado…`, `SituacaoTenantTestes`, `SolicitarExclusaoTestes` |
| M11 | `…/Modulos/Usuarios/EditarUsuarioEndpoint.cs:38` | regra do último Admin só no rebaixamento (`\|\| !ativo` removido) | ❌ **Sobreviveu** (236/236) | — |
| M12 | `…/Modulos/Autenticacao/LoginEndpoint.cs:67` | login deixa entrar usuário de tenant inativo | ✅ (3) | `LoginTestes.Usuario_de_tenant_inativo…`, `SituacaoTenantTestes` ×2 |
| W1 | `src/Jamsoft.DashBoards.Web/src/app/core/auth/auth.interceptor.ts:50-52` | interceptor não trata 403 `ACESSO_DESATIVADO` | ✅ (1) | `auth.interceptor.spec.ts` "403 ACESSO_DESATIVADO encerra a sessão…" |
| W2 | `…/core/auth/auth.interceptor.ts:58` | renovação com 403 `ACESSO_DESATIVADO` → login sem aviso | ✅ (1) | `auth.interceptor.spec.ts` "401 e renovação respondendo 403…" |
| W3 | `…/conta/login/login.page.ts:76-78` | login ignora o aviso vindo da navegação | ✅ (1) | `login.page.spec.ts` "chegando com o aviso…" |

**Profundidade do sensor**: P0/crítica (injeção manual ampliada; 15 mutações distintas, mais 5 repetições do M5)
**Resultado**: 14/15 mortos. **FAIL ❌** (M11 sobreviveu e vira tarefa de correção)

**Isolamento verificado**: `git status --porcelain` da árvore real antes do sensor = vazio; depois de `git worktree remove --force` + `git worktree prune` = vazio. `git worktree list` mostra só a árvore principal. `node_modules` real intacto.

---

## Gate check

Gate de build (`tasks.md`), executado uma vez na árvore real sem alterações (HEAD `301bd50`):

| Comando | Resultado |
| ------- | --------- |
| `dotnet build Jamsoft.DashBoards.slnx -warnaserror --no-incremental` | ✅ 0 avisos, 0 erros |
| `dotnet format Jamsoft.DashBoards.slnx --verify-no-changes` | ✅ exit 0 |
| `dotnet test Jamsoft.DashBoards.slnx` (`TESTES_SQLSERVER` local) | ✅ 236 aprovados, 0 com falha, 0 ignorados (33 s), na primeira execução |
| `npm --prefix src/Jamsoft.DashBoards.Web run lint` | ✅ "All files pass linting." |
| `npm --prefix src/Jamsoft.DashBoards.Web run build` | ✅ bundle gerado |
| `npm --prefix src/Jamsoft.DashBoards.Web test -- --watch=false` | ✅ 17 arquivos, 121 testes aprovados |
| `npm --prefix src/Jamsoft.DashBoards.Web run e2e` | ✅ 4/4 aprovados (`e2e/tenancy.spec.ts`: SuperAdmin cria tenant + Admin; Admin cria Consulta; Consulta barrado em `/cliente/usuarios`; SuperAdmin seleciona tenant e vê usuários) |

- **Testes antes da feature**: 0 (repositório vazio em `8ae509d`)
- **Iteração 1**: 213 API + 118 Web = 331
- **Iteração 2**: 236 API + 121 Web = 357 (+26). Nenhum teste removido. Asserções alteradas: 401 → 403 `ACESSO_DESATIVADO` em `SituacaoTenantTestes`, `StatusAcessoCacheTestes`, `RefreshTestes`, `SolicitarExclusaoTestes` e `EditarUsuarioTestes`, todas alinhadas à spec (ficaram mais fortes: agora afirmam também o `codigo`). O teste de concorrência foi reescrito sem a tolerância a `DEADLOCK`, o que também o fortaleceu.
- **Ignorados**: nenhum

---

## Qualidade do código

| Princípio | Situação |
| --------- | -------- |
| Código mínimo, mudanças cirúrgicas, sem scope creep | ✅ `301bd50` mexe só em `Policies.cs` (flag em `HttpContext.Items` + `OnChallenge`), `RefreshTokens.cs`, interceptor e página de login |
| Segue os padrões (ProblemDetails, UPPER_SNAKE, constante `LoginEndpoint.CodigoAcessoDesativado` reaproveitada) | ✅ |
| Resultado ancorado na spec | ✅ divergência do edge case de TEN-05 resolvida; design.md registra a decisão do usuário |
| Cobertura por camada | ⚠️ USR-05 (inativação) e DEL-09 (Em execução) sem teste do ramo |
| Todo teste mapeia para AC, edge case ou Done-when | ✅ (o trigger `TESTE_LOTE_EXCLUSAO` é instrumentação de DEL-04, criada e apagada no `finally`) |
| Diretrizes | nenhuma no repositório; matriz de `tasks.md` aplicada |

---

## Gaps ranqueados (planos de correção)

1. **USR-05: inativar o único Admin não tem teste** (mutante M11 sobreviveu). *Causa*: só o rebaixamento é testado via HTTP; a condição `|| !ativo` em `src/Jamsoft.DashBoards.Api/Modulos/Usuarios/EditarUsuarioEndpoint.cs:38` pode ser removida sem que nada falhe. *Correção*: em `EditarUsuarioTestes.cs`, o único Admin envia `PUT /api/usuarios/{próprio}` com `perfil = "Admin"` e `ativo = false` → 422 `ULTIMO_ADMIN`, `title == "O tenant precisa de ao menos um Admin ativo"`, usuário continua ativo e com refresh tokens não revogados. *Pronto quando*: M11 morre. Prioridade: **Major** (é o caso do Independent Test da spec).
2. **DEL-09: ramo "Em execução" sem evidência.** *Correção*: em `SolicitarExclusaoTestes.cs`, semear um job `EmExecucao` para o tenant (como em `ProcessadorExclusaoTestes.NovoJobAsync`) e pedir a exclusão → 409 `EXCLUSAO_EM_ANDAMENTO` com o `jobId` desse job e nenhum job novo. Opcional: o mesmo no teste de esquema (`AuditoriaExclusaoEsquemaTestes.cs`), com um segundo job `Pendente` recusado quando já existe um `EmExecucao`. Prioridade: **Minor**.
3. **Observações (não reprovam):** AUTH-09 varre só os logs do fluxo de login (refresh e logout fora); o título "Acesso desativado" é afirmado no login de usuário inativo, mas não nas respostas 403 de sessão e renovação (só o `codigo`). Prioridade: Cosmetic.
4. **Fora do escopo (rastreamento):** ingestão do Coletor em TEN-05/TEN-06 e credencial de Coletor em AUD-01 ficam com `sincronizacao-vendas`. Não marcar como Verified nesta feature.

---

## Atualização de rastreabilidade (proposta; o Verifier não alterou o spec.md)

| Requisito | Novo status |
| --------- | ----------- |
| USR-05 | ❌ Needs Fix (inativação) |
| DEL-09 | ⚠️ Needs Fix (cobertura do ramo Em execução) |
| TEN-05, TEN-06, AUD-01 | ✅ Verified, exceto a parte do Coletor (adiada para `sincronizacao-vendas`) |
| Demais (49) | ✅ Verified |

---

## Lições candidatas (`lessons.py` não executado: Python não instalado)

1. `surviving_mutant`: quando uma regra protege mais de uma transição (rebaixar **ou** inativar), cada operando da condição precisa de um teste negativo próprio; testar só um deixa o outro removível sem falha. Fonte: M11, `EditarUsuarioEndpoint.cs:38`.
2. `spec_precision`: um AC com "A ou B" ("Pendente ou Em execução", "ativo ou inativo") pede um caso por ramo. A lição da iteração 1 sobre CTX-01 se repetiu em DEL-09. Fonte: DEL-09.
3. `process` (positiva, para reforçar): teste de concorrência sincronizado por estado observável (`sys.dm_exec_requests.blocking_session_id`), em vez de pausa, matou o mutante de lock em 6 de 6 execuções. Prefira isso a `Task.Delay`. Fonte: `RegrasUltimoAdminTestes.cs:61-99`.
4. `process` (sensor): ao ligar `node_modules` no worktree de mutação por junção, remova a junção (`rmdir`) antes do `git worktree remove --force`, para que a remoção recursiva não alcance o diretório real.

---

## Resumo

**Geral**: ❌ Not Ready. Faltam dois testes pequenos; nenhum defeito de produção foi encontrado.

**Verificação ancorada na spec**: 52/54 ACs batem com o resultado da spec; 1 GAP (USR-05, inativação), 1 parcial (DEL-09, Em execução); 0 spec-precision gaps; todos os 6 edge cases ✅
**Sensor**: 14/15 mutações mortas (M5 determinístico em 6/6)
**Gate**: API 236 ✅ / Web 121 ✅ / e2e 4 ✅ / build, format, lint e build-web verdes

**O que funciona**: autenticação completa, com o novo 403 `ACESSO_DESATIVADO` de ponta a ponta (API, renovação, interceptor e tela de login); isolamento (filtro, interceptor, contexto, auditoria de acesso cruzado, suíte por rota); regra do último Admin no rebaixamento e sob concorrência; exclusão em lotes de no máximo 5.000 com retomada; auditoria.

**Próximos passos**: levar os gaps 1 e 2 ao implementador como tarefas de correção e reexecutar o Verifier (iteração 3 de 3, a última antes de escalar ao usuário).
