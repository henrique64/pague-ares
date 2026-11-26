using Ares.PagueAres.Domain.Enums;
using Ares.PagueAres.Infrastructure;
using Ares.PagueAres.Infrastructure.Models;
using Ares.PagueAres.Tests.Integration.Infrastructure;
using ClosedXML.Excel;
using System.Net;
using System.Text.Json;

namespace Ares.PagueAres.Tests.Integration.Controllers
{
    /// <summary>
    /// Excel legado <c>Pagamentos.xlsx</c> (ExportController.ExportPaymentList), gerado com ClosedXML:
    /// as mesmas 11 colunas na mesma ordem do relatório antigo, com os cabeçalhos na caixa da planilha nova
    /// (<c>PlanilhaExportacao</c>), Valor numérico e datas como data de verdade, "Nº do documento" vazio no RDDV
    /// e o filtro de parceiro sem diferenciar maiúsculas de minúsculas.
    /// </summary>
    public class ExportLegadoExcelTests : IClassFixture<PagueAresWebAppFactory>
    {
        private const int IdSolicitacaoParceiroMinusculo = 400;
        private const int IdSolicitacaoOutroParceiro = 401;
        private const int IdSolicitacaoDocumentoParceiro = 402;
        private const int IdRddv = 410;

        private static readonly string[] ColunasEsperadas =
        [
            "Código", "Tipo", "Data de inclusão", "Data de vencimento", "Nº do documento", "Solicitante",
            "Parceiro", "Valor", "Status gestor", "Status contábil", "Status financeiro"
        ];

        private readonly PagueAresWebAppFactory _factory;

        public ExportLegadoExcelTests(PagueAresWebAppFactory factory)
        {
            _factory = factory;
            _factory.SeedDatabase(ctx =>
            {
                if (!ctx.Solicitacaos.Any())
                {
                    PagueAresWebAppFactory.SeedBaseData(ctx);
                    SeedCenario(ctx);
                }
            });
        }

        private static Solicitacao NovaSolicitacao(int id, string nomeParceiro, string documentoParceiro, decimal valor,
            DateTime dataSolicitacao, DateTime? vencimento)
        {
            return new Solicitacao
            {
                IdSolicitacao = id,
                IdUsuario = 3,
                IdDepartamento = 1,
                DataSolicitacao = dataSolicitacao,
                DataVencimento = vencimento,
                NumeroDocumento = "NF-" + id,
                Descricao = "Teste do Excel legado",
                Valor = valor,
                FormaPagamento = 1,
                CentroCusto = "",
                Projeto = "",
                Pca = "",
                NumDocParceiro = documentoParceiro,
                CodigoParceiro = "",
                NomeParceiro = nomeParceiro,
                BancoParceiro = "",
                AgenciaParceiro = "",
                ContaParceiro = "",
                Observacao = "",
                ObservacaoGestor = "",
                ObservacaoSetor = "",
                TipoAutorizacao = 0,
                TipoSolicitacao = 2,
                TipoPagamento = 1,
                Rascunho = false,
                Cancelado = false,
                StatusGestor = 2,
                StatusFinanceiro = 1,
                StatusContabilidade = 1,
                AprovadoGestor = true
            };
        }

        private static void SeedCenario(PagueAresContext ctx)
        {
            ctx.Solicitacaos.AddRange(
                NovaSolicitacao(IdSolicitacaoParceiroMinusculo, "fornecedor fictício ltda", "11222333000144", 1234.56m,
                    new DateTime(2026, 11, 3), new DateTime(2026, 12, 10)),
                NovaSolicitacao(IdSolicitacaoOutroParceiro, "Outra Empresa Fictícia", "55666777000188", 99m,
                    new DateTime(2026, 11, 4), null),
                NovaSolicitacao(IdSolicitacaoDocumentoParceiro, "Terceira Empresa Fictícia", "ABC-123/XYZ", 10m,
                    new DateTime(2026, 11, 5), null));

            ctx.Rddv.Add(new Rddv
            {
                IdRelatorio = IdRddv,
                IdUsuario = 3,
                IdDepartamento = 1,
                NomeFuncionario = "Funcionário Fictício",
                CPF = "00000000000",
                Finalidade = "Teste do Excel legado",
                TipoRelatorio = 2,
                Moeda = "BRL",
                TipoViagem = 1,
                LocalViagem = "Cidade Fictícia",
                CentroCusto = "",
                Projeto = "",
                PCA = "",
                Observacao = "",
                ObservacaoGestor = "",
                ObservacaoSetor = "",
                Banco = "",
                Agencia = "",
                Conta = "",
                TipoAutorizacao = 0,
                DataCadastro = new DateTime(2026, 11, 6),
                Rascunho = false,
                Cancelado = false,
                StatusGestor = 2,
                StatusFinanceiro = 2,
                StatusContabilidade = 2,
                AprovadoGestor = true
            });

            ctx.SaveChanges();
        }

