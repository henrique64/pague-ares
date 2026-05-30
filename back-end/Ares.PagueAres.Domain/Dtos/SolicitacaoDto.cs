namespace Ares.PagueAres.Domain.Dtos
{
    public partial class SolicitacaoDto
    {
        public SolicitacaoDto()
        {
            Documentos = new List<DocumentoSolicitacaoDto>();
        }

        public int IdSolicitacao { get; set; }
        public int? StatusGestor { get; set; }
        public int? StatusContabilidade { get; set; }
        public int? StatusFinanceiro { get; set; }
        public int IdUsuario { get; set; }
        public string? NomeUsuario { get; set; }
        public int IdDepartamento { get; set; }
        public int? IdUsuarioAtribuido { get; set; }
        public DateTime? DataAtribuicao { get; set; }
        public int? IdUsuarioAtribuidor { get; set; }
        public DateTime DataSolicitacao { get; set; }
        public int TipoSolicitacao { get; set; }
        public int TipoPagamento { get; set; }
        public DateTime? DataDocumento { get; set; }
        public DateTime? DataVencimento { get; set; }
        public string? NumeroDocumento { get; set; } = null!;
        public string Descricao { get; set; } = null!;
        public decimal Valor { get; set; }
        public bool? Parcelado { get; set; }
        public int? Parcelas { get; set; }
        public int FormaPagamento { get; set; }
        public string CentroCusto { get; set; } = null!;
        public string Projeto { get; set; } = null!;
        public string Pca { get; set; } = null!;
        public string? NumDocParceiro { get; set; } = null!;
        public string? CodigoParceiro { get; set; } = null!;
        public string? NomeParceiro { get; set; } = null!;
        public string? BancoParceiro { get; set; } = null!;
        public string? AgenciaParceiro { get; set; } = null!;
        public string? ContaParceiro { get; set; } = null!;
        public string? Observacao { get; set; } = null!;
        public int TipoAutorizacao { get; set; }
        public int? IdGestor { get; set; }
        public string? NomeGestor { get; set; }
        public bool? AprovadoGestor { get; set; }
        public string ObservacaoGestor { get; set; } = null!;
        public bool? AprovadoSetor { get; set; }
        public string ObservacaoSetor { get; set; } = null!;
        public bool? GravarDadosParceiro { get; set; }
        public DateTime? DataAprovacaoGestor { get; set; } = null!;
        public DateTime? DataAprovacaoFinanceiro { get; set; } = null!;
        public DateTime? DataAprovacaoContabil { get; set; } = null!;
        public bool Rascunho { get; set; }
        public bool Cancelado { get; set; }

        public List<DocumentoSolicitacaoDto> Documentos { get; set; }
    }
}
