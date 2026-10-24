import { HttpErrorResponse } from '@angular/common/http';

/** Erro da API já traduzido para a interface (AD-011). */
export interface Problema {
  status: number;
  /** Código estável da API; `null` quando a resposta não era ProblemDetails. */
  codigo: string | null;
  mensagem: string;
  /** Mensagens por campo (só em `VALIDACAO`). */
  erros: Record<string, string[]>;
}

export const ERRO_INESPERADO = 'Erro inesperado';

/** Mensagens em pt-BR por `codigo` (tabela "Error Handling Strategy" do design + códigos extras da API). */
const MENSAGENS: Record<string, string> = {
  CREDENCIAIS_INVALIDAS: 'Credenciais inválidas',
  ACESSO_DESATIVADO: 'Acesso desativado',
  NAO_AUTENTICADO: 'Sessão expirada. Entre novamente.',
  ACESSO_NEGADO: 'Acesso não permitido',
  TROCA_SENHA_PENDENTE: 'Troque sua senha para continuar',
  NAO_ENCONTRADO: 'Registro não encontrado',
  TENANT_DIVERGENTE: 'Acesso não permitido',
  TENANT_NAO_SELECIONADO: 'Selecione um tenant',
  VALIDACAO: 'Dados inválidos',
  REQUISICAO_INVALIDA: 'Requisição inválida',
  CNPJ_DUPLICADO: 'CNPJ já cadastrado',
  EMAIL_EM_USO: 'E-mail já está em uso',
  EMPRESA_DUPLICADA: 'Já existe empresa com este código',
  CONFLITO_VERSAO: 'Registro alterado por outra pessoa. Recarregue.',
  ULTIMO_ADMIN: 'O tenant precisa de ao menos um Admin ativo',
  CONFIRMACAO_INVALIDA: 'O nome digitado não confere',
  EXCLUSAO_EM_ANDAMENTO: 'Exclusão já em andamento',
  EXCLUSAO_NAO_RETOMAVEL: 'A exclusão não pode ser retomada',
  PERFIL_NAO_PERMITIDO: 'Perfil não permitido',
  MUITAS_REQUISICOES: 'Aguarde um minuto',
};

interface CorpoProblema {
  codigo: string;
  title?: string;
  erros?: Record<string, string[]>;
  bloqueadoAte?: string;
  traceId?: string;
}

/** Converte qualquer erro de requisição em {@link Problema}. */
export function traduzirProblema(erro: unknown): Problema {
  const status = erro instanceof HttpErrorResponse ? erro.status : 0;
  const corpo = corpoProblema(erro);
  if (!corpo) return { status, codigo: null, mensagem: ERRO_INESPERADO, erros: {} };

  return {
    status,
    codigo: corpo.codigo,
    mensagem: mensagem(corpo),
    erros: corpo.codigo === 'VALIDACAO' ? (corpo.erros ?? {}) : {},
  };
}

function corpoProblema(erro: unknown): CorpoProblema | null {
  if (!(erro instanceof HttpErrorResponse)) return null;
  const tipo = erro.headers?.get('Content-Type') ?? '';
  const corpo = erro.error as Partial<CorpoProblema> | null;
  if (!tipo.includes('application/problem+json') || typeof corpo?.codigo !== 'string') return null;
  return corpo as CorpoProblema;
}

function mensagem(corpo: CorpoProblema): string {
  switch (corpo.codigo) {
    case 'USUARIO_BLOQUEADO':
      return corpo.bloqueadoAte
        ? `Muitas tentativas. Tente novamente às ${horario(corpo.bloqueadoAte)}`
        : 'Muitas tentativas. Tente novamente mais tarde';
    case 'ERRO_INTERNO':
      return corpo.traceId ? `${ERRO_INESPERADO} (código ${corpo.traceId})` : ERRO_INESPERADO;
    default:
      return MENSAGENS[corpo.codigo] ?? corpo.title ?? ERRO_INESPERADO;
  }
}

/** HH:mm no fuso local do navegador. */
function horario(iso: string): string {
  const data = new Date(iso);
  const dois = (n: number) => String(n).padStart(2, '0');
  return `${dois(data.getHours())}:${dois(data.getMinutes())}`;
}
