# Plataforma — Tenancy, Usuários e Acesso — Specification

**Projeto:** JAMSOFT Dashboards · **Escopo:** Complex · **Data:** 2026-10-24
**Decisões de projeto aplicáveis:** AD-001, AD-002, AD-003, AD-004, AD-005, AD-008 (`.specs/STATE.md`)

## Problem Statement

A JAMSOFT precisa oferecer um painel de métricas para vários clientes numa única plataforma, com banco compartilhado. Sem uma base sólida de tenants, empresas, usuários, perfis e isolamento, nenhum dado de cliente pode ser exposto com segurança. Esta feature entrega a fundação: quem é o cliente, quem pode entrar e o que cada um pode ver ou alterar.

## Goals

- [ ] SuperAdmin cadastra um tenant e seu primeiro Admin em menos de 2 minutos.
- [ ] Zero acessos cruzados entre tenants nos testes de isolamento automatizados (100% das rotas com dado de tenant cobertas).
- [ ] Admin do cliente cria usuários de consulta sem intervenção da JAMSOFT.
- [ ] Exclusão completa de um tenant remove 100% dos registros dele e 0 registros de outros tenants.

## Out of Scope

| Feature | Reason |
| ------- | ------ |
| SSO / login social / OAuth externo | Não pedido; JWT próprio (AD-004) atende o MVP |
| Autenticação multifator (MFA) | Não pedido; candidato a evolução |
| Usuário vinculado a mais de um tenant | Simplifica isolamento; ver Assumptions |
| Permissões granulares por indicador/tela | §7.1 adia a matriz definitiva para a implementação; MVP usa os 4 perfis |
| Billing, planos e limites comerciais | Fora do documento original |
| Recuperação de senha por e-mail | Exige infraestrutura de e-mail; Admin/SuperAdmin redefinem senha no MVP |
| Credenciais do Coletor | Tratadas na feature `sincronizacao-vendas` |

---

## Assumptions & Open Questions

| Assumption / decision | Chosen default | Rationale | Confirmed? |
| --------------------- | -------------- | --------- | ---------- |
| Usuário pode pertencer a vários tenants? | Não: cada usuário `Admin`/`Consulta` pertence a exatamente 1 tenant; `SuperAdmin` não pertence a nenhum | Isolamento mais simples; documento fala em "usuários do cliente" | n |
| Identificador de login | E-mail, único em toda a plataforma | Evita ambiguidade no login sem seleção de tenant | n |
| Política de senha | Mínimo 10 caracteres, ao menos 1 letra e 1 dígito; hash com algoritmo do ASP.NET Core Identity (PBKDF2) | Padrão nativo do .NET | n |
| Bloqueio por tentativas | 5 falhas consecutivas bloqueiam o login por 15 minutos | Mitiga força bruta sem travar suporte | n |
| Duração de sessão | Access token 15 min; refresh token 7 dias, rotativo | AD-004 | y |
| Empresas do tenant | Empresa identificada por (`TENANT_ID`, `COD_EMPRESA`); criada manualmente ou automaticamente na primeira ingestão com o nome recebido | Coletor já envia `Cod_Empresa`/`Nome_Empresa`; evita rejeitar vendas por cadastro faltante | n |
| Resposta para recurso de outro tenant | HTTP 404 (não 403) | Não revela a existência do recurso | n |
| Exclusão de tenant | Assíncrona, em lotes, na ordem de dependências; confirmação digitando o nome exato do tenant | Volume de vendas pode ser grande; evita lock prolongado | n |
| Registro de auditoria após exclusão | Registros de auditoria da exclusão são mantidos (sem `TENANT_ID` FK, com `ID_PUBLICO` e nome) | Auditoria precisa sobreviver ao tenant | n |
| Último Admin do tenant | Não pode ser inativado nem rebaixado se for o único Admin ativo | Evita tenant sem administrador | n |
| SuperAdmin inicial | Criado por *seed* na implantação, com troca de senha obrigatória no 1º login | Necessário para bootstrap | n |
| Tenant inativo | Usuários do tenant não conseguem logar; dados preservados; Coletor recebe 403 para esse tenant | Permite suspensão sem exclusão | n |

**Open questions:** none - all resolved or logged above.

---

## Perfis (referência)

