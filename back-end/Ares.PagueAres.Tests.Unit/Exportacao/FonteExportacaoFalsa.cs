using Ares.PagueAres.Application.Exportacao;
using Ares.PagueAres.Domain.Dtos.Exportacao;
using Ares.PagueAres.Domain.Enums;
using Ares.PagueAres.Domain.Exportacao;

namespace Ares.PagueAres.Tests.Unit.Exportacao
{
    /// <summary>
    /// Fonte de exportação em memória para os testes do gerador. O conteúdo dos anexos vem de
    /// <see cref="Conteudos"/> ou é gerado sob demanda por <see cref="GerarConteudo"/>.
    /// </summary>
    internal sealed class FonteExportacaoFalsa : IFonteExportacao
    {
        private int _leiturasEmAndamento;

        public List<SolicitacaoExportacao> Solicitacoes { get; } = [];
        public List<RddvExportacao> Rddvs { get; } = [];
        public List<DespesaRddvExportacao> Despesas { get; } = [];
        public List<AnexoExportacao> Anexos { get; } = [];
        public Dictionary<int, string?> Conteudos { get; } = [];
        public Func<TipoTituloExportacao, int, string?>? GerarConteudo { get; set; }

        /// <summary>Executado depois de cada leitura de conteúdo (para cancelar no meio, por exemplo).</summary>
        public Action<int>? AposLeitura { get; set; }

        public int ChamadasListarAnexos { get; private set; }
        public int LeiturasDeConteudo { get; private set; }
        public int MaximoLeiturasSimultaneas { get; private set; }

        public Task<int> ContarTitulosAsync(FiltroExportacaoDto filtro, CancellationToken ct) =>
            Task.FromResult(Solicitacoes.Count + Rddvs.Count);

        public Task<IReadOnlyList<SolicitacaoExportacao>> ListarSolicitacoesAsync(FiltroExportacaoDto filtro, CancellationToken ct) =>
            Task.FromResult<IReadOnlyList<SolicitacaoExportacao>>(Solicitacoes.ToList());

        public Task<IReadOnlyList<RddvExportacao>> ListarRddvsAsync(FiltroExportacaoDto filtro, CancellationToken ct) =>
            Task.FromResult<IReadOnlyList<RddvExportacao>>(Rddvs.ToList());

        public Task<IReadOnlyList<DespesaRddvExportacao>> ListarDespesasAsync(IReadOnlyCollection<int> idsRddv, CancellationToken ct) =>
            Task.FromResult<IReadOnlyList<DespesaRddvExportacao>>(Despesas.Where(d => idsRddv.Contains(d.IdRddv)).ToList());

        public Task<IReadOnlyList<AnexoExportacao>> ListarAnexosAsync(TipoTituloExportacao tipo, IReadOnlyCollection<int> idsTitulo, CancellationToken ct)
        {
            ChamadasListarAnexos++;
            return Task.FromResult<IReadOnlyList<AnexoExportacao>>(
                Anexos.Where(a => a.TipoTitulo == tipo && idsTitulo.Contains(a.IdTitulo)).ToList());
        }

        public async Task<string?> LerConteudoBase64Async(TipoTituloExportacao tipo, int idAnexo, CancellationToken ct)
        {
            int emAndamento = Interlocked.Increment(ref _leiturasEmAndamento);
            MaximoLeiturasSimultaneas = Math.Max(MaximoLeiturasSimultaneas, emAndamento);
            try
            {
                await Task.Yield();
                LeiturasDeConteudo++;
                var conteudo = GerarConteudo is not null ? GerarConteudo(tipo, idAnexo) : Conteudos.GetValueOrDefault(idAnexo);
                AposLeitura?.Invoke(idAnexo);
                return conteudo;
            }
            finally
            {
                Interlocked.Decrement(ref _leiturasEmAndamento);
            }
        }

        /// <summary>Acrescenta um anexo com o conteúdo informado (em bytes), gravado como base64 puro.</summary>
        public void AdicionarAnexo(TipoTituloExportacao tipo, int idTitulo, int idAnexo, string tipoDocumento, string nomeOriginal, byte[] conteudo)
        {
            AdicionarAnexoBase64(tipo, idTitulo, idAnexo, tipoDocumento, nomeOriginal, Convert.ToBase64String(conteudo));
        }

