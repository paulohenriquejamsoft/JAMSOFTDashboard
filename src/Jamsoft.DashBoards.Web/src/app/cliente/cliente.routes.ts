import { Component } from '@angular/core';
import { Routes } from '@angular/router';
import { ADMIN_TENANT, perfilGuard } from '../core/auth/perfil.guard';
import { ShellComponent } from '../shared/layout/shell.component';

/** Placeholder até a tela de empresas (T57). */
@Component({ template: '<p>Empresas</p>' })
class EmpresasPlaceholder {}

/** Área do tenant (Admin, Consulta ou SuperAdmin com tenant selecionado), dentro do layout logado. */
export default [
  {
    path: '',
    component: ShellComponent,
    children: [
      { path: '', loadComponent: () => import('./cliente-inicio.page').then((m) => m.ClienteInicioPage) },
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
      { path: 'empresas', canMatch: [perfilGuard(ADMIN_TENANT)], component: EmpresasPlaceholder },
    ],
  },
] satisfies Routes;
