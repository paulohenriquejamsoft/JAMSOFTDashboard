import { Page, expect, test } from '@playwright/test';
import { randomBytes, randomInt, randomUUID } from 'node:crypto';
import { URL_API, cliApi, connectionString, lerEstado, senhaTemporariaDaSaida, sql } from './ambiente';

/**
 * Painel de vendas ponta a ponta (DASH-01, DASH-03, PWA-05, PWA-06) contra API + Web reais. Cada projeto
 * (Chromium, Firefox, WebKit) semeia o próprio tenant: SuperAdmin próprio pela CLI, tenant e usuários pela API e
 * vendas por SQL no banco descartável do e2e. Assim não depende do SuperAdmin nem da ordem do `tenancy.spec.ts`.
 */

const id = randomUUID().replace(/-/g, '').slice(0, 10);
const novaSenha = (prefixo: string) => `${prefixo}${randomInt(100_000, 999_999)}aB`;
const admin = { email: `painel.admin.${id}@e2e.jamsoft.test`, inicial: novaSenha('Inicial'), senha: novaSenha('PainelAdm') };
const consulta = { email: `painel.consulta.${id}@e2e.jamsoft.test`, inicial: novaSenha('Inicial'), senha: novaSenha('PainelCons') };

/** "Hoje" e o 1º dia dos meses no fuso de São Paulo (AD-018), no formato yyyy-MM-dd. */
function datas(): { inicioMes: string; inicioMesAnterior: string } {
  const hoje = new Intl.DateTimeFormat('en-CA', { timeZone: 'America/Sao_Paulo' }).format(new Date());
  const [ano, mes] = hoje.split('-').map(Number);
  const anterior = mes === 1 ? { ano: ano - 1, mes: 12 } : { ano, mes: mes - 1 };
  const dois = (n: number) => String(n).padStart(2, '0');
  return { inicioMes: `${ano}-${dois(mes)}-01`, inicioMesAnterior: `${anterior.ano}-${dois(anterior.mes)}-01` };
}

/** CNPJ de teste com dígitos verificadores corretos, gerado na hora (nunca um CNPJ real). */
function cnpjDeTeste(): string {
  let base: number[];
  do base = Array.from({ length: 12 }, () => randomInt(10));
  while (base.every((d) => d === base[0]));
  const verificador = (digitos: number[], pesos: number[]) => {
    const resto = pesos.reduce((soma, peso, i) => soma + peso * digitos[i], 0) % 11;
    return resto < 2 ? 0 : 11 - resto;
  };
  const d1 = verificador(base, [5, 4, 3, 2, 9, 8, 7, 6, 5, 4, 3, 2]);
  const d2 = verificador([...base, d1], [6, 5, 4, 3, 2, 9, 8, 7, 6, 5, 4, 3, 2]);
  return [...base, d1, d2].join('');
}

async function api<T>(metodo: string, rota: string, corpo: unknown, token?: string, tenant?: string): Promise<T> {
  const cabecalhos: Record<string, string> = { 'Content-Type': 'application/json' };
  if (token) cabecalhos['Authorization'] = `Bearer ${token}`;
  if (tenant) cabecalhos['X-Tenant'] = tenant;
  const resposta = await fetch(`${URL_API}${rota}`, { method: metodo, headers: cabecalhos, body: JSON.stringify(corpo) });
  if (!resposta.ok) throw new Error(`${metodo} ${rota} → ${resposta.status}: ${await resposta.text()}`);
  return (await resposta.json()) as T;
}

/** Login + troca da senha inicial pela API; devolve o access token já sem a troca pendente. */
async function ativar(email: string, inicial: string, senha: string): Promise<string> {
  const login = await api<{ accessToken: string }>('POST', '/api/auth/login', { email, senha: inicial });
  const troca = await api<{ accessToken: string }>(
    'POST',
    '/api/auth/trocar-senha',
    { senhaAtual: inicial, novaSenha: senha },
    login.accessToken,
  );
  return troca.accessToken;
}

/**
 * Vendas do tenant, com os valores esperados calculados à mão.
 * Mês corrente (dia 1, 12:00 de São Paulo), efetivadas = Finalizada + Contingência:
 *   faturamento 1000 + 500 + 250 + 750 = 2.500,00; vendas 4; ticket 625,00; itens (3+2+1+4)/4 = 2,50;
 *   canceladas 1 / (4 + 1) = 20,00%. A Inutilizada fica fora de tudo.
 * Mês anterior (dia 1): uma Finalizada de 400,00.
 */
