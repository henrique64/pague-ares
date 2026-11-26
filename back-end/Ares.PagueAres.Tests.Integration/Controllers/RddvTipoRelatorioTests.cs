using Ares.PagueAres.Domain.Dtos;
using Ares.PagueAres.Domain.Enums;
using Ares.PagueAres.Infrastructure;
using Ares.PagueAres.Infrastructure.Models;
using Ares.PagueAres.Tests.Integration.Infrastructure;
using ClosedXML.Excel;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using System.Net;
using System.Text;
using System.Text.Json;

namespace Ares.PagueAres.Tests.Integration.Controllers
{
    /// <summary>
    /// Tipo de relatório do RDDV (1 Adiantamento, 2 Prestação de Contas, 3 Reembolso):
    /// <list type="bullet">
    /// <item><c>POST /api/rddv</c> recusa com 400 e mensagem o RDDV que entra no fluxo de aprovação (criado ou enviado
    /// fora do rascunho) sem um tipo de 1 a 3, e qualquer tipo fora de 0 a 3. O rascunho pode ficar com 0 (tipo ainda não
    /// escolhido), porque a tela salva o rascunho sem validar; e o RDDV já enviado com 0, gravado antes desta regra,
    /// continua podendo seguir o fluxo enquanto o tipo não muda.</item>
    /// <item>No Excel <c>Pagamentos.xlsx</c>, o RDDV que já está gravado com tipo 0 sai como RDDV, e não como
    /// "Pagamento", e fica de fora do filtro de documento do parceiro, que é só das solicitações.</item>
    /// </list>
    /// </summary>
    public class RddvTipoRelatorioTests : IClassFixture<PagueAresWebAppFactory>
    {
        // Usuários do SeedBaseData: 1 = ADM, 3 = solicitante.
        private const int IdSolicitacaoComDocumentoDoParceiro = 920;
        private const int IdRddvTipoZeroComOMesmoCodigo = 920;
        private const int IdRddvTipoZeroAprovado = 921;
        private const int IdRddvTipoZeroJaEnviado = 922;
        private const int IdRddvEnviadoComTipoValido = 923;
        private const int IdRascunhoSemTipo = 924;
        private const int IdOutroRascunhoSemTipo = 925;

        private const string DocumentoDoParceiro = "DOC-920-FICTICIO";

        private static readonly JsonSerializerOptions JsonOpts = new() { PropertyNameCaseInsensitive = true };

        private readonly PagueAresWebAppFactory _factory;

        public RddvTipoRelatorioTests(PagueAresWebAppFactory factory)
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

        private static Rddv NovoRddv(int id, int tipoRelatorio, bool rascunho, bool aprovadoPeloGestor)
        {
            return new Rddv
            {
                IdRelatorio = id,
                IdUsuario = 3,
                IdDepartamento = 1,
                NomeFuncionario = "Funcionário Fictício",
                CPF = "00000000000",
                Finalidade = "Teste do tipo de relatório",
                TipoRelatorio = tipoRelatorio,
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
                DataCadastro = DateTime.Now,
                Rascunho = rascunho,
                Cancelado = false,
                StatusGestor = aprovadoPeloGestor ? 2 : 1,
                StatusFinanceiro = 1,
                StatusContabilidade = 1,
                AprovadoGestor = aprovadoPeloGestor ? true : null
            };
        }

        private static void SeedCenario(PagueAresContext ctx)
        {
            ctx.Solicitacaos.Add(new Solicitacao
            {
                IdSolicitacao = IdSolicitacaoComDocumentoDoParceiro,
                IdUsuario = 3,
                IdDepartamento = 1,
                DataSolicitacao = DateTime.Now,
                NumeroDocumento = "NF-" + IdSolicitacaoComDocumentoDoParceiro,
                Descricao = "Teste do tipo de relatório",
                Valor = 100m,
                FormaPagamento = 1,
                CentroCusto = "",
                Projeto = "",
                Pca = "",
                NumDocParceiro = DocumentoDoParceiro,
                CodigoParceiro = "",
                NomeParceiro = "Parceiro Fictício",
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
            });

            ctx.Rddv.AddRange(
                // RDDVs gravados com tipo 0 antes da validação (legado), já no fluxo e aprovados pelo gestor.
                NovoRddv(IdRddvTipoZeroComOMesmoCodigo, tipoRelatorio: 0, rascunho: false, aprovadoPeloGestor: true),
                NovoRddv(IdRddvTipoZeroAprovado, tipoRelatorio: 0, rascunho: false, aprovadoPeloGestor: true),
                // RDDV legado com tipo 0 enviado e ainda pendente no gestor.
                NovoRddv(IdRddvTipoZeroJaEnviado, tipoRelatorio: 0, rascunho: false, aprovadoPeloGestor: false),
                NovoRddv(IdRddvEnviadoComTipoValido, tipoRelatorio: 1, rascunho: false, aprovadoPeloGestor: false),
                NovoRddv(IdRascunhoSemTipo, tipoRelatorio: 0, rascunho: true, aprovadoPeloGestor: false),
                NovoRddv(IdOutroRascunhoSemTipo, tipoRelatorio: 0, rascunho: true, aprovadoPeloGestor: false));

            ctx.SaveChanges();
        }