| Perfil | Escopo | Pode |
| ------ | ------ | ---- |
| `SuperAdmin` | Global | Tudo da área administrativa: tenants, empresas, usuários de qualquer tenant, coletores, exclusão de tenant, auditoria, painéis de qualquer tenant |
| `Admin` | Próprio tenant | Ver painéis; gerenciar usuários `Admin`/`Consulta` e empresas do próprio tenant; ver status de sincronização |
| `Consulta` | Próprio tenant | Somente visualizar painéis |
| `Coletor` | Tenants autorizados à credencial | Somente endpoints de coletor (ver `sincronizacao-vendas`) |

---

## User Stories

### P1: Login e sessão ⭐ MVP

**User Story**: Como usuário da plataforma, quero entrar com e-mail e senha e manter a sessão ativa enquanto uso o painel, para acessar as métricas com segurança.

**Why P1**: Nenhuma outra funcionalidade existe sem autenticação.

**Acceptance Criteria**:

1. WHEN um usuário ativo envia e-mail e senha corretos THEN a API SHALL responder 200 com access token (15 min), refresh token (7 dias), perfil e `ID_PUBLICO` do tenant (nulo para SuperAdmin). (AUTH-01)
2. IF o e-mail não existe ou a senha está incorreta THEN a API SHALL responder 401 com a mesma mensagem genérica "Credenciais inválidas" nos dois casos. (AUTH-02)
3. IF o usuário acumula 5 falhas consecutivas THEN a API SHALL bloquear novos logins desse usuário por 15 minutos e responder 423 durante o bloqueio. (AUTH-03)
4. WHEN um refresh token válido é apresentado THEN a API SHALL emitir novo par de tokens e invalidar o refresh token usado. (AUTH-04)
5. IF um refresh token já usado ou revogado é apresentado THEN a API SHALL responder 401 e revogar todos os refresh tokens ativos daquele usuário. (AUTH-05)
6. WHEN o usuário faz logout THEN a API SHALL revogar o refresh token da sessão e o frontend SHALL descartar os tokens locais. (AUTH-06)
7. IF o usuário ou o tenant dele está inativo THEN a API SHALL recusar o login com 403 e a mensagem "Acesso desativado". (AUTH-07)
8. The API SHALL rejeitar com 401 qualquer requisição a rota protegida sem token válido e com 403 quando o perfil não atende à policy da rota. (AUTH-08)
9. The API SHALL nunca registrar senhas, access tokens ou refresh tokens em logs. (AUTH-09)

**Independent Test**: Com um usuário semeado, fazer login, chamar rota protegida, renovar token, reutilizar o refresh antigo (401) e fazer logout.

---

### P1: SuperAdmin gerencia tenants ⭐ MVP

**User Story**: Como SuperAdmin, quero cadastrar e manter os tenants (clientes) da plataforma, para habilitar novos clientes e suspender os que saírem.

**Why P1**: Sem tenant não há dado de cliente.

**Acceptance Criteria**:

1. WHEN o SuperAdmin cria um tenant com nome válido (3–120 caracteres) e documento (CNPJ válido, único) THEN a API SHALL persistir o tenant com `ID INT` interno, `ID_PUBLICO` GUID novo, situação "Ativo" e responder 201 com o `ID_PUBLICO`. (TEN-01)
2. IF o CNPJ já pertence a outro tenant THEN a API SHALL responder 409 sem criar registro. (TEN-02)
3. WHEN o SuperAdmin cria um tenant THEN a API SHALL permitir informar no mesmo fluxo o primeiro usuário `Admin` (nome, e-mail, senha inicial com troca obrigatória no 1º login). (TEN-03)
4. WHEN o SuperAdmin lista tenants THEN a API SHALL retornar lista paginada (padrão 20, máximo 100 por página) com filtro por nome/CNPJ e situação. (TEN-04)
5. WHEN o SuperAdmin inativa um tenant THEN a API SHALL bloquear login dos usuários e ingestão do Coletor para esse tenant, preservando todos os dados. (TEN-05)
6. WHEN o SuperAdmin reativa um tenant THEN a API SHALL restaurar login e ingestão sem alterar dados. (TEN-06)
7. The API SHALL expor tenants externamente apenas pelo `ID_PUBLICO`, nunca pelo `ID INT`. (TEN-07)
8. IF um usuário sem perfil `SuperAdmin` chama qualquer rota de gestão de tenants THEN a API SHALL responder 403. (TEN-08)

**Independent Test**: Logado como SuperAdmin, criar tenant + Admin, listar, inativar (login do Admin falha com 403), reativar (login funciona).

---

