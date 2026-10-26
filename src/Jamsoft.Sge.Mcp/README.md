# Jamsoft.Sge.Mcp — servidor MCP (HTTP) de vendas

Servidor MCP em .NET 10 + Dapper que expõe as vendas da plataforma JAMSOFT Dashboards para o Claude
(funcionalidade de Painéis). Lê o **banco da plataforma** (as vendas que o Coletor já envia) e cada **api-key**
identifica um **tenant**: o MCP só enxerga as vendas dele.

```
Claude / painel ──HTTPS──▶ /mcp  (X-Api-Key: sge_xxx)
                             │
                             └─▶ banco da plataforma (JamDashboard)
                                   CHAVE_MCP + TENANT: valida a chave (hash) e o tenant ativo
                                   VENDA + EMPRESA:    vendas WHERE TENANT_ID = tenant da chave
```

## Estrutura

```
src/Jamsoft.Sge.Mcp/
  Program.cs                                configuração do host, autenticação e endpoint /mcp
  appsettings.json                          opções e connection string de produção
  appsettings.Development.json              connection string de desenvolvimento
  Configuracao/McpOptions.cs                opções da seção "Mcp"
  Seguranca/RepositorioChaves.cs            valida a chave em CHAVE_MCP (com cache)
  Seguranca/ApiKeyAuthenticationHandler.cs  autenticação ASP.NET Core por api-key
  Seguranca/Tenant.cs                       tenant da requisição (guardado no HttpContext)
  Dados/VendasRepositorio.cs                consulta de vendas (Dapper)
  Dados/VendaDto.cs                         linha de venda (mesmos nomes do Resultados.json)
  Ferramentas/VendasTools.cs                ferramentas MCP: listar_vendas, identificar_tenant
sql/
  001_permissoes_mcp.sql                    role só de leitura nas 4 tabelas que o MCP usa
```

A tabela `CHAVE_MCP` pertence à API (`Modulos/Mcp/ChaveMcp.cs`, migração `ChaveMcp`): é criada pelo
`dotnet run -- migrar` da API e apagada junto com o tenant na exclusão. O formato da chave é compartilhado
em `Jamsoft.DashBoards.Contratos/Mcp/FormatoChaveMcp.cs`.

## Ferramentas MCP

| Ferramenta | Parâmetros | Retorno |
|---|---|---|
| `listar_vendas` | `inicio`, `fim` (yyyy-MM-dd, opcionais, inclusivos no fuso de São Paulo; filtram `DATA_HORA`) | array JSON com `venda_id, TipoDFe, data_hora, valor, cod_cliente, nome_cliente, vendedor_codigo, vendedor_nome, situacao, Nome_Empresa, Cod_Empresa, QtdItens` |
| `identificar_tenant` | — | nome e ID público do tenant e o prefixo da chave atual |

Sem datas, `listar_vendas` usa do 1º dia de dois meses atrás até hoje. O período máximo é `Mcp:MaxDiasConsulta` (400 dias).
`TipoDFe` pode ser `NF-e`, `NFC-e` ou `Pedido`; `situacao`, `Finalizada`, `Contingência`, `Cancelada` ou `Inutilizada`
(efetivadas = Finalizada + Contingência, como no painel da API). `data_hora` vem com o offset de São Paulo.

## Passo a passo

### 1. Banco

Na API, aplique a migração (`cd src/Jamsoft.DashBoards.Api && dotnet run -- migrar`). Depois rode
`sql/001_permissoes_mcp.sql` e vincule um login próprio do MCP à role `jamsoft_dashboards_mcp` (só `SELECT`).

### 2. Connection string

```bash
# desenvolvimento
dotnet user-secrets set "ConnectionStrings:Plataforma" "Server=...;Database=JamDashboard;User Id=<login do MCP>;Password=...;TrustServerCertificate=True;Application Name=Jamsoft.Sge.Mcp"

# produção (IIS / serviço): variável de ambiente
ConnectionStrings__Plataforma=Server=...;Database=JamDashboard;...
```

### 3. Uma api-key por tenant

No portal, o Admin do tenant (ou o SuperAdmin com o tenant selecionado) gera e revoga as chaves em
**Conector Claude** (`/cliente/chaves-mcp`, API `/api/chaves-mcp`). As duas ações ficam na auditoria
(`CHAVE_MCP_CRIADA`, `CHAVE_MCP_REVOGADA`).

Para implantação ou suporte, o CLI da API faz o mesmo pelo CNPJ do tenant:

```bash
cd src/Jamsoft.DashBoards.Api
dotnet run -- gerar-chave-mcp --cnpj 00.000.000/0001-00 --descricao "Claude - Painéis" --expira 2027-12-31
```

A api-key aparece **uma única vez**; no banco fica só o hash SHA-256. `--expira` é opcional e inclusivo.

Para revogar: `dotnet run -- revogar-chave-mcp --prefixo XXXXXXXX` (o prefixo é a parte entre `sge_` e o segundo `_`).
A revogação, e também a inativação ou exclusão do tenant, valem em até `Mcp:CacheSegundos` (60 s).

### 4. Rodar e testar

```bash
dotnet run          # sobe conforme launchSettings ou ASPNETCORE_URLS
curl http://localhost:52859/health
```

Teste com o MCP Inspector:

```bash
npx @modelcontextprotocol/inspector
# Transport: Streamable HTTP · URL: http://localhost:52859/mcp
# Em "Authentication"/headers: X-Api-Key = sge_...
```

Ou direto com curl (JSON-RPC):

```bash
curl -s http://localhost:52859/mcp \
  -H "X-Api-Key: sge_..." \
  -H "Content-Type: application/json" \
  -H "Accept: application/json, text/event-stream" \
  -d '{"jsonrpc":"2.0","id":1,"method":"tools/call","params":{"name":"listar_vendas","arguments":{"inicio":"2026-09-01","fim":"2026-10-22"}}}'
```

Sem chave, com chave errada, revogada, vencida ou de tenant inativo a resposta é **401**.

### 5. Publicar

O Claude acessa o servidor pela internet, então ele precisa de **HTTPS público** (IIS com certificado, Azure App Service, um proxy reverso…).

```bash
dotnet publish -c Release -o ./publicar
```

No IIS: instale o *ASP.NET Core Hosting Bundle* do .NET 10, crie o site apontando para `publicar` e configure as variáveis de ambiente no `web.config` ou no pool.

## Como a api-key é enviada

O servidor aceita, nesta ordem:

1. `X-Api-Key: sge_...`
2. `Authorization: Bearer sge_...`
3. `?api_key=sge_...` na URL — **desligado por padrão** (`Mcp:PermitirChaveNaQuery`). Use só se o cliente MCP não conseguir mandar cabeçalhos; a URL costuma aparecer em logs.

## Conectar ao Claude e ao painel

1. Em **Customize › Connectors › Add custom connector**: URL `https://sistemas.ilojajamsoft.com.br/JamsoftDashboardMcp/mcp` (a tela de chaves já mostra essa URL), autenticação **No sign-in**, e em **Request headers** adicione `x-api-key` com a chave.
   Se os *Request headers* não aparecerem para a conta, ligue `Mcp:PermitirChaveNaQuery` e use `https://sistemas.ilojajamsoft.com.br/JamsoftDashboardMcp/mcp?api_key=sge_...` como URL.
2. Teste numa conversa pedindo a ferramenta `identificar_tenant`.
3. No painel, a fonte `vendas` chama `listar_vendas`. Os gráficos e filtros continuam iguais, porque o JSON tem os mesmos campos.
   Cada pessoa que abrir o painel precisa ter o conector configurado na conta dela.
