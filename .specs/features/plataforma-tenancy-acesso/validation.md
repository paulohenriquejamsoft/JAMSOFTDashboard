# Plataforma — Tenancy, Usuários e Acesso — Validação (iteração 3 de 3)

**Data**: 2026-10-25
**Spec**: `.specs/features/plataforma-tenancy-acesso/spec.md`
**Faixa do diff**: `8ae509d..a4cc456` (a feature inteira, branch `feat/plataforma-tenancy-acesso`). Correções desde a iteração 2: `dce5cc9..a4cc456` (`dc785df`, `a4cc456`; só testes)
**Verifier**: sub-agente independente (autor ≠ verificador), regra evidence-or-zero. Re-derivei a cobertura a partir da spec. Da iteração 2 usei só a lista de gaps.

## Veredito: PASS ✅

- Todos os ACs no escopo têm evidência `file:line` e são afirmados pelo valor da spec: status e `codigo`, e a mensagem quando a spec a define.
- Os 6 edge cases estão cobertos.
- Das 13 mutações distintas, 13 morreram, incluindo M11 (USR-05, inativação) e duas formas de mutação do DEL-09 ("Em execução").
- Os gates estão verdes: build `-warnaserror`, format, 238 testes da API, lint, build, 121 testes e 4 e2e do Web.

Os dois gaps da iteração 2 foram fechados. Ficam 3 observações cosméticas, que não reprovam (ver "Gaps ranqueados").

---

## Iteração 2 → 3

| Gap da iteração 2 | Situação | Evidência nova |
| ----------------- | -------- | -------------- |
| USR-05: inativar o único Admin sem teste (M11 sobreviveu) | ✅ Fechado (`dc785df`). M11 agora morre | `Integracao/Usuarios/EditarUsuarioTestes.cs:163` `Assert.Equal(HttpStatusCode.UnprocessableEntity, …)`; `:165` `codigo == "ULTIMO_ADMIN"`; `:166` `title == "O tenant precisa de ao menos um Admin ativo"`; `:168-169` perfil Admin e `Ativo == true` mantidos; `:172` `Assert.Null(sessao.RevogadoEm)` |
| DEL-09: ramo "Em execução" sem evidência | ✅ Fechado (`a4cc456`). D1 (índice filtrado só com `Pendente`) e D2 (detecção do job ativo só com `Pendente`) morrem | `Integracao/Exclusao/SolicitarExclusaoTestes.cs:172` `Assert.Equal(HttpStatusCode.Conflict, …)`; `:174` `codigo == "EXCLUSAO_EM_ANDAMENTO"`; `:175` `jobId == emExecucao.IdPublico`; `:177` `Assert.Single(jobs).Id == emExecucao.Id` (nenhum job novo) |
| Cosmético: título "Acesso desativado" não afirmado nos 403 de sessão/renovação | ⚪ Continua (não reprova) | Só o `codigo` é afirmado (`RefreshTestes.cs:134,148`, `StatusAcessoCacheTestes.cs:70,86`, `SituacaoTenantTestes.cs:63`). O título vem de uma única linha de produção (`Policies.cs`, `ResponderAcessoDesativadoAsync`; `RefreshTokens.cs`), igual à do login, que é afirmada em `LoginTestes.cs:131` |
| Cosmético: AUTH-09 varre só os logs do login | ⚪ Continua (não reprova) | `LoginTestes.cs:161-166` |

Nenhum teste foi removido nem enfraquecido. Os dois commits só acrescentam testes (+73 linhas em 2 arquivos de teste; produção intocada).

---

## Conclusão das tarefas

| Tarefa | Situação | Observações |
| ------ | -------- | ----------- |
| T1–T58 | ✅ Concluídas | `tasks.md`: 183 `[x]`, 0 `[ ]` |
| Correções da iteração 1 | ✅ | `7203131`…`301bd50` (9 commits) |
| Correções da iteração 2 | ✅ | `dc785df`, `a4cc456` |

---

## Critérios de aceite ancorados na spec

Caminhos relativos a `tests/Jamsoft.DashBoards.Api.Testes/`, salvo indicação. `Web:` = `src/Jamsoft.DashBoards.Web/src/app/`.

### P1: Login e sessão