        private static RddvDto NovoDto(int tipoRelatorio, bool rascunho)
        {
            return new RddvDto
            {
                IdRelatorio = 0,
                IdUsuario = 3,
                IdDepartamento = 1,
                NomeFuncionario = "Funcionário Fictício",
                CPF = "00000000000",
                Finalidade = "RDDV criado pelo teste do tipo de relatório",
                TipoRelatorio = tipoRelatorio,
                Moeda = "BRL",
                TipoViagem = 1,
                LocalViagem = "Cidade Fictícia",
                CentroCusto = "",
                Projeto = "",
                PCA = "",
                Observacao = "",
                ObservacaoGestor = "",
                ObservacaoSetor = "",
                TipoAutorizacao = 0,
                Banco = "",
                Agencia = "",
                Conta = "",
                DataCadastro = DateTime.Now,
                Rascunho = rascunho,
                Cancelado = false,
                Documentos = [],
                Despesas = []
            };
        }

        /// <summary>DTO de edição montado a partir do RDDV gravado, como a tela faz ao reabrir o relatório.</summary>
        private RddvDto DtoDoGravado(int id)
        {
            var r = LerRddv(id);

            return new RddvDto
            {
                IdRelatorio = r.IdRelatorio,
                IdUsuario = r.IdUsuario,
                IdDepartamento = r.IdDepartamento,
                NomeFuncionario = r.NomeFuncionario,
                CPF = r.CPF,
                Finalidade = r.Finalidade,
                TipoRelatorio = r.TipoRelatorio,
                Moeda = r.Moeda,
                TipoViagem = r.TipoViagem,
                LocalViagem = r.LocalViagem,
                CentroCusto = r.CentroCusto,
                Projeto = r.Projeto,
                PCA = r.PCA,
                Observacao = r.Observacao,
                ObservacaoGestor = r.ObservacaoGestor,
                ObservacaoSetor = r.ObservacaoSetor,
                TipoAutorizacao = r.TipoAutorizacao,
                Banco = r.Banco,
                Agencia = r.Agencia,
                Conta = r.Conta,
                DataCadastro = r.DataCadastro,
                StatusGestor = r.StatusGestor,
                StatusFinanceiro = r.StatusFinanceiro,
                StatusContabilidade = r.StatusContabilidade,
                AprovadoGestor = r.AprovadoGestor,
                AprovadoSetor = r.AprovadoSetor,
                Rascunho = r.Rascunho,
                Cancelado = r.Cancelado,
                Documentos = [],
                Despesas = []
            };
        }

        private Rddv LerRddv(int id)
        {
            using var scope = _factory.Services.CreateScope();
            var ctx = scope.ServiceProvider.GetRequiredService<PagueAresContext>();
            return ctx.Rddv.AsNoTracking().First(r => r.IdRelatorio == id);
        }

        private int ContarRddvsComFinalidade(string finalidade)
        {
            using var scope = _factory.Services.CreateScope();
            var ctx = scope.ServiceProvider.GetRequiredService<PagueAresContext>();
            return ctx.Rddv.AsNoTracking().Count(r => r.Finalidade == finalidade);
        }

        private async Task<(HttpStatusCode Status, bool Sucesso, string Mensagem)> GravarAsync(RddvDto dto)
        {
            var client = _factory.GetAuthenticatedClient(3, "Solicitante", "SOL");
            var resposta = await client.PostAsync("/api/rddv",
                new StringContent(JsonSerializer.Serialize(dto), Encoding.UTF8, "application/json"));
            var corpo = await resposta.Content.ReadAsStringAsync();

            using var json = JsonDocument.Parse(corpo);
            var raiz = json.RootElement;
            var sucesso = raiz.TryGetProperty("success", out var s) && s.ValueKind == JsonValueKind.True;
            var mensagem = raiz.TryGetProperty("message", out var m) && m.ValueKind == JsonValueKind.String ? m.GetString()! : corpo;

            return (resposta.StatusCode, sucesso, mensagem);
        }

