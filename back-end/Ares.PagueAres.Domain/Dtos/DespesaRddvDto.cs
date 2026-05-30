namespace Ares.PagueAres.Domain.Dtos
{
    public partial class DespesaRddvDto
    {
        public int IdDespesaRddv { get; set; }
        public int IdRelatorio { get; set; }
        public DateTime DataDespesa { get; set; }
        public int TipoDespesa { get; set; }
        public string? Moeda { get; set; } = null!;
        public decimal Valor { get; set; }
        public bool Excluido { get; set; }
        public decimal? Quantidade { get; set; }
    }
}
