import { HttpClient, HttpHeaders } from '@angular/common/http';
import { Injectable, computed, inject, signal } from '@angular/core';
import { TenantContextoService } from './tenant-contexto.service';
import { Observable, catchError, finalize, firstValueFrom, map, of, share, tap } from 'rxjs';

export type Perfil = 'SuperAdmin' | 'Admin' | 'Consulta';

export interface TenantResumo {
  idPublico: string;
  nome: string;
}

/** Dados da sessão aberta, sem o token. */
export interface UsuarioSessao {
  perfil: Perfil;
  tenant: TenantResumo | null;
  trocarSenha: boolean;
}

/** Resposta de `POST /api/auth/login` e `/refresh` (AUTH-01). */
interface SessaoResposta extends UsuarioSessao {
  accessToken: string;
  expiraEm: string;
}

/** Prefixo de toda chave que a aplicação grava em `sessionStorage`/`localStorage`. */
export const PREFIXO_ARMAZENAMENTO = 'jamsoft.';

/** Cabeçalho exigido pelo `/api/auth/refresh` (proteção contra CSRF). */
export const CABECALHO_CSRF = { 'X-Requested-With': 'jamsoft' };

/**
 * Sessão do usuário (AUTH-01, AUTH-04, AUTH-06). O access token fica só em memória; o refresh token
 * fica no cookie HttpOnly que a API grava (AD-010).
 */
@Injectable({ providedIn: 'root' })
export class AuthService {
  private readonly http = inject(HttpClient);
  private readonly tenantContexto = inject(TenantContextoService);
  private readonly sessao = signal<UsuarioSessao | null>(null);
  private token: string | null = null;
  private renovacao: Observable<UsuarioSessao> | null = null;

  readonly usuario = this.sessao.asReadonly();
  readonly autenticado = computed(() => this.sessao() !== null);

  get accessToken(): string | null {
    return this.token;
  }

  login(email: string, senha: string): Observable<UsuarioSessao> {
    return this.http
      .post<SessaoResposta>('/api/auth/login', { email, senha }, { withCredentials: true })
      .pipe(map((resposta) => this.abrir(resposta)));
  }

  /** Troca a senha (USR-08); a API devolve um access token novo, já sem a troca pendente. */
  trocarSenha(senhaAtual: string, novaSenha: string): Observable<UsuarioSessao> {
    return this.http
      .post<SessaoResposta>('/api/auth/trocar-senha', { senhaAtual, novaSenha })
      .pipe(map((resposta) => this.abrir(resposta)));
  }

  /**
   * Revoga o refresh token na API e descarta a sessão local, mesmo se a chamada falhar: token, tenant selecionado e
   * qualquer chave da aplicação no armazenamento do navegador (PWA-07).
   */
  logout(): Observable<void> {
    return this.http.post<void>('/api/auth/logout', null, { withCredentials: true }).pipe(
      catchError(() => of(undefined)),
      map(() => undefined),
      finalize(() => {
        this.encerrar();
        this.tenantContexto.limpar();
        limparArmazenamentoDaAplicacao();
      }),
    );
  }

  /** Troca o refresh token por um novo par. Chamadas simultâneas compartilham uma única requisição. */
  renovar(): Observable<UsuarioSessao> {
    this.renovacao ??= this.http
      .post<SessaoResposta>('/api/auth/refresh', null, {
        withCredentials: true,
        headers: new HttpHeaders(CABECALHO_CSRF),
      })
      .pipe(
        map((resposta) => this.abrir(resposta)),
        tap({ error: () => this.encerrar() }),
        finalize(() => (this.renovacao = null)),
        share(),
      );
    return this.renovacao;
  }

  /** No carregamento da app: restaura a sessão a partir do cookie; sem cookie válido, segue anônimo. */
  restaurarSessao(): Promise<void> {
    return firstValueFrom(
      this.renovar().pipe(
        map(() => undefined),
        catchError(() => of(undefined)),
      ),
    );
  }

  private abrir(resposta: SessaoResposta): UsuarioSessao {
    this.token = resposta.accessToken;
    const usuario: UsuarioSessao = {
      perfil: resposta.perfil,
      tenant: resposta.tenant,
      trocarSenha: resposta.trocarSenha,
    };
    this.sessao.set(usuario);
    return usuario;
  }

  private encerrar(): void {
    this.token = null;
    this.sessao.set(null);
  }
}

/** Remove de `sessionStorage` e `localStorage` só as chaves da aplicação; as de outros apps ficam. */
function limparArmazenamentoDaAplicacao(): void {
  for (const nome of ['sessionStorage', 'localStorage'] as const) {
    try {
      const storage = globalThis[nome];
      const chaves = Array.from({ length: storage.length }, (_, i) => storage.key(i));
      for (const chave of chaves) if (chave?.startsWith(PREFIXO_ARMAZENAMENTO)) storage.removeItem(chave);
    } catch {
      // Storage indisponível (modo privado, bloqueio): não há o que limpar.
    }
  }
}
