// Arquivos importados como texto ou bytes pelos testes de configuração (`with { loader: 'text' | 'binary' }`).
declare module '*.webmanifest' {
  const conteudo: string;
  export default conteudo;
}

declare module '*.png' {
  const conteudo: Uint8Array;
  export default conteudo;
}
