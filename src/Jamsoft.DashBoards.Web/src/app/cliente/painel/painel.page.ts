import { ChangeDetectionStrategy, Component, DestroyRef, effect, inject, signal, untracked } from '@angular/core';
import { Observable, Subscription } from 'rxjs';
import { SeletorTenantComponent } from '../../admin/seletor-tenant/seletor-tenant.component';
import { AVISO_SELECIONE_TENANT } from '../../core/auth/perfil.guard';
import { tenantEfetivo } from '../../core/auth/tenant-efetivo';
import { traduzirProblema } from '../../core/erros/problema';
import { ConexaoService } from '../../core/pwa/conexao.service';
import { EstadoCarga } from '../../shared/paginado';
import { EstadoCargaComponent } from '../../shared/ui/estado-carga.component';
import { SincronizacaoApi } from '../sincronizacao/sincronizacao.api';
import { DfeComponent } from './dfe.component';
import { EvolucaoComponent } from './evolucao.component';
import { FiltroPainelComponent } from './filtro-painel.component';
import { KpisComponent } from './kpis.component';
import { FiltroPainel, PainelService } from './painel.service';
import { RankingsComponent } from './rankings.component';

export const MENSAGEM_SEM_VENDAS = 'Nenhuma venda no período';
export const MENSAGEM_SEM_SINCRONIZACAO = 'Nenhuma venda sincronizada ainda';
export const MENSAGEM_SEM_CONEXAO = 'Sem conexão';

/** Um bloco do painel: carrega e falha sozinho (UI-07, UI-08). */
class Bloco<T> {
  readonly estado = signal<EstadoCarga>('carregando');
  readonly dados = signal<T | null>(null);
  readonly mensagemErro = signal('');
  private carga?: Subscription;

  constructor(private readonly consulta: (filtro: FiltroPainel) => Observable<T>) {}

  carregar(filtro: FiltroPainel): void {
    this.cancelar();
    this.estado.set('carregando');
    this.carga = this.consulta(filtro).subscribe({
      next: (dados) => {
        this.dados.set(dados);
        this.estado.set('pronto');
      },
      error: (erro: unknown) => {
        this.mensagemErro.set(traduzirProblema(erro).mensagem);
        this.estado.set('erro');
      },
    });
  }

  cancelar(): void {
    this.carga?.unsubscribe();
  }
}

/**
 * `/cliente`: painel de vendas do tenant efetivo. Filtro no topo e 4 blocos independentes (KPIs, evolução,
 * rankings, tipo de documento), cada um com o próprio carregando/erro/"Tentar novamente". Sem vendas no período mostra
 * "Nenhuma venda no período"; tenant que nunca sincronizou mostra "Nenhuma venda sincronizada ainda" no lugar dos
 * blocos (DASH-06). SuperAdmin sem tenant vê o seletor e nada é consultado (UI-03).
 */
@Component({
  selector: 'app-painel',
  imports: [DfeComponent, EstadoCargaComponent, EvolucaoComponent, FiltroPainelComponent, KpisComponent, RankingsComponent, SeletorTenantComponent],
  changeDetection: ChangeDetectionStrategy.OnPush,
  styles: `
    .grade {
      display: grid;
      grid-template-columns: minmax(0, 1fr);
      gap: var(--espaco-4);
    }
    .bloco {
      min-width: 0;
    }
    .sem-conexao {
      margin: 0;
      padding: 32px 16px;
      text-align: center;
      color: var(--text-muted);
    }
    h2 {
      margin: 0;
      font-size: var(--titulo-card-size);
    }
    @media (min-width: 1024px) {
      .grade {
        grid-template-columns: minmax(0, 2fr) minmax(0, 1fr);
      }
      .largura-total {
        grid-column: 1 / -1;
      }
    }
  `,
  template: `
    <section class="pagina">
      <div class="pagina-cabecalho">
        <div>
          <span class="secao-rotulo">Principal</span>
          <h1>Painel</h1>
        </div>
      </div>

      @if (!tenant()) {
        <div class="card">
          <div class="card-corpo">
            <p class="alerta alerta-atencao" role="status">{{ avisoSemTenant }}</p>
            <app-seletor-tenant />
          </div>
        </div>
      } @else {
        <app-filtro-painel (filtrar)="aplicar($event)" (empresasCarregadas)="quantidadeEmpresas.set($event)" />

        @if (semSincronizacao()) {
          <p class="alerta alerta-atencao" role="status">{{ mensagemSemSincronizacao }}</p>
        } @else {
          @if (kpis.dados()?.semVendas && kpis.estado() === 'pronto') {
            <p class="alerta alerta-atencao" role="status">{{ mensagemSemVendas }}</p>
          }

          <div class="grade">
            <section class="card bloco largura-total" data-bloco="kpis" aria-labelledby="bloco-kpis">
              <div class="card-cabecalho"><h2 id="bloco-kpis">Indicadores</h2></div>
              @if (!online()) {
                <p class="sem-conexao" role="status">{{ mensagemSemConexao }}</p>
              } @else {
                <app-estado-carga [estado]="kpis.estado()" [mensagemErro]="kpis.mensagemErro()"
                  (tentarNovamente)="recarregar(kpis)" />
                @if (kpis.dados(); as dados) {
                  @if (kpis.estado() === 'pronto') {
                    <app-kpis class="bloco-conteudo" [kpis]="dados.kpis" />
                  }
                }
              }
            </section>

            <section class="card bloco largura-total" data-bloco="evolucao" aria-labelledby="bloco-evolucao">
              <div class="card-cabecalho"><h2 id="bloco-evolucao">Evolução do faturamento</h2></div>
              @if (!online()) {
                <p class="sem-conexao" role="status">{{ mensagemSemConexao }}</p>
              } @else {
                <app-estado-carga [estado]="evolucao.estado()" [mensagemErro]="evolucao.mensagemErro()"
                  (tentarNovamente)="recarregar(evolucao)" />
                @if (evolucao.dados(); as dados) {
                  @if (evolucao.estado() === 'pronto') {
                    <app-evolucao class="bloco-conteudo" [dados]="dados" />
                  }
                }
              }
            </section>

            <section class="card bloco" data-bloco="rankings" aria-labelledby="bloco-rankings">
              <div class="card-cabecalho"><h2 id="bloco-rankings">Rankings</h2></div>
              @if (!online()) {
                <p class="sem-conexao" role="status">{{ mensagemSemConexao }}</p>
              } @else {
                <app-estado-carga [estado]="rankings.estado()" [mensagemErro]="rankings.mensagemErro()"
                  (tentarNovamente)="recarregar(rankings)" />
                @if (rankings.dados(); as dados) {
                  @if (rankings.estado() === 'pronto') {
                    <app-rankings class="bloco-conteudo" [dados]="dados" [mostrarEmpresas]="quantidadeEmpresas() !== 1" />
                  }
                }
              }
            </section>

            <section class="card bloco" data-bloco="dfe" aria-labelledby="bloco-dfe">
              <div class="card-cabecalho"><h2 id="bloco-dfe">Tipo de documento</h2></div>
              @if (!online()) {
                <p class="sem-conexao" role="status">{{ mensagemSemConexao }}</p>
              } @else {
                <app-estado-carga [estado]="dfe.estado()" [mensagemErro]="dfe.mensagemErro()"
                  (tentarNovamente)="recarregar(dfe)" />
                @if (dfe.dados(); as dados) {
                  @if (dfe.estado() === 'pronto') {
                    <app-dfe class="bloco-conteudo" [dados]="dados" />
                  }
                }
              }
            </section>
          </div>
        }
      }
    </section>
  `,
})
export class PainelPage {
  private readonly painel = inject(PainelService);
  private readonly sincronizacao = inject(SincronizacaoApi);
  private readonly conexao = inject(ConexaoService);

