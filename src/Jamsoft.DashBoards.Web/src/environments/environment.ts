/**
 * Ambiente de desenvolvimento. `apiUrl` vazio mantém as chamadas relativas (`/api/...`), que o `ng serve`
 * encaminha para a API em http://localhost:5170 (`proxy.conf.json`) sem precisar de CORS.
 */
export const environment = {
  production: false,
  apiUrl: '',
};
