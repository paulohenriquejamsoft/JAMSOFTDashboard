import { Routes } from '@angular/router';
import { ADMIN_TENANT, perfilGuard } from '../core/auth/perfil.guard';
import { ShellComponent } from '../shared/layout/shell.component';

/** Área do tenant (Admin, Consulta ou SuperAdmin com tenant selecionado), dentro do layout logado. */
export default [
  {
    path: '',
    component: ShellComponent,
    children: [
      { path: '', loadComponent: () => import('./painel/painel.page').then((m) => m.PainelPage) },
      {
        path: 'usuarios',
        canMatch: [perfilGuard(ADMIN_TENANT)],
        loadComponent: () => import('./usuarios/usuarios-lista.page').then((m) => m.UsuariosListaPage),
      },
      {
        path: 'usuarios/novo',
        canMatch: [perfilGuard(ADMIN_TENANT)],
        loadComponent: () => import('./usuarios/usuario-form.page').then((m) => m.UsuarioFormPage),
      },
      {
        path: 'usuarios/:id',
        canMatch: [perfilGuard(ADMIN_TENANT)],
        loadComponent: () => import('./usuarios/usuario-form.page').then((m) => m.UsuarioFormPage),
      },
      {
        path: 'empresas',
        canMatch: [perfilGuard(ADMIN_TENANT)],
        loadComponent: () => import('./empresas/empresas.page').then((m) => m.EmpresasPage),
      },
      {
        path: 'sincronizacao',
        canMatch: [perfilGuard(ADMIN_TENANT)],
        loadComponent: () => import('./sincronizacao/sincronizacao.page').then((m) => m.SincronizacaoPage),
      },
      {
        path: 'chaves-mcp',
        canMatch: [perfilGuard(ADMIN_TENANT)],
        loadComponent: () => import('./chaves-mcp/chaves-mcp.page').then((m) => m.ChavesMcpPage),
      },
    ],
  },
] satisfies Routes;
