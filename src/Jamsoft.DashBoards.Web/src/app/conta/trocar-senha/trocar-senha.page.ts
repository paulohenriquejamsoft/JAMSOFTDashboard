import { ChangeDetectionStrategy, Component, computed, inject, signal } from '@angular/core';
import { toSignal } from '@angular/core/rxjs-interop';
import { FormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';
import { Router } from '@angular/router';
import { finalize } from 'rxjs';
import { AuthService } from '../../core/auth/auth.service';
import { homeDoPerfil } from '../../core/auth/home';
import { SENHA_TAMANHO_MINIMO, violacoesPoliticaSenha } from '../../core/auth/politica-senha';
import { traduzirProblema } from '../../core/erros/problema';

/**
 * `/conta/trocar-senha` (USR-08): senha atual, nova e confirmação. A política é validada antes do envio;
 * sucesso abre a sessão nova e leva à home do perfil.
 */
@Component({
  selector: 'app-trocar-senha',
  imports: [ReactiveFormsModule],
  changeDetection: ChangeDetectionStrategy.OnPush,
  styleUrl: '../conta.scss',
  template: `
    <main class="cartao">
      <img class="logo" src="marca/logo-vermelha.webp" alt="JAMSOFT Sistemas" />
      <h1>Trocar senha</h1>
      <p class="subtitulo">Defina uma nova senha para continuar.</p>

      <form [formGroup]="form" (ngSubmit)="trocar()" novalidate>
        <div class="campo">
          <label for="senha-atual">Senha atual <span class="obrigatorio" aria-hidden="true">*</span></label>
          <input
            id="senha-atual"
            type="password"
            formControlName="senhaAtual"
            autocomplete="current-password"
            [attr.aria-invalid]="!!erroAtual()"
            aria-describedby="senha-atual-erro"
          />
          @if (erroAtual(); as mensagem) {
            <p id="senha-atual-erro" class="campo-erro">{{ mensagem }}</p>
          }
        </div>

        <div class="campo">
          <label for="senha-nova">Nova senha <span class="obrigatorio" aria-hidden="true">*</span></label>
          <input
            id="senha-nova"
            type="password"
            formControlName="novaSenha"
            autocomplete="new-password"
            [attr.aria-invalid]="tentouEnviar() && violacoes().length > 0"
            aria-describedby="senha-nova-regras"
          />
          @if (tentouEnviar() && violacoes().length > 0) {
            <ul id="senha-nova-regras" class="campo-erro">
              @for (regra of violacoes(); track regra) {
                <li>{{ regra }}</li>
              }
            </ul>
          } @else {
            <p id="senha-nova-regras" class="campo-dica">
              Ao menos {{ minimo }} caracteres, com letra e dígito.
            </p>
          }
        </div>

        <div class="campo">
          <label for="senha-confirmacao">Confirme a nova senha <span class="obrigatorio" aria-hidden="true">*</span></label>
          <input
            id="senha-confirmacao"
            type="password"
            formControlName="confirmacao"
            autocomplete="new-password"
            [attr.aria-invalid]="tentouEnviar() && confirmacaoDiferente()"
            aria-describedby="senha-confirmacao-erro"
          />
          @if (tentouEnviar() && confirmacaoDiferente()) {
            <p id="senha-confirmacao-erro" class="campo-erro">A confirmação não confere com a nova senha.</p>
          }
        </div>

        @if (erro(); as mensagem) {
          <p class="alerta alerta-erro" role="alert">{{ mensagem }}</p>
        }

        <button type="submit" class="btn btn-primario btn-bloco" [disabled]="enviando()">
          {{ enviando() ? 'Salvando...' : 'Trocar senha' }}
        </button>
      </form>
    </main>
  `,
})
export class TrocarSenhaPage {
  private readonly auth = inject(AuthService);
  private readonly router = inject(Router);

  readonly minimo = SENHA_TAMANHO_MINIMO;
  readonly form = inject(FormBuilder).nonNullable.group({
    senhaAtual: ['', [Validators.required]],
    novaSenha: [''],
    confirmacao: [''],
  });
  private readonly valores = toSignal(this.form.valueChanges, { initialValue: this.form.getRawValue() });

  readonly violacoes = computed(() => violacoesPoliticaSenha(this.valores().novaSenha ?? ''));
  readonly confirmacaoDiferente = computed(() => this.valores().confirmacao !== this.valores().novaSenha);
  readonly tentouEnviar = signal(false);
  readonly enviando = signal(false);
  readonly erro = signal<string | null>(null);
  /** Erro da senha atual vindo da API (VALIDACAO em `senhaAtual`). */
  private readonly erroAtualApi = signal<string | null>(null);
  readonly erroAtual = computed(
    () =>
      this.erroAtualApi() ??
      (this.tentouEnviar() && !this.valores().senhaAtual ? 'Informe a senha atual.' : null),
  );

  trocar(): void {
    this.tentouEnviar.set(true);
    this.erroAtualApi.set(null);
    this.erro.set(null);
    if (this.form.invalid || this.violacoes().length > 0 || this.confirmacaoDiferente() || this.enviando()) return;

    const { senhaAtual, novaSenha } = this.form.getRawValue();
    this.enviando.set(true);
    this.auth
      .trocarSenha(senhaAtual, novaSenha)
      .pipe(finalize(() => this.enviando.set(false)))
      .subscribe({
        next: (usuario) => void this.router.navigateByUrl(homeDoPerfil(usuario.perfil)),
        error: (erro: unknown) => {
          const problema = traduzirProblema(erro);
          const atual = problema.erros['senhaAtual']?.[0];
          if (atual) this.erroAtualApi.set(atual);
          else this.erro.set(problema.erros['novaSenha']?.join(' ') ?? problema.mensagem);
        },
      });
  }
}
