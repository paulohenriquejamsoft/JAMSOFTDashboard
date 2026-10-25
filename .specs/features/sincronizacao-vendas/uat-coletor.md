# Roteiro de UAT do Coletor (WinForms)

**Feature**: `sincronizacao-vendas` · **Tarefa**: T41 · **Spec**: `spec.md` (CFG-01..CFG-10, CTL-01..CTL-06, LOG-05)

A casca WinForms não tem testes automáticos: toda a regra está no `Coletor.Nucleo`, já coberto por testes. Este roteiro
confere a tela de ponta a ponta contra um SQL Server local com dois bancos de ERP falsos e a API local.

Marque cada passo com `[x]` quando o resultado bater com o esperado. Anote qualquer divergência na coluna "Obs." ou ao
fim do documento.

---

## 1. Preparação

### 1.1 Pré-requisitos

- Windows com SQL Server local (`localhost`, autenticação Windows), o mesmo usado pelos testes de integração.
- .NET 10 SDK.
- PowerShell 5.1 ou superior.
- Nenhum Coletor aberto (o ícone na bandeja não pode existir).

### 1.2 Banco da plataforma e API local

Rodar na raiz do repositório:

```powershell
$api = "src/Jamsoft.DashBoards.Api"
dotnet user-secrets set "ConnectionStrings:Plataforma" "Server=localhost;Database=JamsoftDashBoards_Uat;Integrated Security=true;TrustServerCertificate=true" --project $api
dotnet user-secrets set "Jwt:Chave" "uma-chave-de-teste-com-pelo-menos-32-bytes!!" --project $api
dotnet run --project $api -- migrar
dotnet run --project $api -- criar-superadmin --email uat@jamsoft.com.br --nome "UAT"
# Anote a senha temporária exibida.
dotnet run --project $api --launch-profile http
# API em http://localhost:5170 (deixe este terminal aberto)
```

Em outro terminal, autenticar como SuperAdmin e trocar a senha temporária:

```powershell
$base = "http://localhost:5170"
$login = Invoke-RestMethod "$base/api/auth/login" -Method Post -ContentType "application/json" `
  -Body (@{ email = "uat@jamsoft.com.br"; senha = "<senha temporária>" } | ConvertTo-Json)
Invoke-RestMethod "$base/api/auth/trocar-senha" -Method Post -ContentType "application/json" `
  -Headers @{ Authorization = "Bearer $($login.accessToken)" } `
  -Body (@{ senhaAtual = "<senha temporária>"; novaSenha = "<nova senha forte>" } | ConvertTo-Json)
$login = Invoke-RestMethod "$base/api/auth/login" -Method Post -ContentType "application/json" `
  -Body (@{ email = "uat@jamsoft.com.br"; senha = "<nova senha forte>" } | ConvertTo-Json)
$h = @{ Authorization = "Bearer $($login.accessToken)" }
```

O token dura 15 minutos: repita o último `Invoke-RestMethod .../login` quando expirar.

### 1.3 Tenants e credencial do Coletor

Crie dois tenants ativos, **Loja A** e **Loja B**, pela tela web de administração (`/admin/tenants`) ou por
`POST /api/admin/tenants`. Anote o `idPublico` de cada um:

```powershell
$tenants = (Invoke-RestMethod "$base/api/admin/tenants" -Headers $h).itens
$tenants | Select-Object idPublico, nome
$A = ($tenants | Where-Object nome -eq "Loja A").idPublico
$B = ($tenants | Where-Object nome -eq "Loja B").idPublico
```

Crie a credencial do Coletor autorizada aos dois tenants. O secret aparece **só nesta resposta**:

```powershell
$coletor = Invoke-RestMethod "$base/api/admin/coletores" -Method Post -Headers $h -ContentType "application/json" `
  -Body (@{ nome = "Coletor UAT"; tenants = @($A, $B) } | ConvertTo-Json)
