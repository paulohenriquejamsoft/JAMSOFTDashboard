# Plataforma — Tenancy, Usuários e Acesso — Validation

**Date**: 2026-10-24
**Spec**: `.specs/features/plataforma-tenancy-acesso/spec.md`
**Diff range**: `8ae509d..bfa741e` (branch `feat/plataforma-tenancy-acesso`; 8ae509d = specs only, 63 feature commits)
**Verifier**: independent sub-agent (author ≠ verifier), evidence-or-zero

## Verdict: FAIL ❌

Gates are green and 9 of the 11 mutants were killed. The verdict is FAIL for three reasons. A spec edge case has a test that asserts a status different from the spec (401 vs 403). Two mutants survived (DEL-04 batch cap, USR-02 promotion via edit). Three ACs are only partly covered (CTX-01 inactive tenant, USR-09, EMP-02 SuperAdmin).

---

## Task Completion

| Task | Status | Notes |
| ---- | ------ | ----- |
| T1–T58 | ✅ Done | `tasks.md`: 0 checkboxes open; one commit per task in the range |

---

## Spec-Anchored Acceptance Criteria

Paths are relative to `tests/Jamsoft.DashBoards.Api.Testes/` unless noted otherwise. `Web:` = `src/Jamsoft.DashBoards.Web/`.

### P1: Login e sessão

| AC | Spec-defined outcome | `file:line` + assertion | Result |
| -- | -------------------- | ----------------------- | ------ |
| AUTH-01 | 200 + access 15 min + refresh 7 d + perfil + tenant `ID_PUBLICO` (null for SuperAdmin) | `Integracao/Autenticacao/LoginTestes.cs:50` `Assert.Equal(HttpStatusCode.OK, …)`; `:56` `InRange(expiraEm, antes+15min…)`; `:57` perfil; `:60` tenant `JsonValueKind.Null`; `:63` tenant `idPublico`; `:82` `InRange(gravado.ExpiraEm, +7 dias)`; `Unidade/Seguranca/TokenServiceTestes.cs:62-63` `ExpiraEm == agora+15min`, `jwt.ValidTo` | ✅ PASS |
| AUTH-02 | 401 + same message "Credenciais inválidas" in both cases | `LoginTestes.cs:106-107` 401 ×2; `:110` `codigo == "CREDENCIAIS_INVALIDAS"`; `:111` `title == "Credenciais inválidas"`; `:115` identical body | ✅ PASS |
| AUTH-03 | 5 consecutive failures → blocked for 15 min, 423 during the block | `Integracao/Autenticacao/BloqueioTestes.cs:89` `(HttpStatusCode)423`; `:91` `USUARIO_BLOQUEADO`; `:93` `bloqueadoAte` = +15 min; `:97` 423 at +14 min; `:108` correct password → 423; `:121` 200 after 15 min; `:70-79` counter resets after success | ✅ PASS |
| AUTH-04 | new token pair, used token invalidated | `Integracao/Autenticacao/RefreshTestes.cs:60` 200; `:65` `NotEqual(antigo, novo)`; `:71` `NotNull(registroAntigo.UsadoEm)`; `:72-73` new token valid | ✅ PASS |
| AUTH-05 | 401 + revoke all of the user's active refresh tokens | `RefreshTestes.cs:88` 401; `:89` `NAO_AUTENTICADO`; `:91-92` 3 tokens, all `RevogadoEm != null`; `:95-96` other sessions → 401 | ✅ PASS |
| AUTH-06 | API revokes the session's refresh token; frontend discards tokens | `Integracao/Autenticacao/LogoutTestes.cs:46` 204; `:53` old refresh → 401; `:58` `NotNull(revogado.RevogadoEm)`; `:72-73` cookie expired; `Web: src/app/core/auth/auth.service.spec.ts:57-58` `usuario()` and `accessToken` null | ✅ PASS |
| AUTH-07 | 403 "Acesso desativado" (inactive user or tenant) | `LoginTestes.cs:128-131` 403 + `ACESSO_DESATIVADO` + `title == "Acesso desativado"`; `:143-144` inactive tenant 403 + `ACESSO_DESATIVADO` (title not asserted on this path); `Integracao/Tenants/SituacaoTenantTestes.cs:64-65` | ✅ PASS |
| AUTH-08 | 401 without a valid token; 403 when the profile does not match the policy | `Integracao/Seguranca/PoliciesTestes.cs:57` `(401, "NAO_AUTENTICADO")`; `:66` expired token 401; `:75` `(403, "ACESSO_NEGADO")`; `:103-104` Coletor 403; `Arquitetura/RegrasArquiteturaTestes.cs:35` every route declares a policy | ✅ PASS |
| AUTH-09 | no passwords or tokens in logs | `LoginTestes.cs:161-167` `DoesNotContain(senha / senhaErrada / accessToken / refresh)` over the captured logs (login flow only; refresh/logout logs not scanned) | ✅ PASS (note) |

