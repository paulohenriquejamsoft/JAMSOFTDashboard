<#
.SYNOPSIS
  Mede o p95 de cada bloco do painel de vendas (T22 de painel-vendas) com um tenant de N vendas.

.DESCRIPTION
  1. Cria um banco descartável JD_MEDICAO_<guid> no SQL Server (Windows auth), aplica as migrações e cria um
     SuperAdmin pela CLI da API (build Release).
  2. Sobe a API em http://localhost:5180, cria um tenant pela API e semeia por SQL N vendas espalhadas pelos
     últimos 24 meses (5 empresas, 200 vendedores, 5000 clientes, ~3% contingência, 2% canceladas, 1% inutilizadas,
     30% NF-e). A semeadura é feita em lotes de 100 mil com o banco em RECOVERY SIMPLE.
  3. Para cada cenário (mês corrente, 12 meses, 24 meses) e cada bloco (kpis, evolucao, rankings, dfe): 1 chamada de
     aquecimento + N repetições sequenciais. Mede a duração no servidor (log "Painel {Bloco} {DuracaoMs} ms" do
     DuracaoPainelFilter, T7) e no cliente (Stopwatch em volta da chamada HTTP).
  4. Imprime uma tabela Markdown com p50/p95/máx por bloco e cenário e grava o JSON bruto em -Saida.
  5. Com -Lighthouse: build de produção do Angular e lighthouse-painel.mjs (Acessibilidade e instalabilidade da PWA) com o Admin do
     tenant; os relatórios JSON ficam na pasta de -Saida.
  6. Derruba a API e apaga SÓ o banco que ele mesmo criou (a menos que -ManterBanco). Nunca toca em outros bancos.

.EXAMPLE
  powershell -ExecutionPolicy Bypass -File scripts\medicao-painel\medir-painel.ps1 -Vendas 1000000 -Repeticoes 30
#>
param(
  [int] $Vendas = 1000000,
  [int] $Repeticoes = 30,
  [string] $Servidor = 'localhost',
  [int] $Porta = 5180,
  [string] $Saida = (Join-Path $env:TEMP 'medicao-painel.json'),
  [switch] $ManterBanco,
  # Roda também o Lighthouse (build de produção do Angular + lighthouse-painel.mjs) com o Admin do tenant.
  [switch] $Lighthouse
)

$ErrorActionPreference = 'Stop'
$raiz = Resolve-Path (Join-Path $PSScriptRoot '..\..')
$projetoApi = Join-Path $raiz 'src\Jamsoft.DashBoards.Api'
$dll = Join-Path $projetoApi 'bin\Release\net10.0\Jamsoft.DashBoards.Api.dll'
$banco = 'JD_MEDICAO_' + [guid]::NewGuid().ToString('N')
$urlApi = "http://localhost:$Porta"
$logApi = Join-Path $env:TEMP "$banco.api.log"
$api = $null

function Sql([string] $comando, [string] $database = 'master') {
  $arquivo = [IO.Path]::GetTempFileName()
  # sqlcmd lê o arquivo como UTF-8 com BOM.
  [IO.File]::WriteAllText($arquivo, "SET NOCOUNT ON;`r`n$comando", (New-Object Text.UTF8Encoding $true))
  try {
    $saida = & sqlcmd -S $Servidor -E -C -b -d $database -h -1 -W -i $arquivo
    if ($LASTEXITCODE -ne 0) { throw "sqlcmd falhou ($LASTEXITCODE): $saida" }
    return $saida
  } finally { Remove-Item $arquivo -Force }
}

function Api([string] $metodo, [string] $rota, $corpo, [string] $token, [string] $tenant) {
  $cabecalhos = @{}
  if ($token) { $cabecalhos['Authorization'] = "Bearer $token" }
  if ($tenant) { $cabecalhos['X-Tenant'] = $tenant }
  $parametros = @{ Method = $metodo; Uri = "$urlApi$rota"; Headers = $cabecalhos; UseBasicParsing = $true }
  if ($null -ne $corpo) {
    $parametros['Body'] = [Text.Encoding]::UTF8.GetBytes(($corpo | ConvertTo-Json -Depth 5))
    $parametros['ContentType'] = 'application/json; charset=utf-8'
  }
  return (Invoke-WebRequest @parametros).Content | ConvertFrom-Json
}

function Percentil([double[]] $valores, [double] $p) {
  if (-not $valores -or $valores.Count -eq 0) { return $null }
  $ordenados = $valores | Sort-Object
  $indice = [math]::Ceiling($p / 100 * $ordenados.Count) - 1
  return $ordenados[[math]::Max(0, $indice)]
}

