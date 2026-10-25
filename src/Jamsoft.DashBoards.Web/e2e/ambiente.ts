import { ChildProcess, execFileSync, spawn } from 'node:child_process';
import { existsSync, readFileSync, rmSync, writeFileSync } from 'node:fs';
import path from 'node:path';

/**
 * Infra do e2e: banco descartável `JD_E2E_<guid>` no SQL Server local (Windows auth), API e `ng serve` reais.
 * O estado (banco, PIDs) fica num arquivo para o teardown limpar mesmo se o setup cair no meio.
 */

export const RAIZ_REPO = path.resolve(__dirname, '../../..');
export const PROJETO_API = path.join(RAIZ_REPO, 'src', 'Jamsoft.DashBoards.Api');
export const PROJETO_WEB = path.resolve(__dirname, '..');
export const DLL_API = path.join(PROJETO_API, 'bin', 'Debug', 'net10.0', 'Jamsoft.DashBoards.Api.dll');
const ARQUIVO_ESTADO = path.join(__dirname, '.estado-e2e.json');

/** Servidor SQL do e2e; troque com `E2E_SQLSERVER` (ex.: `localhost\\SQLEXPRESS`). */
export const SERVIDOR_SQL = process.env['E2E_SQLSERVER'] ?? 'localhost';
export const URL_API = 'http://localhost:5170';
export const URL_WEB = 'http://localhost:4200';

export interface EstadoE2e {
  banco?: string;
  pids: number[];
}

export function lerEstado(): EstadoE2e {
  if (!existsSync(ARQUIVO_ESTADO)) return { pids: [] };
  return JSON.parse(readFileSync(ARQUIVO_ESTADO, 'utf8')) as EstadoE2e;
}

export function gravarEstado(estado: EstadoE2e): void {
  writeFileSync(ARQUIVO_ESTADO, JSON.stringify(estado, null, 2));
}

export function apagarEstado(): void {
  rmSync(ARQUIVO_ESTADO, { force: true });
}

export function connectionString(banco: string): string {
  return `Server=${SERVIDOR_SQL};Database=${banco};Integrated Security=true;TrustServerCertificate=true`;
}

/** Executa T-SQL no `master` com o sqlcmd (Windows auth, `-C` confia no certificado do servidor). */
export function sql(comando: string): string {
  return execFileSync('sqlcmd', ['-S', SERVIDOR_SQL, '-E', '-C', '-b', '-h', '-1', '-W', '-Q', `SET NOCOUNT ON; ${comando}`], {
    encoding: 'utf8',
  });
}

export function criarBanco(banco: string): void {
  // Mesmo nível de compatibilidade do fixture dos testes da API (SQL Server 2019).
  sql(`CREATE DATABASE [${banco}]; ALTER DATABASE [${banco}] SET COMPATIBILITY_LEVEL = 150;`);
}

export function apagarBanco(banco: string): void {
  sql(
    `IF DB_ID('${banco}') IS NOT NULL BEGIN ` +
      `ALTER DATABASE [${banco}] SET SINGLE_USER WITH ROLLBACK IMMEDIATE; DROP DATABASE [${banco}]; END`,
  );
}

/** Roda um comando da CLI da API (`migrar`, `criar-superadmin`) e devolve a saída. */
export function cliApi(args: string[], env: NodeJS.ProcessEnv): string {
  return execFileSync('dotnet', [DLL_API, ...args], { cwd: PROJETO_API, env, encoding: 'utf8' });
}

/** Senha temporária impressa pelo `criar-superadmin` (linha seguinte a "Senha temporária"). */
export function senhaTemporariaDaSaida(saida: string): string {
  const linhas = saida.split(/\r?\n/);
  // Só o prefixo ASCII: a saída redirecionada usa a página de código do console, que estraga os acentos.
  const indice = linhas.findIndex((l) => l.includes('Senha tempor'));
  const senha = linhas[indice + 1]?.trim();
  if (indice < 0 || !senha) throw new Error('Senha temporária não encontrada na saída do criar-superadmin.');
  return senha;
}

/** Sobe um processo em segundo plano; a saída vai para o console só com `E2E_LOG=1`. */
export function iniciar(comando: string, args: string[], opcoes: { cwd: string; env: NodeJS.ProcessEnv; shell?: boolean }): ChildProcess {
  const processo = spawn(comando, args, { ...opcoes, stdio: process.env['E2E_LOG'] ? 'inherit' : 'ignore', windowsHide: true });
  if (!processo.pid) throw new Error(`Não foi possível iniciar ${comando}.`);
  return processo;
}

/** Derruba o processo e os filhos (no Windows, `ng serve` via npx abre uma árvore de processos). */
export function encerrar(pid: number): void {
  try {
    if (process.platform === 'win32') execFileSync('taskkill', ['/PID', String(pid), '/T', '/F'], { stdio: 'ignore' });
    else process.kill(pid, 'SIGTERM');
  } catch {
    // Já tinha saído.
  }
}

/** true quando nada responde na URL (porta livre para subir o servidor do e2e). */
export async function portaLivre(url: string): Promise<boolean> {
  try {
    await fetch(url, { signal: AbortSignal.timeout(1000) });
    return false;
  } catch {
    return true;
  }
}

/** Espera a URL responder 2xx (ou o processo morrer). */
export async function aguardar(url: string, processo: ChildProcess, limiteMs: number): Promise<void> {
  const fim = Date.now() + limiteMs;
  while (Date.now() < fim) {
    if (processo.exitCode !== null) throw new Error(`O processo de ${url} saiu com código ${processo.exitCode}.`);
    try {
      const resposta = await fetch(url, { signal: AbortSignal.timeout(2000) });
      if (resposta.ok) return;
    } catch {
      // Ainda subindo.
    }
    await new Promise((r) => setTimeout(r, 500));
  }
  throw new Error(`${url} não respondeu em ${limiteMs / 1000} s.`);
}
