import { ChangeDetectionStrategy, Component, effect, inject, signal, untracked } from '@angular/core';
import { FormBuilder, FormControl, ReactiveFormsModule, Validators } from '@angular/forms';
import { Subscription, finalize } from 'rxjs';
import { SeletorTenantComponent } from '../../admin/seletor-tenant/seletor-tenant.component';
import { AVISO_SELECIONE_TENANT } from '../../core/auth/perfil.guard';
import { tenantEfetivo } from '../../core/auth/tenant-efetivo';
import { Problema, traduzirProblema } from '../../core/erros/problema';
import { EstadoCarga } from '../../shared/paginado';
import { EstadoCargaComponent } from '../../shared/ui/estado-carga.component';
import { Empresa, EmpresasApi } from './empresas.api';

const NOME_MAXIMO = 80;
const MENSAGEM_NOME = `O nome precisa ter entre 1 e ${NOME_MAXIMO} caracteres.`;
const MENSAGEM_CODIGO = 'O código da empresa precisa ser um inteiro positivo.';

type CampoCadastro = 'codEmpresa' | 'nome';

/**
 * `/cliente/empresas` (EMP-02, EMP-03, EMP-05, EMP-06): empresas do tenant efetivo, cadastro (código e nome) e
 * renomear na própria linha. Consulta não chega aqui (guard); o SuperAdmin sem tenant vê o seletor.
 */