  readonly avisoSemTenant = AVISO_SELECIONE_TENANT;
  readonly mensagemSemVendas = MENSAGEM_SEM_VENDAS;
  readonly mensagemSemSincronizacao = MENSAGEM_SEM_SINCRONIZACAO;
  readonly mensagemSemConexao = MENSAGEM_SEM_CONEXAO;
  readonly online = this.conexao.online;
  readonly tenant = tenantEfetivo();
  readonly semSincronizacao = signal(false);
  /** `null` até o filtro carregar as empresas; com 1, o ranking de empresas some (RNK-05). */
  readonly quantidadeEmpresas = signal<number | null>(null);

  readonly kpis = new Bloco((f) => this.painel.kpis(f));
  readonly evolucao = new Bloco((f) => this.painel.evolucao(f));
  readonly rankings = new Bloco((f) => this.painel.rankings(f));
  readonly dfe = new Bloco((f) => this.painel.dfe(f));
  private readonly blocos = [this.kpis, this.evolucao, this.rankings, this.dfe];

  private filtro: FiltroPainel | null = null;
  private resumo?: Subscription;

  constructor() {
    // SuperAdmin trocou de tenant com o painel aberto: refaz tudo com o filtro atual.
    effect(() => {
      if (this.tenant()) untracked(() => this.filtro && this.aplicar(this.filtro));
    });
    // Sem conexão os blocos param (PWA-03); quando ela volta, o painel refaz tudo com o filtro atual.
    let estavaOnline = this.online();
    effect(() => {
      const online = this.online();
      untracked(() => {
        if (online === estavaOnline) return;
        estavaOnline = online;
        if (!online) this.blocos.forEach((bloco) => bloco.cancelar());
        else if (this.filtro) this.aplicar(this.filtro);
      });
    });
    inject(DestroyRef).onDestroy(() => {
      this.resumo?.unsubscribe();
      this.blocos.forEach((bloco) => bloco.cancelar());
    });
  }

  /** Filtro válido do `app-filtro-painel` (inclusive o inicial): consulta os 4 blocos. */
  aplicar(filtro: FiltroPainel): void {
    this.filtro = filtro;
    if (!this.online()) return;
    this.verificarSincronizacao();
    this.blocos.forEach((bloco) => bloco.carregar(filtro));
  }

  /** "Tentar novamente" de um bloco: refaz só ele (UI-08). */
  recarregar(bloco: Pick<Bloco<unknown>, 'carregar'>): void {
    if (this.filtro && this.online()) bloco.carregar(this.filtro);
  }

  /** Sem nenhuma sincronização bem-sucedida, os KPIs zerados dariam a impressão errada (DASH-06). */
  private verificarSincronizacao(): void {
    this.resumo?.unsubscribe();
    this.resumo = this.sincronizacao.resumo().subscribe({
      next: (resumo) => this.semSincronizacao.set(resumo.ultimoSucessoEm === null),
      // Sem o resumo, o painel segue com os blocos.
      error: () => this.semSincronizacao.set(false),
    });
  }
}
