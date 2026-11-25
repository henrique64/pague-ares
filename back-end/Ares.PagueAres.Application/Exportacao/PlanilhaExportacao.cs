using Ares.PagueAres.Domain.Enums;
using Ares.PagueAres.Domain.Exportacao;
using ClosedXML.Excel;

namespace Ares.PagueAres.Application.Exportacao
{
    /// <summary>
    /// Planilha <c>titulos.xlsx</c> do pacote (contrato C3), gerada com ClosedXML: abas Solicitações, RDDVs,
    /// Despesas dos RDDVs e Anexos, com cabeçalho em negrito, congelado e com filtro automático.
    /// </summary>
    public static class PlanilhaExportacao
    {
        public const string AbaSolicitacoes = "Solicitações";
        public const string AbaRddvs = "RDDVs";
        public const string AbaDespesas = "Despesas dos RDDVs";
        public const string AbaAnexos = "Anexos";

        // Códigos de formato do Excel (no Excel em português aparecem como dd/mm/aaaa).
        public const string FormatoData = "dd/mm/yyyy";
        public const string FormatoDataHora = "dd/mm/yyyy hh:mm";
        public const string FormatoValor = "#,##0.00";

        // Limite de caracteres de uma célula do Excel.
        private const int TamanhoMaximoTexto = 32767;

        // Linhas consideradas no ajuste de largura e largura máxima, para não deixar colunas de texto livre enormes.
        private const int LinhasAjusteLargura = 1000;
        private const double LarguraMinima = 8;
        private const double LarguraMaxima = 60;

        private enum Tipo { Texto, Inteiro, Valor, Data, DataHora }

        private sealed record Coluna(string Nome, Tipo Tipo);

        public static readonly IReadOnlyList<string> ColunasSolicitacoes = Nomes(ColunasSol());
        public static readonly IReadOnlyList<string> ColunasRddvs = Nomes(ColunasRddv());
        public static readonly IReadOnlyList<string> ColunasDespesas = Nomes(ColunasDesp());
        public static readonly IReadOnlyList<string> ColunasAnexos = Nomes(ColunasAnx());

        /// <summary>
        /// Escreve a planilha em <paramref name="destino"/>. A quantidade de anexos de cada título e os hiperlinks
        /// para as pastas saem de <paramref name="arquivos"/> (os anexos que de fato entraram no pacote).
        /// </summary>
        public static void Escrever(Stream destino,
            IReadOnlyList<SolicitacaoExportacao> solicitacoes,
            IReadOnlyList<RddvExportacao> rddvs,
            IReadOnlyList<DespesaRddvExportacao> despesas,
            IReadOnlyList<ArquivoPacoteExportacao> arquivos)
        {
            ArgumentNullException.ThrowIfNull(destino);
            ArgumentNullException.ThrowIfNull(solicitacoes);
            ArgumentNullException.ThrowIfNull(rddvs);
            ArgumentNullException.ThrowIfNull(despesas);
            ArgumentNullException.ThrowIfNull(arquivos);

            var anexosPorTitulo = arquivos
                .GroupBy(a => a.CodigoTitulo, StringComparer.Ordinal)
                .ToDictionary(g => g.Key, g => g.Count(), StringComparer.Ordinal);

            using var pasta = new XLWorkbook();

            EscreverSolicitacoes(pasta.AddWorksheet(AbaSolicitacoes), solicitacoes, anexosPorTitulo);
            EscreverRddvs(pasta.AddWorksheet(AbaRddvs), rddvs, anexosPorTitulo);
            EscreverDespesas(pasta.AddWorksheet(AbaDespesas), despesas);
            EscreverAnexos(pasta.AddWorksheet(AbaAnexos), arquivos);

            pasta.SaveAs(destino);
        }

