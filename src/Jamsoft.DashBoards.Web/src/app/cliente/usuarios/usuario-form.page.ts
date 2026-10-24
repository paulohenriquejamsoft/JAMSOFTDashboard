import { ChangeDetectionStrategy, Component, OnInit, inject, input, signal } from '@angular/core';
import { AbstractControl, FormBuilder, ReactiveFormsModule, ValidationErrors, Validators } from '@angular/forms';
import { Router, RouterLink } from '@angular/router';
import { Observable, finalize } from 'rxjs';
import { violacoesPoliticaSenha } from '../../core/auth/politica-senha';
import { Problema, traduzirProblema } from '../../core/erros/problema';
import { RedefinirSenhaDialog } from './redefinir-senha.dialog';
import { PerfilTenant, Usuario, UsuariosApi } from './usuarios.api';

const NOME_MAXIMO = 120;
const LISTA = '/cliente/usuarios';

/** Único lugar com os perfis oferecidos: a gestão do tenant só cria ou atribui Admin e Consulta (USR-02). */
export const PERFIS_TENANT: readonly PerfilTenant[] = ['Admin', 'Consulta'];

function validarSenha(controle: AbstractControl<string>): ValidationErrors | null {
  const erros = violacoesPoliticaSenha(controle.value);
  return erros.length ? { politica: erros } : null;
}

type Campo = 'nome' | 'email' | 'perfil' | 'senhaInicial';
const CAMPOS: readonly string[] = ['nome', 'email', 'perfil', 'senhaInicial'];

/**
 * `/cliente/usuarios/novo` e `/cliente/usuarios/:id` (USR-01, USR-02, USR-03, USR-05, USR-07): criação com nome,
 * e-mail, perfil e senha inicial; edição de nome, perfil e situação, com "Redefinir senha".
 */
