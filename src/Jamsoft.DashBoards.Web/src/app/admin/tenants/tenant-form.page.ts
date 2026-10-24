import { ChangeDetectionStrategy, Component, OnInit, inject, input, signal } from '@angular/core';
import { AbstractControl, FormBuilder, ReactiveFormsModule, ValidationErrors, Validators } from '@angular/forms';
import { Router, RouterLink } from '@angular/router';
import { finalize } from 'rxjs';
import { Problema, traduzirProblema } from '../../core/erros/problema';
import { violacoesPoliticaSenha } from '../../core/auth/politica-senha';
import { CriarTenantRequisicao, TenantsApi, cnpjValido, mascararCnpj } from './tenants.api';

const NOME_MINIMO = 3;
const NOME_MAXIMO = 120;

function validarCnpj(controle: AbstractControl<string>): ValidationErrors | null {
  return cnpjValido(controle.value) ? null : { cnpj: true };
}

function validarSenha(controle: AbstractControl<string>): ValidationErrors | null {
  const erros = violacoesPoliticaSenha(controle.value);
  return erros.length ? { politica: erros } : null;
}

type CampoComErro = 'nome' | 'cnpj' | 'adminNome' | 'adminEmail' | 'adminSenha';

/** Campos da API (`erros` de VALIDACAO) → campos do formulário. */
const CAMPOS_API: Record<string, CampoComErro> = {
  nome: 'nome',
  cnpj: 'cnpj',
  'adminInicial.nome': 'adminNome',
  'adminInicial.email': 'adminEmail',
  'adminInicial.senhaInicial': 'adminSenha',
};

/**
 * `/admin/tenants/novo` e `/admin/tenants/:id` (TEN-01, TEN-02, TEN-03): nome, CNPJ com máscara e validação e,
 * na criação, a seção opcional do Admin inicial. 409 de CNPJ/e-mail aparece no campo; 409 de versão pede recarga.
 */
