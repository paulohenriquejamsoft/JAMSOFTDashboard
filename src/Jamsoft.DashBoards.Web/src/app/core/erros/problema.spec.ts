import { HttpErrorResponse, HttpHeaders } from '@angular/common/http';
import { traduzirProblema } from './problema';

function problema(status: number, corpo: Record<string, unknown>): HttpErrorResponse {
  return new HttpErrorResponse({
    status,
    error: corpo,
    headers: new HttpHeaders({ 'Content-Type': 'application/problem+json; charset=utf-8' }),
  });
}

describe('traduzirProblema', () => {
  // Tabela "Error Handling Strategy" do design + códigos que a API já usa.
  it.each([
    [401, 'CREDENCIAIS_INVALIDAS', 'Credenciais inválidas'],
    [403, 'ACESSO_DESATIVADO', 'Acesso desativado'],
    [401, 'NAO_AUTENTICADO', 'Sessão expirada. Entre novamente.'],
    [403, 'ACESSO_NEGADO', 'Acesso não permitido'],
    [403, 'TROCA_SENHA_PENDENTE', 'Troque sua senha para continuar'],
    [404, 'NAO_ENCONTRADO', 'Registro não encontrado'],
    [403, 'TENANT_DIVERGENTE', 'Acesso não permitido'],
    [400, 'TENANT_NAO_SELECIONADO', 'Selecione um tenant'],
    [409, 'CNPJ_DUPLICADO', 'CNPJ já cadastrado'],
    [409, 'EMAIL_EM_USO', 'E-mail já está em uso'],
    [409, 'EMPRESA_DUPLICADA', 'Já existe empresa com este código'],
    [409, 'CONFLITO_VERSAO', 'Registro alterado por outra pessoa. Recarregue.'],
    [422, 'ULTIMO_ADMIN', 'O tenant precisa de ao menos um Admin ativo'],
    [422, 'CONFIRMACAO_INVALIDA', 'O nome digitado não confere'],
    [409, 'EXCLUSAO_EM_ANDAMENTO', 'Exclusão já em andamento'],
    [429, 'MUITAS_REQUISICOES', 'Aguarde um minuto'],
    [400, 'REQUISICAO_INVALIDA', 'Requisição inválida'],
    [409, 'EXCLUSAO_NAO_RETOMAVEL', 'A exclusão não pode ser retomada'],
    [403, 'PERFIL_NAO_PERMITIDO', 'Perfil não permitido'],
  ])('%i %s gera a mensagem "%s"', (status, codigo, esperado) => {
    const resultado = traduzirProblema(problema(status, { codigo, title: 'título da API' }));

    expect(resultado).toEqual({ status, codigo, mensagem: esperado, erros: {} });
  });

  it('USUARIO_BLOQUEADO formata o horário de desbloqueio em HH:mm', () => {
    const bloqueadoAte = new Date(2026, 9, 24, 14, 5).toISOString();

    const resultado = traduzirProblema(problema(423, { codigo: 'USUARIO_BLOQUEADO', bloqueadoAte }));

    expect(resultado.mensagem).toBe('Muitas tentativas. Tente novamente às 14:05');
  });

  it('VALIDACAO expõe os erros por campo', () => {
    const erros = { email: ['E-mail inválido'], nome: ['Obrigatório', 'Máx. 120 caracteres'] };

    const resultado = traduzirProblema(problema(400, { codigo: 'VALIDACAO', erros }));

    expect(resultado).toEqual({ status: 400, codigo: 'VALIDACAO', mensagem: 'Dados inválidos', erros });
  });

  it('ERRO_INTERNO mostra "Erro inesperado" com o traceId', () => {
    const resultado = traduzirProblema(problema(500, { codigo: 'ERRO_INTERNO', traceId: '00-abc-01' }));

    expect(resultado.mensagem).toBe('Erro inesperado (código 00-abc-01)');
  });

  it('erro sem ProblemDetails vira "Erro inesperado"', () => {
    const resposta = new HttpErrorResponse({
      status: 502,
      error: '<html>Bad Gateway</html>',
      headers: new HttpHeaders({ 'Content-Type': 'text/html' }),
    });

    expect(traduzirProblema(resposta)).toEqual({
      status: 502,
      codigo: null,
      mensagem: 'Erro inesperado',
      erros: {},
    });
  });

  it('falha de rede (status 0) vira "Erro inesperado"', () => {
    const resposta = new HttpErrorResponse({ status: 0, error: new ProgressEvent('error') });

    expect(traduzirProblema(resposta)).toEqual({
      status: 0,
      codigo: null,
      mensagem: 'Erro inesperado',
      erros: {},
    });
  });
});