        private static void EscreverSolicitacoes(IXLWorksheet aba, IReadOnlyList<SolicitacaoExportacao> solicitacoes,
            Dictionary<string, int> anexosPorTitulo)
        {
            var colunas = ColunasSol();
            PrepararAba(aba, colunas);

            int linha = 2;
            foreach (var s in solicitacoes)
            {
                var codigo = CodigoTitulo.Formatar(TipoTituloExportacao.Solicitacao, s.Id);
                int quantidadeAnexos = anexosPorTitulo.GetValueOrDefault(codigo);
                var l = new Linha(aba, linha++);

                l.Codigo(codigo, quantidadeAnexos > 0);
                l.Inteiro(s.Id);
                l.Texto(s.TipoSolicitacao);
                l.Texto(s.TipoPagamento);
                l.Texto(s.FormaPagamento);
                l.Texto(s.Departamento);
                l.Texto(s.Solicitante);
                l.Texto(s.AtribuidoA);
                l.Texto(s.Gestor);
                l.Data(s.DataSolicitacao);
                l.Data(s.DataDocumento);
                l.Data(s.DataVencimento);
                l.Texto(s.NumeroDocumento);
                l.Texto(s.Descricao);
                l.Valor(s.Valor);
                l.SimNao(s.Parcelado);
                l.Inteiro(s.Parcelas);
                l.Texto(s.CentroCusto);
                l.Texto(s.Projeto);
                l.Texto(s.Pca);
                l.Texto(s.NomeParceiro);
                l.Texto(s.DocumentoParceiro);
                l.Texto(s.CodigoParceiro);
                l.Texto(s.Banco);
                l.Texto(s.Agencia);
                l.Texto(s.Conta);
                l.Texto(s.Observacao);
                l.Texto(s.StatusGestor);
                l.DataHora(s.DataAprovacaoGestor);
                l.Texto(s.ObservacaoGestor);
                l.Texto(s.StatusFinanceiro);
                l.DataHora(s.DataAprovacaoFinanceiro);
                l.Texto(s.ObservacaoSetor);
                l.Texto(s.StatusContabil);
                l.DataHora(s.DataAprovacaoContabil);
                l.Inteiro(quantidadeAnexos);
            }

            FinalizarAba(aba, colunas.Count, linha - 1);
        }

        private static void EscreverRddvs(IXLWorksheet aba, IReadOnlyList<RddvExportacao> rddvs, Dictionary<string, int> anexosPorTitulo)
        {
            var colunas = ColunasRddv();
            PrepararAba(aba, colunas);

            int linha = 2;
            foreach (var r in rddvs)
            {
                var codigo = CodigoTitulo.Formatar(TipoTituloExportacao.Rddv, r.Id);
                int quantidadeAnexos = anexosPorTitulo.GetValueOrDefault(codigo);
                var l = new Linha(aba, linha++);

                l.Codigo(codigo, quantidadeAnexos > 0);
                l.Inteiro(r.Id);
                l.Texto(r.TipoRelatorio);
                l.Texto(r.NomeFuncionario);
                l.Texto(r.Cpf);
                l.Texto(r.Departamento);
                l.Texto(r.Solicitante);
                l.Texto(r.AtribuidoA);
                l.Texto(r.Gestor);
                l.Texto(r.Finalidade);
                l.Texto(r.TipoViagem);
                l.Texto(r.LocalViagem);
                l.Data(r.DataInicio);
                l.Data(r.DataFim);
                l.Data(r.DataCadastro);
                l.Data(r.DataVencimento);
                l.Texto(r.Moeda);
                l.SimNao(r.DiariaViagem);
                l.Valor(r.Diarias);
                l.Valor(r.ValorDiaria);
                l.Valor(r.ValorKm);
                l.SimNao(r.Adiantamento);
                l.Valor(r.ValorAdiantamento);
                l.Valor(r.SaldoCartaoVtm);
                l.Valor(r.TotalDespesas);
                l.Texto(r.CentroCusto);
                l.Texto(r.Projeto);
                l.Texto(r.Pca);
                l.Texto(r.Banco);
                l.Texto(r.Agencia);
                l.Texto(r.Conta);
                l.Texto(r.Observacao);
                l.Texto(r.StatusGestor);
                l.DataHora(r.DataAprovacaoGestor);
                l.Texto(r.ObservacaoGestor);
                l.Texto(r.StatusFinanceiro);
                l.DataHora(r.DataAprovacaoFinanceiro);
                l.Texto(r.ObservacaoSetor);
                l.Texto(r.StatusContabil);
                l.DataHora(r.DataAprovacaoContabil);
                l.Inteiro(quantidadeAnexos);
            }

            FinalizarAba(aba, colunas.Count, linha - 1);
        }

        private static void EscreverDespesas(IXLWorksheet aba, IReadOnlyList<DespesaRddvExportacao> despesas)
        {
            var colunas = ColunasDesp();
            PrepararAba(aba, colunas);

            int linha = 2;
            foreach (var d in despesas)
            {
                var l = new Linha(aba, linha++);

                l.Texto(CodigoTitulo.Formatar(TipoTituloExportacao.Rddv, d.IdRddv));
                l.Inteiro(d.IdDespesa);
                l.Data(d.DataDespesa);
                l.Texto(d.TipoDespesa);
                l.Texto(d.Moeda);
                l.Valor(d.Quantidade);
                l.Valor(d.Valor);
            }

            FinalizarAba(aba, colunas.Count, linha - 1);
        }

