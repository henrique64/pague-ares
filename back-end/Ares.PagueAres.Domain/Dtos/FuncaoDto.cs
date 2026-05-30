namespace Ares.PagueAres.Domain.Dtos
{
    public partial class FuncaoDto
    {
        public int IdFuncao { get; set; }
        public string Nome { get; set; } = null!;
        public string? Alias { get; set; }
    }
}
