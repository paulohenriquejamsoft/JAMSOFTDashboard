import { ChangeDetectionStrategy, Component, OnInit, computed, inject, input, signal } from '@angular/core';
import { FormControl, FormGroup, ReactiveFormsModule } from '@angular/forms';
import { Router, RouterLink } from '@angular/router';
import { finalize, map } from 'rxjs';
import { Problema, traduzirProblema } from '../../core/erros/problema';
import { Tenant, TenantsApi } from '../tenants/tenants.api';
import { Coletor, ColetoresApi, CredencialColetor, TAMANHO_MAXIMO, todasAsPaginas } from './coletores.api';
import { SegredoColetorDialog } from './segredo-coletor.dialog';

const NOME_MINIMO = 3;
const NOME_MAXIMO = 80;
const MENSAGEM_NOME = `O nome precisa ter entre ${NOME_MINIMO} e ${NOME_MAXIMO} caracteres.`;
const MENSAGEM_TENANTS = 'Selecione ao menos um tenant ativo.';

type Campo = 'nome' | 'tenants';

/**
 * `/admin/coletores/novo` e `/admin/coletores/:id` (COL-01, COL-06, COL-07): nome e tenants autorizados (só os
 * ativos). Na criação o secret aparece uma vez no diálogo e some ao fechar. Na edição só a lista de tenants muda
 * (a API não renomeia); 409 de versão pede recarga.
 */