| AC | Resultado definido na spec | `file:line` + asserção | Resultado |
| -- | -------------------------- | ---------------------- | --------- |
| AUTH-01 | 200 + access de 15 min + refresh de 7 dias + perfil + `ID_PUBLICO` do tenant (nulo para SuperAdmin) | `Integracao/Autenticacao/LoginTestes.cs:50` `Equal(OK, …)`; `:56` `InRange(expiraEm, +15 min)`; `:57` `perfil`; `:60` `JsonValueKind.Null` (SuperAdmin); `:63` `tenant.idPublico == tenant.IdPublico`; `:82` `InRange(gravado.ExpiraEm, +7 dias)`; `Unidade/Seguranca/TokenServiceTestes.cs:62-63` `ValidTo == agora+15 min` | ✅ |
| AUTH-02 | 401 com a mesma mensagem "Credenciais inválidas" nos dois casos | `LoginTestes.cs:106-107` 401 ×2; `:110` `codigo == "CREDENCIAIS_INVALIDAS"`; `:111` `title == "Credenciais inválidas"`; `:115` corpos idênticos | ✅ |
| AUTH-03 | 5 falhas → bloqueio de 15 min e 423 durante o bloqueio | `BloqueioTestes.cs:61` 4 falhas = 401; `:89` `(HttpStatusCode)423`; `:91` `USUARIO_BLOQUEADO`; `:93` `bloqueadoAte ≈ +15 min`; `:108` senha correta → 423; `:121` 200 depois de 15 min | ✅ |
| AUTH-04 | novo par e o usado invalidado | `RefreshTestes.cs:60` 200; `:65` `NotEqual(antigo, novo)`; `:71` `NotNull(registroAntigo.UsadoEm)` | ✅ |
| AUTH-05 | 401 + revogação de todos os refresh ativos | `RefreshTestes.cs:88-89` 401 `NAO_AUTENTICADO`; `:91-92` 3 tokens, todos `RevogadoEm != null`; `:95-96` os demais respondem 401 | ✅ |
| AUTH-06 | a API revoga o refresh da sessão; o frontend descarta os tokens | `LogoutTestes.cs:46` 204; `:53` refresh antigo 401; `:58` `NotNull(revogado.RevogadoEm)`; `Web: core/auth/auth.service.spec.ts:57-58` `usuario()` e `accessToken` nulos | ✅ |
| AUTH-07 | 403 + "Acesso desativado" (usuário ou tenant inativo) | usuário: `LoginTestes.cs:128` 403, `:130` `ACESSO_DESATIVADO`, `:131` `title == "Acesso desativado"`; tenant: `:143-144` 403 `ACESSO_DESATIVADO` (mesmo `return AcessoDesativado()` em `LoginEndpoint.cs:67`); M12 morto | ✅ |
| AUTH-08 | 401 sem token válido; 403 com perfil fora da policy | `Integracao/Seguranca/PoliciesTestes.cs:57` `(401, "NAO_AUTENTICADO")`; `:66` expirado → 401; `:75` `(403, "ACESSO_NEGADO")`; `:103-104` Coletor → 403; `Arquitetura/RegrasArquiteturaTestes.cs:35` toda rota declara policy | ✅ |
| AUTH-09 | sem senhas ou tokens nos logs | `LoginTestes.cs:160` logs não vazios; `:163-166` `DoesNotContain(senha, senhaErrada, accessToken, refresh)` | ✅ (obs. cosmética: só o fluxo de login é varrido) |

### P1: SuperAdmin gerencia tenants

| AC | Resultado definido na spec | `file:line` + asserção | Resultado |
| -- | -------------------------- | ---------------------- | --------- |
| TEN-01 | 201 + `ID_PUBLICO` novo, Ativo; nome de 3–120; CNPJ válido | `Integracao/Tenants/CriarTenantTestes.cs:51` 201; `:54` `NotEqual(Guid.Empty, idPublico)`; `:55` sem `id`; `:58`/`:64` ativo; `:168` 3/120 → 201; `:182-185` 2/121 → 400 `VALIDACAO` `erros.nome`; `:151-155` CNPJ inválido → 400; `Unidade/Tenants/CnpjTestes.cs` | ✅ |
| TEN-02 | 409 sem criar registro | `CriarTenantTestes.cs:116-118` 409, `CNPJ_DUPLICADO`, contagem == 1 | ✅ |
| TEN-03 | primeiro Admin no mesmo fluxo, com troca obrigatória | `CriarTenantTestes.cs:92-96` e-mail, nome, `Perfil.Admin`, `TrocarSenha == true`; `:103-104` login com `trocarSenha == true` | ✅ |
| TEN-04 | paginada (20 por padrão, máximo 100); filtro por nome/CNPJ e situação | `TenantsEndpointsTestes.cs:66-69` 20 itens, total 25; `:84-85` `tamanho=500` → 100; `:101-103` nome/CNPJ; `:122-125` situação | ✅ |
| TEN-05 | bloqueia o login dos usuários; dados preservados | `SituacaoTenantTestes.cs:62-63` token já emitido → 403 `ACESSO_DESATIVADO`; `:65-66` login → 403 `ACESSO_DESATIVADO`; `:84-90` dados intactos. Ingestão do Coletor: **fora do escopo** (`sincronizacao-vendas`) | ✅ (parte do Coletor fora do escopo) |
| TEN-06 | restaura o login sem alterar dados | `SituacaoTenantTestes.cs:103-104` reativar 200, `ativo == true`; `:106` login 200; `:108` `/eu` 200. Ingestão: **fora do escopo** | ✅ (parte do Coletor fora do escopo) |
| TEN-07 | só `ID_PUBLICO` exposto | `CriarTenantTestes.cs:55` e `TenantsEndpointsTestes.cs:73` `TryGetProperty("id") == false` | ✅ |
| TEN-08 | quem não é SuperAdmin → 403 | `CriarTenantTestes.cs:198-200` 403 `ACESSO_NEGADO`, nada criado; `TenantsEndpointsTestes.cs:202-203`; `RegrasArquiteturaTestes.cs:47-48` toda rota `/api/admin` exige SuperAdmin | ✅ |

