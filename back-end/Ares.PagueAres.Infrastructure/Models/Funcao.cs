namespace Ares.PagueAres.Infrastructure.Models
{
    public partial class Funcao
    {
        public Funcao()
        {
            //IdUsuarios = new HashSet<Usuario>();
        }

        public int IdFuncao { get; set; }
        public string Nome { get; set; } = null!;
        public string? Alias { get; set; }

        //public virtual ICollection<Usuario> IdUsuarios { get; set; }
    }
}
