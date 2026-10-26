import { DatePipe } from '@angular/common';
import { ChangeDetectionStrategy, Component, effect, inject, signal, untracked } from '@angular/core';
import { FormBuilder, FormControl, ReactiveFormsModule, Validators } from '@angular/forms';
import { Subscription, finalize } from 'rxjs';
import { SeletorTenantComponent } from '../../admin/seletor-tenant/seletor-tenant.component';
import { AVISO_SELECIONE_TENANT } from '../../core/auth/perfil.guard';
import { tenantEfetivo } from '../../core/auth/tenant-efetivo';
import { Problema, traduzirProblema } from '../../core/erros/problema';
import { EstadoCarga } from '../../shared/paginado';
import { ConfirmacaoDialog } from '../../shared/ui/confirmacao.dialog';
import { EstadoCargaComponent } from '../../shared/ui/estado-carga.component';
import { ChaveMcpCriadaDialog } from './chave-mcp-criada.dialog';
import { ChaveMcp, ChavesMcpApi } from './chaves-mcp.api';

const DESCRICAO_MAXIMA = 80;
const MENSAGEM_DESCRICAO = `A descrição precisa ter entre 1 e ${DESCRICAO_MAXIMA} caracteres.`;

type CampoCadastro = 'descricao' | 'expiraEm';

/** Hoje em São Paulo, no formato yyyy-MM-dd (o mesmo de `<input type="date">` e de `expiraEm`). */
export function hojeEmSaoPaulo(agora = new Date()): string {
  return new Intl.DateTimeFormat('en-CA', { timeZone: 'America/Sao_Paulo' }).format(agora);
}

/** yyyy-MM-dd → dd/MM/yyyy, sem passar por `Date` (o DatePipe leria o dia como meia-noite UTC). */
export function formatarDia(dia: string): string {
  const [ano, mes, d] = dia.split('-');
  return `${d}/${mes}/${ano}`;
}

/**
 * `/cliente/chaves-mcp`: api-keys do conector do Claude (servidor MCP) para o tenant efetivo. Criar mostra a chave uma
 * única vez; revogar pede confirmação. Consulta não chega aqui (guard); o SuperAdmin sem tenant vê o seletor.
 */