### P1: Empresas do tenant

| AC | Resultado definido na spec | `file:line` + asserção | Resultado |
| -- | -------------------------- | ---------------------- | --------- |
| EMP-01 | única por (`TENANT_ID`, `COD_EMPRESA`) | `Integracao/Esquema/EmpresaEsquemaTestes.cs:30-31` erro de índice único `IX_EMPRESA_TENANT_ID_COD_EMPRESA`; `:46` primeira coluna `TENANT_ID` | ✅ |
| EMP-02 | Admin ou SuperAdmin; código inteiro positivo; nome de 1–80 → 201 | Admin `EmpresasEndpointsTestes.cs:63,73-75`; SuperAdmin `:131-139` 201 no tenant escolhido, 0 no outro; 1/80 → `:169-171`; código 0 e nome de 81 → `:109-116` 400 `VALIDACAO`; vazio → `:151-156` | ✅ |
| EMP-03 | 409 duplicada | `EmpresasEndpointsTestes.cs:95-96` 409 `EMPRESA_DUPLICADA`; `:94` mesmo código em outro tenant → 201 | ✅ |
| EMP-04 | criação automática com o `Nome_Empresa` recebido | `Integracao/Empresas/ServicoEmpresasTestes.cs:43-47` (serviço; a rota de ingestão pertence a `sincronizacao-vendas`) | ✅ (serviço) |
| EMP-05 | o nome editado sobrevive às ingestões | `EmpresasEndpointsTestes.cs:228-233` `NomeManual == true`; `ServicoEmpresasTestes.cs:85-88` ingestão mantém "Nome do Admin" | ✅ |
| EMP-06 | só o tenant do token ou o selecionado | `EmpresasEndpointsTestes.cs:199` `["Própria 1","Própria 2"]`; `:214` `["Do escolhido"]` | ✅ |

### P1: Isolamento entre tenants

| AC | Resultado definido na spec | `file:line` + asserção | Resultado |
| -- | -------------------------- | ---------------------- | --------- |
| ISO-01 | tenant tirado das claims | `Unidade/Seguranca/TenantAtualTestes.cs:49-52`, `:70-72` (cabeçalho divergente mantém o tenant do token); I2 morto (11 testes) | ✅ |
| ISO-02 | filtro por `TENANT_ID` em todo CRUD | leitura `Integracao/Dados/FiltroTenantTestes.cs:38-39,49`; inserção, alteração e exclusão `TenantSaveChangesInterceptorTestes.cs:55,65-69,82-85,98-101`; ponta a ponta `IsolamentoTenantsTestes.cs:149`; I1 e I3 mortos | ✅ |
| ISO-03 | 404 para recurso de outro tenant | `IsolamentoTenantsTestes.cs:145-146` `(NotFound, "NAO_ENCONTRADO")` para toda rota `{idPublico}` pelo ID; rotas cobertas provadas em `:120-121`; `EditarUsuarioTestes.cs:213-214`; `RedefinirSenhaTestes.cs:134-135`; `EmpresasEndpointsTestes.cs:249-250` | ✅ |
| ISO-04 | tenant divergente (rota/cabeçalho ou corpo) → 403 + auditoria | `ValidacaoContextoTenantFilterTestes.cs:68` `(403, "TENANT_DIVERGENTE")`; `:70-73` `ACESSO_CRUZADO`, `Negado`; corpo `:98-99`; M9 morto | ✅ |
| ISO-05 | índice iniciado por `TENANT_ID` em toda tabela com dado de cliente | `IsolamentoTenantsTestes.cs:168-171` `Assert.Empty(semIndice)` | ✅ |

### P2: Admin do cliente gerencia usuários

