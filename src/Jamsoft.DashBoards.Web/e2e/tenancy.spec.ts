import { Page, expect, test } from '@playwright/test';
import { randomInt } from 'node:crypto';

/**
 * Fluxos críticos de tenancy e acesso (TEN-03, USR-01, USR-08, CTX-01) contra API + Web reais. Os cenários são
 * sequenciais: cada um usa o que o anterior criou no banco descartável do e2e.
 */

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

/** Senha que atende à política (10+ caracteres, letra e dígito), diferente a cada execução. */
const novaSenha = (prefixo: string) => `${prefixo}${randomInt(100_000, 999_999)}aB`;

const sufixo = Date.now().toString(36);
const superAdmin = {
  email: process.env['E2E_SUPERADMIN_EMAIL']!,
  senhaTemporaria: process.env['E2E_SUPERADMIN_SENHA']!,
  senha: novaSenha('SuperE2e'),
};
const tenant = { nome: `Loja E2E ${sufixo}`, cnpj: cnpjDeTeste() };
const admin = { nome: 'Ana Admin E2E', email: `admin.${sufixo}@e2e.jamsoft.test`, senhaInicial: novaSenha('Inicial'), senha: novaSenha('AdminE2e') };
const consulta = {
  nome: 'Caio Consulta E2E',
  email: `consulta.${sufixo}@e2e.jamsoft.test`,
  senhaInicial: novaSenha('Inicial'),
  senha: novaSenha('ConsE2e'),
};

async function entrar(page: Page, email: string, senha: string): Promise<void> {
  await page.goto('/conta/login');
  await page.locator('#login-email').fill(email);
  await page.locator('#login-senha').fill(senha);
  await page.getByRole('button', { name: 'Entrar' }).click();
}

/** Primeiro acesso (USR-08): a troca de senha é a única tela liberada até a senha nova ser gravada. */
async function trocarSenha(page: Page, atual: string, nova: string): Promise<void> {
  await expect(page).toHaveURL(/\/conta\/trocar-senha$/);
  await page.locator('#senha-atual').fill(atual);
  await page.locator('#senha-nova').fill(nova);
  await page.locator('#senha-confirmacao').fill(nova);
  await page.getByRole('button', { name: 'Trocar senha' }).click();
}

const menu = (page: Page) => page.getByRole('navigation', { name: 'Menu principal' });

test.describe.serial('tenancy e acesso', () => {
  test('SuperAdmin troca a senha temporária e cria um tenant com Admin inicial', async ({ page }) => {
    await entrar(page, superAdmin.email, superAdmin.senhaTemporaria);
    await trocarSenha(page, superAdmin.senhaTemporaria, superAdmin.senha);
    await expect(page).toHaveURL(/\/admin\/tenants$/);

    await page.getByRole('link', { name: 'Novo tenant' }).click();
    await page.locator('#tenant-nome').fill(tenant.nome);
    await page.locator('#tenant-cnpj').fill(tenant.cnpj);
    await page.getByLabel('Cadastrar o primeiro Admin do tenant').check();
    await page.locator('#admin-nome').fill(admin.nome);
    await page.locator('#admin-email').fill(admin.email);
    await page.locator('#admin-senha').fill(admin.senhaInicial);
    await page.getByRole('button', { name: 'Salvar' }).click();

    await expect(page).toHaveURL(/\/admin\/tenants$/);
    await expect(page.getByRole('row', { name: new RegExp(tenant.nome) })).toContainText('Ativo');
  });

  test('Admin entra, troca a senha e cria um usuário Consulta', async ({ page }) => {
    await entrar(page, admin.email, admin.senhaInicial);
    await trocarSenha(page, admin.senhaInicial, admin.senha);
    await expect(page).toHaveURL(/\/cliente$/);

    await menu(page).getByRole('link', { name: 'Usuários' }).click();
    await expect(page.getByRole('row', { name: new RegExp(admin.email) })).toContainText('Admin');
    await page.getByRole('link', { name: 'Novo usuário' }).click();
    await page.locator('#usuario-nome').fill(consulta.nome);
    await page.locator('#usuario-email').fill(consulta.email);
    await page.locator('#usuario-perfil').selectOption('Consulta');
    await page.locator('#usuario-senha').fill(consulta.senhaInicial);
    await page.getByRole('button', { name: 'Salvar' }).click();

    await expect(page).toHaveURL(/\/cliente\/usuarios$/);
    const linha = page.getByRole('row', { name: new RegExp(consulta.email) });
    await expect(linha).toContainText(consulta.nome);
    await expect(linha).toContainText('Consulta');
    await expect(linha).toContainText('Ativo');
  });

  test('Consulta não acessa /cliente/usuarios', async ({ page }) => {
    await entrar(page, consulta.email, consulta.senhaInicial);
    await trocarSenha(page, consulta.senhaInicial, consulta.senha);
    await expect(page).toHaveURL(/\/cliente$/);
    await expect(menu(page).getByRole('link', { name: 'Usuários' })).toHaveCount(0);

    // Recarrega a app direto na rota: a sessão volta pelo cookie de refresh e o guard barra o perfil.
    await page.goto('/cliente/usuarios');
    await expect(page).toHaveURL(/\/cliente$/);
    await expect(page.getByRole('status').filter({ hasText: 'Acesso não permitido' })).toBeVisible();
    await expect(page.getByRole('heading', { name: 'Usuários' })).toHaveCount(0);
  });

  test('SuperAdmin seleciona o tenant e vê os usuários dele', async ({ page }) => {
    await entrar(page, superAdmin.email, superAdmin.senha);
    await expect(page).toHaveURL(/\/admin\/tenants$/);

    // Sem tenant selecionado o guard devolve para /admin com o aviso e o seletor abre sozinho (CTX-01).
    await page.goto('/cliente/usuarios');
    await expect(page).toHaveURL(/\/admin$/);
    await expect(page.getByRole('status').filter({ hasText: 'Selecione um tenant' })).toBeVisible();
    const seletor = page.getByRole('dialog', { name: 'Selecionar tenant' });
    await seletor.getByLabel('Buscar tenant').fill(tenant.nome);
    await seletor.getByRole('button', { name: tenant.nome }).click();
    await expect(page.locator('[data-teste="tenant-atual"]')).toHaveText(tenant.nome);

    await menu(page).getByRole('link', { name: 'Usuários' }).click();
    await expect(page).toHaveURL(/\/cliente\/usuarios$/);
    await expect(page.getByRole('row', { name: new RegExp(admin.email) })).toContainText('Admin');
    await expect(page.getByRole('row', { name: new RegExp(consulta.email) })).toContainText('Consulta');
  });
});
