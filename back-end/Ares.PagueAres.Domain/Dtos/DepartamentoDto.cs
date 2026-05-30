namespace Ares.PagueAres.Domain.Dtos
{
    public partial class DepartamentoDto
    {
        public int IdDepartamento { get; set; }
        public string Nome { get; set; } = null!;
        public string? CodigoLancamento { get; set; }
    }
}
