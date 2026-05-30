namespace Ares.PagueAres.Infrastructure.Models
{
    public class Fornecedor
    {

        public int IdFornecedor { get; set; }
        public string NumDocumento { get; set; } = null!;
        public string Nome { get; set; } = null!;
        public string Codigo { get; set; } = null!;

    }
}
