import { HttpClient } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { Observable } from 'rxjs';

/** Perfis que a gestão de usuários do tenant cria ou atribui (USR-02). */
export type PerfilTenant = 'Admin' | 'Consulta';

/** Usuário como sai da API (`UsuarioResposta`); `versao` é o ROWVERSION em Base64. */
export interface Usuario {
  idPublico: string;
  nome: string;
  email: string;
  perfil: PerfilTenant;
  ativo: boolean;
  trocarSenha: boolean;
  ultimoLoginEm: string | null;
  versao: string;
}

export interface CriarUsuarioRequisicao {
  nome: string;
  email: string;
  senhaInicial: string;
  perfil: PerfilTenant;
}

export interface EditarUsuarioRequisicao {
  nome: string;
  perfil: PerfilTenant;
  ativo: boolean;
  versao: string;
}

const BASE = '/api/usuarios';

/**
 * Chamadas de `/api/usuarios` (USR-01..USR-09): Admin do tenant ou SuperAdmin com tenant selecionado
 * (o `X-Tenant` vem do authInterceptor).
 */
@Injectable({ providedIn: 'root' })
export class UsuariosApi {
  private readonly http = inject(HttpClient);

  /** Lista simples do tenant, ordenada por nome. */
  listar(): Observable<Usuario[]> {
    return this.http.get<Usuario[]>(BASE);
  }

  criar(corpo: CriarUsuarioRequisicao): Observable<Usuario> {
    return this.http.post<Usuario>(BASE, corpo);
  }

  editar(idPublico: string, corpo: EditarUsuarioRequisicao): Observable<Usuario> {
    return this.http.put<Usuario>(`${BASE}/${idPublico}`, corpo);
  }

  /** 204 sem corpo: o usuário troca a senha no próximo login (USR-07). */
  redefinirSenha(idPublico: string, senhaTemporaria: string): Observable<void> {
    return this.http.post<void>(`${BASE}/${idPublico}/redefinir-senha`, { senhaTemporaria });
  }
}