@Component({
  selector: 'app-tenant-form',
  imports: [ReactiveFormsModule, RouterLink],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <section class="pagina">
      <div class="pagina-cabecalho">
        <div>
          <span class="secao-rotulo">Tenants</span>
          <h1>{{ edicao() ? 'Editar tenant' : 'Novo tenant' }}</h1>
        </div>
        <a class="btn btn-contorno btn-sm" routerLink="/admin/tenants">Voltar</a>
      </div>

      @if (conflitoVersao()) {
        <div class="alerta alerta-atencao" role="alert">
          Registro alterado por outra pessoa. Recarregue.
          <button type="button" class="btn btn-suave btn-sm" (click)="carregar()">Recarregar</button>
        </div>
      }
      @if (erroGeral(); as mensagem) {
        <p class="alerta alerta-erro" role="alert">{{ mensagem }}</p>
      }

      <form class="card" [formGroup]="form" (ngSubmit)="salvar()" novalidate style="max-width: 860px">
        <div class="card-corpo">
          <div class="linha-campos">
            <div class="campo">
              <label for="tenant-nome">Nome <span class="obrigatorio" aria-hidden="true">*</span></label>
              <input id="tenant-nome" formControlName="nome" maxlength="120" [attr.aria-invalid]="!!erro('nome')"
                aria-describedby="tenant-nome-erro" />
              @if (erro('nome'); as mensagem) {
                <p id="tenant-nome-erro" class="campo-erro">{{ mensagem }}</p>
              }
            </div>
            <div class="campo">
              <label for="tenant-cnpj">CNPJ <span class="obrigatorio" aria-hidden="true">*</span></label>
              <input id="tenant-cnpj" formControlName="cnpj" inputmode="numeric" placeholder="00.000.000/0000-00"
                (input)="mascarar()" [attr.aria-invalid]="!!erro('cnpj')" aria-describedby="tenant-cnpj-erro" />
              @if (erro('cnpj'); as mensagem) {
                <p id="tenant-cnpj-erro" class="campo-erro">{{ mensagem }}</p>
              }
            </div>
          </div>

          @if (!edicao()) {
            <label class="marcador">
              <input type="checkbox" formControlName="comAdmin" (change)="alternarAdmin()" />
              Cadastrar o primeiro Admin do tenant
            </label>

            @if (form.controls.comAdmin.value) {
              <fieldset class="linha-campos" style="border: 0; padding: 0; margin: 0">
                <legend class="visualmente-oculto">Admin inicial</legend>
                <div class="campo">
                  <label for="admin-nome">Nome do Admin <span class="obrigatorio" aria-hidden="true">*</span></label>
                  <input id="admin-nome" formControlName="adminNome" [attr.aria-invalid]="!!erro('adminNome')"
                    aria-describedby="admin-nome-erro" />
                  @if (erro('adminNome'); as mensagem) {
                    <p id="admin-nome-erro" class="campo-erro">{{ mensagem }}</p>
                  }
                </div>
                <div class="campo">
                  <label for="admin-email">E-mail do Admin <span class="obrigatorio" aria-hidden="true">*</span></label>
                  <input id="admin-email" type="email" formControlName="adminEmail"
                    [attr.aria-invalid]="!!erro('adminEmail')" aria-describedby="admin-email-erro" />
                  @if (erro('adminEmail'); as mensagem) {
                    <p id="admin-email-erro" class="campo-erro">{{ mensagem }}</p>
                  }
                </div>
                <div class="campo">
                  <label for="admin-senha">Senha inicial <span class="obrigatorio" aria-hidden="true">*</span></label>
                  <input id="admin-senha" type="password" autocomplete="new-password" formControlName="adminSenha"
                    [attr.aria-invalid]="!!erro('adminSenha')" aria-describedby="admin-senha-erro" />
                  @if (erro('adminSenha'); as mensagem) {
                    <p id="admin-senha-erro" class="campo-erro">{{ mensagem }}</p>
                  } @else {
                    <p id="admin-senha-erro" class="campo-dica">Troca obrigatória no primeiro acesso.</p>
                  }
                </div>
              </fieldset>
            }
          }

          <div>
            <button type="submit" class="btn btn-primario" [disabled]="enviando() || carregando()">
              {{ enviando() ? 'Salvando...' : 'Salvar' }}
            </button>
          </div>
        </div>
      </form>
    </section>
  `,
})
export class TenantFormPage implements OnInit {
  private readonly api = inject(TenantsApi);
  private readonly router = inject(Router);

  /** ID público da rota `:id`; ausente em `/novo`. */
  readonly id = input<string>();

  readonly form = inject(FormBuilder).nonNullable.group({
    nome: ['', [Validators.required, Validators.minLength(NOME_MINIMO), Validators.maxLength(NOME_MAXIMO)]],
    cnpj: ['', [validarCnpj]],
    comAdmin: [false],
    adminNome: [{ value: '', disabled: true }, [Validators.required]],
    adminEmail: [{ value: '', disabled: true }, [Validators.required, Validators.email]],
    adminSenha: [{ value: '', disabled: true }, [validarSenha]],
  });

  readonly edicao = signal(false);
  readonly carregando = signal(false);
  readonly enviando = signal(false);
  readonly tentouEnviar = signal(false);
  readonly conflitoVersao = signal(false);
  readonly erroGeral = signal<string | null>(null);
  private readonly errosApi = signal<Partial<Record<CampoComErro, string>>>({});
  private versao = '';

  ngOnInit(): void {
    this.edicao.set(!!this.id());
    if (this.edicao()) this.carregar();
  }

  carregar(): void {
    const id = this.id();
    if (!id) return;
    this.carregando.set(true);
    this.conflitoVersao.set(false);
    this.erroGeral.set(null);
    this.api
      .obter(id)
      .pipe(finalize(() => this.carregando.set(false)))
      .subscribe({
        next: (tenant) => {
          this.versao = tenant.versao;
          this.form.patchValue({ nome: tenant.nome, cnpj: mascararCnpj(tenant.cnpj) });
        },
        error: (erro: unknown) => this.erroGeral.set(traduzirProblema(erro).mensagem),
      });
  }

  mascarar(): void {
    const controle = this.form.controls.cnpj;
    controle.setValue(mascararCnpj(controle.value), { emitEvent: false });
    this.limparErroApi('cnpj');
  }

  alternarAdmin(): void {
    const ligado = this.form.controls.comAdmin.value;
    for (const campo of ['adminNome', 'adminEmail', 'adminSenha'] as const) {
      if (ligado) this.form.controls[campo].enable();
      else this.form.controls[campo].disable();
    }
  }

  erro(campo: CampoComErro): string | null {
    const api = this.errosApi()[campo];
    if (api) return api;
    const controle = this.form.controls[campo];
    if (!this.tentouEnviar() || controle.disabled || controle.valid) return null;
    switch (campo) {
      case 'nome':
        return `O nome precisa ter entre ${NOME_MINIMO} e ${NOME_MAXIMO} caracteres.`;
      case 'cnpj':
        return 'CNPJ inválido.';
      case 'adminNome':
        return 'Informe o nome do Admin.';
      case 'adminEmail':
        return 'E-mail inválido.';
      case 'adminSenha':
        return (controle.errors?.['politica'] as string[]).join(' ');
    }
  }

  salvar(): void {
    this.tentouEnviar.set(true);
    this.errosApi.set({});
    this.erroGeral.set(null);
    this.conflitoVersao.set(false);
    if (this.form.invalid || this.enviando()) return;

    const valores = this.form.getRawValue();
    const nome = valores.nome.trim();
    const cnpj = valores.cnpj.replace(/\D/g, '');
    const id = this.id();
    let chamada;
    if (id) {
      chamada = this.api.editar(id, { nome, cnpj, versao: this.versao });
    } else {
      const corpo: CriarTenantRequisicao = { nome, cnpj };
      if (valores.comAdmin)
        corpo.adminInicial = { nome: valores.adminNome.trim(), email: valores.adminEmail.trim(), senhaInicial: valores.adminSenha };
      chamada = this.api.criar(corpo);
    }

    this.enviando.set(true);
    chamada.pipe(finalize(() => this.enviando.set(false))).subscribe({
      next: () => void this.router.navigateByUrl('/admin/tenants'),
      error: (erro: unknown) => this.tratarErro(traduzirProblema(erro)),
    });
  }

  private tratarErro(problema: Problema): void {
    switch (problema.codigo) {
      case 'CNPJ_DUPLICADO':
        this.errosApi.set({ cnpj: problema.mensagem });
        return;
      case 'EMAIL_EM_USO':
        this.errosApi.set({ adminEmail: problema.mensagem });
        return;
      case 'CONFLITO_VERSAO':
        this.conflitoVersao.set(true);
        return;
      case 'VALIDACAO': {
        const campos: Partial<Record<CampoComErro, string>> = {};
        for (const [chave, mensagens] of Object.entries(problema.erros)) {
          const campo = CAMPOS_API[chave];
          if (campo) campos[campo] = mensagens.join(' ');
        }
        if (Object.keys(campos).length) {
          this.errosApi.set(campos);
          return;
        }
        break;
      }
    }
    this.erroGeral.set(problema.mensagem);
  }

  private limparErroApi(campo: CampoComErro): void {
    if (this.errosApi()[campo]) this.errosApi.update((erros) => ({ ...erros, [campo]: undefined }));
  }
}
