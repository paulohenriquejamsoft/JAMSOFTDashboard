using System.Net;
using System.Text.Json;
using Jamsoft.DashBoards.Api.Modulos.Tenants;
using Jamsoft.DashBoards.Api.Seguranca;
using Jamsoft.DashBoards.Api.Testes.Infraestrutura;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;

namespace Jamsoft.DashBoards.Api.Testes.Integracao.Tenants;

[Collection(ColecaoIntegracao.Nome)]
[Trait("Categoria", "Integracao")]
public sealed class TenantsEndpointsTestes(BancoDeTestes banco) : IAsyncLifetime
{
    private const string Rota = "/api/admin/tenants";

    private ApiFactory _api = null!;
    private HttpClient _cliente = null!;
    private string _token = "";

    public async Task InitializeAsync()
    {
        await banco.MigrarAsync();
        _api = new ApiFactory(banco);
        _cliente = _api.CreateClient(new WebApplicationFactoryClientOptions { HandleCookies = false });
        _token = await Acesso.TokenSuperAdminAsync(banco);
    }

    public async Task DisposeAsync()
    {
        _cliente.Dispose();
        await _api.DisposeAsync();
    }

    /// <summary>Grava <paramref name="quantidade"/> tenants cujo nome contém um marcador único e devolve o marcador.</summary>
    private async Task<(string Marcador, List<Tenant> Tenants)> SemearAsync(int quantidade, bool ativo = true)
    {
        var marcador = Guid.NewGuid().ToString("N")[..12];
        var tenants = Enumerable.Range(1, quantidade)
            .Select(i => new Tenant { Nome = $"Loja {marcador} {i:000}", Cnpj = Dados.CnpjValido(), Ativo = ativo })
            .ToList();
        await using var contexto = banco.CriarContexto();
        contexto.Tenants.AddRange(tenants);
        await contexto.SaveChangesAsync();
        return (marcador, tenants);
    }

    private async Task<JsonElement> ListarOkAsync(string consulta)
    {
        using var resposta = await _cliente.SendAsync(Acesso.Requisicao(HttpMethod.Get, $"{Rota}?{consulta}", _token));
        Assert.Equal(HttpStatusCode.OK, resposta.StatusCode);
        return await Sessao.JsonAsync(resposta);
    }

    private static List<string> Nomes(JsonElement pagina) =>
        [.. pagina.GetProperty("itens").EnumerateArray().Select(i => i.GetProperty("nome").GetString()!)];

    [Fact]
    public async Task Lista_pagina_com_20_itens_por_padrao()
    {
        var (marcador, _) = await SemearAsync(25);

        var primeira = await ListarOkAsync($"busca={marcador}");
        var segunda = await ListarOkAsync($"busca={marcador}&pagina=2");

        Assert.Equal(20, primeira.GetProperty("itens").GetArrayLength());
        Assert.Equal(1, primeira.GetProperty("pagina").GetInt32());
        Assert.Equal(20, primeira.GetProperty("tamanho").GetInt32());
        Assert.Equal(25, primeira.GetProperty("total").GetInt32());
        Assert.Equal($"Loja {marcador} 001", Nomes(primeira)[0]);
        Assert.Equal([.. Enumerable.Range(21, 5).Select(i => $"Loja {marcador} {i:000}")], Nomes(segunda));
        var item = primeira.GetProperty("itens")[0];
        Assert.False(item.TryGetProperty("id", out _));
        Assert.NotEqual(Guid.Empty, item.GetProperty("idPublico").GetGuid());
    }

    [Fact]
    public async Task Tamanho_500_e_limitado_a_100()
    {
        var (marcador, _) = await SemearAsync(105);

        var pagina = await ListarOkAsync($"busca={marcador}&tamanho=500");

        Assert.Equal(100, pagina.GetProperty("itens").GetArrayLength());
        Assert.Equal(100, pagina.GetProperty("tamanho").GetInt32());
        Assert.Equal(105, pagina.GetProperty("total").GetInt32());
    }

    [Fact]
    public async Task Busca_filtra_por_parte_do_nome_e_por_CNPJ_com_ou_sem_mascara()
    {
        var (marcador, tenants) = await SemearAsync(3);
        var alvo = tenants[1];
        var cnpj = alvo.Cnpj;

        var porNome = await ListarOkAsync($"busca={Uri.EscapeDataString($"{marcador} 002")}");
        var porCnpj = await ListarOkAsync($"busca={cnpj}");
        var porCnpjMascarado = await ListarOkAsync(
            $"busca={Uri.EscapeDataString($"{cnpj[..2]}.{cnpj[2..5]}.{cnpj[5..8]}/{cnpj[8..12]}-{cnpj[12..]}")}");

        Assert.Equal([alvo.Nome], Nomes(porNome));
        Assert.Equal([alvo.Nome], Nomes(porCnpj));
        Assert.Equal([alvo.Nome], Nomes(porCnpjMascarado));
    }