$coletor | Format-List idPublico, clientId, clientSecret
```

### 1.4 ERP falso (dois bancos)

O ERP falso reproduz a estrutura confirmada em 2026-10-25: tabela `NotaSaida` com identity em `NumNotaSaida`, view
`c_NotaSaida` sobre ela e `NotaSaidaProd`. É a mesma estrutura usada pelos testes (`tests/Jamsoft.DashBoards.Coletor.Testes/Infraestrutura/ErpFalso.cs`).

Salve os dois scripts abaixo (`erp-falso.sql` cria a estrutura; `notas.sql` grava `QTD` notas finalizadas da empresa
`EMPRESA`, espalhadas pelos últimos `DIAS` dias, incluindo hoje) e rode:

```powershell
sqlcmd -S localhost -E -Q "CREATE DATABASE ERP_UAT_A; CREATE DATABASE ERP_UAT_B;"
sqlcmd -S localhost -E -d ERP_UAT_A -i erp-falso.sql
sqlcmd -S localhost -E -d ERP_UAT_B -i erp-falso.sql
sqlcmd -S localhost -E -d ERP_UAT_A -i notas.sql -v QTD=1200 EMPRESA=1 DIAS=180
sqlcmd -S localhost -E -d ERP_UAT_B -i notas.sql -v QTD=300 EMPRESA=2 DIAS=180
```

```sql
-- erp-falso.sql: estrutura do ERP (tabela base, itens e a view lida pelo Coletor).
CREATE TABLE dbo.NotaSaida (
    NumNotaSaida       int IDENTITY(1, 1) NOT NULL PRIMARY KEY,
    modelo             int           NOT NULL,
    datafatura         datetime      NOT NULL,
    DataHoraFechamento datetime      NULL,
    dataentrega        datetime      NULL,
    totnota            money         NOT NULL,
    codcliente         int           NULL,
    FANTASIA           varchar(150)  NULL,
    codvendedor        int           NULL,
    nomevend           varchar(100)  NULL,
    situacaonota       int           NOT NULL,
    TotalCondNeutra    money         NOT NULL,
    Empresa            varchar(100)  NOT NULL,
    codEmpresa         int           NOT NULL);
CREATE TABLE dbo.NotaSaidaProd (
    Id           int IDENTITY(1, 1) NOT NULL PRIMARY KEY,
    numnotasaida int            NOT NULL,
    codproduto   int            NOT NULL,
    quantidade   decimal(18, 3) NOT NULL);
GO
CREATE VIEW dbo.c_NotaSaida AS
SELECT NumNotaSaida, modelo, datafatura, DataHoraFechamento, dataentrega, totnota, codcliente, FANTASIA,
       codvendedor, nomevend, situacaonota, TotalCondNeutra, Empresa, codEmpresa
  FROM dbo.NotaSaida;
GO
```

```sql
-- notas.sql: $(QTD) notas finalizadas (situação 5, sem valor neutro) da empresa $(EMPRESA),
-- com datafatura entre hoje - ($(DIAS) - 1) e hoje; um item por nota.
DECLARE @Hoje date = CAST(GETDATE() AS date);
WITH n AS (SELECT TOP ($(QTD)) ROW_NUMBER() OVER (ORDER BY (SELECT NULL)) - 1 AS i
             FROM sys.all_objects a CROSS JOIN sys.all_objects b)
INSERT dbo.NotaSaida (modelo, datafatura, DataHoraFechamento, totnota, codcliente, FANTASIA, codvendedor, nomevend,
                      situacaonota, TotalCondNeutra, Empresa, codEmpresa)
SELECT 65, d.dia, DATEADD(minute, 600 + i % 600, d.dia), 10 + i % 90, 10, 'Cliente', 3, 'Vendedor', 5, 0,
       'Loja ' + CAST($(EMPRESA) AS varchar(10)), $(EMPRESA)
  FROM n CROSS APPLY (SELECT CAST(DATEADD(day, -((i * 7919) % $(DIAS)), @Hoje) AS datetime) AS dia) d;
