# Jamsoft.Sge.Mcp — servidor MCP (HTTP) de vendas do SGE

Servidor MCP em .NET 10 + Dapper que expõe as vendas do SGE para o Claude (e para o painel de vendas).
Cada **api-key** identifica um cliente e aponta para o **banco daquele cliente** (servidor próprio por cliente).

```
Claude / painel ──HTTPS──▶ /mcp  (X-Api-Key: sge_xxx)
                             │
                             ├─▶ banco central SGE_MCP: valida a chave (hash) e pega a
                             │   connection string do cliente (cifrada com AES-256-GCM)
                             └─▶ banco do cliente: executa a consulta de vendas (Dapper)
```

## Estrutura

```
src/Jamsoft.Sge.Mcp/
  Program.cs                          configuração do host, autenticação e endpoint /mcp
  appsettings.json                    connection string do banco central e opções
  Configuracao/McpOptions.cs          opções da seção "Mcp"
  Seguranca/ApiKey.cs                 formato, geração e hash da chave
  Seguranca/ProtetorConexao.cs        cifra/decifra connection strings (AES-256-GCM)
  Seguranca/RepositorioChaves.cs      valida a chave no banco central (com cache)
  Seguranca/ApiKeyAuthenticationHandler.cs  autenticação ASP.NET Core por api-key
  Seguranca/Tenant.cs                 cliente da requisição (guardado no HttpContext)
  Dados/VendasRepositorio.cs          consulta de vendas (Dapper)
  Dados/VendaDto.cs                   linha de venda (mesmos nomes do Resultados.json)
  Ferramentas/VendasTools.cs          ferramentas MCP: listar_vendas, identificar_conexao
  Cli/ComandosCli.cs                  gerar-chave-mestra e gerar-chave
sql/
  001_banco_central_McpApiKey.sql     tabela de chaves
  002_banco_cliente_indices_e_usuario.sql  usuário de leitura e índices sugeridos
```

## Ferramentas MCP

| Ferramenta | Parâmetros | Retorno |
|---|---|---|
| `listar_vendas` | `inicio`, `fim` (yyyy-MM-dd, opcionais, inclusivos; filtram `datafatura`) | array JSON com `venda_id, TipoDFe, data_hora, valor, cod_cliente, nome_cliente, vendedor_codigo, vendedor_nome, situacao, Nome_Empresa, Cod_Empresa, QtdItens` |
| `identificar_conexao` | — | cliente, servidor e banco da chave atual (sem senha) |

Sem datas, `listar_vendas` usa do 1º dia de dois meses atrás até hoje. O período máximo é `Mcp:MaxDiasConsulta` (400 dias).

## Passo a passo

### 1. Banco central

Crie um banco (ex.: `SGE_MCP`) e rode `sql/001_banco_central_McpApiKey.sql`. Crie um login só com `SELECT` na tabela.

### 2. Chave mestra de criptografia

```bash
cd src/Jamsoft.Sge.Mcp
dotnet run -- gerar-chave-mestra
```

Guarde o valor fora do código:

```bash
# desenvolvimento
dotnet user-secrets set "Mcp:ChaveCriptografia" "<valor>"
dotnet user-secrets set "ConnectionStrings:Master" "Server=...;Database=SGE_MCP;..."

# produção (IIS / serviço): variáveis de ambiente
Mcp__ChaveCriptografia=<valor>
ConnectionStrings__Master=Server=...;Database=SGE_MCP;...
```

> Trocar a chave mestra torna ilegíveis todas as connection strings já gravadas. Faça backup dela.

### 3. Uma api-key por cliente

Em cada banco de cliente, crie um usuário **só de leitura** (`sql/002_...`). Depois:

```bash
dotnet run -- gerar-chave --cliente "Loja Centro" \
  --conexao "Server=sql-cliente01;Database=SGE;User Id=mcp_sge_leitura;Password=...;TrustServerCertificate=True;Application Name=Jamsoft.Sge.Mcp" \
  --expira 2027-12-31
```

O comando mostra a api-key **uma única vez** e o `INSERT` para rodar no banco central. No banco fica só o hash SHA-256 da chave e a connection string cifrada.

Para revogar: `UPDATE dbo.McpApiKey SET Ativo = 0 WHERE Prefixo = '...'` (vale em até `Mcp:CacheMinutos`).

### 4. Rodar e testar

```bash
dotnet run          # sobe em http://localhost:5xxx (veja launchSettings ou ASPNETCORE_URLS)
curl http://localhost:5000/health
```

Teste com o MCP Inspector:

```bash
npx @modelcontextprotocol/inspector
# Transport: Streamable HTTP · URL: http://localhost:5000/mcp
# Em "Authentication"/headers: X-Api-Key = sge_...
```

Ou direto com curl (JSON-RPC):

```bash
curl -s http://localhost:5000/mcp \
  -H "X-Api-Key: sge_..." \
  -H "Content-Type: application/json" \
  -H "Accept: application/json, text/event-stream" \
  -d '{"jsonrpc":"2.0","id":1,"method":"tools/call","params":{"name":"listar_vendas","arguments":{"inicio":"2026-09-01","fim":"2026-10-22"}}}'
```

Sem chave ou com chave errada a resposta é **401**. Se o curl reclamar de sessão ou de `initialize`, use o Inspector, que faz o handshake completo.

### 5. Publicar

O Claude acessa o servidor pela internet, então ele precisa de **HTTPS público** (IIS com certificado, Azure App Service, um proxy reverso…).
Se o servidor tiver de ficar só na rede interna, veja os *MCP tunnels* na documentação do Claude.

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

1. Em **Customize › Connectors › Add custom connector**: URL `https://seu-servidor/mcp`, autenticação **No sign-in**, e em **Request headers** adicione `x-api-key` com a chave.
   Os *Request headers* estão em beta e ainda não aparecem para todas as contas. Se não aparecer, ligue `Mcp:PermitirChaveNaQuery` e use `https://seu-servidor/mcp?api_key=sge_...` como URL.
2. Teste numa conversa pedindo a ferramenta `identificar_conexao`.
3. No painel, a fonte `vendas` troca de "arquivo" para "consulta" no conector, chamando `listar_vendas`. Os gráficos e filtros continuam iguais, porque o JSON tem os mesmos campos.
   Cada pessoa que abrir o painel precisa ter o conector configurado na conta dela; a consulta roda com a chave dela.

## Observações sobre a consulta

- O filtro fixo `MONTH(datafatura) >= 9 AND YEAR(datafatura) = 2026` virou `datafatura >= @Inicio AND datafatura < @FimExclusivo`: mesmo resultado para o período, mas pode usar índice.
- O período filtra `datafatura`, mas o campo exibido como `data_hora` é `dataentrega`. Se uma nota for faturada num mês e entregue em outro, ela aparece no gráfico pelo dia da entrega. Vale decidir qual das duas datas o painel deve usar.
- `QtdItens` é uma subconsulta por nota; com índice em `NotaSaidaProd(numnotasaida)` ela fica barata.
