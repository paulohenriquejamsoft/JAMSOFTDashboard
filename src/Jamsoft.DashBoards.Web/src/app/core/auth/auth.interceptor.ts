import {
  HttpErrorResponse,
  HttpEvent,
  HttpInterceptorFn,
  HttpRequest,
} from '@angular/common/http';
import { inject } from '@angular/core';
import { Router } from '@angular/router';
import { Observable, catchError, switchMap, throwError } from 'rxjs';
import { AuthService } from './auth.service';
import { TenantContextoService } from './tenant-contexto.service';

const ROTAS_AUTH = '/api/auth/';
export const AVISO_ACESSO_DESATIVADO = 'Acesso desativado';

/**
 * Põe `Authorization` e, para o SuperAdmin com tenant selecionado, `X-Tenant` (CTX-01) nas chamadas à API.
 * Em 401 renova a sessão uma vez e repete a requisição; se não der, encerra a sessão e leva ao login
 * (AUTH-04). Em 403 `TROCA_SENHA_PENDENTE` leva à troca de senha (USR-08). Em 403 `ACESSO_DESATIVADO`
 * (usuário ou tenant inativado durante a sessão) encerra a sessão e leva ao login com o aviso.
 */
export const authInterceptor: HttpInterceptorFn = (requisicao, next) => {
  if (!requisicao.url.startsWith('/api/')) return next(requisicao);

  const auth = inject(AuthService);
  const contexto = inject(TenantContextoService);
  const router = inject(Router);
  const rotaAuth = requisicao.url.startsWith(ROTAS_AUTH);

  const enviar = (): Observable<HttpEvent<unknown>> =>
    next(comCabecalhos(requisicao, auth, contexto, rotaAuth));

  const irParaLogin = (erro: unknown, aviso?: string): Observable<never> => {
    auth.logout().subscribe();
    void (aviso
      ? router.navigateByUrl('/conta/login', { state: { aviso } })
      : router.navigateByUrl('/conta/login'));
    return throwError(() => erro);
  };

  return enviar().pipe(
    catchError((erro: unknown) => {
      if (!(erro instanceof HttpErrorResponse)) return throwError(() => erro);

      if (erro.status === 403 && codigo(erro) === 'TROCA_SENHA_PENDENTE') {
        void router.navigateByUrl('/conta/trocar-senha');
        return throwError(() => erro);
      }

      if (erro.status === 403 && codigo(erro) === 'ACESSO_DESATIVADO' && !rotaAuth) {
        return irParaLogin(erro, AVISO_ACESSO_DESATIVADO);
      }

      if (erro.status !== 401 || rotaAuth) return throwError(() => erro);

      return auth.renovar().pipe(
        catchError((falha: unknown) =>
          falha instanceof HttpErrorResponse && codigo(falha) === 'ACESSO_DESATIVADO'
            ? irParaLogin(falha, AVISO_ACESSO_DESATIVADO)
            : irParaLogin(erro),
        ),
        switchMap(() =>
          enviar().pipe(
            catchError((repetida: unknown) =>
              repetida instanceof HttpErrorResponse && repetida.status === 401
                ? irParaLogin(repetida)
                : throwError(() => repetida),
            ),
          ),
        ),
      );
    }),
  );
};

function comCabecalhos(
  requisicao: HttpRequest<unknown>,
  auth: AuthService,
  contexto: TenantContextoService,
  rotaAuth: boolean,
): HttpRequest<unknown> {
  const cabecalhos: Record<string, string> = {};
  const token = auth.accessToken;
  if (token) cabecalhos['Authorization'] = `Bearer ${token}`;

  // Admin e Consulta usam o tenant do token; a seleção só vale para o SuperAdmin.
  const tenant = contexto.selecionado();
  if (!rotaAuth && tenant && auth.usuario()?.perfil === 'SuperAdmin') {
    cabecalhos['X-Tenant'] = tenant.idPublico;
  }
  return requisicao.clone({ setHeaders: cabecalhos });
}

function codigo(erro: HttpErrorResponse): string | undefined {
  const corpo = erro.error as { codigo?: unknown } | null;
  return typeof corpo?.codigo === 'string' ? corpo.codigo : undefined;
}
