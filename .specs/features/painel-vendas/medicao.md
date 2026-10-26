# Painel de Vendas — Medição de desempenho e revisão visual (T22)

**Data**: 2026-10-25 · **Branch**: `feat/plataforma-tenancy-acesso` · **Fora do gate de testes**

## Veredito

- **Meta de 2 s (p95, 1 milhão de vendas, KPIs do mês corrente): ATINGIDA.** p95 dos KPIs = 115 ms. Os 4 blocos do mês corrente ficam abaixo de 1 s (pior: evolução, 959 ms).
- **Períodos longos NÃO atingem 2 s.** Com 12 e 24 meses, evolução (7,1–8,2 s), rankings (3,0–6,0 s) e, em 24 meses, KPIs (1,7 s) ficam perto ou acima da meta. Numa das execuções, a evolução de 12 meses estourou o timeout de 30 s do SqlClient e a API respondeu 500 (ver "Anomalia"). **Recomendação registrada abaixo, para decisão.**
- **Acessibilidade (Lighthouse 13.5.0)**: login 100, painel 96. Ambas ≥ 90. O painel perde pontos só em `color-contrast`.
- **PWA instalável**: sim, pelo critério do próprio Chromium (sem erros de instalação nem de manifest, com o service worker controlando a página). **A nota da categoria PWA do Lighthouse não foi medida** (ver "Lighthouse").

## Ambiente

| Item | Valor |
| ---- | ----- |
| Máquina | Notebook, Intel Core i5-13450HX (16 threads), 15,7 GB de RAM, cerca de 2,3 GB livres durante a medição |
| Banco | SQL Server 2022 Developer (16.0.1000.6), local, `max server memory` padrão, banco descartável `JD_MEDICAO_<guid>` em RECOVERY SIMPLE e nível de compatibilidade 150 |
| API | Build Release, `http://localhost:5180`, mesmo computador do banco, requisições sequenciais (1 cliente) |
| Dados | 1.000.000 vendas num tenant, espalhadas pelos últimos 24 meses, em 5 empresas, com 200 vendedores e 5.000 clientes. Cerca de 3% contingência, 2% canceladas, 1% inutilizadas e 30% NF-e. Semeadura em 51 s |
| Navegadores | Chrome instalado (HeadlessChrome 155) para o Lighthouse; Chromium 156 do Playwright para a instalabilidade |

Não é um ambiente de produção: banco, API e cliente dividem a mesma máquina, com pouca memória livre. Os números servem para comparar blocos e períodos, não como SLA.

## Como reproduzir

```powershell
# p95 com 1 milhão de vendas (cria e apaga o próprio banco JD_MEDICAO_<guid>)
powershell -ExecutionPolicy Bypass -File scripts\medicao-painel\medir-painel.ps1 -Vendas 1000000 -Repeticoes 20

# Acessibilidade e instalabilidade (build de produção do Angular + Lighthouse); o volume não muda essa medição
powershell -ExecutionPolicy Bypass -File scripts\medicao-painel\medir-painel.ps1 -Vendas 50000 -Repeticoes 2 -Lighthouse
```

- `scripts/medicao-painel/medir-painel.ps1`: cria o banco, aplica as migrações, cria SuperAdmin, tenant e Admin, semeia as vendas por SQL e mede cada bloco. A duração vem do log do `DuracaoPainelFilter` (T7), `Painel {Bloco} {DuracaoMs} ms`. Para cada combinação de bloco e cenário, o script faz 1 chamada de aquecimento, descartada, e 20 repetições. O p95 usa o método nearest-rank.
- `scripts/medicao-painel/lighthouse-painel.mjs`: serve o build de produção com `/api` repassado para a API. Mede a instalabilidade via CDP e roda o Lighthouse na tela de login e no painel. No painel, a sessão do Admin vem do cookie de refresh.

## p95 por bloco (1.000.000 vendas, 20 repetições, duração no servidor)

