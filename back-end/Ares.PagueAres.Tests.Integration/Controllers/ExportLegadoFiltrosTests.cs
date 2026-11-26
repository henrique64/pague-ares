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
    /// O Excel legado <c>Pagamentos.xlsx</c> (ExportController.ExportPaymentList) filtra com as mesmas regras da lista
    /// da aba Financeiro/Contábil (<c>GET /api/listing</c>): o mesmo filtro, pedido pelo mesmo usuário, traz os mesmos
    /// títulos nos dois. Isso vale para período com um lado só (solicitação e vencimento), para os filtros que o RDDV não
    /// tem (número do documento e nome do parceiro, que deixam os RDDVs de fora), para "atribuído" e para código zero.
    /// O documento do parceiro só existe na planilha e também deixa os RDDVs de fora.
    /// </summary>
    public class ExportLegadoFiltrosTests : IClassFixture<PagueAresWebAppFactory>
    {
        // Títulos aprovados pelo gestor: dois em outubro de 2026 e dois em novembro de 2026.
        private const int IdSolicitacaoOutubro = 700;
        private const int IdSolicitacaoNovembro = 701;
        private const int IdRddvOutubro = 710;
        private const int IdRddvNovembro = 711;

        // Fora do escopo da aba e da planilha: nunca aparecem.
        private const int IdSolicitacaoRascunho = 702;
        private const int IdSolicitacaoCancelada = 703;
        private const int IdRddvNaoAprovadoPeloGestor = 712;

        private readonly PagueAresWebAppFactory _factory;

        public ExportLegadoFiltrosTests(PagueAresWebAppFactory factory)
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

        private static Solicitacao NovaSolicitacao(int id, int idUsuario, int? idUsuarioAtribuido, DateTime dataSolicitacao,
            DateTime vencimento, string nomeParceiro, string documentoParceiro)
        {
            return new Solicitacao
            {
                IdSolicitacao = id,
                IdUsuario = idUsuario,
                IdUsuarioAtribuido = idUsuarioAtribuido,
                IdDepartamento = 1,
                DataSolicitacao = dataSolicitacao,
                DataVencimento = vencimento,
                NumeroDocumento = "NF-" + id,
                Descricao = "Teste dos filtros da planilha",
                Valor = 100m,
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

        private static Rddv NovoRddv(int id, int idUsuario, int? idUsuarioAtribuido, DateTime dataCadastro, DateTime vencimento)
        {
            return new Rddv
            {
                IdRelatorio = id,
                IdUsuario = idUsuario,
                IdUsuarioAtribuido = idUsuarioAtribuido,
                IdDepartamento = 1,
                NomeFuncionario = "Funcionário Fictício",
                CPF = "00000000000",
                Finalidade = "Teste dos filtros da planilha",
                TipoRelatorio = 3,
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
                DataCadastro = dataCadastro,
                DataVencimento = vencimento,
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
            var rascunho = NovaSolicitacao(IdSolicitacaoRascunho, 3, null, new DateTime(2026, 10, 5), new DateTime(2026, 10, 22),
                "Alfa Fictícia Ltda", "11111111000111");
            rascunho.Rascunho = true;

            var cancelada = NovaSolicitacao(IdSolicitacaoCancelada, 3, null, new DateTime(2026, 11, 5), new DateTime(2026, 12, 2),
                "Beta Fictícia SA", "22222222000122");
            cancelada.Cancelado = true;

            ctx.Solicitacaos.AddRange(
                NovaSolicitacao(IdSolicitacaoOutubro, idUsuario: 3, idUsuarioAtribuido: null,
                    new DateTime(2026, 10, 1), new DateTime(2026, 10, 20), "Alfa Fictícia Ltda", "11111111000111"),
                NovaSolicitacao(IdSolicitacaoNovembro, idUsuario: 6, idUsuarioAtribuido: 4,
                    new DateTime(2026, 11, 10), new DateTime(2026, 12, 5), "Beta Fictícia SA", "22222222000122"),
                rascunho,
                cancelada);

            var naoAprovado = NovoRddv(IdRddvNaoAprovadoPeloGestor, 3, null, new DateTime(2026, 10, 3), new DateTime(2026, 10, 23));
            naoAprovado.AprovadoGestor = null;
            naoAprovado.StatusGestor = 1;

            ctx.Rddv.AddRange(
                NovoRddv(IdRddvOutubro, idUsuario: 3, idUsuarioAtribuido: null, new DateTime(2026, 10, 2), new DateTime(2026, 10, 25)),
                NovoRddv(IdRddvNovembro, idUsuario: 6, idUsuarioAtribuido: 4, new DateTime(2026, 11, 12), new DateTime(2026, 12, 10)),
                naoAprovado);

            ctx.SaveChanges();
        }

        private HttpClient ClienteFIN() => _factory.GetAuthenticatedClient(4, "Financeiro", "FIN");

        /// <summary>Títulos do Pagamentos.xlsx, como "S&lt;código&gt;" (solicitação) e "R&lt;código&gt;" (RDDV).</summary>
        private static async Task<List<string>> TitulosDaPlanilhaAsync(HttpClient client, string sFilter)
        {
            var respostaChave = await client.PostAsync(
                $"/api/preview/generate-preview-key?entityId=0&requestType={(int)EnumRequestType.Exportacao}", null);
            respostaChave.StatusCode.Should().Be(HttpStatusCode.OK);

            using var json = JsonDocument.Parse(await respostaChave.Content.ReadAsStringAsync());
            var key = json.RootElement.GetProperty("data").GetProperty("previewKey").GetString()!;

            var resposta = await client.GetAsync(
                $"/api/export/payments?sFilter={Uri.EscapeDataString(sFilter)}&userId=4&key={Uri.EscapeDataString(key)}");
            resposta.StatusCode.Should().Be(HttpStatusCode.OK);

            using var workbook = new XLWorkbook(new MemoryStream(await resposta.Content.ReadAsByteArrayAsync()));
            var planilha = workbook.Worksheet("Pagamentos");

            return planilha.RowsUsed().Skip(1)
                .Select(r => (r.Cell(2).GetString().StartsWith("RDDV", StringComparison.Ordinal) ? "R" : "S") + r.Cell(1).GetString())
                .ToList();
        }

        /// <summary>Títulos da aba Financeiro/Contábil, no mesmo formato de <see cref="TitulosDaPlanilhaAsync"/>.</summary>
        private static async Task<List<string>> TitulosDaListaAsync(HttpClient client, string consulta)
        {
            var url = $"/api/listing?viewMode={(int)PaymentListView.FinancialView}&page=1&pageSize=1000";
            if (consulta.Length > 0) url += "&" + consulta;

            var resposta = await client.GetAsync(url);
            resposta.StatusCode.Should().Be(HttpStatusCode.OK);

            using var json = JsonDocument.Parse(await resposta.Content.ReadAsStringAsync());
            json.RootElement.GetProperty("success").GetBoolean().Should().BeTrue();

            return json.RootElement.GetProperty("data").EnumerateArray()
                .Select(t => (t.GetProperty("idTipo").GetInt32() == 0 ? "S" : "R") + t.GetProperty("codigo").GetInt32())
                .ToList();
        }

        [Theory]
        [InlineData("{}", "", "S700,S701,R710,R711")]
        [InlineData("{\"requestStart\":\"2026-11-01\"}", "requestStart=2026-11-01", "S701,R711")]
        [InlineData("{\"requestEnd\":\"2026-10-31\"}", "requestEnd=2026-10-31", "S700,R710")]
        [InlineData("{\"requestStart\":\"2026-10-01\",\"requestEnd\":\"2026-10-31\"}", "requestStart=2026-10-01&requestEnd=2026-10-31", "S700,R710")]
        [InlineData("{\"dueStart\":\"2026-12-01\"}", "dueStart=2026-12-01", "S701,R711")]
        [InlineData("{\"dueEnd\":\"2026-10-31\"}", "dueEnd=2026-10-31", "S700,R710")]
        [InlineData("{\"documentNumber\":\"NF-700\"}", "documentNumber=NF-700", "S700")]
        [InlineData("{\"partnerName\":\"ALFA\"}", "partnerName=ALFA", "S700")]
        [InlineData("{\"isAssigned\":true}", "isAssigned=true", "S701,R711")]
        [InlineData("{\"requesterId\":6}", "requesterId=6", "S701,R711")]
        [InlineData("{\"documentId\":0}", "documentId=0", "S700,S701,R710,R711")]
        [InlineData("{\"documentId\":710}", "documentId=710", "R710")]
        public async Task PlanilhaELista_ComOMesmoFiltro_TrazemOsMesmosTitulos(string sFilter, string consultaLista, string esperados)
        {
            var client = ClienteFIN();
            var titulosEsperados = esperados.Split(',');

            var daPlanilha = await TitulosDaPlanilhaAsync(client, sFilter);
            var daLista = await TitulosDaListaAsync(client, consultaLista);

            daLista.Should().BeEquivalentTo(titulosEsperados, $"a lista aplica o filtro {sFilter}");
            daPlanilha.Should().BeEquivalentTo(daLista, $"a planilha aplica o filtro {sFilter} com as mesmas regras da lista");
        }

        [Fact]
        public async Task Planilha_FiltroDocumentoDoParceiro_DeixaOsRddvsDeFora()
        {
            var daPlanilha = await TitulosDaPlanilhaAsync(ClienteFIN(), "{\"partnerDocument\":\"11111111\"}");

            daPlanilha.Should().BeEquivalentTo(new[] { "S" + IdSolicitacaoOutubro },
                "o RDDV não tem documento do parceiro, então sai, como sai nos filtros de nome do parceiro e de número do documento");
        }
    }
}
