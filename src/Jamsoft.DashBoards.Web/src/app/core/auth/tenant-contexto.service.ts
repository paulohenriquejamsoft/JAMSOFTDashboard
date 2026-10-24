import { Injectable, signal } from '@angular/core';
import type { TenantResumo } from './auth.service';

const CHAVE = 'jamsoft.tenant-contexto';

/**
 * Tenant selecionado pelo SuperAdmin (CTX-01). Fica em `sessionStorage` para sobreviver ao recarregamento
 * da página, só na aba atual. Storage indisponível (modo privado, bloqueio) não quebra: vale só a memória.
 * Para Admin e Consulta o tenant vem do token e esta seleção é ignorada (ver authInterceptor).
 */
@Injectable({ providedIn: 'root' })
export class TenantContextoService {
  private readonly atual = signal<TenantResumo | null>(ler());

  readonly selecionado = this.atual.asReadonly();

  selecionar(tenant: TenantResumo): void {
    this.atual.set({ idPublico: tenant.idPublico, nome: tenant.nome });
    gravar(JSON.stringify(this.atual()));
  }

  limpar(): void {
    this.atual.set(null);
    gravar(null);
  }
}

function ler(): TenantResumo | null {
  try {
    const salvo = JSON.parse(sessionStorage.getItem(CHAVE) ?? 'null') as Partial<TenantResumo> | null;
    return typeof salvo?.idPublico === 'string' && typeof salvo.nome === 'string'
      ? { idPublico: salvo.idPublico, nome: salvo.nome }
      : null;
  } catch {
    return null;
  }
}

function gravar(valor: string | null): void {
  try {
    if (valor === null) sessionStorage.removeItem(CHAVE);
    else sessionStorage.setItem(CHAVE, valor);
  } catch {
    // Sem storage: a seleção vale só enquanto a página estiver aberta.
  }
}
