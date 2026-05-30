using System;

namespace Ares.PagueAres.Domain.Dtos.Listing
{
    internal class ListingDto
    {
        // Solicitacao
        public int? Id { get; set; }
        public int? StatusGestor { get; set; }
        public int? StatusContabilidade { get; set; }
        public int? StatusFinanceiro { get; set; }
        public int IdUsuario { get; set; }
        public int IdDepartamento { get; set; }
        public int? IdUsuarioAtribuido { get; set; }
        public DateTime? DataAtribuicao { get; set; }
        public int? IdUsuarioAtribuidor { get; set; }
        public DateTime? DataSolicitacao { get; set; }
        public int? TipoSolicitacao { get; set; }
        public int? TipoPagamento { get; set; }
        public DateTime? DataDocumento { get; set; }
        public DateTime? DataVencimento { get; set; }
        public string? NumeroDocumento { get; set; }
        public string Descricao { get; set; } = string.Empty;
        public decimal Valor { get; set; }
        public bool? Parcelado { get; set; }
        public int? Parcelas { get; set; }
        public int FormaPagamento { get; set; }
        public string CentroCusto { get; set; } = string.Empty;
        public string Projeto { get; set; } = string.Empty;
        public string PCA { get; set; } = string.Empty;
        public string NumDocParceiro { get; set; } = string.Empty;
        public string CodigoParceiro { get; set; } = string.Empty;
        public string NomeParceiro { get; set; } = string.Empty;
        public string BancoParceiro { get; set; } = string.Empty;
        public string AgenciaParceiro { get; set; } = string.Empty;
        public string ContaParceiro { get; set; } = string.Empty;
        public string Observacao { get; set; } = string.Empty;
        public int TipoAutorizacao { get; set; }
        public int? IdGestor { get; set; }
        public bool? AprovadoGestor { get; set; }
        public string ObservacaoGestor { get; set; } = string.Empty;
        public bool? AprovadoSetor { get; set; }
        public string ObservacaoSetor { get; set; } = string.Empty;
        public DateTime? DataAprovacaoGestor { get; set; }
        public DateTime? DataAprovacaoFinanceiro { get; set; }
        public DateTime? DataAprovacaoContabil { get; set; }
        public bool Rascunho { get; set; }
        public bool Cancelado { get; set; }

        // Rddv
        public DateTime? DataCadastro { get; set; }
        public string NomeFuncionario { get; set; } = string.Empty;
        public string CPF { get; set; } = string.Empty;
        public string Finalidade { get; set; } = string.Empty;
        public int? TipoRelatorio { get; set; }
        public string Moeda { get; set; } = string.Empty;
        public int? TipoViagem { get; set; }
        public DateTime? DataInicio { get; set; }
        public DateTime? DataFim { get; set; }
        public string LocalViagem { get; set; } = string.Empty;
        public bool? DiariaViagem { get; set; }
        public decimal? Diarias { get; set; }
        public bool? Adiantamento { get; set; }
        public decimal ValorAdiantamento { get; set; }
        public decimal SaldoCartaoVtm { get; set; }
        public string Banco { get; set; } = string.Empty;
        public string Agencia { get; set; } = string.Empty;
        public string Conta { get; set; } = string.Empty;
        public decimal ValorDiaria { get; set; }
        public decimal ValorKm { get; set; }
    }
}