using Ares.PagueAres.Domain.Dtos.Exportacao;
using Ares.PagueAres.Domain.Enums;

namespace Ares.PagueAres.Application.Exportacao
{
    public interface IFonteExportacao
    {
        Task<int> ContarTitulosAsync(FiltroExportacaoDto filtro, CancellationToken ct);
        Task<IReadOnlyList<SolicitacaoExportacao>> ListarSolicitacoesAsync(FiltroExportacaoDto filtro, CancellationToken ct);
        Task<IReadOnlyList<RddvExportacao>> ListarRddvsAsync(FiltroExportacaoDto filtro, CancellationToken ct);
        Task<IReadOnlyList<DespesaRddvExportacao>> ListarDespesasAsync(IReadOnlyCollection<int> idsRddv, CancellationToken ct);
        Task<IReadOnlyList<AnexoExportacao>> ListarAnexosAsync(TipoTituloExportacao tipo, IReadOnlyCollection<int> idsTitulo, CancellationToken ct); // só metadados
        Task<string?> LerConteudoBase64Async(TipoTituloExportacao tipo, int idAnexo, CancellationToken ct);  // um anexo por chamada, AsNoTracking, projeção só da coluna Arquivo
    }
}
