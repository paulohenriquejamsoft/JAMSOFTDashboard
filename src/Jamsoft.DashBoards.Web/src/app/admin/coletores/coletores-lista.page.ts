import { DatePipe } from '@angular/common';
import { ChangeDetectionStrategy, Component, DestroyRef, inject, signal } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { RouterLink } from '@angular/router';
import { Subject, catchError, finalize, map, of, startWith, switchMap } from 'rxjs';
import { traduzirProblema } from '../../core/erros/problema';
import { EstadoCarga, Paginado } from '../../shared/paginado';
import { ConfirmacaoDialog } from '../../shared/ui/confirmacao.dialog';
import { EstadoCargaComponent } from '../../shared/ui/estado-carga.component';
import { PaginacaoComponent } from '../../shared/ui/paginacao.component';
import { Coletor, ColetoresApi, CredencialColetor } from './coletores.api';
import { SegredoColetorDialog } from './segredo-coletor.dialog';

type Resultado = { estado: 'carregando' } | { estado: 'erro'; mensagem: string } | { estado: 'ok'; pagina: Paginado<Coletor> };

interface AcaoPendente {
  tipo: 'revogar' | 'rotacionar';
  coletor: Coletor;
}

/**
 * `/admin/coletores` (COL-05, COL-06, COL-08): credenciais do Coletor com nome, `clientId`, situação, tenants
 * autorizados e último token. Revogar e rotacionar pedem confirmação; o secret novo aparece uma vez no diálogo.
 */
