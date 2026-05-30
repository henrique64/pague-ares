namespace Ares.PagueAres.Infrastructure.Models
{
    public partial class Usuario
    {
        public Usuario()
        {

        }

        public int IdUsuario { get; set; }
        public string Email { get; set; } = null!;
        public string? Codigo { get; set; } = null!;
        public string Nome { get; set; } = null!;
        public int Origem { get; set; }
        public string? IdExterno { get; set; }
        public string Senha { get; set; } = null!;
        public int IdDepartamento { get; set; }
        public bool? Ativo { get; set; }
        public DateTime DataCadastro { get; set; }
        public int? UsuarioCadastro { get; set; }
        public DateTime? UltimaAlteracao { get; set; }
        public int? UsuarioAlteracao { get; set; }
    }
}