| Cenário | Bloco | p50 (ms) | **p95 (ms)** | máx (ms) | p95 no cliente (ms) | ≤ 2 s? |
| ------- | ----- | -------: | -----------: | -------: | ------------------: | :----: |
| Mês corrente (25 dias) | kpis | 105 | **115** | 120 | 135 | ✅ |
| Mês corrente | evolucao | 908 | **959** | 961 | 985 | ✅ |
| Mês corrente | rankings | 226 | **254** | 271 | 276 | ✅ |
| Mês corrente | dfe | 50 | **58** | 60 | 85 | ✅ |
| 12 meses | kpis | 1.508 | **1.586** | 1.587 | 1.609 | ✅ (no limite) |
| 12 meses | evolucao | 7.730 | **8.223** | 8.228 | 8.246 | ❌ |
| 12 meses | rankings | 2.959 | **3.045** | 3.058 | 3.075 | ❌ |
| 12 meses | dfe | 768 | **798** | 800 | 826 | ✅ |
| 24 meses | kpis | 1.504 | **1.674** | 1.699 | 1.703 | ✅ (no limite) |
| 24 meses | evolucao | 6.941 | **7.106** | 7.514 | 7.140 | ❌ |
| 24 meses | rankings | 5.908 | **6.047** | 6.195 | 6.071 | ❌ |
| 24 meses | dfe | 1.571 | **1.632** | 1.639 | 1.652 | ✅ (no limite) |

Cada consulta calcula o período e o período anterior. Em 12 meses, isso cobre cerca de 1 milhão de linhas. Em 24 meses, cobre cerca de 1 milhão no atual e nenhuma no anterior.

### Anomalia observada (não explicada)

Houve 3 execuções completas com 1 milhão de vendas:

1. **Primeira execução.** Os números do mês corrente bateram com a tabela acima: KPIs com p95 de 132 ms e evolução com 1.034 ms. A evolução de 12 meses levou cerca de 7 s por chamada, mas uma das chamadas levou 33 s, estourou o `CommandTimeout` de 30 s e a API respondeu **500**. Por isso o script passou a contar falhas e seguir em frente.
2. **Segunda execução.** **Todas** as consultas estouraram os 30 s, inclusive os KPIs do mês corrente, que em condições normais levam cerca de 110 ms. Não havia bloqueio visível. Suspeita: pressão de memória ou de I/O na máquina logo depois de apagar o banco da execução anterior. A causa não foi confirmada.
3. **Terceira execução.** É a da tabela acima, com 0 falhas.

**Conclusão:** neste ambiente, uma consulta de período longo pode passar de 30 s e virar erro 500. Na tela, o bloco mostra "Tentar novamente" (UI-08).

## Recomendação (para decisão, conforme a spec)

O p95 do mês corrente atende à meta. O custo cresce de forma linear com o número de linhas do período, e 2 causas dominam:

1. **`AT TIME ZONE` por linha na evolução** (`EF.Functions.AtTimeZone`, AD-018). Com 20 mil vendas, a evolução de 12 meses já levava cerca de 470 ms, contra cerca de 30 ms dos KPIs no mesmo período. A conversão de fuso por linha é o gargalo.
   - **Opção A, mais barata:** como São Paulo não tem horário de verão desde 2019, trocar por `SWITCHOFFSET(DATA_HORA, '-03:00')` ou por uma coluna computada persistida `DATA_LOCAL` (date) incluída no índice. Isso muda o AD-018 e exige uma nova decisão.
2. **Rankings em períodos longos:** o agrupamento por código percorre todas as linhas do período 3 vezes, uma por lista.
   - **Opção B, a da spec:** **tabelas de agregados pré-calculados** por tenant × empresa × dia, com faturamento, vendas, itens e canceladas, e, para os rankings, por vendedor ou cliente × dia. A ingestão atualiza essas tabelas. Esta opção resolve evolução, KPIs, DF-e e rankings em qualquer período.
3. **Medida paliativa e independente:** limitar o período padrão dos atalhos, ou avisar quando o período for maior que 6 meses. Também cabe rever o `CommandTimeout` das consultas do painel para falhar rápido com uma mensagem clara.

A decisão fica com o Design. Nada foi alterado no código por esta tarefa.

**Decisão:** a feature fecha como está e a otimização dos períodos longos vai para uma feature separada (coluna de data local ou agregados pré-calculados). Ver AD-019 em `.specs/STATE.md`.

## Lighthouse e PWA

