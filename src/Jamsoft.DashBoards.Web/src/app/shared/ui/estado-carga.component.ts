import { ChangeDetectionStrategy, Component, input, output } from '@angular/core';
import { EstadoCarga } from '../paginado';

/**
 * Estados de carregamento, vazio e erro de uma listagem. A ação do estado vazio vem por projeção
 * (`<ng-content select="[acaoVazio]">`), para a tela decidir se é link ou botão.
 */
@Component({
  selector: 'app-estado-carga',
  changeDetection: ChangeDetectionStrategy.OnPush,
  styles: `
    .estado {
      padding: 32px 16px;
      display: flex;
      flex-direction: column;
      align-items: center;
      gap: 12px;
      text-align: center;
      color: var(--text-muted);
    }
    p {
      margin: 0;
    }
  `,
  template: `
    @switch (estado()) {
      @case ('carregando') {
        <div class="estado" role="status"><p>Carregando...</p></div>
      }
      @case ('vazio') {
        <div class="estado">
          <p>{{ textoVazio() }}</p>
          <ng-content select="[acaoVazio]" />
        </div>
      }
      @case ('erro') {
        <div class="estado" role="alert">
          <p>{{ mensagemErro() }}</p>
          <button type="button" class="btn btn-suave btn-sm" (click)="tentarNovamente.emit()">
            Tentar novamente
          </button>
        </div>
      }
    }
  `,
})
export class EstadoCargaComponent {
  readonly estado = input.required<EstadoCarga>();
  readonly textoVazio = input('Nenhum registro encontrado.');
  readonly mensagemErro = input('Não foi possível carregar os dados.');
  readonly tentarNovamente = output<void>();
}