### P1: SuperAdmin gerencia tenants

| AC | Spec-defined outcome | `file:line` + assertion | Result |
| -- | -------------------- | ----------------------- | ------ |
| TEN-01 | 201 + `ID_PUBLICO`, Active; name 3–120; valid unique CNPJ | `Integracao/Tenants/CriarTenantTestes.cs:51` 201; `:54` `idPublico != Empty`; `:55` no `id`; `:58` `ativo == true`; `:151-154` name "AB" → 400 `VALIDACAO` `erros.nome`; `Unidade/Tenants/CnpjTestes.cs:14-55` | ⚠️ PASS with boundary gap: 3 chars (accepted), 120 (accepted) and 121 (rejected) are not tested |
| TEN-02 | 409 without creating a row | `CriarTenantTestes.cs:116-118` 409 `CNPJ_DUPLICADO`, count == 1 | ✅ PASS |
| TEN-03 | first Admin in the same flow, password change required | `CriarTenantTestes.cs:86-96` Admin, `TrocarSenha == true`; `:103-104` login `trocarSenha == true`; `:135-139` rollback | ✅ PASS |
| TEN-04 | pagination default 20 / max 100; filter by name/CNPJ and status | `Integracao/Tenants/TenantsEndpointsTestes.cs:66-69` 20 items, `tamanho == 20`; `:84-85` `tamanho=500` → 100; `:101-103` name/CNPJ; `:122-125` status | ✅ PASS |
| TEN-05 | blocks user login and Coletor ingestion; data preserved | `SituacaoTenantTestes.cs:59-65` login 403 `ACESSO_DESATIVADO`, token 401; `:83-89` data intact. Coletor: no evidence (ingestion belongs to `sincronizacao-vendas`) | ⚠️ Partial (Coletor deferred) |
| TEN-06 | restores login and ingestion without changing data | `SituacaoTenantTestes.cs:102-107` reactivate 200, login 200, `/eu` 200. Coletor: no evidence (deferred) | ⚠️ Partial (Coletor deferred) |
| TEN-07 | tenant exposed only by `ID_PUBLICO` | `CriarTenantTestes.cs:55` and `TenantsEndpointsTestes.cs:73` `TryGetProperty("id") == false`; `:158-159` malformed GUID → 400 `REQUISICAO_INVALIDA` | ✅ PASS |
| TEN-08 | non-SuperAdmin → 403 on any tenant route | `CriarTenantTestes.cs:167-169` 403 `ACESSO_NEGADO`; `TenantsEndpointsTestes.cs:202-203`; `RegrasArquiteturaTestes.cs:47-48` every `/api/admin*` route requires the `SuperAdmin` policy | ✅ PASS |

### P1: Empresas do tenant

