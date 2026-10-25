import { DOCUMENT } from '@angular/common';
import { DestroyRef, Injectable, inject, signal } from '@angular/core';

/**
 * Conectividade do dispositivo (PWA-03): começa em `navigator.onLine` e acompanha os eventos `online`/`offline`
 * da janela. Sem conexão, o shell mostra a faixa "Sem conexão" e o painel não consulta a API.
 */
@Injectable({ providedIn: 'root' })
export class ConexaoService {
  private readonly estado = signal(true);

  readonly online = this.estado.asReadonly();

  constructor() {
    const janela = inject(DOCUMENT).defaultView;
    if (!janela) return;
    this.estado.set(janela.navigator.onLine);
    const ficouOnline = () => this.estado.set(true);
    const ficouOffline = () => this.estado.set(false);
    janela.addEventListener('online', ficouOnline);
    janela.addEventListener('offline', ficouOffline);
    inject(DestroyRef).onDestroy(() => {
      janela.removeEventListener('online', ficouOnline);
      janela.removeEventListener('offline', ficouOffline);
    });
  }
}
