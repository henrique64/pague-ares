namespace Ares.PagueAres.Domain.Dtos
{
    public class FornecedorDto
    {

        public int IdFornecedor { get; set; }
        public string NumDocumento { get; set; } = null!;
        public string Nome { get; set; } = null!;
        public string Codigo { get; set; } = null!;

    }
}