| AC | Spec-defined outcome | `file:line` + assertion | Result |
| -- | -------------------- | ----------------------- | ------ |
| EMP-01 | unique by (`TENANT_ID`, `COD_EMPRESA`) | `Integracao/Esquema/EmpresaEsquemaTestes.cs:30-31` `ErroIndiceUnico`, `IX_EMPRESA_TENANT_ID_COD_EMPRESA` | ✅ PASS |
| EMP-02 | Admin **or SuperAdmin** create; positive integer code; name 1–80 → 201 | `Integracao/Empresas/EmpresasEndpointsTestes.cs:63-75` Admin 201 + tenant bound; `:109-116` code 0 / name 81 → 400 `VALIDACAO`. No test: SuperAdmin creation (201), empty name, name of 80 | ⚠️ Partial |
| EMP-03 | 409 for duplicate | `EmpresasEndpointsTestes.cs:95-96` 409 `EMPRESA_DUPLICADA` (same code in another tenant accepted, `:94`) | ✅ PASS |
| EMP-04 | auto-create with the received `Nome_Empresa` | `Integracao/Empresas/ServicoEmpresasTestes.cs:43-47` single row, `Nome == "Filial Centro"`, `NomeManual == false` (service level; the ingestion route is in `sincronizacao-vendas`) | ✅ PASS (service) |
| EMP-05 | edited name survives later ingestions | `EmpresasEndpointsTestes.cs:175-180` rename → `NomeManual == true`; `ServicoEmpresasTestes.cs:85-88` ingestion keeps "Nome do Admin" | ✅ PASS |
| EMP-06 | only the token's tenant (or the selected one for SuperAdmin) | `EmpresasEndpointsTestes.cs:146` `["Própria 1","Própria 2"]`; `:161` SuperAdmin with X-Tenant `["Do escolhido"]` | ✅ PASS |

### P1: Isolamento entre tenants

| AC | Spec-defined outcome | `file:line` + assertion | Result |
| -- | -------------------- | ----------------------- | ------ |
| ISO-01 | tenant comes from token claims | `Unidade/Seguranca/TenantAtualTestes.cs:49-52`, `:70-72` divergent header keeps the token's tenant | ✅ PASS |
| ISO-02 | `TENANT_ID` filter on read/insert/update/delete | `Integracao/Dados/FiltroTenantTestes.cs:38-39`, `:49`; `Integracao/Dados/TenantSaveChangesInterceptorTestes.cs:65-69`, `:82-85`, `:98-101` `ThrowsAsync<AcessoCruzadoException>` + nothing written; `Integracao/IsolamentoTenantsTestes.cs:149` tenant B fingerprint unchanged | ✅ PASS |
| ISO-03 | 404 for another tenant's resource (by `ID_PUBLICO`) | `IsolamentoTenantsTestes.cs:139-146` `(404, NAO_ENCONTRADO)` on every route with `{idPublico}`; `Integracao/Usuarios/EditarUsuarioTestes.cs:127-128`; `Integracao/Usuarios/RedefinirSenhaTestes.cs:111-112`; `EmpresasEndpointsTestes.cs:196-197` | ✅ PASS |
| ISO-04 | divergent tenant id in body/route → 403 + audit | `Integracao/Seguranca/ValidacaoContextoTenantFilterTestes.cs:68-74` `(403, TENANT_DIVERGENTE)` + `ACESSO_CRUZADO`/`Negado`; `:98-99` body; `Integracao/Seguranca/AcessoCruzadoTestes.cs:49-59` | ✅ PASS |
| ISO-05 | index led by `TENANT_ID` on every table with client data | `IsolamentoTenantsTestes.cs:168-171` `Assert.Empty(semIndice)` | ✅ PASS |

### P2: Admin do cliente gerencia usuários

