import { ChangeDetectionStrategy, Component, input, output, signal } from '@angular/core';
import { CredencialColetor } from './coletores.api';

export const AVISO_SEGREDO = 'Guarde o secret agora: ele não será exibido novamente.';

/**
 * Mostra `clientId` e `clientSecret` uma única vez, logo depois de criar ou rotacionar (COL-01, COL-06), com botão
 * de copiar. Quem abre guarda a credencial só enquanto o diálogo está aberto e a descarta em `fechado`.
 */
@Component({
  selector: 'app-segredo-coletor',
  changeDetection: ChangeDetectionStrategy.OnPush,
  host: { '(document:keydown.escape)': 'fechado.emit()' },
  template: `
    <div class="dialogo-fundo">
      <div class="dialogo" role="dialog" aria-modal="true" aria-labelledby="segredo-titulo">
        <div class="dialogo-cabecalho">
          <h2 id="segredo-titulo">Credencial do Coletor</h2>
        </div>
        <div class="dialogo-corpo">
          <p class="alerta alerta-atencao" role="status">{{ aviso }}</p>
          <div class="campo">
            <label for="segredo-client-id">Client ID</label>
            <input id="segredo-client-id" class="mono" readonly [value]="credencial().clientId" />
            <button type="button" class="btn btn-claro btn-sm" aria-label="Copiar client ID"
              (click)="copiar('id', credencial().clientId)">Copiar</button>
          </div>
          <div class="campo">
            <label for="segredo-client-secret">Client secret</label>
            <input id="segredo-client-secret" class="mono" readonly [value]="credencial().clientSecret" />
            <button type="button" class="btn btn-claro btn-sm" aria-label="Copiar client secret"
              (click)="copiar('secret', credencial().clientSecret)">Copiar</button>
          </div>
          @if (copiado()) {
            <p class="campo-dica" role="status">Copiado.</p>
          }
        </div>
        <div class="dialogo-rodape">
          <button type="button" class="btn btn-primario btn-sm" (click)="fechado.emit()">Fechar</button>
        </div>
      </div>
    </div>
  `,
})
export class SegredoColetorDialog {
  readonly aviso = AVISO_SEGREDO;
  readonly credencial = input.required<CredencialColetor>();
  readonly fechado = output<void>();
  readonly copiado = signal<'id' | 'secret' | null>(null);

  copiar(campo: 'id' | 'secret', texto: string): void {
    // Sem Clipboard API (HTTP sem TLS, navegador antigo) o texto continua selecionável no campo.
    navigator.clipboard?.writeText(texto).then(
      () => this.copiado.set(campo),
      () => this.copiado.set(null),
    );
  }
}
