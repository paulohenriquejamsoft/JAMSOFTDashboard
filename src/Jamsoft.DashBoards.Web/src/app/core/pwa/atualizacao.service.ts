import { DOCUMENT } from '@angular/common';
import { Injectable, InjectionToken, inject, signal } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { SwUpdate } from '@angular/service-worker';
import { filter } from 'rxjs';

/** Recarrega a página; trocado nos testes. */
export const RECARREGAR_PAGINA = new InjectionToken<() => void>('RECARREGAR_PAGINA', {
  providedIn: 'root',
  factory: () => {
    const documento = inject(DOCUMENT);
    return () => documento.location.reload();
  },
});

/**
 * Nova versão publicada (PWA-04): quando o service worker baixa a versão nova (`VERSION_READY`), a app mostra
 * "Nova versão disponível"; "Recarregar" ativa a versão e recarrega. Sem service worker (dev, navegador sem
 * suporte), nada acontece.
 */
@Injectable({ providedIn: 'root' })
export class AtualizacaoService {
  private readonly sw = inject(SwUpdate, { optional: true });
  private readonly recarregarPagina = inject(RECARREGAR_PAGINA);
  private readonly pronta = signal(false);

  readonly disponivel = this.pronta.asReadonly();

  constructor() {
    if (!this.sw?.isEnabled) return;
    this.sw.versionUpdates
      .pipe(
        filter((evento) => evento.type === 'VERSION_READY'),
        takeUntilDestroyed(),
      )
      .subscribe(() => this.pronta.set(true));
  }

  async recarregar(): Promise<void> {
    try {
      if (this.sw?.isEnabled) await this.sw.activateUpdate();
    } finally {
      this.recarregarPagina();
    }
  }
}