INSERT dbo.NotaSaidaProd (numnotasaida, codproduto, quantidade)
SELECT ns.NumNotaSaida, 1, 1 FROM dbo.NotaSaida AS ns
 WHERE NOT EXISTS (SELECT 1 FROM dbo.NotaSaidaProd AS p WHERE p.numnotasaida = ns.NumNotaSaida);
GO
```

Usuário só de leitura (CFG-03 com autenticação SQL). Crie o login uma vez no `master` e rode o script oficial em cada banco:

```powershell
sqlcmd -S localhost -E -Q "CREATE LOGIN jd_coletor_leitura WITH PASSWORD = '<senha forte do ERP>', CHECK_POLICY = ON;"
sqlcmd -S localhost -E -d ERP_UAT_A -i sql/coletor/001_erp_usuario_leitura_e_indices.sql
sqlcmd -S localhost -E -d ERP_UAT_B -i sql/coletor/001_erp_usuario_leitura_e_indices.sql
```

> O SQL Server precisa aceitar autenticação mista (SQL e Windows) para o passo com usuário SQL.

### 1.5 Coletor

- Pasta de dados: `%ProgramData%\Jamsoft\DashBoards.Coletor\` (`config.json`, `checkpoints.json`, `execucoes.json`,
  `logs\coletor-AAAAMMDD.jsonl`). Para começar do zero, apague a pasta com o Coletor fechado.
- Executar: `dotnet run --project src/Jamsoft.DashBoards.Coletor` (ou o `.exe` em `bin\Debug\net10.0-windows`).

Consulta de apoio no banco da plataforma (contagem de vendas por tenant):

```sql
SELECT t.NOME, COUNT(v.ID) AS VENDAS, SUM(CASE WHEN v.SITUACAO = N'Cancelada' THEN 1 ELSE 0 END) AS CANCELADAS
  FROM JamsoftDashBoards_Uat.dbo.TENANT t
  LEFT JOIN JamsoftDashBoards_Uat.dbo.VENDA v ON v.TENANT_ID = t.ID
 GROUP BY t.NOME;
```

---

## 2. Roteiro

### Host e bandeja (T35)

| # | Passos | Resultado esperado | OK | Obs. |
| - | ------ | ------------------ | -- | ---- |
| H-1 | Abrir o Coletor. | Janela "Jamsoft DashBoards Coletor" aberta e ícone na bandeja. A pasta de dados existe em `%ProgramData%\Jamsoft\DashBoards.Coletor`. | [ ] | |
| H-2 | Fechar a janela pelo "X". | A janela some, o ícone continua na bandeja e o processo segue rodando. | [ ] | |
| H-3 | Duplo clique no ícone (ou menu "Abrir"). | A janela volta. | [ ] | |
| H-4 | Abrir o Coletor de novo, com o primeiro aberto. | Aviso "O Coletor já está aberto"; nenhuma segunda janela. | [ ] | |
| H-5 | Menu da bandeja: marcar "Iniciar com o Windows". Conferir com `reg query HKCU\Software\Microsoft\Windows\CurrentVersion\Run /v Jamsoft.DashBoards.Coletor`. | O valor existe com o caminho do `.exe` e `--minimizado`. Antes de marcar, o valor não existia. | [ ] | |
| H-6 | Desmarcar "Iniciar com o Windows" e repetir o `reg query`. | O valor foi removido. | [ ] | |
| H-7 | Menu da bandeja: "Sair". | Ícone some e o processo termina (Gerenciador de Tarefas) em até 30 s. | [ ] | |

### CFG-01: sem login, só login e logs

| # | Passos | Resultado esperado | OK | Obs. |
| - | ------ | ------------------ | -- | ---- |
| 1-1 | Pasta de dados vazia. Abrir o Coletor. | Rodapé: "Sem login na API: só login e logs ficam disponíveis." Botões "Nova configuração", "Editar", "Excluir", "Pausar", "Retomar", "Reiniciar" e "Instâncias" desabilitados. | [ ] | |
| 1-2 | Clicar em "Logs". | A tela de logs abre (sem login). | [ ] | |
| 1-3 | Clicar em "Login". | A tela de login abre. | [ ] | |
| 1-4 | Depois do login (CFG-02), revogar a credencial: `Invoke-RestMethod "$base/api/admin/coletores/$($coletor.idPublico)/revogar" -Method Post -Headers $h`. Esperar a próxima execução de uma configuração ativa (ou "Reiniciar" uma delas). | Em até ~1 min depois da execução, a tela de login abre sozinha; o rodapé volta a "Sem login" e os botões de alteração ficam desabilitados. Rotacione o secret (`.../rotacionar`) e anote o novo para os passos seguintes. | [ ] | Fazer ao final do roteiro |

### CFG-02: login

| # | Passos | Resultado esperado | OK | Obs. |
| - | ------ | ------------------ | -- | ---- |
| 2-1 | Login com URL `http://localhost:5170`, o `clientId` e um secret errado. | "Credencial inválida ou revogada." Campo do secret limpo. | [ ] | |
| 2-2 | Login com a URL `http://localhost:5999` (porta sem API). | Após as tentativas, "Não foi possível falar com a API..." sem expor o secret. "Cancelar" interrompe a espera. | [ ] | |
| 2-3 | Login com o `clientId` e o `clientSecret` corretos. | Tela fecha; rodapé "Conectado à API: 2 tenant(s) autorizado(s)"; botões de cadastro habilitados. | [ ] | |
| 2-4 | Abrir `config.json`. | Tem `urlApi`, `clientId` e `clientSecretCifrado` (Base64); o secret em texto não aparece (ver CFG-08). | [ ] | |
| 2-5 | Sair pela bandeja e abrir de novo. | Autentica sozinho com a credencial gravada; rodapé "Conectado à API". | [ ] | |
| 2-6 | Abrir "Login" de novo. | URL e client id preenchidos; secret vazio com a dica "(gravado; deixe em branco para manter)". "Entrar" com o secret vazio funciona. | [ ] | |

