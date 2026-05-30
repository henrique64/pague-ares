namespace Ares.PagueAres.Domain.Dtos
{
    public partial class DocumentoSolicitacaoDto
    {
        public int IdDocumentoSolicitacao { get; set; }
        public int IdSolicitacao { get; set; }
        public int IdTipoDocumento { get; set; }
        public int IdUsuario { get; set; }
        public DateTime DataCriacao { get; set; }
        public int Indice { get; set; }
        public string NomeArquivo { get; set; } = null!;
        public string CaminhoArquivo { get; set; } = null!;
        public string? Arquivo { get; set; } = null!;

        public bool Excluido { get; set; } = false;

    }
}
