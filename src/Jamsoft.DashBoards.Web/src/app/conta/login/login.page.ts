import { ChangeDetectionStrategy, Component, inject, signal } from '@angular/core';
import { FormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';
import { Router } from '@angular/router';
import { finalize } from 'rxjs';
import { AuthService } from '../../core/auth/auth.service';
import { homeDoPerfil } from '../../core/auth/home';
import { traduzirProblema } from '../../core/erros/problema';

/**
 * `/conta/login` (AUTH-01, AUTH-02, AUTH-03, AUTH-07): e-mail e senha; erros da API pelo `codigo`;
 * sucesso leva à troca de senha pendente ou à home do perfil.
 */
@Component({
  selector: 'app-login',
  imports: [ReactiveFormsModule],
  changeDetection: ChangeDetectionStrategy.OnPush,
  styleUrl: '../conta.scss',
  template: `
    <main class="cartao">
      <img class="logo" src="marca/logo-vermelha.webp" alt="JAMSOFT Sistemas" />
      <h1>Entrar</h1>
      <p class="subtitulo">Acesse o painel com seu e-mail e senha.</p>

      <form [formGroup]="form" (ngSubmit)="entrar()" novalidate>
        <div class="campo">
          <label for="login-email">E-mail <span class="obrigatorio" aria-hidden="true">*</span></label>
          <input
            id="login-email"
            type="email"
            formControlName="email"
            autocomplete="username"
            [attr.aria-invalid]="invalido('email')"
            aria-describedby="login-email-erro"
          />
          @if (invalido('email')) {
            <p id="login-email-erro" class="campo-erro">Informe o e-mail.</p>
          }
        </div>

        <div class="campo">
          <label for="login-senha">Senha <span class="obrigatorio" aria-hidden="true">*</span></label>
          <input
            id="login-senha"
            type="password"
            formControlName="senha"
            autocomplete="current-password"
            [attr.aria-invalid]="invalido('senha')"
            aria-describedby="login-senha-erro"
          />
          @if (invalido('senha')) {
            <p id="login-senha-erro" class="campo-erro">Informe a senha.</p>
          }
        </div>

        @if (erro(); as mensagem) {
          <p class="alerta alerta-erro" role="alert">{{ mensagem }}</p>
        }

        <button type="submit" class="btn btn-primario btn-bloco" [disabled]="enviando()">
          {{ enviando() ? 'Entrando...' : 'Entrar' }}
        </button>
      </form>
    </main>
  `,
})
export class LoginPage {
  private readonly auth = inject(AuthService);
  private readonly router = inject(Router);

  readonly form = inject(FormBuilder).nonNullable.group({
    email: ['', [Validators.required]],
    senha: ['', [Validators.required]],
  });
  readonly enviando = signal(false);
  readonly erro = signal<string | null>(null);
  private readonly tentouEnviar = signal(false);

  invalido(campo: 'email' | 'senha'): boolean {
    const controle = this.form.controls[campo];
    return controle.invalid && (controle.touched || this.tentouEnviar());
  }

  entrar(): void {
    this.tentouEnviar.set(true);
    if (this.form.invalid || this.enviando()) return;

    const { email, senha } = this.form.getRawValue();
    this.erro.set(null);
    this.enviando.set(true);
    this.auth
      .login(email.trim(), senha)
      .pipe(finalize(() => this.enviando.set(false)))
      .subscribe({
        next: (usuario) =>
          void this.router.navigateByUrl(
            usuario.trocarSenha ? '/conta/trocar-senha' : homeDoPerfil(usuario.perfil),
          ),
        error: (erro: unknown) => this.erro.set(traduzirProblema(erro).mensagem),
      });
  }
}
