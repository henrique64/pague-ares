using Ares.PagueAres.Domain.Enums;

namespace Ares.PagueAres.Domain.Dtos.Exportacao
{
    public class ExportacaoJobDto
    {
        public int Id { get; set; }
        public StatusExportacao Status { get; set; }
        public string StatusDescricao { get; set; } = "";      // Pendente | Em processamento | Concluída | Falhou | Expirada | Cancelada | Excluída
        public DateTime DataCriacao { get; set; }               // UTC
        public DateTime? DataInicio { get; set; }
        public DateTime? DataConclusao { get; set; }
        public DateTime? DataExpiracao { get; set; }
        public int TotalTitulos { get; set; }
        public int TitulosProcessados { get; set; }
        public int Percentual { get; set; }                    // 0..100
        public int TotalAnexos { get; set; }
        public long? TamanhoBytes { get; set; }
        public string? MensagemErro { get; set; }              // sempre amigável, nunca texto de exceção
        public FiltroExportacaoDto Filtros { get; set; } = new();
    }
}
