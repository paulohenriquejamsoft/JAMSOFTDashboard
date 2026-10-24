import { ChangeDetectionStrategy, Component, input, output } from '@angular/core';

/** Diálogo de confirmação simples (sem biblioteca): título, mensagem, Cancelar e a ação. */
@Component({
  selector: 'app-confirmacao',
  changeDetection: ChangeDetectionStrategy.OnPush,
  host: { '(document:keydown.escape)': 'cancelar.emit()' },
  template: `
    <div class="dialogo-fundo">
      <div class="dialogo" role="alertdialog" aria-modal="true" aria-labelledby="confirmacao-titulo">
        <div class="dialogo-cabecalho">
          <h2 id="confirmacao-titulo">{{ titulo() }}</h2>
        </div>
        <div class="dialogo-corpo">
          <p>{{ mensagem() }}</p>
        </div>
        <div class="dialogo-rodape">
          <button type="button" class="btn btn-contorno btn-sm" (click)="cancelar.emit()">Cancelar</button>
          <button type="button" class="btn btn-primario btn-sm" [disabled]="ocupado()" (click)="confirmar.emit()">
            {{ textoConfirmar() }}
          </button>
        </div>
      </div>
    </div>
  `,
})
export class ConfirmacaoDialog {
  readonly titulo = input.required<string>();
  readonly mensagem = input.required<string>();
  readonly textoConfirmar = input('Confirmar');
  readonly ocupado = input(false);
  readonly confirmar = output<void>();
  readonly cancelar = output<void>();
}
