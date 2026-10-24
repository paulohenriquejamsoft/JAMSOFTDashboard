import { apagarBanco, apagarEstado, encerrar, lerEstado } from './ambiente';

/** Derruba a API e o `ng serve` e apaga o banco do e2e. Idempotente: o setup também chama em caso de falha. */
export default async function globalTeardown(): Promise<void> {
  const estado = lerEstado();
  for (const pid of estado.pids) encerrar(pid);
  if (estado.banco) apagarBanco(estado.banco);
  apagarEstado();
}
