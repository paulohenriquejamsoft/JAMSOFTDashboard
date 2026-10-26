import { HttpInterceptorFn } from '@angular/common/http';
import { environment } from '../../environments/environment';

/**
 * Prefixa as chamadas relativas `/api/...` com `environment.apiUrl`. Fica por último na cadeia para que os
 * demais interceptors continuem reconhecendo as rotas da API pelo caminho relativo.
 */
export const apiUrlInterceptor: HttpInterceptorFn = (requisicao, next) =>
  next(
    environment.apiUrl && requisicao.url.startsWith('/api/')
      ? requisicao.clone({ url: environment.apiUrl + requisicao.url })
      : requisicao,
  );