        private static void EscreverAnexos(IXLWorksheet aba, IReadOnlyList<ArquivoPacoteExportacao> arquivos)
        {
            var colunas = ColunasAnx();
            PrepararAba(aba, colunas);

            int linha = 2;
            foreach (var a in arquivos)
            {
                var l = new Linha(aba, linha++);

                l.Texto(a.CodigoTitulo);
                l.Texto(a.TipoTitulo == TipoTituloExportacao.Solicitacao ? "Solicitação" : "RDDV");
                l.Texto(a.TipoDocumento);
                l.Texto(a.NomeOriginal);
                l.Link(a.CaminhoRelativo, a.CaminhoRelativo);
                l.Inteiro(a.TamanhoBytes);
                l.Texto(a.Sha256);
                l.DataHora(a.DataCriacao);
            }

            FinalizarAba(aba, colunas.Count, linha - 1);
        }

        private static void PrepararAba(IXLWorksheet aba, IReadOnlyList<Coluna> colunas)
        {
            for (int i = 0; i < colunas.Count; i++)
            {
                aba.Cell(1, i + 1).Value = colunas[i].Nome;

                var formato = colunas[i].Tipo switch
                {
                    Tipo.Data => FormatoData,
                    Tipo.DataHora => FormatoDataHora,
                    Tipo.Valor => FormatoValor,
                    _ => null
                };
                if (formato is not null)
                {
                    aba.Column(i + 1).Style.NumberFormat.Format = formato;
                }
            }

            aba.Range(1, 1, 1, colunas.Count).Style.Font.Bold = true;
        }

        private static void FinalizarAba(IXLWorksheet aba, int quantidadeColunas, int ultimaLinha)
        {
            aba.SheetView.FreezeRows(1);
            aba.Range(1, 1, Math.Max(1, ultimaLinha), quantidadeColunas).SetAutoFilter();

            int ultimaLinhaAjuste = Math.Min(Math.Max(1, ultimaLinha), LinhasAjusteLargura);
            for (int c = 1; c <= quantidadeColunas; c++)
            {
                aba.Column(c).AdjustToContents(1, ultimaLinhaAjuste, LarguraMinima, LarguraMaxima);
            }
        }

        /// <summary>
        /// Endereço do hiperlink relativo. Só <c>%</c> e <c>#</c> são escapados: <c>#</c> seria lido como âncora e
        /// <c>%</c> como início de escape; os demais caracteres ficam como no nome da entrada do ZIP.
        /// </summary>
        public static string EnderecoHiperlink(string caminhoRelativo) =>
            caminhoRelativo.Replace("%", "%25", StringComparison.Ordinal).Replace("#", "%23", StringComparison.Ordinal);

        /// <summary>Escreve as células de uma linha da esquerda para a direita, cada uma com o tipo de C3.</summary>
        private sealed class Linha(IXLWorksheet aba, int numero)
        {
            private int _coluna = 1;

            private IXLCell Proxima() => aba.Cell(numero, _coluna++);

            public void Texto(string? valor)
            {
                var celula = Proxima();
                if (valor is null)
                {
                    return;
                }

                celula.Value = valor.Length > TamanhoMaximoTexto ? valor[..TamanhoMaximoTexto] : valor;
            }

            public void Inteiro(long? valor)
            {
                var celula = Proxima();
                if (valor is not null)
                {
                    celula.Value = valor.Value;
                }
            }

            public void Valor(decimal? valor)
            {
                var celula = Proxima();
                if (valor is not null)
                {
                    celula.Value = valor.Value;
                }
            }

            public void Data(DateTime? valor)
            {
                var celula = Proxima();
                if (valor is not null)
                {
                    celula.Value = valor.Value.Date;
                }
            }

            public void DataHora(DateTime? valor)
            {
                var celula = Proxima();
                if (valor is not null)
                {
                    celula.Value = valor.Value;
                }
            }

            public void SimNao(bool? valor) => Texto(valor is null ? null : valor.Value ? "Sim" : "Não");

            /// <summary>Código do título; com anexos, ganha hiperlink relativo para a pasta <c>&lt;código&gt;/</c>.</summary>
            public void Codigo(string codigo, bool temAnexos)
            {
                if (temAnexos)
                {
                    Link(codigo, codigo + "/");
                }
                else
                {
                    Texto(codigo);
                }
            }

            public void Link(string texto, string caminhoRelativo)
            {
                var celula = Proxima();
                celula.Value = texto;
                celula.SetHyperlink(new XLHyperlink(new Uri(EnderecoHiperlink(caminhoRelativo), UriKind.Relative)));
            }
        }