### P1: Empresas do tenant ⭐ MVP

**User Story**: Como SuperAdmin ou Admin do cliente, quero ver e nomear as empresas (filiais) do tenant, para filtrar indicadores por empresa.

**Why P1**: Um tenant pode ter várias empresas; o painel e a ingestão dependem desse vínculo.

**Acceptance Criteria**:

1. The API SHALL identificar uma empresa de forma única por (`TENANT_ID`, `COD_EMPRESA`). (EMP-01)
2. WHEN o Admin (próprio tenant) ou o SuperAdmin cadastra uma empresa com `COD_EMPRESA` inteiro positivo e nome (1–80 caracteres) THEN a API SHALL criá-la vinculada ao tenant e responder 201. (EMP-02)
3. IF já existe empresa com o mesmo `COD_EMPRESA` no tenant THEN a API SHALL responder 409. (EMP-03)
4. WHEN a ingestão recebe venda com `COD_EMPRESA` ainda não cadastrado no tenant THEN a API SHALL criar a empresa automaticamente com o `Nome_Empresa` recebido. (EMP-04)
5. WHEN o Admin ou SuperAdmin renomeia uma empresa THEN a API SHALL manter o nome editado mesmo que ingestões futuras enviem outro `Nome_Empresa`. (EMP-05)
6. WHEN um usuário lista empresas THEN a API SHALL retornar somente as empresas do tenant do token (ou do tenant selecionado, para SuperAdmin). (EMP-06)

**Independent Test**: Cadastrar empresa 1 no tenant A e empresa 1 no tenant B (ambas aceitas); duplicar no tenant A (409); listar como usuário de A (só a de A).

---

### P1: Isolamento entre tenants ⭐ MVP

**User Story**: Como cliente, quero a garantia de que nenhum outro cliente vê ou altera meus dados.

**Why P1**: Requisito central de segurança de uma plataforma multi-tenant (§5.3).

**Acceptance Criteria**:

1. The API SHALL obter o tenant efetivo de cada requisição a partir das claims do token, nunca de valor enviado pelo cliente sem validação. (ISO-01)
2. The API SHALL aplicar filtro por `TENANT_ID` em toda leitura, inserção, atualização e exclusão de entidades com dado de cliente. (ISO-02)
3. IF um usuário `Admin` ou `Consulta` solicita recurso (por `ID_PUBLICO`) pertencente a outro tenant THEN a API SHALL responder 404 sem revelar a existência do recurso. (ISO-03)
4. IF o corpo ou a rota de uma requisição de usuário de tenant contém um identificador de tenant diferente do token THEN a API SHALL responder 403 e registrar o evento em auditoria. (ISO-04)
5. The banco da plataforma SHALL ter índice com `TENANT_ID` como coluna inicial em toda tabela com dado de cliente. (ISO-05)

**Independent Test**: Suíte automatizada que, para cada rota com dado de tenant, autentica como usuário do tenant A e tenta ler/alterar/excluir recurso do tenant B, esperando 404/403 e nenhum efeito no banco.

---

### P2: Admin do cliente gerencia usuários

**User Story**: Como Admin de um tenant, quero cadastrar e manter usuários da minha empresa, para dar acesso às métricas sem depender da JAMSOFT.

**Why P2**: SuperAdmin pode cadastrar usuários no MVP; autonomia do cliente vem em seguida.

**Acceptance Criteria**:

1. WHEN o Admin cria usuário com nome, e-mail único na plataforma, senha inicial válida e perfil `Admin` ou `Consulta` THEN a API SHALL criá-lo no tenant do Admin com troca de senha obrigatória no 1º login e responder 201. (USR-01)
2. IF o Admin tenta criar ou promover usuário a `SuperAdmin` ou `Coletor` THEN a API SHALL responder 403. (USR-02)
3. IF o e-mail informado já existe na plataforma THEN a API SHALL responder 409 sem revelar a qual tenant pertence. (USR-03)
4. WHEN o Admin edita nome, perfil (`Admin`/`Consulta`) ou situação de usuário do próprio tenant THEN a API SHALL aplicar a alteração e registrar auditoria. (USR-04)
5. IF a alteração deixaria o tenant sem nenhum `Admin` ativo THEN a API SHALL responder 422 com a mensagem "O tenant precisa de ao menos um Admin ativo". (USR-05)
6. WHEN um usuário é inativado THEN a API SHALL revogar todos os refresh tokens dele. (USR-06)
7. WHEN o Admin redefine a senha de um usuário do próprio tenant THEN a API SHALL exigir troca no próximo login e revogar os refresh tokens dele. (USR-07)
8. WHILE o usuário tem troca de senha pendente a API SHALL aceitar apenas a rota de troca de senha e logout, respondendo 403 às demais. (USR-08)
9. WHEN o SuperAdmin gerencia usuários THEN a API SHALL permitir as mesmas operações em qualquer tenant selecionado. (USR-09)
10. WHEN um usuário `Consulta` acessa qualquer rota de gestão de usuários THEN a API SHALL responder 403. (USR-10)