| AC | Resultado definido na spec | `file:line` + asserção | Resultado |
| -- | -------------------------- | ---------------------- | --------- |
| USR-01 | 201, tenant do Admin, troca obrigatória | `UsuariosEndpointsTestes.cs:68` 201; `:77` `TenantId == tenant.Id`; `:79` `TrocarSenha == true`; política de senha `:210-213` 400 | ✅ |
| USR-02 | criar **ou promover** a SuperAdmin/Coletor → 403 | criar `UsuariosEndpointsTestes.cs:102-104` 403 `PERFIL_NAO_PERMITIDO`, nada criado; promover `EditarUsuarioTestes.cs:84-92` 403 `PERFIL_NAO_PERMITIDO`, nada alterado nem auditado | ✅ |
| USR-03 | 409 sem revelar o tenant | `UsuariosEndpointsTestes.cs:116` 409; `:118` `EMAIL_EM_USO`; `:119-121` sem o ID/nome do outro tenant | ✅ |
| USR-04 | aplica nome, perfil e situação + auditoria | `EditarUsuarioTestes.cs:58-65` aplicados; `:68-71` `USUARIO_ALTERADO`; situação `:190-191` `ativo == false` | ✅ (obs.: auditoria afirmada na edição de nome/perfil; a inativação passa pela mesma chamada incondicional `EditarUsuarioEndpoint.cs:47`) |
| USR-05 | 422 "O tenant precisa de ao menos um Admin ativo" (rebaixamento **ou inativação**) | rebaixamento `EditarUsuarioTestes.cs:134` 422, `:136` `ULTIMO_ADMIN`, `:137` título exato; **inativação** `:163` 422, `:165` `ULTIMO_ADMIN`, `:166` título exato, `:169` continua ativo, `:172` sessão não revogada; regra `RegrasUltimoAdminTestes.cs:37-39`; concorrência `:86-99`; M11 morto | ✅ |
| USR-06 | inativação revoga todos os refresh | `EditarUsuarioTestes.cs:194` refresh → 401; `:196-197` access → 403 `ACESSO_DESATIVADO`; `:200-201` todos `RevogadoEm != null` | ✅ |
| USR-07 | exige troca + revoga refresh | `RedefinirSenhaTestes.cs:60` `TrocarSenha`; `:63` login `trocarSenha == true`; `:103-106` refresh 401 e todos revogados | ✅ |
| USR-08 | com troca pendente, só troca e logout; 403 nas demais | `TrocarSenhaTestes.cs:62-63` 403 `TROCA_SENHA_PENDENTE`; `:67` troca 200; `PoliciesTestes.cs:84-85,93`; `LogoutTestes.cs:86-88` | ✅ |
| USR-09 | SuperAdmin: as mesmas operações em qualquer tenant | criar `UsuariosEndpointsTestes.cs:153-157`; listar `:172-173`; editar `EditarUsuarioTestes.cs:113-123`; redefinir `RedefinirSenhaTestes.cs:79-88` | ✅ |
| USR-10 | Consulta → 403 | `UsuariosEndpointsTestes.cs:137-140` 403 `ACESSO_NEGADO`; `IsolamentoTenantsTestes.cs:139-146` (PUT e redefinir com Consulta → `ACESSO_NEGADO`) | ✅ |

### P2: SuperAdmin acessa o contexto de um tenant

| AC | Resultado definido na spec | `file:line` + asserção | Resultado |
| -- | -------------------------- | ---------------------- | --------- |
| CTX-01 | aceita o `ID_PUBLICO` de tenant ativo **ou inativo** | ativo `EmpresasEndpointsTestes.cs:213-214`; inativo `Integracao/Seguranca/ContextoTenantInativoTestes.cs:55-58` 200 com os dados só do inativo | ✅ |
| CTX-02 | auditoria com SuperAdmin, tenant e rota | `AuditoriaContextoSuperAdminFilterTestes.cs:67` usuário; `:69` tenant; `:71` `"rota":"/api/empresas/"` | ✅ |
| CTX-03 | outro perfil: cabeçalho igual é ignorado; diferente → 403 | `TenantAtualTestes.cs:60-61`; `ValidacaoContextoTenantFilterTestes.cs:84-85` (igual → 200, sem auditoria); `:68` (diferente → 403 `TENANT_DIVERGENTE`) | ✅ |

### P2: Exclusão total de um tenant

