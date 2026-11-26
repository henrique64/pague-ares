using Ares.PagueAres.Domain.Dtos.Exportacao;

namespace Ares.PagueAres.Application.Exportacao
{
    public interface IGeradorPacoteExportacao
    {
        Task<ResultadoPacoteExportacao> GerarAsync(int idExportacao, FiltroExportacaoDto filtro, Stream destino,
            IProgress<ProgressoExportacao>? progresso, CancellationToken ct);
    }

    public sealed record ProgressoExportacao(int TitulosProcessados, int TotalTitulos);

    public sealed record ResultadoPacoteExportacao(int TotalTitulos, int TotalAnexos, long BytesAnexos);
}