@Component({
  selector: 'app-empresas',
  imports: [ReactiveFormsModule, EstadoCargaComponent, SeletorTenantComponent],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <section class="pagina">
      <div class="pagina-cabecalho">
        <div>
          <span class="secao-rotulo">Cadastros</span>
          <h1>Empresas</h1>
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
        <form class="card" [formGroup]="cadastro" (ngSubmit)="cadastrar()" novalidate aria-label="Nova empresa">
          <div class="barra-ferramentas" style="align-items: flex-start">
            <div class="campo" style="flex: 0 1 160px">
              <label for="empresa-codigo">Código <span class="obrigatorio" aria-hidden="true">*</span></label>
              <input id="empresa-codigo" type="number" min="1" step="1" formControlName="codEmpresa"
                [attr.aria-invalid]="!!erroCadastro('codEmpresa')" aria-describedby="empresa-codigo-erro" />
              @if (erroCadastro('codEmpresa'); as mensagem) {
                <p id="empresa-codigo-erro" class="campo-erro">{{ mensagem }}</p>
              }
            </div>
            <div class="campo">
              <label for="empresa-nome">Nome <span class="obrigatorio" aria-hidden="true">*</span></label>
              <input id="empresa-nome" formControlName="nome" maxlength="80"
                [attr.aria-invalid]="!!erroCadastro('nome')" aria-describedby="empresa-nome-erro" />
              @if (erroCadastro('nome'); as mensagem) {
                <p id="empresa-nome-erro" class="campo-erro">{{ mensagem }}</p>
              }
            </div>
            <div class="campo" style="flex: 0 0 auto">
              <span aria-hidden="true">&nbsp;</span>
              <button type="submit" class="btn btn-sucesso" [disabled]="cadastrando()">Cadastrar</button>
            </div>
          </div>
        </form>

        <div class="card">
          @if (avisoAcao(); as aviso) {
            <p class="alerta alerta-erro" role="alert" style="margin: 16px 16px 0">{{ aviso }}</p>
          }

          <app-estado-carga
            [estado]="estado()"
            textoVazio="Nenhuma empresa cadastrada."
            [mensagemErro]="mensagemErro()"
            (tentarNovamente)="recarregar()"
          />

          @if (estado() === 'pronto') {
            <div class="tabela-rolagem">
              <table class="tabela">
                <thead>
                  <tr>
                    <th scope="col">Código</th>
                    <th scope="col">Nome</th>
                    <th scope="col"><span class="visualmente-oculto">Ações</span></th>
                  </tr>
                </thead>
                <tbody>
                  @for (empresa of empresas(); track empresa.idPublico) {
                    <tr>
                      <td class="mono">{{ empresa.codEmpresa }}</td>
                      @if (emEdicao()?.idPublico === empresa.idPublico) {
                        <td>
                          <div class="campo">
                            <label class="visualmente-oculto" for="empresa-renomear">Novo nome</label>
                            <input id="empresa-renomear" [formControl]="novoNome" maxlength="80"
                              [attr.aria-invalid]="!!erroRenomear()" aria-describedby="empresa-renomear-erro" />
                            @if (erroRenomear(); as mensagem) {
                              <p id="empresa-renomear-erro" class="campo-erro">{{ mensagem }}</p>
                            }
                          </div>
                        </td>
                        <td class="acoes">
                          <button type="button" class="btn btn-primario btn-sm" [disabled]="renomeando()"
                            (click)="salvarNome(empresa)">Salvar</button>
                          <button type="button" class="btn btn-contorno btn-sm" (click)="emEdicao.set(null)">Cancelar</button>
                        </td>
                      } @else {
                        <td>{{ empresa.nome }}</td>
                        <td class="acoes">
                          <button type="button" class="btn btn-claro btn-sm" (click)="editarNome(empresa)">Renomear</button>
                        </td>
                      }
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
export class EmpresasPage {
  private readonly api = inject(EmpresasApi);

  readonly avisoSemTenant = AVISO_SELECIONE_TENANT;
  readonly tenant = tenantEfetivo();
  readonly estado = signal<EstadoCarga>('carregando');
  readonly mensagemErro = signal('');
  readonly empresas = signal<Empresa[]>([]);
  readonly avisoAcao = signal<string | null>(null);

  readonly cadastro = inject(FormBuilder).group({
    codEmpresa: new FormControl<number | null>(null, [Validators.required, Validators.min(1), Validators.pattern(/^\d+$/)]),
    nome: new FormControl('', { nonNullable: true, validators: [Validators.required, Validators.maxLength(NOME_MAXIMO)] }),
  });
  readonly cadastrando = signal(false);
  readonly tentouCadastrar = signal(false);
  private readonly errosCadastroApi = signal<Partial<Record<CampoCadastro, string>>>({});

  readonly emEdicao = signal<Empresa | null>(null);
  readonly novoNome = new FormControl('', { nonNullable: true });
  readonly erroRenomear = signal<string | null>(null);
  readonly renomeando = signal(false);
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
      next: (empresas) => this.aplicarLista(empresas),
      error: (erro: unknown) => {
        this.mensagemErro.set(traduzirProblema(erro).mensagem);
        this.estado.set('erro');
      },
    });
  }

  erroCadastro(campo: CampoCadastro): string | null {
    const api = this.errosCadastroApi()[campo];
    if (api) return api;
    if (!this.tentouCadastrar() || this.cadastro.controls[campo].valid) return null;
    return campo === 'codEmpresa' ? MENSAGEM_CODIGO : MENSAGEM_NOME;
  }

  cadastrar(): void {
    this.tentouCadastrar.set(true);
    this.errosCadastroApi.set({});
    this.avisoAcao.set(null);
    const nome = this.cadastro.controls.nome.value.trim();
    if (!nome) this.cadastro.controls.nome.setValue('');
    if (this.cadastro.invalid || this.cadastrando()) return;

    this.cadastrando.set(true);
    this.api
      .criar(Number(this.cadastro.controls.codEmpresa.value), nome)
      .pipe(finalize(() => this.cadastrando.set(false)))
      .subscribe({
        next: (empresa) => {
          this.aplicarLista([...this.empresas(), empresa].sort((a, b) => a.codEmpresa - b.codEmpresa));
          this.cadastro.reset({ codEmpresa: null, nome: '' });
          this.tentouCadastrar.set(false);
        },
        error: (erro: unknown) => this.tratarErroCadastro(traduzirProblema(erro)),
      });
  }

  editarNome(empresa: Empresa): void {
    this.avisoAcao.set(null);
    this.erroRenomear.set(null);
    this.novoNome.setValue(empresa.nome);
    this.emEdicao.set(empresa);
  }

  salvarNome(empresa: Empresa): void {
    const nome = this.novoNome.value.trim();
    if (nome.length === 0 || nome.length > NOME_MAXIMO) {
      this.erroRenomear.set(MENSAGEM_NOME);
      return;
    }
    this.renomeando.set(true);
    this.api
      .renomear(empresa.idPublico, nome)
      .pipe(finalize(() => this.renomeando.set(false)))
      .subscribe({
        next: (atualizada) => {
          this.empresas.update((lista) => lista.map((e) => (e.idPublico === atualizada.idPublico ? atualizada : e)));
          this.emEdicao.set(null);
        },
        error: (erro: unknown) => {
          const problema = traduzirProblema(erro);
          this.erroRenomear.set(problema.erros['nome']?.join(' ') ?? problema.mensagem);
        },
      });
  }

  private aplicarLista(empresas: Empresa[]): void {
    this.empresas.set(empresas);
    this.estado.set(empresas.length === 0 ? 'vazio' : 'pronto');
  }

  private tratarErroCadastro(problema: Problema): void {
    if (problema.codigo === 'EMPRESA_DUPLICADA') {
      this.errosCadastroApi.set({ codEmpresa: problema.mensagem });
      return;
    }
    if (problema.codigo === 'VALIDACAO') {
      const campos: Partial<Record<CampoCadastro, string>> = {};
      if (problema.erros['codEmpresa']) campos.codEmpresa = problema.erros['codEmpresa'].join(' ');
      if (problema.erros['nome']) campos.nome = problema.erros['nome'].join(' ');
      if (Object.keys(campos).length) {
        this.errosCadastroApi.set(campos);
        return;
      }
    }
    this.avisoAcao.set(problema.mensagem);
  }
}