| AC | Resultado definido na spec | `file:line` + asserção | Resultado |
| -- | -------------------------- | ---------------------- | --------- |
| DEL-01 | quem não é SuperAdmin → 403 | `SolicitarExclusaoTestes.cs:51-55` 403 `ACESSO_NEGADO`, tenant ativo, sem job; `ExclusoesEndpointsTestes.cs:167-170` | ✅ |
| DEL-02 | nome diferente → 422, nada iniciado | `SolicitarExclusaoTestes.cs:67` 422; `:68` `CONFIRMACAO_INVALIDA`; `:70-71` ativo, sem job | ✅ |
| DEL-03 | inativa, job "Pendente", 202 + id | `SolicitarExclusaoTestes.cs:87` 202; `:90` inativo; `:92-93` `jobId` e `Pendente`; `:100-101` token → 403 `ACESSO_DESATIVADO` | ✅ |
| DEL-04 | lotes de no máximo 5.000 por transação, filhos → pais, filtro por `TENANT_ID` | `Unidade/Exclusao/ExclusaoOptionsTestes.cs:28-30` padrão = máximo = 5000, `:48-49` acima disso é recusado; `ProcessadorExclusaoTestes.cs:190` `[5_000, 5_000, 2_000, 0]`, `:192` transações distintas; ordem `Unidade/Exclusao/PlanoExclusaoTestes.cs:22,31-35`; filtro `ProcessadorExclusaoTestes.cs:126-127` | ✅ |
| DEL-05 | "Concluída" + linhas por tabela + tenant removido | `ProcessadorExclusaoTestes.cs:126` zero linhas de A (inclusive `TENANT`); `:133` `Concluida`; `:136` contagem por tabela | ✅ |
| DEL-06 | "Falhou" com etapa e erro, tenant inativo, retomada | `ProcessadorExclusaoTestes.cs:220-223` `Falhou`, etapa, erro; `:232` inativo; retomada `ExclusoesEndpointsTestes.cs:111-132` | ✅ |
| DEL-07 | não altera outros tenants | `ProcessadorExclusaoTestes.cs:127` impressão de B idêntica | ✅ |
| DEL-08 | auditoria: solicitante, `ID_PUBLICO`, nome, situação | `SolicitarExclusaoTestes.cs:106-111`; `ProcessadorExclusaoTestes.cs:141-150` (`solicitadoPorIdPublico`, situações `EmExecucao`/`Concluida`); `:235-237` `Falhou`; `ExclusoesEndpointsTestes.cs:137-140` retomada | ✅ |
| DEL-09 | job "Pendente" **ou "Em execução"** → 409 | Pendente `SolicitarExclusaoTestes.cs:124-127`, `:141-144`; **Em execução** `:172` 409, `:174` `EXCLUSAO_EM_ANDAMENTO`, `:175` `jobId` do job em execução, `:177` nenhum job novo; esquema `Esquema/AuditoriaExclusaoEsquemaTestes.cs:38-39`; D1 e D2 mortos | ✅ |

### P3: Auditoria consultável

| AC | Resultado definido na spec | `file:line` + asserção | Resultado |
| -- | -------------------------- | ---------------------- | --------- |
| AUD-01 | eventos auditados | login de SuperAdmin `LoginTestes.cs:90`; tenant `CriarTenantTestes.cs:67`, `TenantsEndpointsTestes.cs:190`, `SituacaoTenantTestes.cs:124`; usuário `UsuariosEndpointsTestes.cs:86`, `EditarUsuarioTestes.cs:68`; empresa `EmpresasEndpointsTestes.cs:77,235`; senha `RedefinirSenhaTestes.cs:151`; contexto `AuditoriaContextoSuperAdminFilterTestes.cs:66-71`; exclusão `SolicitarExclusaoTestes.cs:106`, `ProcessadorExclusaoTestes.cs:141,235`. Credencial de Coletor: **fora do escopo** | ✅ (Coletor fora do escopo) |
| AUD-02 | UTC, autor, perfil, operação, recurso, tenant, IP, resultado, sem segredos | `Integracao/Auditoria/AuditoriaTestes.cs:125-133`; sem segredos `:110-114` | ✅ |
| AUD-03 | paginada; filtros de período, tenant, usuário e operação | `AuditoriaEndpointsTestes.cs:80` período; `:95` tenant; `:113-114` usuário; `:127` operação; `:151-155` combinados e paginados | ✅ |
| AUD-04 | sem rota de alteração ou exclusão | `AuditoriaEndpointsTestes.cs:181-183` 404/405; `Esquema/PermissoesAplicacaoTestes.cs:60-61` UPDATE/DELETE negados no banco | ✅ |

**Situação**: ✅ os 54 ACs têm evidência. A parte do Coletor fica fora do escopo em TEN-05, TEN-06 e AUD-01, e EMP-04 é verificado no serviço. Nenhum spec-precision gap: todo AC com status, `codigo` ou mensagem definidos é afirmado pelo valor exato. Regra payload/conjunction: nos ACs compostos, cada conjunto é afirmado por valor (USR-05: 422 + código + título + estado + sessões; DEL-03: 202 + inativo + Pendente + jobId; DEL-09: 409 + código + jobId + contagem de jobs).