| AC | Spec-defined outcome | `file:line` + assertion | Result |
| -- | -------------------- | ----------------------- | ------ |
| USR-01 | 201, Admin's tenant, password change required | `Integracao/Usuarios/UsuariosEndpointsTestes.cs:68-80` 201, `TenantId == tenant.Id`, `TrocarSenha` | ✅ PASS |
| USR-02 | **create or promote** to SuperAdmin/Coletor → 403 | create: `UsuariosEndpointsTestes.cs:102-104` 403 `PERFIL_NAO_PERMITIDO`. Promote via `PUT`: **no evidence** (mutant M10 survived) | ❌ GAP |
| USR-03 | 409 without revealing the tenant | `UsuariosEndpointsTestes.cs:116-121` 409 `EMAIL_EM_USO`, body without the other tenant's id/name | ✅ PASS |
| USR-04 | applies name/profile/status + audit | `EditarUsuarioTestes.cs:58-71` 200, persisted, `USUARIO_ALTERADO`; status `:106-107` | ✅ PASS |
| USR-05 | 422 "O tenant precisa de ao menos um Admin ativo" | `EditarUsuarioTestes.cs:82-85` 422 `ULTIMO_ADMIN` + exact title (demotion); `Integracao/Usuarios/RegrasUltimoAdminTestes.cs:37-39`, `:101-104` concurrency. HTTP inactivation of the last Admin is covered only by the shared rule | ✅ PASS (note) |
| USR-06 | inactivation revokes all refresh tokens | `EditarUsuarioTestes.cs:109-115` refresh 401, `/eu` 401, all `RevogadoEm != null` | ✅ PASS |
| USR-07 | requires password change + revokes refresh tokens | `RedefinirSenhaTestes.cs:59-65` `TrocarSenha == true`, login `trocarSenha`; `:80-83` refresh 401 + all revoked | ✅ PASS |
| USR-08 | while pending: only password change and logout; 403 on everything else | `Integracao/Autenticacao/TrocarSenhaTestes.cs:62-63` 403 `TROCA_SENHA_PENDENTE`; `:67` password change 200; `PoliciesTestes.cs:84-85`, `:93`; logout is `AllowAnonymous` (`LogoutTestes.cs:86-88`, 204 without bearer) | ✅ PASS |
| USR-09 | SuperAdmin: **same operations** in any selected tenant | `UsuariosEndpointsTestes.cs:153-157` create only. List/edit/reset by SuperAdmin: no API evidence (e2e `Web: e2e/tenancy.spec.ts:126-128` lists users, e2e not run) | ⚠️ Partial |
| USR-10 | Consulta → 403 on user management | `UsuariosEndpointsTestes.cs:137-140` GET/POST 403 `ACESSO_NEGADO`; `IsolamentoTenantsTestes.cs:139-140` PUT/reset-password with Consulta → `(403, ACESSO_NEGADO)` | ✅ PASS |

### P2: SuperAdmin acessa o contexto de um tenant

| AC | Spec-defined outcome | `file:line` + assertion | Result |
| -- | -------------------- | ----------------------- | ------ |
| CTX-01 | accepts the `ID_PUBLICO` of an **active or inactive** tenant as context | active: `EmpresasEndpointsTestes.cs:160-161`, `UsuariosEndpointsTestes.cs:153-155`. Inactive tenant: **no evidence** | ⚠️ Partial (GAP for inactive tenant) |
| CTX-02 | audit with SuperAdmin, tenant and route | `Integracao/Seguranca/AuditoriaContextoSuperAdminFilterTestes.cs:66-71` user, tenant, `"rota":"/api/empresas/"` (dedupe of 10 min, a design choice, `:83`, `:95`) | ✅ PASS |
| CTX-03 | non-SuperAdmin: equal header ignored, different header → 403 | `TenantAtualTestes.cs:60-61`, `:70`; `ValidacaoContextoTenantFilterTestes.cs:68`, `:84-85` | ✅ PASS |

### P2: Exclusão total de um tenant

