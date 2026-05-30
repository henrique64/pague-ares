namespace Ares.PagueAres.Domain.Dtos
{
    public class ListagemSolicitacoesDto
    {
        public int Codigo { get; set; }
        public int IdTipo { get; set; }
        public string Tipo { get; set; } = null!;
        public DateTime DataSolicitacao { get; set; }
        public DateTime? DataVencimento { get; set; }
        public string? NumeroDocumento { get; set; }
        public string NomeSolicitante { get; set; } = null!;
        public int IdSolicitante { get; set; }
        public string? UsuarioAtribuido { get; set; }
        public int? IdUsuarioAtribuido { get; set; }
        public string NomeParceiro { get; set; } = null!;
        public decimal? Valor { get; set; }
        public string Descricao { get; set; } = null!;
        public string ObservacaoGestor { get; set; } = null!;
        public int? IdStatusGestor { get; set; }
        public int? IdStatusFinanceiro { get; set; }
        public int? IdStatusContabilidade { get; set; }
        public string StatusGestor { get; set; } = null!;
        public string StatusFinanceiro { get; set; } = null!;
        public string StatusContabilidade { get; set; } = null!;
        public int? IdGestor { get; set; } = null!;
        public bool? AprovadoGestor { get; set; } = null!;
        public bool? AprovadoSetor { get; set; } = null!;
        public bool? Rascunho { get; set; } = null!;
        public bool? Cancelado { get; set; } = null!;

    }
}
