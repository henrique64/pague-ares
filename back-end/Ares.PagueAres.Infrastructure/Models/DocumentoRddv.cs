namespace Ares.PagueAres.Infrastructure.Models
{
    public partial class DocumentoRddv
    {
        public int IdDocumentoRddv { get; set; }
        public int IdRelatorio { get; set; }
        public int IdTipoDocumento { get; set; }
        public int IdUsuario { get; set; }
        public DateTime DataCriacao { get; set; }
        public int Indice { get; set; }
        public string NomeArquivo { get; set; } = null!;
        public string CaminhoArquivo { get; set; } = null!;
        public string Arquivo { get; set; } = null!;

    }
}
