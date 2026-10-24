import { ChangeDetectionStrategy, Component } from '@angular/core';

/** Home da área do cliente (`/cliente`); os painéis de vendas chegam em outra feature. */
@Component({
  selector: 'app-cliente-inicio',
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <section class="pagina">
      <div class="pagina-cabecalho">
        <div>
          <span class="secao-rotulo">Principal</span>
          <h1>Painel</h1>
        </div>
      </div>
    </section>
  `,
})
export class ClienteInicioPage {}
