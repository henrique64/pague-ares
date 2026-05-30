namespace Ares.PagueAres.Infrastructure.Models
{
    public partial class Configuracao
    {
        public int IdConfiguracao { get; set; }
        public string Chave { get; set; } = null!;
        public string Valor { get; set; } = null!;
        public DateTime? DataAlteracao { get; set; }
        public int? UsuarioAlteracao { get; set; }
    }
}
