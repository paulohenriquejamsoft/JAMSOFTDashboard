import { CurrencyPipe, DecimalPipe } from '@angular/common';
import { ChangeDetectionStrategy, Component, computed, input } from '@angular/core';
import { ItemRanking, RankingsResposta } from './painel.service';

interface Tabela {
  id: string;
  titulo: string;
  itens: ItemRanking[];
}

/**
 * Top 10 de vendedores, clientes e empresas (RNK-02): nome, faturamento, nº de vendas e participação com barra
 * inline. A tabela de Empresas some quando o tenant tem uma só empresa (RNK-05). Em telas estreitas a tabela rola
 * dentro do card, nunca a página.
 */
@Component({
  selector: 'app-rankings',
  imports: [CurrencyPipe, DecimalPipe],
  changeDetection: ChangeDetectionStrategy.OnPush,
  styles: `
    :host {
      display: flex;
      flex-direction: column;
      gap: var(--espaco-4);
      padding: var(--espaco-2) 0 var(--espaco-4);
    }
    caption {
      text-align: left;
      padding: 0 var(--espaco-4) var(--espaco-2);
      font-size: var(--rotulo-size);
      font-weight: var(--rotulo-weight);
      letter-spacing: var(--rotulo-spacing);
      text-transform: uppercase;
      color: var(--text-muted);
    }
    .numero {
      text-align: right;
      white-space: nowrap;
    }
    .participacao {
      display: flex;
      align-items: center;
      justify-content: flex-end;
      gap: var(--espaco-2);
    }
    .trilho {
      width: 64px;
      height: 6px;
      border-radius: 3px;
      background: var(--borda-suave);
      overflow: hidden;
    }
    .barra {
      display: block;
      height: 100%;
      border-radius: 3px;
      background: var(--primary);
    }
    .vazio {
      margin: 0;
      padding: var(--espaco-3) var(--espaco-4);
      color: var(--text-muted);
    }
  `,
  template: `
    @for (tabela of tabelas(); track tabela.id) {
      <div class="tabela-rolagem" [attr.data-ranking]="tabela.id">
        <table class="tabela">
          <caption>{{ tabela.titulo }}</caption>
          <thead>
            <tr>
              <th scope="col">Nome</th>
              <th scope="col" class="numero">Faturamento</th>
              <th scope="col" class="numero">Vendas</th>
              <th scope="col" class="numero">Participação</th>
            </tr>
          </thead>
          <tbody>
            @for (item of tabela.itens; track $index) {
              <tr>
                <td>{{ item.nome }}</td>
                <td class="numero mono">{{ item.faturamento | currency }}</td>
                <td class="numero mono">{{ item.vendas | number: '1.0-0' }}</td>
                <td class="numero mono">
                  <span class="participacao">
                    <span class="trilho" aria-hidden="true"><span class="barra" [style.width.%]="item.participacao"></span></span>
                    {{ item.participacao | number: '1.2-2' }}%
                  </span>
                </td>
              </tr>
            }
          </tbody>
        </table>
        @if (tabela.itens.length === 0) {
          <p class="vazio">Nenhuma venda no período.</p>
        }
      </div>
    }
  `,
})
export class RankingsComponent {
  readonly dados = input.required<RankingsResposta>();
  /** Falso quando o tenant tem uma única empresa (RNK-05). */
  readonly mostrarEmpresas = input(true);

  readonly tabelas = computed<Tabela[]>(() => {
    const dados = this.dados();
    const tabelas: Tabela[] = [
      { id: 'vendedores', titulo: 'Vendedores', itens: dados.vendedores },
      { id: 'clientes', titulo: 'Clientes', itens: dados.clientes },
    ];
    if (this.mostrarEmpresas()) tabelas.push({ id: 'empresas', titulo: 'Empresas', itens: dados.empresas });
    return tabelas;
  });
}
