import { TestRequest } from '@angular/common/http/testing';

/** Status HTTP → texto, só para as respostas falsas dos testes. */
const TEXTOS: Record<number, string> = {
  400: 'Bad Request',
  401: 'Unauthorized',
  403: 'Forbidden',
  404: 'Not Found',
  409: 'Conflict',
  422: 'Unprocessable Entity',
  423: 'Locked',
  429: 'Too Many Requests',
  500: 'Internal Server Error',
};

/** Responde a requisição de teste com um ProblemDetails da API (AD-011). */
export function responderProblema(
  requisicao: TestRequest,
  status: number,
  codigo: string,
  extras: Record<string, unknown> = {},
): void {
  requisicao.flush(
    { status, title: codigo, codigo, ...extras },
    {
      status,
      statusText: TEXTOS[status] ?? 'Erro',
      headers: { 'Content-Type': 'application/problem+json; charset=utf-8' },
    },
  );
}
