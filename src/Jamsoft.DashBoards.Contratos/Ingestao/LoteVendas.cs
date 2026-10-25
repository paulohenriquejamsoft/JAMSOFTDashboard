namespace Jamsoft.DashBoards.Contratos.Ingestao;

/// <summary>Lote de vendas enviado pelo Coletor para <c>POST /api/coletor/vendas/lotes</c> (ING-01).</summary>
/// <param name="Tenant">ID público do tenant de destino.</param>
/// <param name="ConfiguracaoId">Gerado pelo Coletor, estável por configuração.</param>
/// <param name="BancoOrigem">Só para exibição (servidor/banco); nunca leva credenciais.</param>
public sealed record LoteVendas(Guid BatchId, Guid Tenant, Guid ConfiguracaoId, string Rotina,
    string? BancoOrigem, IReadOnlyList<VendaContrato> Vendas);

/// <summary>Limites de um lote (ING-08): acima deles a API responde 413.</summary>
public static class Limites
{
    public const int VendasPorLote = 500;
    public const int BytesPorLote = 2 * 1024 * 1024;
}