**Independent Test**: Como Admin do tenant A, criar usuário Consulta, tentar criar SuperAdmin (403), tentar inativar a si mesmo sendo o único Admin (422).

---

### P2: SuperAdmin acessa o contexto de um tenant

**User Story**: Como SuperAdmin, quero selecionar um tenant e ver seus painéis e cadastros, para dar suporte ao cliente.

**Why P2**: Suporte operacional; não bloqueia o cliente de usar o painel.

**Acceptance Criteria**:

1. WHEN o SuperAdmin seleciona um tenant ativo ou inativo THEN a API SHALL aceitar o `ID_PUBLICO` desse tenant como contexto nas rotas de painel e cadastro do tenant. (CTX-01)
2. WHEN o SuperAdmin acessa dados no contexto de um tenant THEN a API SHALL registrar em auditoria o SuperAdmin, o tenant e a rota acessada. (CTX-02)
3. IF um perfil diferente de `SuperAdmin` envia contexto de tenant explícito THEN a API SHALL ignorar a seleção só quando ela coincide com o tenant do token e responder 403 caso contrário. (CTX-03)

**Independent Test**: Como SuperAdmin, selecionar tenant B, abrir painel e verificar registro de auditoria.

---

### P2: Exclusão total de um tenant

**User Story**: Como SuperAdmin, quero excluir todos os dados de um tenant, para atender encerramentos de contrato e pedidos de remoção.

**Why P2**: Operação rara e crítica; não é necessária para o cliente começar a usar.

**Acceptance Criteria**:

1. IF um perfil diferente de `SuperAdmin` solicita a exclusão THEN a API SHALL responder 403. (DEL-01)
2. IF o nome digitado na confirmação não coincide exatamente com o nome do tenant THEN a API SHALL responder 422 sem iniciar a exclusão. (DEL-02)
3. WHEN a confirmação é válida THEN a API SHALL inativar o tenant imediatamente, criar um job de exclusão com situação "Pendente" e responder 202 com o identificador do job. (DEL-03)
4. WHILE o job está "Em execução" a API SHALL excluir registros em lotes de no máximo 5.000 linhas por transação, na ordem filhos → pais, filtrando sempre pelo `TENANT_ID` do tenant alvo. (DEL-04)
5. WHEN todos os registros do tenant forem removidos THEN a API SHALL marcar o job como "Concluída", registrar contagem de linhas removidas por tabela e excluir o registro do tenant. (DEL-05)
6. IF um lote falhar THEN a API SHALL marcar o job como "Falhou" com a etapa e o erro, mantendo o tenant inativo, e SHALL permitir ao SuperAdmin retomar o job do ponto em que parou. (DEL-06)
7. The rotina de exclusão SHALL não alterar nenhum registro cujo `TENANT_ID` seja diferente do tenant alvo. (DEL-07)
8. WHEN o job muda de situação THEN a API SHALL registrar em auditoria o SuperAdmin solicitante, o `ID_PUBLICO`, o nome do tenant e a nova situação. (DEL-08)
9. IF já existe job de exclusão "Pendente" ou "Em execução" para o tenant THEN a API SHALL responder 409. (DEL-09)

**Independent Test**: Com dois tenants semeados com vendas, excluir o tenant A e verificar contagem zero de linhas de A em todas as tabelas e contagem inalterada de B.

---

### P3: Auditoria consultável

**User Story**: Como SuperAdmin, quero consultar o registro de operações críticas, para investigar incidentes.

**Why P3**: A gravação é obrigatória desde o P1/P2; a tela de consulta pode vir depois.

**Acceptance Criteria**:

