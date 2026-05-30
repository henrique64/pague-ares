namespace Ares.PagueAres.Infrastructure.Models
{
    public partial class Departamento
    {
        public int IdDepartamento { get; set; }
        public string Nome { get; set; } = null!;
        public string? CodigoLancamento { get; set; }
    }
}
