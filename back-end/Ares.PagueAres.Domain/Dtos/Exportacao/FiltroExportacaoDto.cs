using Ares.PagueAres.Domain.Enums;

namespace Ares.PagueAres.Domain.Dtos.Exportacao
{
    public class FiltroExportacaoDto
    {
        public TipoTituloExportacao TipoTitulo { get; set; } = TipoTituloExportacao.Ambos;
        public int? Codigo { get; set; }                       // Id do título
        public string? NumeroDocumento { get; set; }           // só Solicitação; igualdade exata
        public DateTime? DataSolicitacaoInicio { get; set; }   // Solicitação: DataSolicitacao; RDDV: DataCadastro
        public DateTime? DataSolicitacaoFim { get; set; }
        public DateTime? DataVencimentoInicio { get; set; }
        public DateTime? DataVencimentoFim { get; set; }
        public DateTime? DataAprovacaoFinanceiroInicio { get; set; }
        public DateTime? DataAprovacaoFinanceiroFim { get; set; }
        public int? IdSolicitante { get; set; }                // IdUsuario
        public int? IdAtribuido { get; set; }                  // IdUsuarioAtribuido
        public int? IdDepartamento { get; set; }
        public int? StatusGestor { get; set; }
        public int? StatusFinanceiro { get; set; }
        public int? StatusContabil { get; set; }
        public string? NomeParceiro { get; set; }              // Solicitação: NomeParceiro; RDDV: NomeFuncionario; contém, sem diferenciar caixa
        public string? DocumentoParceiro { get; set; }         // Solicitação: NumDocParceiro; RDDV: CPF; contém, ignorando . - / e espaços
        public int? TipoSolicitacao { get; set; }              // só Solicitação
        public int? TipoPagamento { get; set; }                // só Solicitação
        public int? FormaPagamento { get; set; }               // só Solicitação
        public int? TipoRelatorio { get; set; }                // só RDDV
        public bool IncluirAnexos { get; set; } = true;
    }
}
