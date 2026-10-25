import { DatePipe } from '@angular/common';
import { ChangeDetectionStrategy, Component, effect, inject, signal, untracked } from '@angular/core';
import { Subscription } from 'rxjs';
import { SeletorTenantComponent } from '../../admin/seletor-tenant/seletor-tenant.component';
import { AVISO_SELECIONE_TENANT } from '../../core/auth/perfil.guard';
import { tenantEfetivo } from '../../core/auth/tenant-efetivo';
import { traduzirProblema } from '../../core/erros/problema';
import { EstadoCarga } from '../../shared/paginado';
import { EstadoCargaComponent } from '../../shared/ui/estado-carga.component';
import { OrigemSincronizacao, SincronizacaoApi, SituacaoExecucao } from './sincronizacao.api';

const BADGE: Record<SituacaoExecucao, string> = {
  Sucesso: 'badge-sucesso',
  Falha: 'badge-perigo',
  Cancelada: 'badge-atencao',
};

/**
 * `/cliente/sincronizacao` (STS-01): origens do tenant efetivo com rotina, banco, último sucesso, situação e
 * mensagem da última execução. Consulta não chega aqui (guard); o SuperAdmin sem tenant vê o seletor.
 */
@Component({
  selector: 'app-sincronizacao',
  imports: [DatePipe, EstadoCargaComponent, SeletorTenantComponent],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <section class="pagina">
      <div class="pagina-cabecalho">
        <div>
          <span class="secao-rotulo">Dados</span>
          <h1>Sincronização</h1>
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
        <div class="card">
          <app-estado-carga
            [estado]="estado()"
            textoVazio="Nenhuma sincronização recebida para este tenant."
            [mensagemErro]="mensagemErro()"
            (tentarNovamente)="recarregar()"
          />

          @if (estado() === 'pronto') {
            <div class="tabela-rolagem">
              <table class="tabela">
                <thead>
                  <tr>
                    <th scope="col">Rotina</th>
                    <th scope="col">Banco</th>
                    <th scope="col">Último sucesso</th>
                    <th scope="col">Situação</th>
                    <th scope="col">Mensagem</th>
                  </tr>
                </thead>
                <tbody>
                  @for (origem of origens(); track origem.idPublico) {
                    <tr>
                      <td>{{ origem.rotina }}</td>
                      <td class="mono">{{ origem.bancoOrigem ?? '—' }}</td>
                      <td class="mono">
                        {{ origem.ultimoSucessoEm ? (origem.ultimoSucessoEm | date: 'dd/MM/yyyy HH:mm') : 'Nunca' }}
                      </td>
                      <td>
                        @if (origem.ultimaSituacao; as situacao) {
                          <span [class]="'badge ' + badge(situacao)">{{ situacao }}</span>
                        } @else {
                          <span class="badge badge-neutro">Sem execução</span>
                        }
                      </td>
                      <td>{{ origem.ultimaMensagem ?? '—' }}</td>
                    </tr>
                  }
                </tbody>
              </table>
            </div>
          }
        </div>
      }
    </section>
  `,
})
export class SincronizacaoPage {
  private readonly api = inject(SincronizacaoApi);

  readonly avisoSemTenant = AVISO_SELECIONE_TENANT;
  readonly tenant = tenantEfetivo();
  readonly estado = signal<EstadoCarga>('carregando');
  readonly mensagemErro = signal('');
  readonly origens = signal<OrigemSincronizacao[]>([]);
  private carga?: Subscription;

  constructor() {
    // Recarrega quando o tenant efetivo muda (SuperAdmin troca a seleção no cabeçalho).
    effect(() => {
      if (this.tenant()) untracked(() => this.recarregar());
    });
  }

  badge(situacao: SituacaoExecucao): string {
    return BADGE[situacao] ?? 'badge-neutro';
  }

  recarregar(): void {
    this.carga?.unsubscribe();
    this.estado.set('carregando');
    this.carga = this.api.listar().subscribe({
      next: (origens) => {
        this.origens.set(origens);
        this.estado.set(origens.length === 0 ? 'vazio' : 'pronto');
      },
      error: (erro: unknown) => {
        this.mensagemErro.set(traduzirProblema(erro).mensagem);
        this.estado.set('erro');
      },
    });
  }
}