        private static async Task<byte[]> ExportarAsync(HttpClient client, string sFilter)
        {
            var respostaChave = await client.PostAsync(
                $"/api/preview/generate-preview-key?entityId=0&requestType={(int)EnumRequestType.Exportacao}", null);
            respostaChave.StatusCode.Should().Be(HttpStatusCode.OK);

            using var json = JsonDocument.Parse(await respostaChave.Content.ReadAsStringAsync());
            var key = json.RootElement.GetProperty("data").GetProperty("previewKey").GetString()!;

            var resposta = await client.GetAsync(
                $"/api/export/payments?sFilter={Uri.EscapeDataString(sFilter)}&userId=1&key={Uri.EscapeDataString(key)}");
            resposta.StatusCode.Should().Be(HttpStatusCode.OK);

            return await resposta.Content.ReadAsByteArrayAsync();
        }

        private HttpClient ClienteADM() => _factory.GetAuthenticatedClient(1, "Admin", "ADM");

        private static IXLRow? LinhaDoCodigo(IXLWorksheet planilha, string codigo, string tipoComecaCom) =>
            planilha.RowsUsed().Skip(1).FirstOrDefault(r =>
                r.Cell(1).GetString() == codigo && r.Cell(2).GetString().StartsWith(tipoComecaCom, StringComparison.Ordinal));

        [Fact]
        public async Task ExcelLegado_TemAs11ColunasNaMesmaOrdemComOsRotulosDaPlanilhaNova()
        {
            var conteudo = await ExportarAsync(ClienteADM(), "{}");

            using var workbook = new XLWorkbook(new MemoryStream(conteudo));
            var planilha = workbook.Worksheet("Pagamentos");

            var cabecalho = planilha.Row(1).CellsUsed().Select(c => c.GetString()).ToList();
            cabecalho.Should().Equal(ColunasEsperadas);
            planilha.Row(1).Cell(1).Style.Font.Bold.Should().BeTrue("o cabeçalho continua em negrito");
        }

        [Fact]
        public async Task ExcelLegado_ValorEhNumericoEDatasSaoDatas()
        {
            var conteudo = await ExportarAsync(ClienteADM(), "{}");

            using var workbook = new XLWorkbook(new MemoryStream(conteudo));
            var planilha = workbook.Worksheet("Pagamentos");

            var linha = LinhaDoCodigo(planilha, IdSolicitacaoParceiroMinusculo.ToString(), "Pagamento");
            linha.Should().NotBeNull("a solicitação aprovada pelo gestor entra no relatório");

            var valor = linha!.Cell(8);
            valor.DataType.Should().Be(XLDataType.Number);
            valor.GetDouble().Should().Be(1234.56);

            var inclusao = linha.Cell(3);
            inclusao.DataType.Should().Be(XLDataType.DateTime);
            inclusao.GetDateTime().Should().Be(new DateTime(2026, 11, 3));

            var vencimento = linha.Cell(4);
            vencimento.DataType.Should().Be(XLDataType.DateTime);
            vencimento.GetDateTime().Should().Be(new DateTime(2026, 12, 10));

            linha.Cell(5).GetString().Should().Be("NF-" + IdSolicitacaoParceiroMinusculo);
            linha.Cell(9).GetString().Should().Be("Autorizado");
            linha.Cell(10).GetString().Should().Be("Aguardando Lançamento");
            linha.Cell(11).GetString().Should().Be("Aguardando Autorização");
        }