---

## Edge cases

- [x] CNPJ concorrente → um 201 e um 409: `CriarTenantTestes.cs:212` `[Created, Conflict]`, `:213` `CNPJ_DUPLICADO`, `:215` um registro
- [x] Usuário inativado com access token válido: renovação recusada (`RefreshTestes.cs:133-134` 403 `ACESSO_DESATIVADO`; `EditarUsuarioTestes.cs:194` 401 com o token revogado); token vale no máximo 15 min (`TokenServiceTestes.cs:62-63`); revalidação a cada requisição (`StatusAcessoCacheTestes.cs:70`, `:118`)
- [x] Tenant inativado durante a sessão → 403: validação do token `SituacaoTenantTestes.cs:62-63`, `StatusAcessoCacheTestes.cs:86-87`; renovação `RefreshTestes.cs:147-150`; frontend `Web: core/auth/auth.interceptor.spec.ts:139-155`, `:157-173`; `Web: conta/login/login.page.spec.ts:63` `"Acesso desativado"`. M3, M4, M10, W1 e W2 mortos
- [x] Lista de tenants vazia → "Cadastrar tenant": `Web: admin/tenants/tenants-lista.page.spec.ts:115` `href == '/admin/tenants/novo'`; API `TenantsEndpointsTestes.cs:133-135`
- [x] `ID_PUBLICO` mal formatado → 400: `TenantsEndpointsTestes.cs:158-159` `REQUISICAO_INVALIDA`; `X-Tenant` mal formatado `ValidacaoContextoTenantFilterTestes.cs:121`
- [x] API reinicia durante a exclusão → retomada a partir da etapa salva: `ProcessadorExclusaoTestes.cs:257-266`

---

## Sensor de discriminação

**Isolamento**: `git worktree add --detach C:/Users/PauloHenrique/AppData/Local/Temp/vw3 HEAD` (caminho curto). `node_modules` do Web ligado por junção (`New-Item -ItemType Junction`) e removido com `rmdir` antes do `worktree remove`. Cada mutação foi aplicada com perl e conferida por `git diff` (arquivo realmente alterado). Em seguida rodou a suíte completa da API (`dotnet test Jamsoft.DashBoards.slnx`) ou do Web (Vitest), e depois `git checkout -- .`. Nenhuma execução teve erro de compilação ou de infraestrutura (SNI, login ou timeout). M4 e I3 não casaram na primeira tentativa por causa do `\r` de fim de linha (CRLF). Reapliquei com regex tolerante ao CRLF e conferi o diff antes de rodar.
**Linha de base sem mutação**: API 238/238 ✅, Web 121/121 ✅.