        public void AdicionarAnexoBase64(TipoTituloExportacao tipo, int idTitulo, int idAnexo, string tipoDocumento, string nomeOriginal, string? base64)
        {
            Anexos.Add(new AnexoExportacao(idAnexo, tipo, idTitulo, CodigoTitulo.Formatar(tipo, idTitulo), tipoDocumento.Length, tipoDocumento,
                nomeOriginal, new DateTime(2026, 11, 3, 10, 15, 0)));
            Conteudos[idAnexo] = base64;
        }

        public static SolicitacaoExportacao Solicitacao(int id) => new(
            Id: id,
            Codigo: CodigoTitulo.Formatar(TipoTituloExportacao.Solicitacao, id),
            TipoSolicitacao: "Pagamento a fornecedor",
            TipoPagamento: "Boleto",
            FormaPagamento: "À vista",
            Departamento: "Financeiro",
            Solicitante: "Maria Exemplo",
            AtribuidoA: null,
            Gestor: "Gestor Exemplo",
            DataSolicitacao: new DateTime(2026, 11, 2, 9, 30, 0),
            DataDocumento: new DateTime(2026, 11, 1),
            DataVencimento: new DateTime(2026, 11, 30),
            NumeroDocumento: "000123",
            Descricao: "Compra de material de escritório; teste",
            Valor: 1234.56m,
            Parcelado: false,
            Parcelas: null,
            CentroCusto: "CC-01",
            Projeto: "Projeto Exemplo",
            Pca: "PCA-01",
            NomeParceiro: "Fornecedor Fictício Ltda",
            DocumentoParceiro: "00.000.000/0000-00",
            CodigoParceiro: "F-001",
            Banco: "Banco Exemplo",
            Agencia: "0001",
            Conta: "000123-4",
            Observacao: "",
            StatusGestor: "Autorizado",
            DataAprovacaoGestor: new DateTime(2026, 11, 2, 14, 5, 0),
            ObservacaoGestor: "",
            StatusFinanceiro: "Aguardando Autorização",
            DataAprovacaoFinanceiro: null,
            ObservacaoSetor: "",
            StatusContabil: "Aguardando Lançamento",
            DataAprovacaoContabil: null);

        public static RddvExportacao Rddv(int id) => new(
            Id: id,
            Codigo: CodigoTitulo.Formatar(TipoTituloExportacao.Rddv, id),
            TipoRelatorio: "Prestação de Contas",
            NomeFuncionario: "João Exemplo",
            Cpf: "000.000.000-00",
            Departamento: "Comercial",
            Solicitante: "João Exemplo",
            AtribuidoA: "Ana Exemplo",
            Gestor: "Gestor Exemplo",
            Finalidade: "Visita a cliente fictício",
            TipoViagem: "Nacional",
            LocalViagem: "Cidade Exemplo",
            DataInicio: new DateTime(2026, 10, 20),
            DataFim: new DateTime(2026, 10, 22),
            DataCadastro: new DateTime(2026, 10, 23, 8, 0, 0),
            DataVencimento: null,
            Moeda: "BRL",
            DiariaViagem: true,
            Diarias: 2.5m,
            ValorDiaria: 150m,
            ValorKm: 1.2m,
            Adiantamento: null,
            ValorAdiantamento: 0m,
            SaldoCartaoVtm: 0m,
            TotalDespesas: 875.40m,
            CentroCusto: "CC-02",
            Projeto: "Projeto Exemplo",
            Pca: "PCA-02",
            Banco: "Banco Exemplo",
            Agencia: "0002",
            Conta: "000456-7",
            Observacao: "Linha 1\nLinha 2",
            StatusGestor: "Autorizado",
            DataAprovacaoGestor: new DateTime(2026, 10, 24, 11, 0, 0),
            ObservacaoGestor: "",
            StatusFinanceiro: "Autorizado",
            DataAprovacaoFinanceiro: new DateTime(2026, 10, 25, 16, 45, 0),
            ObservacaoSetor: "",
            StatusContabil: "Lançado",
            DataAprovacaoContabil: new DateTime(2026, 10, 26, 9, 10, 0));

        public static DespesaRddvExportacao Despesa(int idDespesa, int idRddv) => new(
            IdDespesa: idDespesa,
            IdRddv: idRddv,
            CodigoRddv: CodigoTitulo.Formatar(TipoTituloExportacao.Rddv, idRddv),
            DataDespesa: new DateTime(2026, 10, 21),
            TipoDespesa: "Alimentação",
            Moeda: "BRL",
            Quantidade: 1m,
            Valor: 437.70m);
    }
}
