import { ChangeDetectionStrategy, Component, input, output } from '@angular/core';
import { CredencialColetor } from './coletores.api';

export const AVISO_SEGREDO = 'Guarde o secret agora: ele não será exibido novamente.';

/**
 * Mostra `clientId` e `clientSecret` uma única vez, logo depois de criar ou rotacionar (COL-01, COL-06). Quem abre
 * guarda a credencial só enquanto o diálogo está aberto e a descarta em `fechado`.
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
          </div>
          <div class="campo">
            <label for="segredo-client-secret">Client secret</label>
            <input id="segredo-client-secret" class="mono" readonly [value]="credencial().clientSecret" />
          </div>
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
}
