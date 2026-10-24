import { Component } from '@angular/core';
import { Routes } from '@angular/router';
import { ADMIN_TENANT, perfilGuard } from '../core/auth/perfil.guard';

/** Placeholders da área até as telas da fase 10. */
@Component({ template: '<p>Cliente</p>' })
class ClientePlaceholder {}

@Component({ template: '<p>Usuários</p>' })
class UsuariosPlaceholder {}

@Component({ template: '<p>Empresas</p>' })
class EmpresasPlaceholder {}

export default [
  { path: '', component: ClientePlaceholder },
  { path: 'usuarios', canMatch: [perfilGuard(ADMIN_TENANT)], component: UsuariosPlaceholder },
  { path: 'empresas', canMatch: [perfilGuard(ADMIN_TENANT)], component: EmpresasPlaceholder },
] satisfies Routes;