function semearVendas(tenantIdPublico: string): void {
  const { inicioMes, inicioMesAnterior } = datas();
  const banco = lerEstado().banco!;
  const contingencia = "N'Conting' + NCHAR(234) + N'ncia'";
  const venda = (empresa: string, cod: number, vendaId: number, tipo: string, dia: string, valor: string, situacao: string, itens: number) =>
    `(@t, ${empresa}, ${cod}, ${vendaId}, '${tipo}', '${dia}T12:00:00-03:00', ${valor}, ${vendaId}, N'Cliente ${vendaId}', ` +
    `${vendaId % 3}, N'Vendedor ${vendaId % 3}', ${situacao}, ${itens}, @o)`;
  sql(`
    USE [${banco}];
    DECLARE @t INT = (SELECT ID FROM TENANT WHERE ID_PUBLICO = '${tenantIdPublico}');
    INSERT INTO COLETOR (NOME, CLIENT_ID, SECRET_HASH) VALUES (N'Coletor painel ${id}', NEWID(), HASHBYTES('SHA2_256', '${id}'));
    DECLARE @c INT = SCOPE_IDENTITY();
    INSERT INTO ORIGEM_SINCRONIZACAO (TENANT_ID, COLETOR_ID, CONFIGURACAO_ID, ROTINA, BANCO_ORIGEM, ULTIMA_EXECUCAO_INICIO,
      ULTIMA_EXECUCAO_FIM, ULTIMA_SITUACAO, REGISTROS_ULTIMA, ULTIMO_SUCESSO_EM)
      VALUES (@t, @c, NEWID(), 'Vendas', N'ERP_E2E', SYSUTCDATETIME(), SYSUTCDATETIME(), 'Sucesso', 7, SYSUTCDATETIME());
    DECLARE @o INT = SCOPE_IDENTITY();
    INSERT INTO EMPRESA (TENANT_ID, COD_EMPRESA, NOME, ORIGEM_ID) VALUES (@t, 1, N'Matriz E2E', @o);
    DECLARE @e1 INT = SCOPE_IDENTITY();
    INSERT INTO EMPRESA (TENANT_ID, COD_EMPRESA, NOME, ORIGEM_ID) VALUES (@t, 2, N'Filial E2E', @o);
    DECLARE @e2 INT = SCOPE_IDENTITY();
    INSERT INTO VENDA (TENANT_ID, EMPRESA_ID, COD_EMPRESA, VENDA_ID, TIPO_DFE, DATA_HORA, VALOR, COD_CLIENTE, NOME_CLIENTE,
      VENDEDOR_CODIGO, VENDEDOR_NOME, SITUACAO, QTD_ITENS, ORIGEM_ID) VALUES
      ${venda('@e1', 1, 1, 'NF-e', inicioMes, '1000.00', "N'Finalizada'", 3)},
      ${venda('@e1', 1, 2, 'NFC-e', inicioMes, '500.00', "N'Finalizada'", 2)},
      ${venda('@e1', 1, 3, 'NFC-e', inicioMes, '250.00', contingencia, 1)},
      ${venda('@e1', 1, 4, 'NFC-e', inicioMes, '300.00', "N'Cancelada'", 1)},
      ${venda('@e1', 1, 5, 'NFC-e', inicioMes, '999.00', "N'Inutilizada'", 9)},
      ${venda('@e2', 2, 6, 'NFC-e', inicioMes, '750.00', "N'Finalizada'", 4)},
      ${venda('@e2', 2, 7, 'NF-e', inicioMesAnterior, '400.00', "N'Finalizada'", 2)};
  `);
}

async function entrar(page: Page, email: string, senha: string): Promise<void> {
  await page.goto('/conta/login');
  await page.locator('#login-email').fill(email);
  await page.locator('#login-senha').fill(senha);
  await page.getByRole('button', { name: 'Entrar' }).click();
  await expect(page).toHaveURL(/\/cliente(\?.*)?$/);
}

const kpi = (page: Page, nome: string) => page.locator(`[data-kpi="${nome}"] .valor`);
const menu = (page: Page) => page.getByRole('navigation', { name: 'Menu principal' });

/** Espera os 4 blocos com dados (nenhum carregando) e mede a rolagem horizontal da página. */
async function semRolagemHorizontal(page: Page): Promise<void> {
  await expect(kpi(page, 'faturamento')).toHaveText(/R\$\s2\.500,00/);
  for (const bloco of ['kpis', 'evolucao', 'rankings', 'dfe']) {
    await expect(page.locator(`[data-bloco="${bloco}"] .bloco-conteudo`)).toBeVisible();
  }
  const { scrollWidth, clientWidth } = await page.evaluate(() => ({
    scrollWidth: document.documentElement.scrollWidth,
    clientWidth: document.documentElement.clientWidth,
  }));
  expect(scrollWidth).toBeLessThanOrEqual(clientWidth);
}