1. The API SHALL registrar em auditoria: login de SuperAdmin, criação/alteração/inativação de tenant, usuário e empresa, redefinição de senha, criação/revogação de credencial de Coletor, acesso de SuperAdmin a contexto de tenant e todo o ciclo da exclusão de tenant. (AUD-01)
2. The registro de auditoria SHALL conter data/hora UTC, usuário autor, perfil, operação, `ID_PUBLICO` do recurso, tenant afetado, IP de origem e resultado, sem senhas ou tokens. (AUD-02)
3. WHEN o SuperAdmin consulta a auditoria THEN a API SHALL retornar registros paginados com filtros por período, tenant, usuário e operação. (AUD-03)
4. The API SHALL não oferecer rota para alterar ou excluir registros de auditoria. (AUD-04)

**Independent Test**: Executar criação de tenant e redefinição de senha; consultar auditoria filtrando pelo tenant e ver os dois eventos.

---

## Edge Cases

- IF dois SuperAdmins criam tenants com o mesmo CNPJ simultaneamente THEN o banco SHALL garantir unicidade por índice único e a segunda requisição SHALL receber 409. (TEN-02)
- IF um usuário é inativado enquanto possui access token válido THEN a API SHALL recusar novas renovações e o token SHALL perder validade em no máximo 15 minutos. (USR-06)
- IF o tenant é inativado durante uma sessão ativa THEN as requisições seguintes do usuário SHALL receber 403 assim que o token for renovado ou validado contra a situação do tenant. (TEN-05)
- WHEN a lista de tenants está vazia THEN o frontend SHALL exibir o estado vazio com ação "Cadastrar tenant". (TEN-04)
- IF um `ID_PUBLICO` mal formatado é enviado THEN a API SHALL responder 400. (TEN-07)
- IF a API reinicia durante uma exclusão de tenant THEN o job SHALL ser retomado a partir do último lote confirmado. (DEL-06)

---

## Implicit-Requirement Dimensions Sweep

| Dimension | Coverage |
| --------- | -------- |
| Input validation & bounds | TEN-01, EMP-02, USR-01, política de senha (Assumptions), TEN-04 paginação |
| Failure / partial-failure states | DEL-06, edge case de reinício |
| Idempotency / retry / duplicate handling | TEN-02, EMP-03, USR-03, DEL-09 |
| Auth boundaries & rate limits | AUTH-03, AUTH-08, TEN-08, USR-02, USR-10, DEL-01 |
| Concurrency / ordering | Edge case de CNPJ concorrente, DEL-09 |
| Data lifecycle / expiry | AUTH-01 (expiração), DEL-03..DEL-05, Assumption de auditoria pós-exclusão |
| Observability | AUD-01..AUD-04, AUTH-09 |
| External-dependency failure | N/A because esta feature não chama serviços externos (e-mail/SSO estão fora de escopo) |
| State-transition integrity | Tenant Ativo↔Inativo (TEN-05/06), job Pendente→Em execução→Concluída/Falhou (DEL-03..DEL-06), USR-08 |

---

## Requirement Traceability