| AC | Spec-defined outcome | `file:line` + assertion | Result |
| -- | -------------------- | ----------------------- | ------ |
| DEL-01 | non-SuperAdmin → 403 | `Integracao/Exclusao/SolicitarExclusaoTestes.cs:51-55` 403 `ACESSO_NEGADO`, no job; `Integracao/Exclusao/ExclusoesEndpointsTestes.cs:167-170` | ✅ PASS |
| DEL-02 | name not exact → 422 without starting | `SolicitarExclusaoTestes.cs:67-71` 422 `CONFIRMACAO_INVALIDA` (case, trailing space), tenant active, no job | ✅ PASS |
| DEL-03 | inactivates immediately, job "Pendente", 202 + job id | `SolicitarExclusaoTestes.cs:87-99` 202, `jobId`, `Ativo == false`, `Pendente`, token 401 | ✅ PASS |
| DEL-04 | batches of **at most 5,000** rows per transaction, children → parents, filtered by `TENANT_ID` | order: `Unidade/Exclusao/PlanoExclusaoTestes.cs:22`, `:31-35`; filter: `Integracao/Exclusao/ProcessadorExclusaoTestes.cs:122-123`. 5,000 cap: **no evidence** (tests use batch=400; mutant M9 survived) | ❌ GAP (batch limit) |
| DEL-05 | "Concluída" + rows per table + tenant removed | `ProcessadorExclusaoTestes.cs:129-134` `Concluida`, dictionary per table incl. `TENANT = 1`; `:122` zero rows | ✅ PASS |
| DEL-06 | "Falhou" with step and error, tenant stays inactive, resume | `ProcessadorExclusaoTestes.cs:170-182` `Falhou`, `EtapaAtual`, error, `ATIVO == false`; `ExclusoesEndpointsTestes.cs:111-132` resume 202 continues from the step | ✅ PASS |
| DEL-07 | does not touch other tenants | `ProcessadorExclusaoTestes.cs:123` tenant B fingerprint identical; `:124-126` | ✅ PASS |
| DEL-08 | audit: requester, `ID_PUBLICO`, name, new status | `SolicitarExclusaoTestes.cs:103-108`; `ProcessadorExclusaoTestes.cs:137-146` (requester in `solicitadoPorIdPublico`), `:185-187` | ✅ PASS |
| DEL-09 | existing "Pendente"/"Em execução" job → 409 | `SolicitarExclusaoTestes.cs:121-124` 409 `EXCLUSAO_EM_ANDAMENTO` (Pendente); `:138-141` returns `jobId`. "Em execução" relies on the filtered index (`Integracao/Esquema/AuditoriaExclusaoEsquemaTestes.cs:38-39` only tests Pendente) | ✅ PASS (note) |

### P3: Auditoria consultável

| AC | Spec-defined outcome | `file:line` + assertion | Result |
| -- | -------------------- | ----------------------- | ------ |
| AUD-01 | lists the audited events | SuperAdmin login `LoginTestes.cs:90`; tenant `CriarTenantTestes.cs:67`, `TenantsEndpointsTestes.cs:190-191`, `SituacaoTenantTestes.cs:123`; user `UsuariosEndpointsTestes.cs:86`, `EditarUsuarioTestes.cs:68`; company `EmpresasEndpointsTestes.cs:77`, `:182`; reset `RedefinirSenhaTestes.cs:128`; context `AuditoriaContextoSuperAdminFilterTestes.cs:66`; deletion `ProcessadorExclusaoTestes.cs:137`, `:185`. Coletor credential: deferred (`sincronizacao-vendas`) | ✅ PASS (Coletor deferred) |
| AUD-02 | UTC, author, profile, operation, resource, tenant, IP, result, no secrets | `Integracao/Auditoria/AuditoriaTestes.cs:125-133` all fields; `:110-114` without password/token/secret | ✅ PASS |
| AUD-03 | paginated, filters for period/tenant/user/operation | `Integracao/Auditoria/AuditoriaEndpointsTestes.cs:80`, `:95`, `:113-114`, `:127`, `:151-155` | ✅ PASS |
| AUD-04 | no route to change or delete | `AuditoriaEndpointsTestes.cs:181-186` PUT/DELETE → 404/405, record unchanged; `Integracao/Esquema/PermissoesAplicacaoTestes.cs:59-62` UPDATE/DELETE denied | ✅ PASS |

**Status**: ❌ gaps present. Of 54 ACs: 43 ✅, 2 ❌ (USR-02, DEL-04), 6 ⚠️ partial (TEN-01, TEN-05, TEN-06, EMP-02, USR-09, CTX-01). 3 have notes and count as covered.