@Component({
  selector: 'app-coletor-form',
  imports: [ReactiveFormsModule, RouterLink, SegredoColetorDialog],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <section class="pagina">
      <div class="pagina-cabecalho">
        <div>
          <span class="secao-rotulo">Coletores</span>
          <h1>{{ edicao() ? (coletor()?.nome ?? 'Editar coletor') : 'Novo coletor' }}</h1>
        </div>
        <a class="btn btn-contorno btn-sm" routerLink="/admin/coletores">Voltar</a>
      </div>

      @if (conflitoVersao()) {
        <div class="alerta alerta-atencao" role="alert">
          Registro alterado por outra pessoa. Recarregue.
          <button type="button" class="btn btn-suave btn-sm" (click)="carregarColetor()">Recarregar</button>
        </div>
      }
      @if (erroGeral(); as mensagem) {
        <p class="alerta alerta-erro" role="alert">{{ mensagem }}</p>
      }

      <form class="card" [formGroup]="form" (ngSubmit)="salvar()" novalidate style="max-width: 860px">
        <div class="card-corpo">
          @if (edicao()) {
            @if (coletor(); as c) {
              <p>Client ID: <span class="mono">{{ c.clientId }}</span></p>
            }
          } @else {
            <div class="campo">
              <label for="coletor-nome">Nome <span class="obrigatorio" aria-hidden="true">*</span></label>
              <input id="coletor-nome" formControlName="nome" maxlength="80" [attr.aria-invalid]="!!erro('nome')"
                aria-describedby="coletor-nome-erro" />
              @if (erro('nome'); as mensagem) {
                <p id="coletor-nome-erro" class="campo-erro">{{ mensagem }}</p>
              }
            </div>
          }

          <fieldset class="campo" style="border: 0; padding: 0; margin: 0" aria-describedby="coletor-tenants-erro">
            <legend>Tenants autorizados <span class="obrigatorio" aria-hidden="true">*</span></legend>
            @for (tenant of tenantsAtivos(); track tenant.idPublico) {
              <label class="marcador">
                <input type="checkbox" [checked]="selecionados().has(tenant.idPublico)"
                  (change)="alternar(tenant.idPublico)" />
                {{ tenant.nome }}
              </label>
            } @empty {
              <p class="campo-dica">{{ carregandoTenants() ? 'Carregando...' : 'Nenhum tenant ativo.' }}</p>
            }
            @if (erro('tenants'); as mensagem) {
              <p id="coletor-tenants-erro" class="campo-erro">{{ mensagem }}</p>
            }
          </fieldset>

          <div>
            <button type="submit" class="btn btn-primario" [disabled]="enviando() || carregandoColetor()">
              {{ enviando() ? 'Salvando...' : 'Salvar' }}
            </button>
          </div>
        </div>
      </form>
    </section>

    @if (credencialNova(); as credencial) {
      <app-segredo-coletor [credencial]="credencial" (fechado)="fecharSegredo()" />
    }
  `,
})
export class ColetorFormPage implements OnInit {
  private readonly api = inject(ColetoresApi);
  private readonly tenantsApi = inject(TenantsApi);
  private readonly router = inject(Router);

  /** ID público da rota `:id`; ausente em `/novo`. */
  readonly id = input<string>();

  readonly nome = new FormControl('', { nonNullable: true });
  readonly form = new FormGroup({ nome: this.nome });
  readonly tenantsAtivos = signal<Tenant[]>([]);
  readonly selecionados = signal<ReadonlySet<string>>(new Set());
  readonly coletor = signal<Coletor | null>(null);
  readonly edicao = computed(() => !!this.id());
  readonly carregandoTenants = signal(true);
  readonly carregandoColetor = signal(false);
  readonly enviando = signal(false);
  readonly tentouEnviar = signal(false);
  readonly conflitoVersao = signal(false);
  readonly erroGeral = signal<string | null>(null);
  /** Credencial recém-criada; existe só enquanto o diálogo está aberto (COL-01). */
  readonly credencialNova = signal<CredencialColetor | null>(null);
  private readonly errosApi = signal<Partial<Record<Campo, string>>>({});

  ngOnInit(): void {
    todasAsPaginas((pagina) =>
      this.tenantsApi.listar({ busca: '', situacao: 'Ativo', pagina, tamanho: TAMANHO_MAXIMO }),
    )
      .pipe(finalize(() => this.carregandoTenants.set(false)))
      .subscribe({
        next: (tenants) => this.tenantsAtivos.set(tenants),
        error: (erro: unknown) => this.erroGeral.set(traduzirProblema(erro).mensagem),
      });
    if (this.edicao()) this.carregarColetor();
  }

  carregarColetor(): void {
    const id = this.id();
    if (!id) return;
    this.carregandoColetor.set(true);
    this.conflitoVersao.set(false);
    this.erroGeral.set(null);
    this.api
      .listarTodos()
      .pipe(
        map((coletores) => coletores.find((c) => c.idPublico === id) ?? null),
        finalize(() => this.carregandoColetor.set(false)),
      )
      .subscribe({
        next: (coletor) => {
          if (!coletor) {
            this.erroGeral.set('Registro não encontrado');
            return;
          }
          this.coletor.set(coletor);
          this.selecionados.set(new Set(coletor.tenants.map((t) => t.idPublico)));
        },
        error: (erro: unknown) => this.erroGeral.set(traduzirProblema(erro).mensagem),
      });
  }

  alternar(idPublico: string): void {
    const novos = new Set(this.selecionados());
    if (novos.has(idPublico)) novos.delete(idPublico);
    else novos.add(idPublico);
    this.selecionados.set(novos);
    this.errosApi.update((erros) => ({ ...erros, tenants: undefined }));
  }

  erro(campo: Campo): string | null {
    const api = this.errosApi()[campo];
    if (api) return api;
    if (!this.tentouEnviar()) return null;
    if (campo === 'nome') return this.nomeValido() ? null : MENSAGEM_NOME;
    return this.tenantsEscolhidos().length ? null : MENSAGEM_TENANTS;
  }

  salvar(): void {
    this.tentouEnviar.set(true);
    this.errosApi.set({});
    this.erroGeral.set(null);
    this.conflitoVersao.set(false);
    const tenants = this.tenantsEscolhidos();
    const coletor = this.coletor();
    if (this.edicao() ? !coletor : !this.nomeValido()) return;
    if (!tenants.length || this.enviando()) return;

    this.enviando.set(true);
    const fim = () => this.enviando.set(false);
    if (coletor) {
      this.api
        .alterarTenants(coletor.idPublico, { tenants, versao: coletor.versao })
        .pipe(finalize(fim))
        .subscribe({
          next: () => void this.router.navigateByUrl('/admin/coletores'),
          error: (erro: unknown) => this.tratarErro(traduzirProblema(erro)),
        });
      return;
    }
    this.api
      .criar({ nome: this.nome.value.trim(), tenants })
      .pipe(finalize(fim))
      .subscribe({
        next: (credencial) => this.credencialNova.set(credencial),
        error: (erro: unknown) => this.tratarErro(traduzirProblema(erro)),
      });
  }

  /** Descarta o secret e volta à lista: ele não fica em lugar nenhum da tela. */
  fecharSegredo(): void {
    this.credencialNova.set(null);
    void this.router.navigateByUrl('/admin/coletores');
  }

  /** Marcados, na ordem da lista de ativos; autorizados que deixaram de estar ativos saem. */
  private tenantsEscolhidos(): string[] {
    const marcados = this.selecionados();
    return this.tenantsAtivos()
      .map((t) => t.idPublico)
      .filter((id) => marcados.has(id));
  }

  private nomeValido(): boolean {
    const tamanho = this.nome.value.trim().length;
    return tamanho >= NOME_MINIMO && tamanho <= NOME_MAXIMO;
  }

  private tratarErro(problema: Problema): void {
    if (problema.codigo === 'CONFLITO_VERSAO') {
      this.conflitoVersao.set(true);
      return;
    }
    if (problema.codigo === 'VALIDACAO') {
      const campos: Partial<Record<Campo, string>> = {};
      if (problema.erros['nome']) campos.nome = problema.erros['nome'].join(' ');
      if (problema.erros['tenants']) campos.tenants = problema.erros['tenants'].join(' ');
      if (Object.keys(campos).length) {
        this.errosApi.set(campos);
        return;
      }
    }
    this.erroGeral.set(problema.mensagem);
  }
}
