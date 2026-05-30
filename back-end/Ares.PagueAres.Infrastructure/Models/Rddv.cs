namespace Ares.PagueAres.Infrastructure.Models
{
    public partial class Rddv
    {
        public int IdRelatorio { get; set; }
        public int IdUsuario { get; set; }
        public int? IdUsuarioAtribuido { get; set; }
        public DateTime? DataAtribuicao { get; set; }
        public int? IdUsuarioAtribuidor { get; set; }
        public string NomeFuncionario { get; set; } = null!;
        public string CPF { get; set; } = null!;
        public int IdDepartamento { get; set; }
        public string Finalidade { get; set; } = null!;
        public int TipoRelatorio { get; set; }
        public string Moeda { get; set; } = null!;
        public int TipoViagem { get; set; }
        public DateTime? DataInicio { get; set; }
        public DateTime? DataFim { get; set; }
        public DateTime DataCadastro { get; set; }
        public DateTime? DataVencimento { get; set; }
        public string LocalViagem { get; set; } = null!;
        public string CentroCusto { get; set; } = null!;
        public string Projeto { get; set; } = null!;
        public string PCA { get; set; } = null!;
        public bool? DiariaViagem { get; set; }
        public decimal? Diarias { get; set; }
        public bool? Adiantamento { get; set; }
        public decimal ValorAdiantamento { get; set; }
        public decimal SaldoCartaoVtm { get; set; }
        public string Observacao { get; set; } = null!;
        public int TipoAutorizacao { get; set; }
        public int? IdGestor { get; set; }
        public bool? AprovadoGestor { get; set; }
        public string ObservacaoGestor { get; set; } = null!;
        public bool? AprovadoSetor { get; set; }
        public string ObservacaoSetor { get; set; } = null!;
        public int? StatusGestor { get; set; }
        public int? StatusContabilidade { get; set; }
        public int? StatusFinanceiro { get; set; }
        public string Banco { get; set; } = null!;
        public string Agencia { get; set; } = null!;
        public string Conta { get; set; } = null!;
        public decimal ValorDiaria { get; set; }
        public decimal ValorKm { get; set; }
        public DateTime? DataAprovacaoGestor { get; set; } = null!;
        public DateTime? DataAprovacaoFinanceiro { get; set; } = null!;
        public DateTime? DataAprovacaoContabil { get; set; } = null!;
        public bool Rascunho { get; set; } = true;
        public bool Cancelado { get; set; } = false;

    }
}