### CFG-03 e CFG-04: instância e teste de conexão

| # | Passos | Resultado esperado | OK | Obs. |
| - | ------ | ------------------ | -- | ---- |
| 3-1 | "Instâncias" → servidor `localhost`, autenticação Windows → "Testar conexão". | "Conexão bem-sucedida: N banco(s)"; a lista mostra `ERP_UAT_A`, `ERP_UAT_B` (e outros bancos acessíveis). | [ ] | |
| 3-2 | "Salvar". | A instância aparece na lista da esquerda como `localhost (Windows)`. | [ ] | |
| 3-3 | "Nova" → `localhost`, SQL Server, usuário `jd_coletor_leitura`, senha correta → "Testar conexão" → "Salvar". | Bancos listados (só os que o usuário acessa: `ERP_UAT_A`, `ERP_UAT_B`); instância salva como `localhost (SQL: jd_coletor_leitura)`. | [ ] | |
| 4-1 | Instância SQL com senha errada → "Testar conexão". | "Tipo de erro: autenticação" e a mensagem do servidor; a senha digitada não aparece em lugar nenhum. | [ ] | |
| 4-2 | Servidor `servidor-inexistente` → "Testar conexão". | "Tipo de erro: rede" após o tempo de conexão (até 15 s). | [ ] | |
| 4-3 | Servidor `10.255.255.1` (IP sem resposta) → "Testar conexão". | "Tipo de erro: timeout" ou "rede", conforme o erro devolvido pelo driver; nenhuma senha na mensagem. | [ ] | |
| 4-4 | Com uma falha na tela, clicar "Salvar". | Pergunta "A conexão falhou. Salvar a instância mesmo assim?"; "Não" mantém sem gravar. | [ ] | |
| 4-5 | Selecionar a instância SQL salva. | Campo senha vazio com a dica "(gravada; deixe em branco para manter)"; "Testar conexão" com o campo vazio funciona (usa a gravada). | [ ] | |

### CFG-05, CFG-06, CFG-07: configuração de sincronização