---

## Edge Cases

- [x] Concurrent CNPJ → one 201 and one 409: `CriarTenantTestes.cs:181-184`
- [x] User inactivated while holding a valid token: renewals refused and token valid ≤ 15 min: `EditarUsuarioTestes.cs:109-111`, `TokenServiceTestes.cs:62-63`, `Integracao/Seguranca/StatusAcessoCacheTestes.cs:70`
- [ ] **Tenant inactivated during a session → spec: 403.** The tests assert **401** (`SituacaoTenantTestes.cs:62` `Assert.Equal(HttpStatusCode.Unauthorized, depois.StatusCode)`; `StatusAcessoCacheTestes.cs:86-87` `(401, "NAO_AUTENTICADO")`). The design (`OnTokenValidated` → 401) diverged from the spec with no `SPEC_DEVIATION` marker and no AD in STATE.md. **Asserted value ≠ spec outcome.**
- [x] Empty tenant list → "Cadastrar tenant": `Web: src/app/admin/tenants/tenants-lista.page.spec.ts:113-116`
- [x] Malformed `ID_PUBLICO` → 400: `TenantsEndpointsTestes.cs:158-159`; malformed `X-Tenant` → 400 `ValidacaoContextoTenantFilterTestes.cs:121`
- [x] API restarts during deletion → resumes from the last batch: `ProcessadorExclusaoTestes.cs:206-216`

---

## Discrimination Sensor

Isolated scratch: `git worktree add --detach <temp>/vwt HEAD`. Each mutation was applied with perl, the full suite ran (`dotnet test Jamsoft.DashBoards.slnx`, or Vitest for the Web), then `git checkout -- .`. Baseline M0 in the worktree with no mutation: 213/213 green.

Round 1 (worktree under the scratchpad) was discarded for the .NET mutants. The long path caused `DllNotFoundException: Microsoft.Data.SqlClient.SNI.dll … nome muito grande`, and 170 tests failed in 1 s from infrastructure. Those "kills" were invalid and all .NET mutants were rerun in round 2. M8 (Vitest) is not affected and stays from round 1.

| # | File (production code) | Description | Killed? | Killing tests |
| - | ---------------------- | ----------- | ------- | ------------- |
| M1 | `src/Jamsoft.DashBoards.Api/Infraestrutura/Dados/PlataformaDbContext.cs` (`HasQueryFilter`) | Global tenant filter removed | ✅ Killed (11 failures) | `FiltroTenantTestes`, `EmpresasEndpointsTestes`, isolation suite |
| M2 | `…/Infraestrutura/Dados/TenantSaveChangesInterceptor.cs:60-61` | Interceptor no longer throws `AcessoCruzadoException` | ✅ Killed (4) | `TenantSaveChangesInterceptorTestes` ×3, `AcessoCruzadoTestes` |
| M3 | `…/Seguranca/TenantAtual.cs:115` | Admin/Consulta: divergent `X-Tenant` accepted as the tenant (`Divergente = false`) | ✅ Killed (11) | `TenantAtualTestes`, `ValidacaoContextoTenantFilterTestes`, isolation suite |
| M4 | `…/Seguranca/RefreshTokens.cs:62` | Refresh token reuse no longer revokes all tokens | ✅ Killed (1) | `RefreshTestes.Reuso_do_token_antigo…` |
| M5 | `…/Modulos/Usuarios/RegrasUltimoAdmin.cs:27` | `WITH (UPDLOCK, HOLDLOCK)` removed | ✅ Killed (1) | `RegrasUltimoAdminTestes.Dois_Admins_se_rebaixando…` (timing-dependent test: 500 ms pause) |
| M6 | `…/Modulos/Exclusao/ExclusaoTenantWorker.cs:93` | `DELETE TOP(n)` with no tenant filter (`WHERE @tenant = @tenant`) | ✅ Killed (4) | `ProcessadorExclusaoTestes` ×2, `ExclusoesEndpointsTestes` ×2 |
| M7 | `…/Modulos/Autenticacao/Bloqueio.cs:20` | Lockout responds 401 instead of 423 | ✅ Killed (2) | `BloqueioTestes` ×2 |
| M8 | `src/Jamsoft.DashBoards.Web/src/app/core/auth/perfil.guard.ts:27` | `ADMIN_TENANT` lets Consulta in (`/cliente/usuarios`) | ✅ Killed (2) | `perfil.guard.spec.ts` "Consulta em /cliente/usuarios", "/cliente/empresas" |
| M9 | `…/Configuracao/ExclusaoOptions.cs:15,23` | Default batch and cap raised to 50,000 (DEL-04 says ≤ 5,000) | ❌ **Survived** | — |
| M10 | `…/Modulos/Usuarios/EditarUsuarioEndpoint.cs:25` | `PERFIL_NAO_PERMITIDO` check removed from edit (promote to SuperAdmin/Coletor) | ❌ **Survived** | — |
| M11 | `…/Modulos/Autenticacao/LogoutEndpoint.cs:14` | Logout no longer revokes the refresh token family | ✅ Killed (1) | `LogoutTestes.Apos_logout…` |