| # | Arquivo de produção | Descrição | Morto? | Testes que mataram |
| - | ------------------- | --------- | ------ | ------------------ |
| M11 | `src/Jamsoft.DashBoards.Api/Modulos/Usuarios/EditarUsuarioEndpoint.cs:38` | regra do último Admin só no rebaixamento (`\|\| !ativo` removido) | ✅ (1) | `EditarUsuarioTestes.Inativar_o_unico_Admin_responde_422_ULTIMO_ADMIN…` |
| D1 (DEL-09) | `…/Modulos/Exclusao/ExclusaoTenant.cs:46` + migração `20261024175759_AuditoriaExclusaoTenant.cs:83` + `.Designer.cs:285` + `PlataformaDbContextModelSnapshot.cs:282` | índice único filtrado `[SITUACAO] IN ('Pendente','EmExecucao')` → `[SITUACAO] = 'Pendente'`. Escolhi esta forma porque o 409 nasce do índice no banco (os testes aplicam as migrações) e o teste novo deveria pegá-la. Mudei os 4 arquivos juntos, como se a migração tivesse sido gerada a partir da configuração | ✅ (1): `Expected: Conflict, Actual: Accepted` | `SolicitarExclusaoTestes.Solicitacao_com_job_EmExecucao…` |
| D2 (DEL-09) | `…/Modulos/Exclusao/SolicitarExclusaoEndpoint.cs` (catch do 409) | busca do job ativo para o `jobId` só com `Pendente` | ✅ (1): `jobId` nulo | `SolicitarExclusaoTestes.Solicitacao_com_job_EmExecucao…` |
| M9 | `…/Seguranca/ValidacaoContextoTenantFilter.cs:38` | 403 sem gravar a auditoria (ISO-04) | ✅ (2) | `ValidacaoContextoTenantFilterTestes` ×2 |
| M10 | `…/Seguranca/Policies.cs` (`RevalidarAcessoAsync`) | revalidação por requisição ignora o tenant (passa `null`) | ✅ (3) | `StatusAcessoCacheTestes.Tenant_inativado…`, `SituacaoTenantTestes.Apos_inativar…`, `SolicitarExclusaoTestes.Confirmacao_valida…` |
| M3 | `…/Seguranca/Policies.cs` (`ResponderAcessoDesativadoAsync`) | `OnChallenge` nunca responde → 403 `ACESSO_DESATIVADO` volta a 401 | ✅ (6) | `StatusAcessoCacheTestes` ×3, `SituacaoTenantTestes`, `SolicitarExclusaoTestes`, `EditarUsuarioTestes.Inativar_usuario…` |
| M4 | `…/Seguranca/RefreshTokens.cs:73` | renovação deixa passar tenant inativo | ✅ (1) | `RefreshTestes.Refresh_com_tenant_inativado_durante_a_sessao…` |
| M12 | `…/Modulos/Autenticacao/LoginEndpoint.cs:67` | login deixa entrar usuário de tenant inativo | ✅ (3) | `LoginTestes.Usuario_de_tenant_inativo…`, `SituacaoTenantTestes` ×2 |
| I1 (isolamento) | `…/Infraestrutura/Dados/PlataformaDbContext.cs:98` | filtro global de tenant (`HasQueryFilter`) removido | ✅ (17) | `FiltroTenantTestes` ×2, `EmpresasEndpointsTestes`, `ServicoEmpresasTestes`… |
| I2 (isolamento) | `…/Seguranca/TenantAtual.cs:113-115` | `X-Tenant` de Admin/Consulta substitui o tenant do token e não marca divergência | ✅ (11) | suíte de isolamento por rota, `ValidacaoContextoTenantFilterTestes`, `TenantAtualTestes` ×2 |
| I3 (isolamento) | `…/Infraestrutura/Dados/TenantSaveChangesInterceptor.cs:60` | interceptor só acusa acesso cruzado em operação global (na prática, nunca) | ✅ (4) | `TenantSaveChangesInterceptorTestes` ×3, `AcessoCruzadoTestes` |
| W1 | `src/Jamsoft.DashBoards.Web/src/app/core/auth/auth.interceptor.ts:50` | interceptor não trata o 403 `ACESSO_DESATIVADO` | ✅ (1) | `auth.interceptor.spec.ts` "403 ACESSO_DESATIVADO encerra a sessão…" |
| W2 | `…/core/auth/auth.interceptor.ts:58` | renovação com 403 `ACESSO_DESATIVADO` → login sem aviso | ✅ (1) | `auth.interceptor.spec.ts` "401 e renovação respondendo 403…" |

**Profundidade do sensor**: P0/crítica (injeção manual ampliada; 13 mutações distintas: 11 na API, 2 no Web)
**Resultado**: 13/13 mortos. **PASS ✅**

**Isolamento verificado**: o `git status --porcelain` da árvore real estava vazio antes do sensor e continuou vazio depois de `git worktree remove --force` + `git worktree prune`. `git worktree list` mostra só a árvore principal, o diretório `vw3` não existe mais e o `node_modules` real está intacto.

---

## Gate check

Gate de build (`tasks.md`), executado uma vez na árvore real sem alterações (HEAD `a4cc456`):

| Comando | Resultado |
| ------- | --------- |
| `dotnet build Jamsoft.DashBoards.slnx -warnaserror --no-incremental` | ✅ 0 avisos, 0 erros |
| `dotnet format Jamsoft.DashBoards.slnx --verify-no-changes` | ✅ exit 0 |
| `dotnet test Jamsoft.DashBoards.slnx` (`TESTES_SQLSERVER` local) | ✅ 238 aprovados, 0 com falha, 0 ignorados (34 s), na primeira execução |
| `npm --prefix src/Jamsoft.DashBoards.Web run lint` | ✅ "All files pass linting." |
| `npm --prefix src/Jamsoft.DashBoards.Web run build` | ✅ bundle gerado |
| `npm --prefix src/Jamsoft.DashBoards.Web test -- --watch=false` | ✅ 17 arquivos, 121 testes |
| `npm --prefix src/Jamsoft.DashBoards.Web run e2e` | ✅ 4/4 (`e2e/tenancy.spec.ts`) |

- **Testes antes da feature**: 0 (repositório vazio em `8ae509d`)
- **Iteração 2**: 236 API + 121 Web = 357
- **Iteração 3**: 238 API + 121 Web = 359 (+2). Nenhum teste removido ou enfraquecido.
- **Ignorados**: nenhum

---

## Qualidade do código