@Component({
  selector: 'app-coletores-lista',
  imports: [RouterLink, DatePipe, EstadoCargaComponent, PaginacaoComponent, ConfirmacaoDialog, SegredoColetorDialog],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <section class="pagina">
      <div class="pagina-cabecalho">
        <div>
          <span class="secao-rotulo">Administração</span>
          <h1>Coletores</h1>
        </div>
        <a class="btn btn-sucesso" routerLink="/admin/coletores/novo">Novo coletor</a>
      </div>

      <div class="card">
        @if (avisoAcao(); as aviso) {
          <p class="alerta alerta-erro" role="alert" style="margin: 16px 16px 0">{{ aviso }}</p>
        }

        <app-estado-carga
          [estado]="estado()"
          textoVazio="Nenhum coletor cadastrado."
          [mensagemErro]="mensagemErro()"
          (tentarNovamente)="recarregar()"
        >
          <a acaoVazio class="btn btn-sucesso btn-sm" routerLink="/admin/coletores/novo">Cadastrar coletor</a>
        </app-estado-carga>

        @if (estado() === 'pronto') {
          <div class="tabela-rolagem">
            <table class="tabela">
              <thead>
                <tr>
                  <th scope="col">Nome</th>
                  <th scope="col">Client ID</th>
                  <th scope="col">Situação</th>
                  <th scope="col">Tenants</th>
                  <th scope="col">Último token</th>
                  <th scope="col"><span class="visualmente-oculto">Ações</span></th>
                </tr>
              </thead>
              <tbody>
                @for (coletor of itens(); track coletor.idPublico) {
                  <tr>
                    <td>{{ coletor.nome }}</td>
                    <td class="mono">{{ coletor.clientId }}</td>
                    <td>
                      <span class="badge" [class.badge-sucesso]="coletor.ativo" [class.badge-perigo]="!coletor.ativo">
                        {{ coletor.ativo ? 'Ativo' : 'Revogado' }}
                      </span>
                    </td>
                    <td>{{ nomesTenants(coletor) }}</td>
                    <td class="mono">
                      {{ coletor.ultimoTokenEm ? (coletor.ultimoTokenEm | date: 'dd/MM/yyyy HH:mm') : 'Nunca' }}
                    </td>
                    <td class="acoes">
                      <a class="btn btn-claro btn-sm" [routerLink]="['/admin/coletores', coletor.idPublico]">Editar</a>
                      @if (coletor.ativo) {
                        <button type="button" class="btn btn-claro btn-sm" (click)="pedir('revogar', coletor)">Revogar</button>
                        <button type="button" class="btn btn-suave btn-sm" (click)="pedir('rotacionar', coletor)">
                          Rotacionar
                        </button>
                      }
                    </td>
                  </tr>
                }
              </tbody>
            </table>
          </div>
        }

        @if (pagina(); as p) {
          @if (p.total > 0) {
            <div class="barra-ferramentas">
              <app-paginacao [pagina]="p.pagina" [tamanho]="p.tamanho" [total]="p.total" (mudar)="irParaPagina($event)" />
            </div>
          }
        }
      </div>
    </section>

    @if (pendente(); as acao) {
      <app-confirmacao
        [titulo]="acao.tipo === 'revogar' ? 'Revogar credencial' : 'Rotacionar secret'"
        [mensagem]="
          acao.tipo === 'revogar'
            ? acao.coletor.nome + ' deixará de enviar dados e não conseguirá novos tokens.'
            : 'O secret atual de ' + acao.coletor.nome + ' deixará de funcionar. Configure o novo no Coletor.'
        "
        [textoConfirmar]="acao.tipo === 'revogar' ? 'Revogar' : 'Rotacionar'"
        [ocupado]="executando()"
        (confirmar)="confirmar(acao)"
        (cancelar)="pendente.set(null)"
      />
    }

    @if (credencialNova(); as credencial) {
      <app-segredo-coletor [credencial]="credencial" (fechado)="credencialNova.set(null)" />
    }
  `,
})
export class ColetoresListaPage {
  private readonly api = inject(ColetoresApi);
  private readonly destroyRef = inject(DestroyRef);

  readonly numeroPagina = signal(1);
  readonly estado = signal<EstadoCarga>('carregando');
  readonly mensagemErro = signal('');
  readonly pagina = signal<Paginado<Coletor> | null>(null);
  readonly itens = signal<Coletor[]>([]);
  readonly pendente = signal<AcaoPendente | null>(null);
  readonly executando = signal(false);
  readonly avisoAcao = signal<string | null>(null);
  /** Credencial recém-rotacionada; descartada ao fechar o diálogo (COL-06). */
  readonly credencialNova = signal<CredencialColetor | null>(null);

  private readonly carregar$ = new Subject<number>();

  constructor() {
    this.carregar$
      .pipe(
        switchMap((numero) =>
          this.api.listar(numero).pipe(
            map((pagina): Resultado => ({ estado: 'ok', pagina })),
            catchError((erro: unknown) => of<Resultado>({ estado: 'erro', mensagem: traduzirProblema(erro).mensagem })),
            startWith<Resultado>({ estado: 'carregando' }),
          ),
        ),
        takeUntilDestroyed(this.destroyRef),
      )
      .subscribe((resultado) => this.aplicar(resultado));

    this.recarregar();
  }

  nomesTenants(coletor: Coletor): string {
    return coletor.tenants.map((t) => t.nome).join(', ');
  }

  irParaPagina(numero: number): void {
    this.numeroPagina.set(numero);
    this.recarregar();
  }

  recarregar(): void {
    this.carregar$.next(this.numeroPagina());
  }

  pedir(tipo: AcaoPendente['tipo'], coletor: Coletor): void {
    this.avisoAcao.set(null);
    this.pendente.set({ tipo, coletor });
  }

  confirmar(acao: AcaoPendente): void {
    this.executando.set(true);
    const falhou = (erro: unknown) => {
      this.pendente.set(null);
      this.avisoAcao.set(traduzirProblema(erro).mensagem);
    };

    if (acao.tipo === 'revogar') {
      this.api.revogar(acao.coletor.idPublico).pipe(finalize(() => this.executando.set(false))).subscribe({
        next: (atualizado) => {
          this.itens.update((itens) => itens.map((c) => (c.idPublico === atualizado.idPublico ? atualizado : c)));
          this.pendente.set(null);
        },
        error: falhou,
      });
      return;
    }
    this.api.rotacionar(acao.coletor.idPublico).pipe(finalize(() => this.executando.set(false))).subscribe({
      next: (credencial) => {
        this.pendente.set(null);
        this.credencialNova.set(credencial);
      },
      error: falhou,
    });
  }

  private aplicar(resultado: Resultado): void {
    if (resultado.estado === 'carregando') {
      this.estado.set('carregando');
      return;
    }
    if (resultado.estado === 'erro') {
      this.mensagemErro.set(resultado.mensagem);
      this.pagina.set(null);
      this.estado.set('erro');
      return;
    }
    this.pagina.set(resultado.pagina);
    this.itens.set(resultado.pagina.itens);
    this.estado.set(resultado.pagina.itens.length === 0 ? 'vazio' : 'pronto');
  }
}
