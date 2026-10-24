import { ChangeDetectionStrategy, Component, DestroyRef, computed, inject, input, output, signal } from '@angular/core';
import { HttpErrorResponse } from '@angular/common/http';
import { Subscription, finalize, switchMap, takeWhile, timer } from 'rxjs';
import { TenantResumo } from '../../core/auth/auth.service';
import { traduzirProblema } from '../../core/erros/problema';
import { ExclusaoApi, JobExclusao, SituacaoExclusao } from './exclusao.api';

/** Intervalo do acompanhamento do job. */
export const INTERVALO_POLLING_MS = 5000;

const ROTULOS: Record<SituacaoExclusao, string> = {
  Pendente: 'Pendente',
  EmExecucao: 'Em execução',
  Concluida: 'Concluída',
  Falhou: 'Falhou',
};

const FINAIS: readonly SituacaoExclusao[] = ['Concluida', 'Falhou'];

/**
 * Exclusão total de um tenant (DEL-02, DEL-03, DEL-06): explica a operação, exige o nome exato do tenant,
 * envia a solicitação e acompanha o job a cada 5 s (situação, etapa, linhas). `Falhou` mostra o erro e "Retomar".
 */
@Component({
  selector: 'app-exclusao-tenant',
  changeDetection: ChangeDetectionStrategy.OnPush,
  host: { '(document:keydown.escape)': 'fechar()' },
  styles: `
    dl {
      display: grid;
      grid-template-columns: max-content 1fr;
      gap: 6px 12px;
      margin: 0;
    }
    dt {
      font-weight: 700;
    }
    dd {
      margin: 0;
    }
    .tabela td,
    .tabela th {
      padding: 4px 8px;
    }
    .nome {
      font-family: var(--font-mono);
      font-weight: 700;
    }
  `,
  template: `
    <div class="dialogo-fundo">
      <div class="dialogo" role="dialog" aria-modal="true" aria-labelledby="exclusao-titulo">
        <div class="dialogo-cabecalho">
          <h2 id="exclusao-titulo">Excluir tenant</h2>
        </div>

        @if (job(); as atual) {
          <div class="dialogo-corpo" aria-live="polite">
            @if (aviso(); as texto) {
              <p class="alerta alerta-atencao">{{ texto }}</p>
            }
            <dl>
              <dt>Tenant</dt>
              <dd>{{ atual.tenantNome }}</dd>
              <dt>Situação</dt>
              <dd>
                <span class="badge" [class]="classeSituacao(atual.situacao)" data-teste="situacao">
                  {{ rotulo(atual.situacao) }}
                </span>
              </dd>
              <dt>Etapa</dt>
              <dd data-teste="etapa">{{ atual.etapaAtual ?? '—' }}</dd>
            </dl>
            @if (linhas().length > 0) {
              <table class="tabela">
                <caption class="visualmente-oculto">Linhas removidas por tabela</caption>
                <thead>
                  <tr><th scope="col">Tabela</th><th scope="col">Linhas removidas</th></tr>
                </thead>
                <tbody>
                  @for (linha of linhas(); track linha.tabela) {
                    <tr><td class="mono">{{ linha.tabela }}</td><td>{{ linha.quantidade }}</td></tr>
                  }
                </tbody>
              </table>
            }
            @if (atual.situacao === 'Falhou') {
              <p class="alerta alerta-erro" role="alert">{{ atual.erro ?? 'A exclusão falhou.' }}</p>
            }
            @if (atual.situacao === 'Concluida') {
              <p class="alerta alerta-sucesso" role="status">Todos os dados do tenant foram excluídos.</p>
            }
            @if (erro(); as mensagem) {
              <p class="alerta alerta-erro" role="alert">{{ mensagem }}</p>
            }
          </div>
          <div class="dialogo-rodape">
            @if (atual.situacao === 'Falhou') {
              <button type="button" class="btn btn-primario btn-sm" [disabled]="enviando()" (click)="retomar()">
                Retomar
              </button>
            }
            <button type="button" class="btn btn-contorno btn-sm" (click)="fechar()">Fechar</button>
          </div>
        } @else {
          <div class="dialogo-corpo">
            <p>
              Todos os dados de <span class="nome">{{ tenant().nome }}</span> serão excluídos definitivamente:
              empresas, usuários, sessões e demais registros do tenant. O tenant é inativado na hora e a exclusão
              continua em segundo plano. Esta ação não pode ser desfeita.
            </p>
            <div class="campo">
              <label for="exclusao-confirmacao">Digite o nome do tenant para confirmar</label>
              <input id="exclusao-confirmacao" autocomplete="off" [value]="confirmacao()"
                (input)="confirmacao.set($any($event.target).value)" aria-describedby="exclusao-dica" />
              <p id="exclusao-dica" class="campo-dica">Exatamente como cadastrado, com maiúsculas e acentos.</p>
            </div>
            @if (erro(); as mensagem) {
              <p class="alerta alerta-erro" role="alert">{{ mensagem }}</p>
            }
          </div>
          <div class="dialogo-rodape">
            <button type="button" class="btn btn-contorno btn-sm" (click)="fechar()">Cancelar</button>
            <button type="button" class="btn btn-perigo btn-sm" [disabled]="!nomeConfere() || enviando()" (click)="confirmar()">
              Excluir definitivamente
            </button>
          </div>
        }
      </div>
    </div>
  `,
})
export class ExclusaoTenantDialog {
  private readonly api = inject(ExclusaoApi);