| Princípio | Situação |
| --------- | -------- |
| Código mínimo, mudanças cirúrgicas, sem scope creep | ✅ as correções desta iteração são só testes e seguem os helpers existentes (`TenantComAdminAsync`, `EstadoAsync`, `Sessao.JsonAsync`) |
| Segue os padrões | ✅ o teste do DEL-09 encerra o job semeado no `finally`, para não deixar job ativo no banco compartilhado da coleção |
| Resultado ancorado na spec | ✅ |
| Cobertura por camada (domínio 1:1; rotas com caminho feliz, borda e erro) | ✅ |
| Todo teste mapeia para AC, edge case ou Done-when | ✅ |
| Diretrizes | nenhuma no repositório; matriz de `tasks.md` aplicada |

---

## Gaps ranqueados

Nenhum gap que reprove.

Observações cosméticas (não reprovam):
1. **AUTH-07 / edge de TEN-05**: o título "Acesso desativado" é afirmado só no login de usuário inativo (`LoginTestes.cs:131`). Nos 403 de sessão (`Policies.cs`), de renovação (`RefreshTokens.cs`) e do login com tenant inativo, só o `codigo` é afirmado. *Sugestão*: acrescentar `title == "Acesso desativado"` em `StatusAcessoCacheTestes.cs:70`, `RefreshTestes.cs:148` e `LoginTestes.cs:144`.
2. **AUTH-09**: a varredura de segredos nos logs cobre só o login (`LoginTestes.cs:149-166`). Refresh, logout, troca e redefinição de senha não são varridos. *Sugestão*: repetir a varredura de `LogsCapturados` nesses fluxos.
3. **USR-04/AUD-01**: a auditoria é afirmada na edição de nome/perfil (`EditarUsuarioTestes.cs:68-71`), mas não especificamente na inativação (`"ativo":false`). A chamada é única e incondicional (`EditarUsuarioEndpoint.cs:47`), então não há ramo descoberto.

Fora do escopo (rastreamento): a ingestão do Coletor em TEN-05/TEN-06, a rota de ingestão em EMP-04 e a credencial de Coletor em AUD-01 ficam com `sincronizacao-vendas`.

---

## Atualização de rastreabilidade (proposta; o Verifier não alterou o spec.md)

| Requisito | Novo status |
| --------- | ----------- |
| TEN-05, TEN-06, AUD-01 | ✅ Verified, exceto a parte do Coletor (fica para `sincronizacao-vendas`) |
| EMP-04 | ✅ Verified no serviço (a rota de ingestão fica para `sincronizacao-vendas`) |
| Demais (50) | ✅ Verified |

---

## Lições candidatas (`lessons.py` não executado: Python não instalado)

Não houve sinal reprovador nesta iteração (nenhum mutante sobreviveu, nenhum AC descoberto). As lições abaixo vêm do ciclo de correção das iterações 2 → 3 e da execução do sensor:

1. `surviving_mutant` (confirmada): quando uma regra protege mais de uma transição (rebaixar **ou** inativar), cada operando da condição precisa de um teste negativo próprio. A correção `dc785df` matou M11. Fonte: `EditarUsuarioEndpoint.cs:38`.
2. `spec_precision` (confirmada): um AC com "A ou B" ("Pendente ou Em execução") pede um caso por ramo. Quando a regra vive num índice filtrado, a mutação certa é no filtro da migração aplicada ao banco de teste, não só na configuração do EF. Fonte: D1/DEL-09.
3. `process` (sensor): em repositórios com fim de linha CRLF, um regex de mutação ancorado em `$` não casa. Confira o `git diff` de cada mutação antes de rodar a suíte e trate "nada mudou" como mutação não aplicada, nunca como mutante morto. Fonte: M4 e I3, primeira tentativa.
4. `process` (sensor): remover a junção de `node_modules` antes de `git worktree remove --force` mantém o diretório real intacto (reconfirmado).

---

## Resumo

**Geral**: ✅ Ready.

**Verificação ancorada na spec**: 54/54 ACs batem com o resultado da spec (3 com a parte do Coletor fora do escopo; EMP-04 no serviço); 0 spec-precision gaps; 6/6 edge cases ✅
**Sensor**: 13/13 mutações mortas (M11, D1, D2, M9, M10, M3, M4, M12, I1, I2, I3, W1, W2)
**Gate**: API 238 ✅ / Web 121 ✅ / e2e 4 ✅ / build `-warnaserror`, format, lint e build do Web verdes

**O que funciona**: autenticação completa, com o 403 `ACESSO_DESATIVADO` de ponta a ponta; isolamento (filtro de leitura, interceptor de gravação, contexto, auditoria de acesso cruzado, suíte por rota); regra do último Admin no rebaixamento, na inativação e sob concorrência; exclusão em lotes de no máximo 5.000, com retomada e 409 para job Pendente ou Em execução; auditoria.

**Próximos passos**: marcar a feature como concluída. As 3 observações cosméticas podem virar tarefas opcionais.