| Página | Ferramenta | Acessibilidade | Falhas | PWA instalável |
| ------ | ---------- | -------------: | ------ | -------------- |
| `/conta/login` | Lighthouse 13.5.0 (mobile, headless) | **100** | nenhuma | — |
| `/cliente` (painel, Admin logado) | Lighthouse 13.5.0 (mobile, headless) | **96** | `color-contrast` | — |
| `/conta/login` (build de produção) | CDP `Page.getInstallabilityErrors` + `Page.getAppManifest` (Chromium 156) | — | — | **Sim**: 0 erros de instalação, 0 erros de manifest, service worker controlando a página |

- **Não medido: a nota da categoria "PWA" do Lighthouse.** Ela não existe a partir do Lighthouse 12. O Lighthouse 11.7.1, último com essa categoria, falhou com `PROTOCOL_TIMEOUT` (`Debugger.disable`) no Chrome 155/156, tanto no Chrome instalado quanto no do Playwright. Em lugar dela, a instalabilidade foi medida pelo próprio critério do Chromium, o mesmo que o Lighthouse 11 consultava.
- **Falha de contraste no painel:** a variação positiva dos KPIs (`.variacao.sucesso`, `--sucesso` = `#388e3c` sobre branco, 12 px) tem contraste de **4,11:1**, abaixo de 4,5:1. É preciso escurecer o token de sucesso usado em texto pequeno, ou criar um `--sucesso-texto`, como já existe `--primary-texto`. **Corrigido depois** (`bcf8457`): por decisão do usuário, só o texto da variação do KPI passou a usar `#2e7d32` (5,13:1); `--sucesso` do DS não mudou.
- O Chrome headless desta máquina às vezes não pinta a página (`NO_FCP`). O script tenta até 3 vezes.

## Revisão visual contra o Design System

Fonte: screenshots do painel com o conjunto semeado do e2e, em 1440 × 900 e 360 × 740 (Chromium), mais a leitura dos componentes.

| Item do DS | Situação |
| ---------- | -------- |
| Shell: menu com gradiente vermelho, logo branca, header de 58 px com usuário e "Sair" | ✅ |
| Tipografia: Inter nos textos e JetBrains Mono nos valores (KPIs, tabelas, legenda) | ✅ |
| Cards com cabeçalho e título em caixa alta, rótulos de seção em mono | ✅ |
| Botões: "Atualizar" primário, atalhos em `btn-claro` | ✅ |
| Cores dos gráficos: atual em `--primary` e anterior tracejado em `--text-muted` | ✅ |
| Tabelas de ranking com barra de participação em vermelho do DS | ✅ |
| Variação "—" quando o anterior é 0 | ✅ |
| Eixo Y da evolução com separador en-US (`2,500` em vez de `2.500`) | ✅ Corrigido em `d430d2a` (formatter pt-BR: `2.500`) |
| Rosca NF-e × NFC-e em 1440 px | ⚠️ Na captura de 1440 px, o card mostrou só a legenda, sem a rosca. Em 360 px a rosca aparece, mas a captura saiu no meio da animação. Precisa de verificação manual: pode ser só o momento da captura, ou o gráfico com altura 0 quando o card estica na grade |
| Contraste da variação positiva (`--sucesso`) | ✅ Corrigido em `bcf8457` (`#2e7d32`, 5,13:1, só no painel) |
| 360 px: sem rolagem horizontal da página | ✅ (e2e T21). Tabelas de ranking rolam dentro do card |
| 360 px: menu lateral vira bloco no topo, com todos os itens, e empurra o painel para baixo; "Dados atualizados em…" quebra em 4 linhas no header; o valor "R$ 2.500,00" quebra em 2 linhas no KPI | ⚠️ Funcional, mas pede ajuste de layout móvel (menu recolhível, header compacto). Melhoria para decidir |

## O que não foi medido

- A nota da categoria PWA do Lighthouse: indisponível com o Chrome atual (ver acima).
- O p95 sob concorrência (vários usuários simultâneos): o script mede requisições sequenciais.
- A medição num ambiente com memória adequada e banco separado da API, como prevê o design: não havia esse ambiente disponível.
- A instalação real num Android e o modo avião: dependem de um aparelho.