test.describe.serial('painel de vendas', () => {
  test.beforeAll(async () => {
    const env: NodeJS.ProcessEnv = {
      ...process.env,
      ASPNETCORE_ENVIRONMENT: 'Development',
      ConnectionStrings__Plataforma: connectionString(lerEstado().banco!),
      Jwt__Chave: randomBytes(48).toString('base64'),
    };
    const emailSuperAdmin = `painel.super.${id}@e2e.jamsoft.test`;
    const temporaria = senhaTemporariaDaSaida(
      cliApi(['criar-superadmin', '--email', emailSuperAdmin, '--nome', 'SuperAdmin Painel E2E'], env),
    );
    const tokenSuper = await ativar(emailSuperAdmin, temporaria, novaSenha('PainelSuper'));

    const tenant = await api<{ idPublico: string }>(
      'POST',
      '/api/admin/tenants',
      {
        nome: `Painel E2E ${id}`,
        cnpj: cnpjDeTeste(),
        adminInicial: { nome: 'Admin Painel E2E', email: admin.email, senhaInicial: admin.inicial },
      },
      tokenSuper,
    );
    await api(
      'POST',
      '/api/usuarios',
      { nome: 'Consulta Painel E2E', email: consulta.email, senhaInicial: consulta.inicial, perfil: 'Consulta' },
      tokenSuper,
      tenant.idPublico,
    );
    await ativar(admin.email, admin.inicial, admin.senha);
    await ativar(consulta.email, consulta.inicial, consulta.senha);
    semearVendas(tenant.idPublico);
  });

  test('Admin abre o painel e vê os KPIs do mês corrente', async ({ page }) => {
    await entrar(page, admin.email, admin.senha);

    await expect(kpi(page, 'faturamento')).toHaveText(/^R\$\s2\.500,00$/);
    await expect(kpi(page, 'vendas')).toHaveText('4');
    await expect(kpi(page, 'ticketMedio')).toHaveText(/^R\$\s625,00$/);
    await expect(kpi(page, 'itensPorVenda')).toHaveText('2,50');
    await expect(kpi(page, 'percentualCanceladas')).toHaveText('20,00%');
  });

  test('Admin muda o período e os valores mudam', async ({ page }) => {
    await entrar(page, admin.email, admin.senha);
    await expect(kpi(page, 'faturamento')).toHaveText(/^R\$\s2\.500,00$/);

    await page.getByRole('group', { name: 'Atalhos de período' }).getByRole('button', { name: 'Mês anterior' }).click();

    await expect(page).toHaveURL(new RegExp(`inicio=${datas().inicioMesAnterior}`));
    await expect(kpi(page, 'faturamento')).toHaveText(/^R\$\s400,00$/);
    await expect(kpi(page, 'vendas')).toHaveText('1');
  });

  test('Consulta vê o painel e não acessa usuários', async ({ page }) => {
    await entrar(page, consulta.email, consulta.senha);
    await expect(kpi(page, 'faturamento')).toHaveText(/^R\$\s2\.500,00$/);
    await expect(menu(page).getByRole('link', { name: 'Usuários' })).toHaveCount(0);

    await page.goto('/cliente/usuarios');

    await expect(page).toHaveURL(/\/cliente$/);
    await expect(page.getByRole('status').filter({ hasText: 'Acesso não permitido' })).toBeVisible();
    await expect(page.getByRole('heading', { name: 'Usuários' })).toHaveCount(0);
  });

  test('360 × 740: painel sem rolagem horizontal', async ({ page }) => {
    await page.setViewportSize({ width: 360, height: 740 });
    await entrar(page, admin.email, admin.senha);

    await semRolagemHorizontal(page);
  });

  test('1920 × 1080: painel sem rolagem horizontal', async ({ page }) => {
    await page.setViewportSize({ width: 1920, height: 1080 });
    await entrar(page, admin.email, admin.senha);

    await semRolagemHorizontal(page);
  });

  test('smoke: Admin entra e o painel carrega os 4 blocos @smoke', async ({ page }) => {
    await entrar(page, admin.email, admin.senha);

    await expect(kpi(page, 'faturamento')).toHaveText(/^R\$\s2\.500,00$/);
    for (const bloco of ['kpis', 'evolucao', 'rankings', 'dfe']) {
      await expect(page.locator(`[data-bloco="${bloco}"] .bloco-conteudo`)).toBeVisible();
    }
  });
});