  readonly tenant = input.required<TenantResumo>();
  /** Fechou; `true` quando houve solicitação de exclusão (a lista deve recarregar). */
  readonly fechado = output<boolean>();

  readonly confirmacao = signal('');
  readonly nomeConfere = computed(() => this.confirmacao() === this.tenant().nome);
  readonly enviando = signal(false);
  readonly job = signal<JobExclusao | null>(null);
  readonly erro = signal<string | null>(null);
  readonly aviso = signal<string | null>(null);
  readonly linhas = computed(() =>
    Object.entries(this.job()?.linhasRemovidas ?? {}).map(([tabela, quantidade]) => ({ tabela, quantidade })),
  );

  private polling: Subscription | null = null;
  private solicitou = false;

  constructor() {
    inject(DestroyRef).onDestroy(() => this.pararPolling());
  }

  rotulo(situacao: SituacaoExclusao): string {
    return ROTULOS[situacao];
  }

  classeSituacao(situacao: SituacaoExclusao): string {
    const cor = { Pendente: 'badge-neutro', EmExecucao: 'badge-atencao', Concluida: 'badge-sucesso', Falhou: 'badge-perigo' };
    return `badge ${cor[situacao]}`;
  }

  confirmar(): void {
    if (!this.nomeConfere() || this.enviando()) return;
    this.erro.set(null);
    this.enviando.set(true);
    this.api
      .solicitar(this.tenant().idPublico, this.confirmacao())
      .pipe(finalize(() => this.enviando.set(false)))
      .subscribe({
        next: ({ jobId }) => {
          this.solicitou = true;
          this.acompanhar(jobId);
        },
        error: (erro: unknown) => {
          const problema = traduzirProblema(erro);
          const jobAtual = jobIdDoProblema(erro);
          if (problema.codigo === 'EXCLUSAO_EM_ANDAMENTO' && jobAtual) {
            this.aviso.set('Já existe uma exclusão em andamento para este tenant. Acompanhe abaixo.');
            this.acompanhar(jobAtual);
          } else {
            this.erro.set(problema.mensagem);
          }
        },
      });
  }

  retomar(): void {
    const atual = this.job();
    if (!atual || this.enviando()) return;
    this.erro.set(null);
    this.enviando.set(true);
    this.api
      .retomar(atual.jobId)
      .pipe(finalize(() => this.enviando.set(false)))
      .subscribe({
        next: ({ jobId }) => this.acompanhar(jobId),
        error: (erro: unknown) => this.erro.set(traduzirProblema(erro).mensagem),
      });
  }

  fechar(): void {
    this.pararPolling();
    this.fechado.emit(this.solicitou);
  }

  /** Consulta o job agora e a cada 5 s, até `Concluida` ou `Falhou`. */
  private acompanhar(jobId: string): void {
    this.pararPolling();
    this.polling = timer(0, INTERVALO_POLLING_MS)
      .pipe(
        switchMap(() => this.api.obter(jobId)),
        takeWhile((job) => !FINAIS.includes(job.situacao), true),
      )
      .subscribe({
        next: (job) => this.job.set(job),
        error: (erro: unknown) => this.erro.set(traduzirProblema(erro).mensagem),
      });
  }

  private pararPolling(): void {
    this.polling?.unsubscribe();
    this.polling = null;
  }
}

/** `jobId` que a API manda no 409 EXCLUSAO_EM_ANDAMENTO (job ativo do tenant). */
function jobIdDoProblema(erro: unknown): string | null {
  const corpo = erro instanceof HttpErrorResponse ? (erro.error as { jobId?: unknown } | null) : null;
  return typeof corpo?.jobId === 'string' ? corpo.jobId : null;
}
