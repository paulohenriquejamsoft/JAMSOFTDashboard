/**
 * Ambiente de desenvolvimento. `apiUrl` vazio mantém as chamadas relativas (`/api/...`), que o `ng serve`
 * encaminha para a API em http://localhost:5170 (`proxy.conf.json`) sem precisar de CORS. `mcpUrl` é o endpoint do
 * servidor MCP (Jamsoft.Sge.Mcp) que o Admin informa no conector do Claude.
 */
export const environment = {
  production: false,
  apiUrl: '',
  mcpUrl: 'http://localhost:52859/mcp',
};