| Requirement ID | Story | Phase | Status |
| -------------- | ----- | ----- | ------ |
| AUTH-01 | P1: Login e sessão | Execute | Implementing |
| AUTH-02 | P1: Login e sessão | Execute | Implementing |
| AUTH-03 | P1: Login e sessão | Execute | Implementing |
| AUTH-04 | P1: Login e sessão | Execute | Implementing |
| AUTH-05 | P1: Login e sessão | Execute | Implementing |
| AUTH-06 | P1: Login e sessão | Execute | Implementing |
| AUTH-07 | P1: Login e sessão | Execute | Implementing |
| AUTH-08 | P1: Login e sessão | Execute | Implementing |
| AUTH-09 | P1: Login e sessão | Execute | Implementing |
| TEN-01 | P1: SuperAdmin gerencia tenants | Execute | Implementing |
| TEN-02 | P1: SuperAdmin gerencia tenants | Execute | Implementing |
| TEN-03 | P1: SuperAdmin gerencia tenants | Execute | Implementing |
| TEN-04 | P1: SuperAdmin gerencia tenants | Execute | Implementing |
| TEN-05 | P1: SuperAdmin gerencia tenants | Execute | Implementing |
| TEN-06 | P1: SuperAdmin gerencia tenants | Execute | Implementing |
| TEN-07 | P1: SuperAdmin gerencia tenants | Execute | Implementing |
| TEN-08 | P1: SuperAdmin gerencia tenants | Execute | Implementing |
| EMP-01 | P1: Empresas do tenant | Execute | Implementing |
| EMP-02 | P1: Empresas do tenant | Tasks | In Tasks |
| EMP-03 | P1: Empresas do tenant | Tasks | In Tasks |
| EMP-04 | P1: Empresas do tenant | Execute | Implementing |
| EMP-05 | P1: Empresas do tenant | Execute | Implementing |
| EMP-06 | P1: Empresas do tenant | Tasks | In Tasks |
| ISO-01 | P1: Isolamento entre tenants | Execute | Implementing |
| ISO-02 | P1: Isolamento entre tenants | Execute | Implementing |
| ISO-03 | P1: Isolamento entre tenants | Tasks | In Tasks |
| ISO-04 | P1: Isolamento entre tenants | Execute | Implementing |
| ISO-05 | P1: Isolamento entre tenants | Execute | Implementing |
| USR-01 | P2: Admin gerencia usuários | Execute | Implementing |
| USR-02 | P2: Admin gerencia usuários | Tasks | In Tasks |
| USR-03 | P2: Admin gerencia usuários | Execute | Implementing |
| USR-04 | P2: Admin gerencia usuários | Tasks | In Tasks |
| USR-05 | P2: Admin gerencia usuários | Tasks | In Tasks |
| USR-06 | P2: Admin gerencia usuários | Execute | Implementing |
| USR-07 | P2: Admin gerencia usuários | Tasks | In Tasks |
| USR-08 | P2: Admin gerencia usuários | Execute | Implementing |
| USR-09 | P2: Admin gerencia usuários | Tasks | In Tasks |
| USR-10 | P2: Admin gerencia usuários | Execute | Implementing |
| CTX-01 | P2: SuperAdmin acessa contexto | Execute | Implementing |
| CTX-02 | P2: SuperAdmin acessa contexto | Tasks | In Tasks |
| CTX-03 | P2: SuperAdmin acessa contexto | Execute | Implementing |
| DEL-01 | P2: Exclusão total de tenant | Tasks | In Tasks |
| DEL-02 | P2: Exclusão total de tenant | Tasks | In Tasks |
| DEL-03 | P2: Exclusão total de tenant | Tasks | In Tasks |
| DEL-04 | P2: Exclusão total de tenant | Tasks | In Tasks |
| DEL-05 | P2: Exclusão total de tenant | Tasks | In Tasks |
| DEL-06 | P2: Exclusão total de tenant | Tasks | In Tasks |
| DEL-07 | P2: Exclusão total de tenant | Tasks | In Tasks |
| DEL-08 | P2: Exclusão total de tenant | Tasks | In Tasks |
| DEL-09 | P2: Exclusão total de tenant | Execute | Implementing |
| AUD-01 | P3: Auditoria consultável | Execute | Implementing |
| AUD-02 | P3: Auditoria consultável | Execute | Implementing |
| AUD-03 | P3: Auditoria consultável | Execute | Implementing |
| AUD-04 | P3: Auditoria consultável | Execute | Implementing |

**Coverage:** 54 total, 54 mapped to tasks (`tasks.md`), 0 unmapped

---

## Cobertura do documento original

| Seção original | Requisitos |
| -------------- | ---------- |
| §1 Visão geral (multi-tenant, N empresas e N usuários por tenant) | TEN-*, EMP-*, USR-* |
| §5.1–5.2 Modelo e identificadores | AD-001, TEN-01, TEN-07, EMP-01 |
| §5.3 Isolamento | ISO-01..ISO-05 |
| §5.4 Exclusão de tenant | DEL-01..DEL-09 |
| §6.1 Área administrativa | TEN-*, CTX-*, DEL-*, AUD-* |
| §6.2 Área do cliente (usuários) | USR-* |
| §7 Níveis de acesso / Policies | Tabela de perfis, AUTH-08, AD-004 |
| §8.3 Segurança (auth, auditoria, segredos, erros) | AUTH-*, AUD-*, ISO-* |
| §12.4 Segurança | ISO-*, AUTH-09, TEN-07 |

---

## Success Criteria

- [ ] Suíte de isolamento cobre 100% das rotas com dado de tenant e passa sem nenhum acesso cruzado.
- [ ] Onboarding de tenant + primeiro Admin concluído em < 2 minutos por um SuperAdmin.
- [ ] Exclusão de tenant com 1 milhão de vendas conclui sem bloquear consultas de outros tenants por mais de 1 segundo por lote.
- [ ] Nenhum segredo (senha/token) encontrado em logs da API em teste de varredura.