    [Fact]
    public async Task Situacao_filtra_ativos_e_inativos()
    {
        var marcador = Guid.NewGuid().ToString("N")[..12];
        await using (var contexto = banco.CriarContexto())
        {
            contexto.Tenants.AddRange(
                new Tenant { Nome = $"Ativo {marcador}", Cnpj = Dados.CnpjValido(), Ativo = true },
                new Tenant { Nome = $"Inativo {marcador}", Cnpj = Dados.CnpjValido(), Ativo = false });
            await contexto.SaveChangesAsync();
        }

        var inativos = await ListarOkAsync($"busca={marcador}&situacao=Inativo");
        var ativos = await ListarOkAsync($"busca={marcador}&situacao=Ativo");
        var todos = await ListarOkAsync($"busca={marcador}");

        Assert.Equal([$"Inativo {marcador}"], Nomes(inativos));
        Assert.False(inativos.GetProperty("itens")[0].GetProperty("ativo").GetBoolean());
        Assert.Equal([$"Ativo {marcador}"], Nomes(ativos));
        Assert.Equal(2, todos.GetProperty("total").GetInt32());
    }

    [Fact]
    public async Task Lista_vazia_responde_200_com_itens_vazios()
    {
        var pagina = await ListarOkAsync($"busca={Guid.NewGuid():N}");

        Assert.Equal(JsonValueKind.Array, pagina.GetProperty("itens").ValueKind);
        Assert.Equal(0, pagina.GetProperty("itens").GetArrayLength());
        Assert.Equal(0, pagina.GetProperty("total").GetInt32());
    }

    [Fact]
    public async Task GUID_inexistente_responde_404_NAO_ENCONTRADO_na_leitura_e_na_edicao()
    {
        var rota = $"{Rota}/{Guid.NewGuid()}";

        using var leitura = await _cliente.SendAsync(Acesso.Requisicao(HttpMethod.Get, rota, _token));
        using var edicao = await _cliente.SendAsync(Acesso.ComCorpo(HttpMethod.Put, rota, _token,
            new { nome = "Qualquer nome", cnpj = Dados.CnpjValido(), versao = Convert.ToBase64String(new byte[8]) }));

        Assert.Equal(HttpStatusCode.NotFound, leitura.StatusCode);
        Assert.Equal("NAO_ENCONTRADO", await Sessao.CodigoAsync(leitura));
        Assert.Equal(HttpStatusCode.NotFound, edicao.StatusCode);
        Assert.Equal("NAO_ENCONTRADO", await Sessao.CodigoAsync(edicao));
    }

    [Fact]
    public async Task GUID_mal_formatado_responde_400()
    {
        using var resposta = await _cliente.SendAsync(Acesso.Requisicao(HttpMethod.Get, $"{Rota}/nao-e-um-guid", _token));

        Assert.Equal(HttpStatusCode.BadRequest, resposta.StatusCode);
        Assert.Equal("REQUISICAO_INVALIDA", await Sessao.CodigoAsync(resposta));
    }

    [Fact]
    public async Task Edicao_com_versao_atual_responde_200_e_com_versao_antiga_responde_409_CONFLITO_VERSAO()
    {
        var (_, tenants) = await SemearAsync(1);
        var rota = $"{Rota}/{tenants[0].IdPublico}";
        using var leitura = await _cliente.SendAsync(Acesso.Requisicao(HttpMethod.Get, rota, _token));
        var detalhe = await Sessao.JsonAsync(leitura);
        Assert.Equal(tenants[0].Nome, detalhe.GetProperty("nome").GetString());
        var versaoLida = detalhe.GetProperty("versao").GetString()!;
        var novoCnpj = Dados.CnpjValido();

        using var primeira = await _cliente.SendAsync(Acesso.ComCorpo(HttpMethod.Put, rota, _token,
            new { nome = "Nome editado", cnpj = novoCnpj, versao = versaoLida }));
        using var segunda = await _cliente.SendAsync(Acesso.ComCorpo(HttpMethod.Put, rota, _token,
            new { nome = "Edição atrasada", cnpj = novoCnpj, versao = versaoLida }));

        Assert.Equal(HttpStatusCode.OK, primeira.StatusCode);
        var editado = await Sessao.JsonAsync(primeira);
        Assert.Equal("Nome editado", editado.GetProperty("nome").GetString());
        Assert.NotEqual(versaoLida, editado.GetProperty("versao").GetString());
        Assert.Equal(HttpStatusCode.Conflict, segunda.StatusCode);
        Assert.Equal("CONFLITO_VERSAO", await Sessao.CodigoAsync(segunda));

        await using var contexto = banco.CriarContexto();
        var gravado = await contexto.Tenants.AsNoTracking().SingleAsync(t => t.Id == tenants[0].Id);
        Assert.Equal("Nome editado", gravado.Nome);
        Assert.Equal(novoCnpj, gravado.Cnpj);
        Assert.NotNull(gravado.AlteradoEm);
        Assert.Equal(1, await contexto.Auditoria.CountAsync(a =>
            a.RecursoIdPublico == tenants[0].IdPublico && a.Operacao == "TENANT_ALTERADO"));
    }

    [Fact]
    public async Task Admin_listando_tenants_responde_403()
    {
        var tenant = await Dados.NovoTenantAsync(banco);
        var admin = await Dados.NovoUsuarioAsync(banco, Perfil.Admin, tenant);

        using var resposta = await _cliente.SendAsync(Acesso.Requisicao(HttpMethod.Get, Rota, Acesso.Token(admin, tenant)));

        Assert.Equal(HttpStatusCode.Forbidden, resposta.StatusCode);
        Assert.Equal("ACESSO_NEGADO", await Sessao.CodigoAsync(resposta));
    }
}
