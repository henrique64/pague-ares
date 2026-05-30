namespace Ares.PagueAres.Domain.Dtos
{
    public class TipoViagemDto
    {
        public int IdTipoViagem { get; set; }
        public string Nome { get; set; } = null!;
        public decimal ValorDiaria { get; set; }
    }
}