| # | Passos | Resultado esperado | OK | Obs. |
| - | ------ | ------------------ | -- | ---- |
| 5-1 | "Nova configuração". | Tenant com Loja A e Loja B (lista da API); Rotina só "Vendas"; Intervalo 15; Janela 7; Situação "Ativa"; Período inicial desmarcado com a nota "Desmarcado: 6 meses antes da carga inicial." | [ ] | |
| 5-2 | "Salvar" sem escolher nada. | Ícone de erro no campo Tenant e "Informe o tenant." | [ ] | |
| 5-3 | Tenant Loja A, instância Windows, banco vazio → "Salvar". | Erro no campo Banco: "Informe o banco de origem." | [ ] | |
| 5-4 | Intervalo: tentar digitar 4 e 1500. | O campo limita a 5 e 1440 (5 min a 24 h). Janela limita a 0..90. | [ ] | |
| 5-5 | "Listar bancos" → escolher `ERP_UAT_A`, intervalo 5 → "Salvar". | Configuração aparece na grade como Loja A / ERP_UAT_A, "Executando" e depois "Ativa". | [ ] | |
| 6-1 | "Nova configuração": Tenant Loja B, mesma instância, banco `ERP_UAT_A` → "Salvar". | Erro no campo Banco: "Banco já vinculado ao tenant Loja A"; nada gravado. | [ ] | |
| 6-2 | Trocar o banco para `ERP_UAT_B`, intervalo 10 → "Salvar". | Configuração Loja B / ERP_UAT_B criada. | [ ] | |
| 7-1 | Depois da primeira execução de Loja A: logs (`execucao_fim`) e consulta de apoio. | Loja A com 1.200 vendas (3 lotes de até 500); o período consultado começou 6 meses antes da data da execução. | [ ] | |
| 7-2 | Editar Loja A. | Período inicial marcado com a data gravada na carga inicial (6 meses antes). | [ ] | |

### CFG-08: segredos cifrados

| # | Passos | Resultado esperado | OK | Obs. |
| - | ------ | ------------------ | -- | ---- |
| 8-1 | `Select-String -Path "$env:ProgramData\Jamsoft\DashBoards.Coletor\*", "$env:ProgramData\Jamsoft\DashBoards.Coletor\logs\*" -Pattern "<client secret>", "<senha do ERP>" -SimpleMatch` | Nenhuma ocorrência. | [ ] | |
| 8-2 | Abrir `config.json`. | `clientSecretCifrado` e `senhaCifrada` em Base64; nenhuma connection string. | [ ] | |

### CFG-09: excluir configuração

| # | Passos | Resultado esperado | OK | Obs. |
| - | ------ | ------------------ | -- | ---- |
| 9-1 | Selecionar Loja B → "Excluir" → "Não". | Nada muda. | [ ] | |
| 9-2 | "Excluir" → "Sim". | Linha some da grade; `config.json` sem a configuração; `checkpoints.json` sem a entrada dela. | [ ] | |
| 9-3 | Consulta de apoio. | As vendas de Loja B continuam na plataforma (nenhuma exclusão enviada à API). | [ ] | |
| 9-4 | Recriar Loja B / ERP_UAT_B (CFG-06 6-2) para os passos seguintes. | Nova carga inicial; vendas não duplicam (upsert). | [ ] | |

### CFG-10: tela principal

| # | Passos | Resultado esperado | OK | Obs. |
| - | ------ | ------------------ | -- | ---- |
| 10-1 | Observar a grade com as duas configurações. | Colunas Tenant, Banco, Situação, Intervalo ("5 min", "10 min"), Último sucesso (dd/MM/aaaa HH:mm), Último resultado ("Sucesso em ...") e Próxima execução. | [ ] | |
| 10-2 | Esperar um ciclo sem mexer na tela. | A linha muda para "Executando" e volta para "Ativa" sozinha, com novo "Último sucesso" e nova "Próxima execução". | [ ] | |
| 10-3 | Editar a instância usada por Loja B para o servidor `servidor-inexistente` e esperar o ciclo. | Loja B: Último resultado "Falha: ..." (dica com a mensagem); Último sucesso mantém a data anterior; Loja A segue com sucesso (SCH-04). Depois, voltar a instância para `localhost`. | [ ] | |

