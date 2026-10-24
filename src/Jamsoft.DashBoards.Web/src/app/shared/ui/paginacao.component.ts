import { ChangeDetectionStrategy, Component, computed, input, output } from '@angular/core';

const JANELA = 5;

/** Paginador do Design System ("Tabela e paginação"): «, números, » e o tamanho da página. */
@Component({
  selector: 'app-paginacao',
  changeDetection: ChangeDetectionStrategy.OnPush,
  styles: `
    nav {
      display: flex;
      align-items: center;
      gap: 4px;
      flex-wrap: wrap;
    }
    button {
      height: 30px;
      min-width: 30px;
      padding: 0 10px;
      border: 1px solid #dee2e6;
      background: var(--surface-bg);
      font: inherit;
      font-size: 12px;
      cursor: pointer;
      color: var(--text-primary);
    }
    button[aria-current='page'] {
      background: rgba(23, 25, 35, 0.08);
      font-weight: 700;
    }
    button:disabled {
      color: #b06161;
      cursor: not-allowed;
    }
    .info {
      margin-left: 10px;
      font-size: 12px;
      color: var(--text-muted);
    }
  `,
  template: `
    <nav aria-label="Paginação">
      <button type="button" aria-label="Página anterior" [disabled]="pagina() <= 1" (click)="ir(pagina() - 1)">
        «
      </button>
      @for (numero of numeros(); track numero) {
        <button
          type="button"
          [attr.aria-current]="numero === pagina() ? 'page' : null"
          [attr.aria-label]="'Página ' + numero"
          (click)="ir(numero)"
        >
          {{ numero }}
        </button>
      }
      <button type="button" aria-label="Próxima página" [disabled]="pagina() >= paginas()" (click)="ir(pagina() + 1)">
        »
      </button>
      <span class="info">{{ total() }} registro(s) · {{ tamanho() }} por página</span>
    </nav>
  `,
})
export class PaginacaoComponent {
  readonly pagina = input.required<number>();
  readonly tamanho = input.required<number>();
  readonly total = input.required<number>();
  readonly mudar = output<number>();

  readonly paginas = computed(() => Math.max(1, Math.ceil(this.total() / Math.max(1, this.tamanho()))));
  readonly numeros = computed(() => {
    const ultima = this.paginas();
    const inicio = Math.max(1, Math.min(this.pagina() - Math.floor(JANELA / 2), ultima - JANELA + 1));
    const fim = Math.min(ultima, inicio + JANELA - 1);
    return Array.from({ length: fim - inicio + 1 }, (_, i) => inicio + i);
  });

  ir(numero: number): void {
    if (numero >= 1 && numero <= this.paginas() && numero !== this.pagina()) this.mudar.emit(numero);
  }
}
