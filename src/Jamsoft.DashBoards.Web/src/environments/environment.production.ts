/** Ambiente de produção: o frontend é servido em outra origem e chama a API pela URL completa (CORS). */
export const environment = {
  production: true,
  apiUrl: 'https://sistemas.ilojajamsoft.com.br/JamsoftDashboardApi',
  /** Endpoint `/mcp` do Jamsoft.Sge.Mcp, informado no conector do Claude. */
  mcpUrl: 'https://sistemas.ilojajamsoft.com.br/JamsoftDashboardMcp/mcp',
};