### CTL-01 a CTL-06: pausar, retomar e reiniciar

| # | Passos | Resultado esperado | OK | Obs. |
| - | ------ | ------------------ | -- | ---- |
| C1-1 | Selecionar Loja A (ativa) → "Pausar". | Em até 2 s a situação vira "Pausada" (ou "Pausando..." enquanto termina o lote em andamento); "Próxima execução" fica vazia; "Pausar" desabilita e "Retomar" habilita. | [ ] | |
| C1-2 | Esperar mais que o intervalo. | Nenhuma execução nova de Loja A nos logs. | [ ] | |
| C2-1 | Inserir 10 notas de hoje: `sqlcmd -S localhost -E -d ERP_UAT_A -i notas.sql -v QTD=10 EMPRESA=1 DIAS=1` → "Retomar". | Situação "Ativa"; execução logo depois; plataforma com 1.210 vendas em Loja A (a partir do checkpoint, sem recarregar tudo). | [ ] | |
| C3-1 | "Reiniciar" em Loja A. | Diálogo explica que o checkpoint é zerado e nenhuma venda é excluída; campo "Novo período inicial" com 6 meses antes de hoje. | [ ] | |
| C3-2 | "Cancelar". | Nada muda (`checkpoints.json` igual). | [ ] | |
| C4-1 | "Reiniciar" → informar período 1 mês antes de hoje → "Reiniciar". | Nova carga inicial: logs mostram `execucao_inicio` e a consulta desde a nova data; Período inicial da configuração passa a ser essa data; o checkpoint de Loja B não muda. | [ ] | |
| C5-1 | Consulta de apoio após o reinício. | Loja A continua com 1.210 vendas (nada excluído, nada duplicado). | [ ] | |
| C6-1 | Aumentar `ERP_UAT_A` com `sqlcmd -S localhost -E -d ERP_UAT_A -i notas.sql -v QTD=50000 EMPRESA=1 DIAS=180` e "Reiniciar" com 6 meses. Enquanto aparece "Executando", clicar "Reiniciar" de novo. | A execução em andamento termina depois do lote atual com resultado "Cancelada" nos logs; só então começa a nova carga inicial; vendas sem duplicar. | [ ] | |
| C6-2 | Durante uma carga longa, "Pausar". | O lote atual é concluído (resultado "Cancelada" no `execucao_fim`) e nenhuma execução nova começa. | [ ] | |

### LOG-05: tela de logs

| # | Passos | Resultado esperado | OK | Obs. |
| - | ------ | ------------------ | -- | ---- |
| L-1 | "Logs" (período padrão: últimos 7 dias). | Eventos de todas as configurações: data/hora, nível, evento, tenant, instância, banco, rotina, resultado, tipo de erro, duração, lidos, enviados, rejeitados, mensagem. | [ ] | |
| L-2 | Tenant = Loja B → "Filtrar". | Só eventos de Loja B. | [ ] | |
| L-3 | Banco = `ERP_UAT_A` → "Filtrar". | Só eventos do banco `ERP_UAT_A`. | [ ] | |
| L-4 | Rotina = Vendas. | Todos os eventos (única rotina). | [ ] | |
| L-5 | Resultado = Falha. | Só os eventos das execuções que terminaram em Falha (passo 10-3), incluindo o erro tipo "Conexão" e a duração; nenhuma senha. | [ ] | |
| L-6 | Datas: De = amanhã, Até = hoje. | "A data final é anterior à inicial."; grade inalterada. | [ ] | |
| L-7 | Com o Coletor fechado, acrescentar uma linha `{quebrado` ao arquivo de log de hoje; abrir o Coletor e os logs. | Aviso "Não foi possível ler os logs do período: há um arquivo com linha inválida..."; a tela não fecha. Remover a linha depois. | [ ] | |

---

## 3. Resultado

| Item | Valor |
| ---- | ----- |
| Data da execução | |
| Executado por | |
| Versão (commit) | |
| Passos OK / total | |
| Divergências | |