@Component({
  selector: 'app-chaves-mcp',
  imports: [ReactiveFormsModule, DatePipe, EstadoCargaComponent, SeletorTenantComponent, ConfirmacaoDialog, ChaveMcpCriadaDialog],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <section class="pagina">
      <div class="pagina-cabecalho">
        <div>
          <span class="secao-rotulo">Integrações</span>
          <h1>Conector Claude</h1>
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
        <form class="card" [formGroup]="cadastro" (ngSubmit)="criar()" novalidate aria-label="Nova chave">
          <div class="barra-ferramentas" style="align-items: flex-start">
            <div class="campo">
              <label for="chave-descricao">Descrição <span class="obrigatorio" aria-hidden="true">*</span></label>
              <input id="chave-descricao" formControlName="descricao" maxlength="80" placeholder="Ex.: Claude - Painéis"
                [attr.aria-invalid]="!!erroCadastro('descricao')" aria-describedby="chave-descricao-erro" />
              @if (erroCadastro('descricao'); as mensagem) {
                <p id="chave-descricao-erro" class="campo-erro">{{ mensagem }}</p>
              }
            </div>
            <div class="campo" style="flex: 0 1 200px">
              <label for="chave-expira">Válida até</label>
              <input id="chave-expira" type="date" formControlName="expiraEm" [min]="hoje"
                [attr.aria-invalid]="!!erroCadastro('expiraEm')" aria-describedby="chave-expira-dica chave-expira-erro" />
              <p id="chave-expira-dica" class="campo-dica">Vazio: não expira.</p>
              @if (erroCadastro('expiraEm'); as mensagem) {
                <p id="chave-expira-erro" class="campo-erro">{{ mensagem }}</p>
              }
            </div>
            <div class="campo" style="flex: 0 0 auto">
              <span aria-hidden="true">&nbsp;</span>
              <button type="submit" class="btn btn-sucesso" [disabled]="criando()">Gerar chave</button>
            </div>
          </div>
        </form>

        <div class="card">
          @if (avisoAcao(); as aviso) {
            <p class="alerta alerta-erro" role="alert" style="margin: 16px 16px 0">{{ aviso }}</p>
          }

          <app-estado-carga
            [estado]="estado()"
            textoVazio="Nenhuma chave gerada."
            [mensagemErro]="mensagemErro()"
            (tentarNovamente)="recarregar()"
          />

          @if (estado() === 'pronto') {
            <div class="tabela-rolagem">
              <table class="tabela">
                <thead>
                  <tr>
                    <th scope="col">Chave</th>
                    <th scope="col">Descrição</th>
                    <th scope="col">Situação</th>
                    <th scope="col">Criada em</th>
                    <th scope="col">Válida até</th>
                    <th scope="col"><span class="visualmente-oculto">Ações</span></th>
                  </tr>
                </thead>
                <tbody>
                  @for (chave of chaves(); track chave.idPublico) {
                    <tr>
                      <td class="mono">sge_{{ chave.prefixo }}_…</td>
                      <td>{{ chave.descricao }}</td>
                      <td>
                        @switch (situacao(chave)) {
                          @case ('ativa') { <span class="badge badge-sucesso">Ativa</span> }
                          @case ('vencida') { <span class="badge badge-atencao">Vencida</span> }
                          @default { <span class="badge badge-perigo">Revogada</span> }
                        }
                      </td>
                      <td class="mono">{{ chave.criadoEm | date: 'dd/MM/yyyy HH:mm' }}</td>
                      <td class="mono">{{ chave.expiraEm ? formatarDia(chave.expiraEm) : 'Sem validade' }}</td>
                      <td class="acoes">
                        @if (chave.ativo) {
                          <button type="button" class="btn btn-claro btn-sm" (click)="pendente.set(chave)">Revogar</button>
                        }
                      </td>
                    </tr>
                  }
                </tbody>
              </table>
            </div>
          }
        </div>
      }
    </section>

    @if (pendente(); as chave) {
      <app-confirmacao
        titulo="Revogar chave"
        [mensagem]="'O Claude deixará de acessar as vendas com a chave ' + chave.descricao + ' em até 1 minuto.'"
        textoConfirmar="Revogar"
        [ocupado]="revogando()"
        (confirmar)="revogar(chave)"
        (cancelar)="pendente.set(null)"
      />
    }

    @if (chaveNova(); as chave) {
      <app-chave-mcp-criada [chave]="chave" (fechado)="chaveNova.set(null)" />
    }
  `,
})
export class ChavesMcpPage {
  private readonly api = inject(ChavesMcpApi);

  readonly avisoSemTenant = AVISO_SELECIONE_TENANT;
  readonly formatarDia = formatarDia;
  readonly hoje = hojeEmSaoPaulo();
  readonly tenant = tenantEfetivo();
  readonly estado = signal<EstadoCarga>('carregando');
  readonly mensagemErro = signal('');
  readonly chaves = signal<ChaveMcp[]>([]);
  readonly avisoAcao = signal<string | null>(null);

  readonly cadastro = inject(FormBuilder).group({
    descricao: new FormControl('', {
      nonNullable: true,
      validators: [Validators.required, Validators.maxLength(DESCRICAO_MAXIMA)],
    }),
    expiraEm: new FormControl('', { nonNullable: true }),
  });
  readonly criando = signal(false);
  readonly tentouCriar = signal(false);
  private readonly errosCadastroApi = signal<Partial<Record<CampoCadastro, string>>>({});

  readonly pendente = signal<ChaveMcp | null>(null);
  readonly revogando = signal(false);
  /** Chave recém-criada; descartada ao fechar o diálogo. */
  readonly chaveNova = signal<string | null>(null);
  private carga?: Subscription;

  constructor() {
    // Recarrega quando o tenant efetivo muda (SuperAdmin troca a seleção no cabeçalho).
    effect(() => {
      if (this.tenant()) untracked(() => this.recarregar());
    });
  }

  recarregar(): void {
    this.carga?.unsubscribe();
    this.estado.set('carregando');
    this.carga = this.api.listar().subscribe({
      next: (chaves) => this.aplicarLista(chaves),
      error: (erro: unknown) => {
        this.mensagemErro.set(traduzirProblema(erro).mensagem);
        this.estado.set('erro');
      },
    });
  }

  situacao(chave: ChaveMcp): 'ativa' | 'vencida' | 'revogada' {
    if (!chave.ativo) return 'revogada';
    return chave.expiraEm && chave.expiraEm < this.hoje ? 'vencida' : 'ativa';
  }

  erroCadastro(campo: CampoCadastro): string | null {
    const api = this.errosCadastroApi()[campo];
    if (api) return api;
    if (campo === 'descricao' && this.tentouCriar() && this.cadastro.controls.descricao.invalid) return MENSAGEM_DESCRICAO;
    return null;
  }

  criar(): void {
    this.tentouCriar.set(true);
    this.errosCadastroApi.set({});
    this.avisoAcao.set(null);
    const descricao = this.cadastro.controls.descricao.value.trim();
    if (!descricao) this.cadastro.controls.descricao.setValue('');
    if (this.cadastro.invalid || this.criando()) return;

    this.criando.set(true);
    this.api
      .criar(descricao, this.cadastro.controls.expiraEm.value || null)
      .pipe(finalize(() => this.criando.set(false)))
      .subscribe({
        next: (criada) => {
          this.aplicarLista([criada.registro, ...this.chaves()]);
          this.chaveNova.set(criada.chave);
          this.cadastro.reset({ descricao: '', expiraEm: '' });
          this.tentouCriar.set(false);
        },
        error: (erro: unknown) => this.tratarErroCadastro(traduzirProblema(erro)),
      });
  }

  revogar(chave: ChaveMcp): void {
    this.revogando.set(true);
    this.avisoAcao.set(null);
    this.api
      .revogar(chave.idPublico)
      .pipe(finalize(() => this.revogando.set(false)))
      .subscribe({
        next: (revogada) => {
          // Revogada desce para depois das ativas, como na ordem da API.
          const resto = this.chaves().filter((c) => c.idPublico !== revogada.idPublico);
          const ativas = resto.filter((c) => c.ativo);
          this.aplicarLista([...ativas, revogada, ...resto.filter((c) => !c.ativo)]);
          this.pendente.set(null);
        },
        error: (erro: unknown) => {
          this.avisoAcao.set(traduzirProblema(erro).mensagem);
          this.pendente.set(null);
        },
      });
  }

  private aplicarLista(chaves: ChaveMcp[]): void {
    this.chaves.set(chaves);
    this.estado.set(chaves.length === 0 ? 'vazio' : 'pronto');
  }

  private tratarErroCadastro(problema: Problema): void {
    if (problema.codigo === 'VALIDACAO') {
      const campos: Partial<Record<CampoCadastro, string>> = {};
      if (problema.erros['descricao']) campos.descricao = problema.erros['descricao'].join(' ');
      if (problema.erros['expiraEm']) campos.expiraEm = problema.erros['expiraEm'].join(' ');
      if (Object.keys(campos).length) {
        this.errosCadastroApi.set(campos);
        return;
      }
    }
    this.avisoAcao.set(problema.mensagem);
  }
}