        // ---------------------------------------------------------------------------------------------------------
        // POST /api/rddv: validação do tipo de relatório
        // ---------------------------------------------------------------------------------------------------------

        [Fact]
        public async Task PostRddv_NovoEnviadoComTipoRelatorioZero_Responde400ComMensagemENaoGrava()
        {
            var dto = NovoDto(tipoRelatorio: 0, rascunho: false);
            dto.Finalidade = "Novo enviado com tipo 0";

            var (status, sucesso, mensagem) = await GravarAsync(dto);

            status.Should().Be(HttpStatusCode.BadRequest, $"a API respondeu: {mensagem}");
            sucesso.Should().BeFalse();
            mensagem.Should().Contain("Tipo de relatório");
            ContarRddvsComFinalidade(dto.Finalidade).Should().Be(0, "o RDDV recusado não é gravado");
        }

        [Theory]
        [InlineData(4, false)]
        [InlineData(-1, false)]
        [InlineData(99, false)]
        [InlineData(4, true)]
        [InlineData(-1, true)]
        public async Task PostRddv_NovoComTipoRelatorioForaDaFaixa_Responde400ENaoGrava(int tipoRelatorio, bool rascunho)
        {
            var dto = NovoDto(tipoRelatorio, rascunho);
            dto.Finalidade = $"Novo com tipo {tipoRelatorio}, rascunho {rascunho}";

            var (status, sucesso, mensagem) = await GravarAsync(dto);

            status.Should().Be(HttpStatusCode.BadRequest, $"a API respondeu: {mensagem}");
            sucesso.Should().BeFalse();
            mensagem.Should().Contain("Tipo de relatório");
            ContarRddvsComFinalidade(dto.Finalidade).Should().Be(0, "o RDDV recusado não é gravado");
        }

        [Fact]
        public async Task PostRddv_EdicaoTrocandoOTipoParaZero_Responde400ENaoGrava()
        {
            var dto = DtoDoGravado(IdRddvEnviadoComTipoValido);
            dto.TipoRelatorio = 0;

            var (status, sucesso, mensagem) = await GravarAsync(dto);

            status.Should().Be(HttpStatusCode.BadRequest, $"a API respondeu: {mensagem}");
            sucesso.Should().BeFalse();
            LerRddv(IdRddvEnviadoComTipoValido).TipoRelatorio.Should().Be(1, "a edição recusada não grava o tipo");
        }

        [Fact]
        public async Task PostRddv_EnvioDeRascunhoSemTipoRelatorio_Responde400EContinuaRascunho()
        {
            var dto = DtoDoGravado(IdRascunhoSemTipo);
            dto.Rascunho = false;

            var (status, sucesso, mensagem) = await GravarAsync(dto);

            status.Should().Be(HttpStatusCode.BadRequest, $"a API respondeu: {mensagem}");
            sucesso.Should().BeFalse();
            mensagem.Should().Contain("Tipo de relatório");
            LerRddv(IdRascunhoSemTipo).Rascunho.Should().BeTrue("o envio recusado não tira o RDDV do rascunho");
        }

        [Fact]
        public async Task PostRddv_NovoRascunhoSemTipoRelatorio_EGravado()
        {
            var dto = NovoDto(tipoRelatorio: 0, rascunho: true);
            dto.Finalidade = "Novo rascunho sem tipo";

            var (status, sucesso, mensagem) = await GravarAsync(dto);

            status.Should().Be(HttpStatusCode.OK);
            sucesso.Should().BeTrue($"a tela salva o rascunho sem validar, antes de o tipo ser escolhido; a API respondeu: {mensagem}");
            ContarRddvsComFinalidade(dto.Finalidade).Should().Be(1);
        }

        [Fact]
        public async Task PostRddv_RascunhoSemTipoRelatorioSalvoDeNovoComoRascunho_EGravado()
        {
            var dto = DtoDoGravado(IdOutroRascunhoSemTipo);
            dto.Observacao = "Rascunho salvo de novo";

            var (status, sucesso, mensagem) = await GravarAsync(dto);

            status.Should().Be(HttpStatusCode.OK);
            sucesso.Should().BeTrue($"a API respondeu: {mensagem}");
            LerRddv(IdOutroRascunhoSemTipo).Observacao.Should().Be("Rascunho salvo de novo");
        }

