using Jamsoft.DashBoards.Api.Configuracao;
using Jamsoft.DashBoards.Api.Infraestrutura.Dados;
using Jamsoft.DashBoards.Api.Infraestrutura.Erros;
using Jamsoft.DashBoards.Api.Modulos.Usuarios;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Jamsoft.DashBoards.Api.Modulos.Autenticacao;

/// <summary>
/// Bloqueio por tentativas (AUTH-03): cada senha errada soma 1 em FALHAS_LOGIN num único UPDATE (sem
/// corrida entre requisições paralelas). Ao atingir o limite, grava BLOQUEADO_ATE e zera o contador.
/// Login bem-sucedido zera tudo.
/// </summary>
public sealed class Bloqueio(PlataformaDbContext banco, IOptions<AcessoOptions> opcoes, TimeProvider tempo)
{
    public const string CodigoUsuarioBloqueado = "USUARIO_BLOQUEADO";

    public static IResult Bloqueado(DateTime ate) =>
        ProblemasApi.Problema(StatusCodes.Status423Locked, CodigoUsuarioBloqueado, "Muitas tentativas. Tente novamente mais tarde",
            new Dictionary<string, object?> { ["bloqueadoAte"] = DateTime.SpecifyKind(ate, DateTimeKind.Utc) });

    /// <summary>Fim do bloqueio em vigor, ou nulo.</summary>
    public DateTime? BloqueadoAte(Usuario usuario) =>
        usuario.BloqueadoAte is { } ate && ate > Agora() ? ate : null;

    /// <summary>Conta uma falha; devolve o fim do bloqueio quando esta falha o iniciou.</summary>
    public async Task<DateTime?> RegistrarFalhaAsync(Usuario usuario, CancellationToken ct)
    {
        var limite = opcoes.Value.TentativasAteBloqueio;
        // Segundos inteiros: a coluna é DATETIME2(0) e o SQL Server arredondaria a fração.
        var ate = Truncar(Agora().AddMinutes(opcoes.Value.MinutosBloqueio));

        var resultado = await banco.Database.SqlQuery<DateTime?>($"""
            UPDATE USUARIO SET
                BLOQUEADO_ATE = CASE WHEN FALHAS_LOGIN + 1 >= {limite} THEN {ate} ELSE BLOQUEADO_ATE END,
                FALHAS_LOGIN = CASE WHEN FALHAS_LOGIN + 1 >= {limite} THEN 0 ELSE FALHAS_LOGIN + 1 END
            OUTPUT CASE WHEN DELETED.FALHAS_LOGIN + 1 >= {limite} THEN INSERTED.BLOQUEADO_ATE END AS Value
            WHERE ID = {usuario.Id}
            """).ToListAsync(ct);
        return resultado.SingleOrDefault();
    }

    public Task RegistrarSucessoAsync(Usuario usuario, CancellationToken ct)
    {
        var agora = Agora();
        return banco.Usuarios.Where(u => u.Id == usuario.Id).ExecuteUpdateAsync(u => u
            .SetProperty(x => x.FalhasLogin, (byte)0)
            .SetProperty(x => x.BloqueadoAte, (DateTime?)null)
            .SetProperty(x => x.UltimoLoginEm, agora), ct);
    }

    private DateTime Agora() => tempo.GetUtcNow().UtcDateTime;

    private static DateTime Truncar(DateTime valor) => new(valor.Ticks - valor.Ticks % TimeSpan.TicksPerSecond, valor.Kind);
}
