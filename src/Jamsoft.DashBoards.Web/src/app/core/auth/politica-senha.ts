export const SENHA_TAMANHO_MINIMO = 10;

/** Política de senha da plataforma (mesmas regras e mensagens da API): 10+ caracteres, letra e dígito. */
export function violacoesPoliticaSenha(senha: string): string[] {
  const erros: string[] = [];
  if (senha.length < SENHA_TAMANHO_MINIMO)
    erros.push(`A senha precisa ter ao menos ${SENHA_TAMANHO_MINIMO} caracteres.`);
  if (!/\p{L}/u.test(senha)) erros.push('A senha precisa ter ao menos uma letra.');
  if (!/\p{Nd}/u.test(senha)) erros.push('A senha precisa ter ao menos um dígito.');
  return erros;
}