        [Theory]
        [InlineData(1)]
        [InlineData(2)]
        [InlineData(3)]
        public async Task PostRddv_NovoEnviadoComTipoRelatorioValido_EGravado(int tipoRelatorio)
        {
            var dto = NovoDto(tipoRelatorio, rascunho: false);
            dto.Finalidade = $"Novo enviado com tipo {tipoRelatorio}";

            var (status, sucesso, mensagem) = await GravarAsync(dto);

            status.Should().Be(HttpStatusCode.OK);
            sucesso.Should().BeTrue($"a API respondeu: {mensagem}");
            ContarRddvsComFinalidade(dto.Finalidade).Should().Be(1);
        }

        [Fact]
        public async Task PostRddv_EdicaoDeRddvJaEnviadoComTipoZeroSemTrocarOTipo_EAceita()
        {
            var dto = DtoDoGravado(IdRddvTipoZeroJaEnviado);
            dto.Observacao = "Edição do legado com tipo 0";

            var (status, sucesso, mensagem) = await GravarAsync(dto);

            status.Should().Be(HttpStatusCode.OK);
            sucesso.Should().BeTrue(
                $"o RDDV gravado com tipo 0 antes da regra continua podendo seguir o fluxo enquanto o tipo não muda; a API respondeu: {mensagem}");
            LerRddv(IdRddvTipoZeroJaEnviado).Observacao.Should().Be("Edição do legado com tipo 0");
        }

        // ---------------------------------------------------------------------------------------------------------
        // Excel Pagamentos.xlsx: RDDV gravado com tipo 0
        // ---------------------------------------------------------------------------------------------------------

        private async Task<IXLWorksheet> ExportarAsync(string sFilter)
        {
            var client = _factory.GetAuthenticatedClient(1, "Admin", "ADM");

            var respostaChave = await client.PostAsync(
                $"/api/preview/generate-preview-key?entityId=0&requestType={(int)EnumRequestType.Exportacao}", null);
            respostaChave.StatusCode.Should().Be(HttpStatusCode.OK);

            using var json = JsonDocument.Parse(await respostaChave.Content.ReadAsStringAsync());
            var key = json.RootElement.GetProperty("data").GetProperty("previewKey").GetString()!;

            var resposta = await client.GetAsync(
                $"/api/export/payments?sFilter={Uri.EscapeDataString(sFilter)}&userId=1&key={Uri.EscapeDataString(key)}");
            resposta.StatusCode.Should().Be(HttpStatusCode.OK);

            var workbook = new XLWorkbook(new MemoryStream(await resposta.Content.ReadAsByteArrayAsync()));
            return workbook.Worksheet("Pagamentos");
        }

        private static List<IXLRow> LinhasDoCodigo(IXLWorksheet planilha, int codigo) =>
            planilha.RowsUsed().Skip(1).Where(r => r.Cell(1).GetString() == codigo.ToString()).ToList();

        [Fact]
        public async Task Planilha_RddvGravadoComTipoRelatorioZero_SaiComoRddv()
        {
            var planilha = await ExportarAsync("{}");

            var linhas = LinhasDoCodigo(planilha, IdRddvTipoZeroAprovado);
            linhas.Should().ContainSingle("o RDDV aprovado pelo gestor entra na planilha");

            var linha = linhas[0];
            linha.Cell(2).GetString().Should().Be("RDDV - Código 0",
                "o RDDV com tipo 0 sai como RDDV, com o código do tipo desconhecido, e não como Pagamento");
            linha.Cell(5).GetString().Should().BeEmpty("o RDDV não tem número de documento");
            linha.Cell(7).GetString().Should().Be("Solicitante", "no RDDV o parceiro é o próprio solicitante");
        }

        [Fact]
        public async Task Planilha_FiltroDocumentoDoParceiro_DeixaDeForaORddvComTipoZeroDeMesmoCodigo()
        {
            var planilha = await ExportarAsync($"{{\"partnerDocument\":\"{DocumentoDoParceiro}\"}}");

            var titulos = planilha.RowsUsed().Skip(1)
                .Select(r => r.Cell(1).GetString() + " " + r.Cell(2).GetString())
                .ToList();

            titulos.Should().Equal(new[] { IdSolicitacaoComDocumentoDoParceiro + " Pagamento" },
                "o documento do parceiro é filtro só das solicitações; o RDDV de mesmo código fica de fora");
        }
    }
}