**Sensor depth**: P0/critical (expanded manual fault injection, 11 mutants)
**Result**: 9/11 killed. **FAIL ❌** (2 survivors become fix tasks)

**Isolation**: real tree `git status --porcelain` before the sensor = empty; after removing the worktree (`git worktree remove --force` + `prune`) = empty. `git worktree list` shows only the main tree.

---

## Gate Check

Build gate (`tasks.md`), run once on the real tree with no changes:

| Command | Result |
| ------- | ------ |
| `dotnet build Jamsoft.DashBoards.slnx -warnaserror --no-incremental` | ✅ 0 warnings, 0 errors |
| `dotnet format Jamsoft.DashBoards.slnx --verify-no-changes` | ✅ exit 0 |
| `dotnet test Jamsoft.DashBoards.slnx` (`TESTES_SQLSERVER` local) | ✅ 213 passed, 0 failed, 0 skipped (30 s) |
| `npm --prefix src/Jamsoft.DashBoards.Web run lint` | ✅ "All files pass linting." |
| `npm --prefix src/Jamsoft.DashBoards.Web run build` | ✅ bundle generated |
| `npm --prefix src/Jamsoft.DashBoards.Web test -- --watch=false` | ✅ 17 files, 118 tests passed |
| `npm --prefix src/Jamsoft.DashBoards.Web run e2e` | ⏭️ Not run (optional; needs API + Web + database up). Spec: `Web: e2e/tenancy.spec.ts` |

- **Test count before the feature**: 0 (empty repository at 8ae509d)
- **After**: 213 API + 118 Web = 331
- **Skipped**: none

---

## Code Quality

| Principle | Status |
| --------- | ------ |
| Minimum code / surgical changes / no scope creep | ✅ (spot check: endpoints, interceptor, worker) |
| Matches patterns (design: modules, ProblemDetails, UPPER_SNAKE) | ✅ |
| Spec-anchored outcome check | ❌ tenant-inactivated-during-session edge case asserts 401 against the spec's 403 |
| Per-layer coverage | ⚠️ boundaries missing on TEN-01/EMP-02; DEL-04 cap without a test |
| Every test maps to an AC / edge case / Done-when | ✅ (rate limit 429 and version conflict 409 map to design/Done-when T18/T24) |
| Guidelines | none in the repo; matrix in `tasks.md` applied |

---

## Ranked gaps (fix plans)

