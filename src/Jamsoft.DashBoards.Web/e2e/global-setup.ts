import { execFileSync } from 'node:child_process';
import { randomBytes, randomUUID } from 'node:crypto';
import path from 'node:path';
import {
  PROJETO_API,
  PROJETO_WEB,
  URL_API,
  URL_WEB,
  DLL_API,
  aguardar,
  cliApi,
  connectionString,
  criarBanco,
  gravarEstado,
  iniciar,
  lerEstado,
  portaLivre,
} from './ambiente';
import limpar from './global-teardown';

/** E-mail do SuperAdmin semeado pelo `criar-superadmin`; a senha temporária vai para `E2E_SUPERADMIN_SENHA`. */
export const EMAIL_SUPERADMIN = 'superadmin@e2e.jamsoft.test';

/**
 * Prepara o e2e: banco `JD_E2E_<guid>`, migrações, SuperAdmin (senha temporária capturada da saída da CLI),
 * API em http://localhost:5170 com chave JWT gerada aqui e `ng serve` (proxy `/api` → API).
 */
export default async function globalSetup(): Promise<void> {
  for (const url of [URL_API, URL_WEB]) {
    if (!(await portaLivre(url))) throw new Error(`Já existe algo respondendo em ${url}; pare-o antes do e2e.`);
  }

  try {
    execFileSync('dotnet', ['build', PROJETO_API, '-c', 'Debug', '-v', 'q', '-nologo'], { stdio: 'inherit' });

    const banco = `JD_E2E_${randomUUID().replace(/-/g, '')}`;
    gravarEstado({ banco, pids: [] });
    criarBanco(banco);

    const env: NodeJS.ProcessEnv = {
      ...process.env,
      ASPNETCORE_ENVIRONMENT: 'Development',
      ConnectionStrings__Plataforma: connectionString(banco),
      // Chave só deste e2e (48 bytes aleatórios); nunca uma chave real.
      Jwt__Chave: randomBytes(48).toString('base64'),
    };

    cliApi(['migrar'], env);
    const saida = cliApi(['criar-superadmin', '--email', EMAIL_SUPERADMIN, '--nome', 'SuperAdmin E2E'], env);
    const linhas = saida.split(/\r?\n/);
    // Só o prefixo ASCII: a saída redirecionada usa a página de código do console, que estraga os acentos.
    const indice = linhas.findIndex((l) => l.includes('Senha tempor'));
    const senha = linhas[indice + 1]?.trim();
    if (indice < 0 || !senha) throw new Error('Senha temporária não encontrada na saída do criar-superadmin.');
    process.env['E2E_SUPERADMIN_EMAIL'] = EMAIL_SUPERADMIN;
    process.env['E2E_SUPERADMIN_SENHA'] = senha;

    const api = iniciar('dotnet', [DLL_API, '--urls', URL_API], { cwd: PROJETO_API, env });
    gravarEstado({ ...lerEstado(), pids: [...lerEstado().pids, api.pid!] });
    await aguardar(`${URL_API}/health`, api, 60_000);

    const ng = path.join(PROJETO_WEB, 'node_modules', '@angular', 'cli', 'bin', 'ng.js');
    const web = iniciar(process.execPath, [ng, 'serve', '--port', '4200'], { cwd: PROJETO_WEB, env: process.env });
    gravarEstado({ ...lerEstado(), pids: [...lerEstado().pids, web.pid!] });
    await aguardar(URL_WEB, web, 180_000);
  } catch (erro) {
    await limpar();
    throw erro;
  }
}