@Component({
  selector: 'app-usuario-form',
  imports: [ReactiveFormsModule, RouterLink, RedefinirSenhaDialog],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <section class="pagina">
      <div class="pagina-cabecalho">
        <div>
          <span class="secao-rotulo">Usuários</span>
          <h1>{{ edicao() ? 'Editar usuário' : 'Novo usuário' }}</h1>
        </div>
        <div class="acoes">
          @if (usuario()) {
            <button type="button" class="btn btn-claro btn-sm" (click)="redefinindo.set(true)">Redefinir senha</button>
          }
          <a class="btn btn-contorno btn-sm" routerLink="/cliente/usuarios">Voltar</a>
        </div>
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
              <label for="usuario-nome">Nome <span class="obrigatorio" aria-hidden="true">*</span></label>
              <input id="usuario-nome" formControlName="nome" maxlength="120" [attr.aria-invalid]="!!erro('nome')"
                aria-describedby="usuario-nome-erro" />
              @if (erro('nome'); as mensagem) {
                <p id="usuario-nome-erro" class="campo-erro">{{ mensagem }}</p>
              }
            </div>
            <div class="campo">
              <label for="usuario-email">E-mail <span class="obrigatorio" aria-hidden="true">*</span></label>
              <input id="usuario-email" type="email" formControlName="email" [attr.aria-invalid]="!!erro('email')"
                aria-describedby="usuario-email-erro" />
              @if (erro('email'); as mensagem) {
                <p id="usuario-email-erro" class="campo-erro">{{ mensagem }}</p>
              }
            </div>
          </div>

          <div class="linha-campos">
            <div class="campo">
              <label for="usuario-perfil">Perfil <span class="obrigatorio" aria-hidden="true">*</span></label>
              <select id="usuario-perfil" formControlName="perfil" aria-describedby="usuario-perfil-erro">
                @for (perfil of perfis; track perfil) {
                  <option [value]="perfil">{{ perfil }}</option>
                }
              </select>
              @if (erro('perfil'); as mensagem) {
                <p id="usuario-perfil-erro" class="campo-erro">{{ mensagem }}</p>
              }
            </div>
            @if (edicao()) {
              <div class="campo">
                <label class="marcador">
                  <input type="checkbox" formControlName="ativo" />
                  Ativo
                </label>
              </div>
            } @else {
              <div class="campo">
                <label for="usuario-senha">Senha inicial <span class="obrigatorio" aria-hidden="true">*</span></label>
                <input id="usuario-senha" type="password" autocomplete="new-password" formControlName="senhaInicial"
                  [attr.aria-invalid]="!!erro('senhaInicial')" aria-describedby="usuario-senha-erro" />
                @if (erro('senhaInicial'); as mensagem) {
                  <p id="usuario-senha-erro" class="campo-erro">{{ mensagem }}</p>
                } @else {
                  <p id="usuario-senha-erro" class="campo-dica">Troca obrigatória no primeiro acesso.</p>
                }
              </div>
            }
          </div>

          <div>
            <button type="submit" class="btn btn-primario" [disabled]="enviando() || carregando()">
              {{ enviando() ? 'Salvando...' : 'Salvar' }}
            </button>
          </div>
        </div>
      </form>
    </section>

    @if (redefinindo() && usuario(); as alvo) {
      <app-redefinir-senha [usuario]="alvo" (fechado)="redefinindo.set(false)" />
    }
  `,
})
export class UsuarioFormPage implements OnInit {
  private readonly api = inject(UsuariosApi);
  private readonly router = inject(Router);

  /** ID público da rota `:id`; ausente em `/novo`. */
  readonly id = input<string>();
  readonly perfis = PERFIS_TENANT;

  readonly form = inject(FormBuilder).nonNullable.group({
    nome: ['', [Validators.required, Validators.maxLength(NOME_MAXIMO)]],
    email: ['', [Validators.required, Validators.email]],
    perfil: ['Consulta' as PerfilTenant, [Validators.required]],
    senhaInicial: ['', [validarSenha]],
    ativo: [true],
  });

  readonly edicao = signal(false);
  readonly usuario = signal<Usuario | null>(null);
  readonly carregando = signal(false);
  readonly enviando = signal(false);
  readonly tentouEnviar = signal(false);
  readonly conflitoVersao = signal(false);
  readonly redefinindo = signal(false);
  readonly erroGeral = signal<string | null>(null);
  private readonly errosApi = signal<Partial<Record<Campo, string>>>({});

  ngOnInit(): void {
    this.edicao.set(!!this.id());
    if (this.edicao()) {
      // E-mail não muda depois de criado; a senha só pela redefinição.
      this.form.controls.email.disable();
      this.form.controls.senhaInicial.disable();
      this.carregar();
    }
  }

  /** A API não tem GET por ID: lê a lista do tenant e escolhe o usuário. */
  carregar(): void {
    const id = this.id();
    if (!id) return;
    this.carregando.set(true);
    this.conflitoVersao.set(false);
    this.erroGeral.set(null);
    this.api
      .listar()
      .pipe(finalize(() => this.carregando.set(false)))
      .subscribe({
        next: (usuarios) => {
          const usuario = usuarios.find((u) => u.idPublico === id);
          if (!usuario) {
            this.erroGeral.set('Registro não encontrado');
            return;
          }
          this.usuario.set(usuario);
          this.form.patchValue({ nome: usuario.nome, email: usuario.email, perfil: usuario.perfil, ativo: usuario.ativo });
        },
        error: (erro: unknown) => this.erroGeral.set(traduzirProblema(erro).mensagem),
      });
  }

  erro(campo: Campo): string | null {
    const api = this.errosApi()[campo];
    if (api) return api;
    const controle = this.form.controls[campo];
    if (!this.tentouEnviar() || controle.disabled || controle.valid) return null;
    switch (campo) {
      case 'nome':
        return `O nome precisa ter entre 1 e ${NOME_MAXIMO} caracteres.`;
      case 'email':
        return 'E-mail inválido.';
      case 'perfil':
        return 'Use Admin ou Consulta.';
      case 'senhaInicial':
        return (controle.errors?.['politica'] as string[]).join(' ');
    }
  }

  salvar(): void {
    this.tentouEnviar.set(true);
    this.errosApi.set({});
    this.erroGeral.set(null);
    this.conflitoVersao.set(false);
    if (this.form.invalid || this.enviando()) return;
    if (this.edicao() && !this.usuario()) return;

    const valores = this.form.getRawValue();
    const nome = valores.nome.trim();
    const atual = this.usuario();
    const chamada: Observable<Usuario> = atual
      ? this.api.editar(atual.idPublico, { nome, perfil: valores.perfil, ativo: valores.ativo, versao: atual.versao })
      : this.api.criar({ nome, email: valores.email.trim(), senhaInicial: valores.senhaInicial, perfil: valores.perfil });

    this.enviando.set(true);
    chamada.pipe(finalize(() => this.enviando.set(false))).subscribe({
      next: () => void this.router.navigateByUrl(LISTA),
      error: (erro: unknown) => this.tratarErro(traduzirProblema(erro)),
    });
  }

  private tratarErro(problema: Problema): void {
    switch (problema.codigo) {
      case 'EMAIL_EM_USO':
        this.errosApi.set({ email: problema.mensagem });
        return;
      case 'CONFLITO_VERSAO':
        this.conflitoVersao.set(true);
        return;
      case 'VALIDACAO': {
        const campos: Partial<Record<Campo, string>> = {};
        for (const [chave, mensagens] of Object.entries(problema.erros)) {
          if (CAMPOS.includes(chave)) campos[chave as Campo] = mensagens.join(' ');
        }
        if (Object.keys(campos).length) {
          this.errosApi.set(campos);
          return;
        }
        break;
      }
    }
    // ULTIMO_ADMIN, PERFIL_NAO_PERMITIDO e o resto: mensagem geral traduzida pelo código.
    this.erroGeral.set(problema.mensagem);
  }
}