        [Fact]
        public async Task ExcelLegado_SemVencimento_DeixaACelulaVazia()
        {
            var conteudo = await ExportarAsync(ClienteADM(), "{}");

            using var workbook = new XLWorkbook(new MemoryStream(conteudo));
            var planilha = workbook.Worksheet("Pagamentos");

            var linha = LinhaDoCodigo(planilha, IdSolicitacaoOutroParceiro.ToString(), "Pagamento");
            linha.Should().NotBeNull();
            linha!.Cell(4).IsEmpty().Should().BeTrue("sem vencimento a célula fica vazia, sem texto");
        }

        [Fact]
        public async Task ExcelLegado_Rddv_TemTipoRotuladoEValorNumerico()
        {
            var conteudo = await ExportarAsync(ClienteADM(), "{}");

            using var workbook = new XLWorkbook(new MemoryStream(conteudo));
            var planilha = workbook.Worksheet("Pagamentos");

            var linha = LinhaDoCodigo(planilha, IdRddv.ToString(), "RDDV");
            linha.Should().NotBeNull("o RDDV aprovado pelo gestor entra no relatório");

            linha!.Cell(2).GetString().Should().Be("RDDV - Prestação de Contas");
            linha.Cell(3).DataType.Should().Be(XLDataType.DateTime);
            linha.Cell(6).GetString().Should().Be("Solicitante");
            linha.Cell(7).GetString().Should().Be("Solicitante", "no RDDV o parceiro é o próprio solicitante");
            linha.Cell(8).DataType.Should().Be(XLDataType.Number);
            linha.Cell(9).GetString().Should().Be("Autorizado");
            linha.Cell(10).GetString().Should().Be("Lançado");
            linha.Cell(11).GetString().Should().Be("Autorizado");
        }

        [Fact]
        public async Task ExcelLegado_Rddv_NumeroDoDocumentoFicaVazio()
        {
            var conteudo = await ExportarAsync(ClienteADM(), "{}");

            using var workbook = new XLWorkbook(new MemoryStream(conteudo));
            var planilha = workbook.Worksheet("Pagamentos");

            var linha = LinhaDoCodigo(planilha, IdRddv.ToString(), "RDDV");
            linha.Should().NotBeNull();
            linha!.Cell(5).GetString().Should().BeEmpty("o RDDV não tem número de documento, e a célula não leva \"#\"");
        }

        [Fact]
        public async Task ExcelLegado_FiltroPartnerNameEmMaiusculas_EncontraParceiroEmMinusculas()
        {
            var conteudo = await ExportarAsync(ClienteADM(), "{\"PartnerName\":\"FORNECEDOR FICTÍCIO\"}");

            using var workbook = new XLWorkbook(new MemoryStream(conteudo));
            var planilha = workbook.Worksheet("Pagamentos");

            var codigos = planilha.Column(1).CellsUsed().Skip(1).Select(c => c.GetString()).ToList();
            codigos.Should().Contain(IdSolicitacaoParceiroMinusculo.ToString());
            codigos.Should().NotContain(IdSolicitacaoOutroParceiro.ToString());
        }

        [Fact]
        public async Task ExcelLegado_FiltroPartnerDocumentEmMinusculas_EncontraDocumentoEmMaiusculas()
        {
            var conteudo = await ExportarAsync(ClienteADM(), "{\"PartnerDocument\":\"abc-123\"}");

            using var workbook = new XLWorkbook(new MemoryStream(conteudo));
            var planilha = workbook.Worksheet("Pagamentos");

            var codigos = planilha.Column(1).CellsUsed().Skip(1).Select(c => c.GetString()).ToList();
            codigos.Should().Contain(IdSolicitacaoDocumentoParceiro.ToString());
            codigos.Should().NotContain(IdSolicitacaoParceiroMinusculo.ToString());
        }
    }
}