1. **Spec mismatch, inactive tenant during a session** (Edge TEN-05). Spec: 403; tests and implementation: 401 (`SituacaoTenantTestes.cs:62`, `StatusAcessoCacheTestes.cs:86-87`). *Fix*: decide with the user. Either change the spec/STATE (new AD, 401 `NAO_AUTENTICADO` so the interceptor can renew/redirect) or return 403 `ACESSO_DESATIVADO` when the token is validated or renewed for an inactive tenant. Then align the assertions. Priority: Major.
2. **USR-02 promotion via edit not tested** (mutant M10 survived). *Fix*: test `PUT /api/usuarios/{id}` with `perfil = "SuperAdmin"` and `"Coletor"` → 403 `PERFIL_NAO_PERMITIDO` and the user unchanged (`EditarUsuarioTestes.cs`). Priority: Major (security).
3. **DEL-04 cap of 5,000 rows per batch not tested** (mutant M9 survived). *Fix*: unit test that `ExclusaoOptions` defaults to 5,000 and that `Valida()` rejects 5,001. Optionally an integration test that counts batches (`linhas` grows by ≤ lote per transaction). Priority: Major.
4. **CTX-01 with an inactive tenant has no evidence.** *Fix*: SuperAdmin with `X-Tenant` of an inactive tenant on `GET /api/empresas` and `GET /api/usuarios` → 200 with that tenant's data. Priority: Minor.
5. **USR-09 partial**: only creation by SuperAdmin is tested. *Fix*: SuperAdmin + `X-Tenant` on `GET /api/usuarios`, `PUT /api/usuarios/{id}` and `POST …/redefinir-senha` → success in the selected tenant. Priority: Minor.
6. **EMP-02 partial**: SuperAdmin creating a company (201 in the selected tenant) and name boundaries (empty → 400; 80 → 201). Priority: Minor.
7. **TEN-01 boundaries**: name of 3 and 120 accepted, 121 → 400 `VALIDACAO`. Priority: Minor.
8. **TEN-05/06 (Coletor) and AUD-01 (Coletor credential)**: deferred to `sincronizacao-vendas`. Record it in the traceability so they are not marked Verified here. Priority: tracking.
9. **M5 depends on timing** (500 ms pause in `RegrasUltimoAdminTestes.cs`). It killed the mutant, but under load it may give false positives or negatives. Monitor it. Priority: Cosmetic.

---

## Requirement Traceability Update (proposed; spec.md not changed by the Verifier)

| Requirement | New Status |
| ----------- | ---------- |
| USR-02, DEL-04 | ❌ Needs Fix |
| TEN-01, EMP-02, USR-09, CTX-01 | ⚠️ Needs Fix (coverage) |
| TEN-05, TEN-06 | ⚠️ Verified except Coletor (deferred) |
| Others (45) | ✅ Verified |

---

## Candidate lessons (`lessons.py` not run: Python not installed)

1. `spec_deviation`: when the design picks a different status code from the spec (401 vs 403), record an AD or a `// SPEC_DEVIATION` marker in the same commit. Test assertions must follow the spec, not the design. Source: Edge TEN-05, `SituacaoTenantTestes.cs:62`.
2. `surviving_mutant`: rules that apply to both create and edit (allowed profile) need a negative test on each route that changes the field. Source: M10, `EditarUsuarioEndpoint.cs:25`.
3. `surviving_mutant`: limits written in the spec as numbers ("at most 5,000") need a test on the default/validated value, even when the test swaps the value for speed. Source: M9, `ExclusaoOptions.cs`.
4. `spec_precision`: "active or inactive" in an AC requires a case for each branch. Source: CTX-01.
5. Process (sensor): on Windows, put the mutation worktree on a short path. Paths over 260 characters break native DLL loading and produce false kills. Check for a green M0 baseline before trusting the results. Source: discarded round 1.

---

## Summary

**Overall**: ❌ Not Ready

**Spec-anchored check**: 43/54 ACs fully match the spec outcome; 2 GAPs, 6 partial, 1 edge case with a contradicting assertion
**Sensor**: 9/11 mutations killed
**Gate**: API 213 passed / Web 118 passed / build, format, lint, build-web green

**What works**: authentication (lockout, refresh rotation and reuse, logout), read/write isolation (filter, interceptor, isolation suite with checksums), last-Admin rule under concurrency, batched deletion with resume, audit, and the Web guard per profile.

**Next steps**: route gaps 1–3 as fix tasks (gap 1 needs a user decision), then 4–7; re-run the Verifier (iteration 1/3).
