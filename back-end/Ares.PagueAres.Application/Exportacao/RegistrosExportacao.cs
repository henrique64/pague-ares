using Ares.PagueAres.Domain.Enums;

namespace Ares.PagueAres.Application.Exportacao
{
    public sealed record SolicitacaoExportacao(
        int Id, string Codigo, string TipoSolicitacao, string TipoPagamento, string FormaPagamento,
        string Departamento, string Solicitante, string? AtribuidoA, string? Gestor,
        DateTime DataSolicitacao, DateTime? DataDocumento, DateTime? DataVencimento,
        string? NumeroDocumento, string Descricao, decimal Valor, bool? Parcelado, int? Parcelas,
        string CentroCusto, string Projeto, string Pca,
        string NomeParceiro, string DocumentoParceiro, string CodigoParceiro,
        string Banco, string Agencia, string Conta, string Observacao,
        string StatusGestor, DateTime? DataAprovacaoGestor, string ObservacaoGestor,
        string StatusFinanceiro, DateTime? DataAprovacaoFinanceiro, string ObservacaoSetor,
        string StatusContabil, DateTime? DataAprovacaoContabil);

    public sealed record RddvExportacao(
        int Id, string Codigo, string TipoRelatorio, string NomeFuncionario, string Cpf,
        string Departamento, string Solicitante, string? AtribuidoA, string? Gestor,
        string Finalidade, string TipoViagem, string LocalViagem,
        DateTime? DataInicio, DateTime? DataFim, DateTime DataCadastro, DateTime? DataVencimento,
        string Moeda, bool? DiariaViagem, decimal? Diarias, decimal ValorDiaria, decimal ValorKm,
        bool? Adiantamento, decimal ValorAdiantamento, decimal SaldoCartaoVtm, decimal? TotalDespesas,
        string CentroCusto, string Projeto, string Pca,
        string Banco, string Agencia, string Conta, string Observacao,
        string StatusGestor, DateTime? DataAprovacaoGestor, string ObservacaoGestor,
        string StatusFinanceiro, DateTime? DataAprovacaoFinanceiro, string ObservacaoSetor,
        string StatusContabil, DateTime? DataAprovacaoContabil);

    public sealed record DespesaRddvExportacao(
        int IdDespesa, int IdRddv, string CodigoRddv, DateTime DataDespesa,
        string TipoDespesa, string? Moeda, decimal? Quantidade, decimal Valor);

    public sealed record AnexoExportacao(
        int IdAnexo, TipoTituloExportacao TipoTitulo, int IdTitulo, string CodigoTitulo,
        int IdTipoDocumento, string TipoDocumento, string NomeOriginal, DateTime DataCriacao);
}