        private static List<string> Nomes(IEnumerable<Coluna> colunas) => colunas.Select(c => c.Nome).ToList();

        private static List<Coluna> ColunasSol() =>
        [
            new("Código", Tipo.Texto), new("Id", Tipo.Inteiro), new("Tipo de solicitação", Tipo.Texto),
            new("Tipo de pagamento", Tipo.Texto), new("Forma de pagamento", Tipo.Texto), new("Departamento", Tipo.Texto),
            new("Solicitante", Tipo.Texto), new("Atribuído a", Tipo.Texto), new("Gestor", Tipo.Texto),
            new("Data da solicitação", Tipo.Data), new("Data do documento", Tipo.Data), new("Data de vencimento", Tipo.Data),
            new("Nº do documento", Tipo.Texto), new("Descrição", Tipo.Texto), new("Valor", Tipo.Valor),
            new("Parcelado", Tipo.Texto), new("Parcelas", Tipo.Inteiro), new("Centro de custo", Tipo.Texto),
            new("Projeto", Tipo.Texto), new("PCA", Tipo.Texto), new("Parceiro", Tipo.Texto),
            new("Documento do parceiro", Tipo.Texto), new("Código do parceiro", Tipo.Texto), new("Banco", Tipo.Texto),
            new("Agência", Tipo.Texto), new("Conta", Tipo.Texto), new("Observação", Tipo.Texto),
            new("Status gestor", Tipo.Texto), new("Data aprovação gestor", Tipo.DataHora), new("Observação do gestor", Tipo.Texto),
            new("Status financeiro", Tipo.Texto), new("Data aprovação financeiro", Tipo.DataHora), new("Observação do setor", Tipo.Texto),
            new("Status contábil", Tipo.Texto), new("Data aprovação contábil", Tipo.DataHora), new("Qtde. de anexos", Tipo.Inteiro)
        ];

        private static List<Coluna> ColunasRddv() =>
        [
            new("Código", Tipo.Texto), new("Id", Tipo.Inteiro), new("Tipo de relatório", Tipo.Texto),
            new("Funcionário", Tipo.Texto), new("CPF", Tipo.Texto), new("Departamento", Tipo.Texto),
            new("Solicitante", Tipo.Texto), new("Atribuído a", Tipo.Texto), new("Gestor", Tipo.Texto),
            new("Finalidade", Tipo.Texto), new("Tipo de viagem", Tipo.Texto), new("Local da viagem", Tipo.Texto),
            new("Data início", Tipo.Data), new("Data fim", Tipo.Data), new("Data de cadastro", Tipo.Data),
            new("Data de vencimento", Tipo.Data), new("Moeda", Tipo.Texto), new("Diária de viagem", Tipo.Texto),
            new("Diárias", Tipo.Valor), new("Valor da diária", Tipo.Valor), new("Valor do km", Tipo.Valor),
            new("Adiantamento", Tipo.Texto), new("Valor do adiantamento", Tipo.Valor), new("Saldo cartão VTM", Tipo.Valor),
            new("Valor líquido do RDDV", Tipo.Valor), new("Centro de custo", Tipo.Texto), new("Projeto", Tipo.Texto),
            new("PCA", Tipo.Texto), new("Banco", Tipo.Texto), new("Agência", Tipo.Texto),
            new("Conta", Tipo.Texto), new("Observação", Tipo.Texto), new("Status gestor", Tipo.Texto),
            new("Data aprovação gestor", Tipo.DataHora), new("Observação do gestor", Tipo.Texto), new("Status financeiro", Tipo.Texto),
            new("Data aprovação financeiro", Tipo.DataHora), new("Observação do setor", Tipo.Texto), new("Status contábil", Tipo.Texto),
            new("Data aprovação contábil", Tipo.DataHora), new("Qtde. de anexos", Tipo.Inteiro)
        ];

        private static List<Coluna> ColunasDesp() =>
        [
            new("Código do RDDV", Tipo.Texto), new("Id da despesa", Tipo.Inteiro), new("Data da despesa", Tipo.Data),
            new("Tipo de despesa", Tipo.Texto), new("Moeda", Tipo.Texto), new("Quantidade", Tipo.Valor), new("Valor", Tipo.Valor)
        ];

        private static List<Coluna> ColunasAnx() =>
        [
            new("Código do título", Tipo.Texto), new("Tipo do título", Tipo.Texto), new("Tipo de documento", Tipo.Texto),
            new("Nome original", Tipo.Texto), new("Arquivo no pacote", Tipo.Texto), new("Tamanho (bytes)", Tipo.Inteiro),
            new("SHA-256", Tipo.Texto), new("Data de envio", Tipo.DataHora)
        ];
    }
}
