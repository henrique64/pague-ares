namespace Ares.PagueAres.Infrastructure.Models
{
    public class TipoViagem
    {
        public int IdTipoViagem { get; set; }
        public string Nome { get; set; } = null!;
        public decimal ValorDiaria { get; set; }
    }
}