function CnpjDeTeste {
  $r = New-Object Random
  $base = 1..12 | ForEach-Object { $r.Next(0, 10) }
  $dv = {
    param($digitos, $pesos)
    $soma = 0; for ($i = 0; $i -lt $pesos.Count; $i++) { $soma += $digitos[$i] * $pesos[$i] }
    $resto = $soma % 11; if ($resto -lt 2) { 0 } else { 11 - $resto }
  }
  $d1 = & $dv $base @(5, 4, 3, 2, 9, 8, 7, 6, 5, 4, 3, 2)
  $d2 = & $dv ($base + $d1) @(6, 5, 4, 3, 2, 9, 8, 7, 6, 5, 4, 3, 2)
  return (($base + $d1 + $d2) -join '')
}

try {
  Write-Host "Build Release da API..."
  & dotnet build $projetoApi -c Release -v q -nologo | Out-Null
  if ($LASTEXITCODE -ne 0) { throw 'Build da API falhou.' }

  Write-Host "Criando o banco $banco..."
  Sql "CREATE DATABASE [$banco]; ALTER DATABASE [$banco] SET COMPATIBILITY_LEVEL = 150; ALTER DATABASE [$banco] SET RECOVERY SIMPLE;" | Out-Null

  $env:ASPNETCORE_ENVIRONMENT = 'Development'
  $env:ConnectionStrings__Plataforma = "Server=$Servidor;Database=$banco;Integrated Security=true;TrustServerCertificate=true"
  $bytes = New-Object byte[] 48; [Security.Cryptography.RandomNumberGenerator]::Create().GetBytes($bytes)
  $env:Jwt__Chave = [Convert]::ToBase64String($bytes)
  $env:Acesso__LimiteLoginPorMinuto = '200'

  Push-Location $projetoApi
  try {
    & dotnet $dll migrar | Out-Null
    $emailSuper = 'medicao@medicao.jamsoft.test'
    $saidaCli = & dotnet $dll criar-superadmin --email $emailSuper --nome 'SuperAdmin Medicao'
  } finally { Pop-Location }
  $linhas = @($saidaCli)
  $indice = [array]::FindIndex($linhas, [Predicate[string]] { param($l) $l -like '*Senha tempor*' })
  $temporaria = $linhas[$indice + 1].Trim()

  Write-Host "Subindo a API em $urlApi (log em $logApi)..."
  $api = Start-Process dotnet -ArgumentList "`"$dll`" --urls $urlApi" -WorkingDirectory $projetoApi -PassThru `
    -WindowStyle Hidden -RedirectStandardOutput $logApi -RedirectStandardError "$logApi.err"
  $limite = (Get-Date).AddSeconds(60)
  while ($true) {
    try { if ((Invoke-WebRequest "$urlApi/health" -UseBasicParsing -TimeoutSec 2).StatusCode -eq 200) { break } } catch { }
    if ((Get-Date) -gt $limite) { throw 'A API não respondeu em 60 s.' }
    Start-Sleep -Milliseconds 500
  }

  $senha = 'Medicao' + (Get-Random -Minimum 100000 -Maximum 999999) + 'aB'
  $login = Api POST '/api/auth/login' @{ email = $emailSuper; senha = $temporaria }
  $token = (Api POST '/api/auth/trocar-senha' @{ senhaAtual = $temporaria; novaSenha = $senha } $login.accessToken).accessToken
  $admin = @{ nome = 'Admin Medicao'; email = 'admin@medicao.jamsoft.test'; senhaInicial = 'Inicial' + (Get-Random -Minimum 100000 -Maximum 999999) + 'aB' }
  $tenant = Api POST '/api/admin/tenants' @{ nome = 'Tenant Medicao'; cnpj = (CnpjDeTeste); adminInicial = $admin } $token
  $senhaAdmin = 'Admin' + (Get-Random -Minimum 100000 -Maximum 999999) + 'aB'
  $loginAdmin = Api POST '/api/auth/login' @{ email = $admin.email; senha = $admin.senhaInicial }
  Api POST '/api/auth/trocar-senha' @{ senhaAtual = $admin.senhaInicial; novaSenha = $senhaAdmin } $loginAdmin.accessToken | Out-Null

  Write-Host "Semeando $Vendas vendas..."
  $semeadura = [Diagnostics.Stopwatch]::StartNew()
  Sql @"
DECLARE @t INT = (SELECT ID FROM TENANT WHERE ID_PUBLICO = '$($tenant.idPublico)');
INSERT INTO COLETOR (NOME, CLIENT_ID, SECRET_HASH) VALUES (N'Coletor medicao', NEWID(), HASHBYTES('SHA2_256', 'medicao'));
DECLARE @c INT = SCOPE_IDENTITY();
INSERT INTO ORIGEM_SINCRONIZACAO (TENANT_ID, COLETOR_ID, CONFIGURACAO_ID, ROTINA, ULTIMA_SITUACAO, ULTIMO_SUCESSO_EM)
  VALUES (@t, @c, NEWID(), 'Vendas', 'Sucesso', SYSUTCDATETIME());
DECLARE @o INT = SCOPE_IDENTITY();
INSERT INTO EMPRESA (TENANT_ID, COD_EMPRESA, NOME, ORIGEM_ID)
  SELECT @t, n, CONCAT(N'Empresa ', n), @o FROM (VALUES (1), (2), (3), (4), (5)) v(n);

DECLARE @total INT = $Vendas, @lote INT = 100000, @feito INT = 0;
DECLARE @inicio DATETIMEOFFSET(0) = DATEADD(MONTH, -24, SYSDATETIMEOFFSET());
DECLARE @segundos INT = DATEDIFF(SECOND, @inicio, SYSDATETIMEOFFSET());
WHILE @feito < @total
BEGIN
  ;WITH n AS (
    SELECT TOP (CASE WHEN @total - @feito < @lote THEN @total - @feito ELSE @lote END)
      -- BIGINT: os multiplicadores do CHECKSUM estouram INT acima de ~270 mil vendas.
      CAST(ROW_NUMBER() OVER (ORDER BY (SELECT NULL)) AS BIGINT) + @feito AS i
    FROM sys.all_objects a CROSS JOIN sys.all_objects b
  ), v AS (
    SELECT i,
      i % 5 + 1 AS cod_empresa,
      CASE WHEN i % 20 = 0 THEN NULL ELSE (CHECKSUM(i * 17) & 2147483647) % 5000 + 1 END AS cliente,
      CASE WHEN i % 25 = 0 THEN NULL ELSE (CHECKSUM(i * 23) & 2147483647) % 200 + 1 END AS vendedor
    FROM n
  )
  INSERT INTO VENDA (TENANT_ID, EMPRESA_ID, COD_EMPRESA, VENDA_ID, TIPO_DFE, DATA_HORA, VALOR, COD_CLIENTE, NOME_CLIENTE,
    VENDEDOR_CODIGO, VENDEDOR_NOME, SITUACAO, QTD_ITENS, ORIGEM_ID)
  SELECT @t, e.ID, v.cod_empresa, CAST(v.i AS INT),
    CASE WHEN v.i % 10 < 3 THEN 'NF-e' ELSE 'NFC-e' END,
    SWITCHOFFSET(DATEADD(SECOND, (CHECKSUM(v.i * 7919) & 2147483647) % @segundos, @inicio), '-03:00'),
    CAST(((CHECKSUM(v.i * 31) & 2147483647) % 100000) / 100.0 + 5 AS DECIMAL(19, 4)),
    v.cliente, CASE WHEN v.cliente IS NULL THEN NULL ELSE CONCAT(N'Cliente ', v.cliente) END,
    v.vendedor, CASE WHEN v.vendedor IS NULL THEN NULL ELSE CONCAT(N'Vendedor ', v.vendedor) END,
    CASE WHEN v.i % 50 = 0 THEN N'Cancelada' WHEN v.i % 97 = 0 THEN N'Inutilizada'
         WHEN v.i % 33 = 0 THEN N'Conting' + NCHAR(234) + N'ncia' ELSE N'Finalizada' END,
    (CHECKSUM(v.i * 13) & 2147483647) % 15 + 1, @o
  FROM v JOIN EMPRESA e ON e.TENANT_ID = @t AND e.COD_EMPRESA = v.cod_empresa;
  SET @feito += @lote;
  CHECKPOINT;
END
"@ $banco | Out-Null
  $semeadura.Stop()
  $contagem = (Sql "SELECT COUNT_BIG(*) FROM VENDA;" $banco | Select-Object -First 1).Trim()
  Write-Host "Semeadura: $contagem vendas em $([math]::Round($semeadura.Elapsed.TotalSeconds)) s."

  $hoje = [TimeZoneInfo]::ConvertTime([DateTime]::UtcNow, [TimeZoneInfo]::FindSystemTimeZoneById('E. South America Standard Time')).Date
  $cenarios = [ordered]@{
    'mes-corrente' = ''
    '12-meses'     = "?inicio=$($hoje.AddMonths(-12).AddDays(1).ToString('yyyy-MM-dd'))&fim=$($hoje.ToString('yyyy-MM-dd'))"
    '24-meses'     = "?inicio=$($hoje.AddMonths(-24).AddDays(1).ToString('yyyy-MM-dd'))&fim=$($hoje.ToString('yyyy-MM-dd'))"
  }
  $blocos = 'kpis', 'evolucao', 'rankings', 'dfe'
  $resultado = @()
  foreach ($cenario in $cenarios.Keys) {
    foreach ($bloco in $blocos) {
      $rota = "/api/painel/$bloco$($cenarios[$cenario])"
      # Uma falha (ex.: 500 por timeout de 30 s do SqlClient) é contada e a medição segue.
      $falhas = 0
      try { Api GET $rota $null $token $tenant.idPublico | Out-Null } catch { $falhas++ }   # aquecimento
      $marca = (Get-Content $logApi | Measure-Object -Line).Lines
      [double[]] $duracoesCliente = @()
      for ($i = 0; $i -lt $Repeticoes; $i++) {
        $sw = [Diagnostics.Stopwatch]::StartNew()
        try { Api GET $rota $null $token $tenant.idPublico | Out-Null } catch { $falhas++ }
        $sw.Stop(); $duracoesCliente += $sw.Elapsed.TotalMilliseconds
      }
      Start-Sleep -Milliseconds 300
      [double[]] $duracoesServidor = @(Get-Content $logApi | Select-Object -Skip $marca |
        Select-String -Pattern "Painel $bloco (\d+) ms" | ForEach-Object { [double] $_.Matches[0].Groups[1].Value })
      $resultado += [pscustomobject]@{
        cenario = $cenario; bloco = $bloco; n = $duracoesServidor.Count
        servidorP50 = (Percentil -valores $duracoesServidor -p 50); servidorP95 = (Percentil -valores $duracoesServidor -p 95)
        servidorMax = ($duracoesServidor | Measure-Object -Maximum).Maximum
        clienteP95 = [math]::Round((Percentil -valores $duracoesCliente -p 95), 1); falhas = $falhas
      }
    }
  }

  $ambiente = [pscustomobject]@{
    data = (Get-Date).ToString('yyyy-MM-dd HH:mm'); vendas = $contagem; repeticoes = $Repeticoes
    cpu = (Get-CimInstance Win32_Processor).Name
    memoriaGB = [math]::Round((Get-CimInstance Win32_ComputerSystem).TotalPhysicalMemory / 1GB, 1)
    memoriaLivreGB = [math]::Round((Get-CimInstance Win32_OperatingSystem).FreePhysicalMemory / 1MB, 1)
    sqlServer = (Sql "SELECT @@VERSION;" | Select-Object -First 1).Trim()
    semeaduraSegundos = [math]::Round($semeadura.Elapsed.TotalSeconds)
  }
  [pscustomobject]@{ ambiente = $ambiente; resultado = $resultado } | ConvertTo-Json -Depth 4 | Set-Content $Saida -Encoding UTF8

  Write-Host ''
  Write-Host '| Cenário | Bloco | n | p50 servidor (ms) | p95 servidor (ms) | máx servidor (ms) | p95 cliente (ms) | falhas |'
  Write-Host '| ------- | ----- | - | ----------------- | ----------------- | ----------------- | ---------------- | ------ |'
  foreach ($r in $resultado) {
    Write-Host "| $($r.cenario) | $($r.bloco) | $($r.n) | $($r.servidorP50) | $($r.servidorP95) | $($r.servidorMax) | $($r.clienteP95) | $($r.falhas) |"
  }
  Write-Host ''
  Write-Host "JSON: $Saida"

  if ($Lighthouse) {
    Write-Host 'Build de produção do Angular e Lighthouse...'
    $web = Join-Path $raiz 'src\Jamsoft.DashBoards.Web'
    & npm --prefix $web run build | Out-Null
    if ($LASTEXITCODE -ne 0) { throw 'Build do Angular falhou.' }
    $env:API_URL = $urlApi; $env:ADMIN_EMAIL = $admin.email; $env:ADMIN_SENHA = $senhaAdmin
    $env:SAIDA = Split-Path $Saida -Parent
    & node (Join-Path $PSScriptRoot 'lighthouse-painel.mjs')
    if ($LASTEXITCODE -ne 0) { throw 'Lighthouse falhou.' }
  }
} finally {
  if ($api -and -not $api.HasExited) { Stop-Process -Id $api.Id -Force }
  if (-not $ManterBanco) {
    try {
      Sql "IF DB_ID('$banco') IS NOT NULL BEGIN ALTER DATABASE [$banco] SET SINGLE_USER WITH ROLLBACK IMMEDIATE; DROP DATABASE [$banco]; END" | Out-Null
    } catch { Write-Warning "Não foi possível apagar $banco : $_" }
  } else { Write-Host "Banco mantido: $banco" }
}
