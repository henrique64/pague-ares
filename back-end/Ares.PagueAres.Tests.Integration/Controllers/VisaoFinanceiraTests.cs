using Ares.PagueAres.Domain.Enums;
using Ares.PagueAres.Infrastructure;
using Ares.PagueAres.Infrastructure.Models;
using Ares.PagueAres.Tests.Integration.Infrastructure;
using System.Net;
using System.Text.Json;

namespace Ares.PagueAres.Tests.Integration.Controllers
{
    /// <summary>
    /// Aba Financeiro/Contábil da tela de pagamentos (<c>GET /api/listing</c> com <c>viewMode=FinancialView</c>):
    /// Financeiro e Administrador veem todos os títulos aprovados pelo gestor, inclusive os atribuídos a outro usuário;
    /// os demais perfis (Contabilidade, por exemplo) veem só os atribuídos a eles. Rascunho, cancelado e título não
    /// aprovado pelo gestor nunca entram.
    /// </summary>
    public class VisaoFinanceiraTests : IClassFixture<PagueAresWebAppFactory>
    {
        // Usuários do SeedBaseData: 1 = ADM, 3 = solicitante, 4 = FIN, 5 = CON.
        private const int IdSolicitacaoAtribuidaAoFinanceiro = 600;
        private const int IdSolicitacaoAtribuidaAContabilidade = 601;
        private const int IdSolicitacaoSemAtribuicao = 602;
        private const int IdSolicitacaoRascunho = 603;
        private const int IdSolicitacaoCancelada = 604;
        private const int IdSolicitacaoNaoAprovadaPeloGestor = 605;
        private const int IdRddvAtribuidoAoFinanceiro = 610;
        private const int IdRddvAtribuidoAContabilidade = 611;

        private readonly PagueAresWebAppFactory _factory;

        public VisaoFinanceiraTests(PagueAresWebAppFactory factory)
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

        private static Solicitacao NovaSolicitacao(int id, int? idUsuarioAtribuido, bool aprovadaPeloGestor = true,
            bool rascunho = false, bool cancelada = false)
        {
            return new Solicitacao
            {
                IdSolicitacao = id,
                IdUsuario = 3,
                IdUsuarioAtribuido = idUsuarioAtribuido,
                IdDepartamento = 1,
                DataSolicitacao = DateTime.Now,
                NumeroDocumento = "NF-" + id,
                Descricao = "Teste da visão financeira",
                Valor = 100m,
                FormaPagamento = 1,
                CentroCusto = "",
                Projeto = "",
                Pca = "",
                NumDocParceiro = "",
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
                Rascunho = rascunho,
                Cancelado = cancelada,
                StatusGestor = aprovadaPeloGestor ? 2 : 1,
                StatusFinanceiro = 1,
                StatusContabilidade = 1,
                AprovadoGestor = aprovadaPeloGestor ? true : null
            };
        }

        private static Rddv NovoRddv(int id, int? idUsuarioAtribuido)
        {
            return new Rddv
            {
                IdRelatorio = id,
                IdUsuario = 3,
                IdUsuarioAtribuido = idUsuarioAtribuido,
                IdDepartamento = 1,
                NomeFuncionario = "Funcionário Fictício",
                CPF = "00000000000",
                Finalidade = "Teste da visão financeira",
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
                DataCadastro = DateTime.Now,
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
                NovaSolicitacao(IdSolicitacaoAtribuidaAoFinanceiro, idUsuarioAtribuido: 4),
                NovaSolicitacao(IdSolicitacaoAtribuidaAContabilidade, idUsuarioAtribuido: 5),
                NovaSolicitacao(IdSolicitacaoSemAtribuicao, idUsuarioAtribuido: null),
                NovaSolicitacao(IdSolicitacaoRascunho, idUsuarioAtribuido: 1, rascunho: true),
                NovaSolicitacao(IdSolicitacaoCancelada, idUsuarioAtribuido: 1, cancelada: true),
                NovaSolicitacao(IdSolicitacaoNaoAprovadaPeloGestor, idUsuarioAtribuido: 1, aprovadaPeloGestor: false));

            ctx.Rddv.AddRange(
                NovoRddv(IdRddvAtribuidoAoFinanceiro, idUsuarioAtribuido: 4),
                NovoRddv(IdRddvAtribuidoAContabilidade, idUsuarioAtribuido: 5));

            ctx.SaveChanges();
        }

        /// <summary>
        /// Títulos da aba Financeiro/Contábil para o perfil do cliente, como "S&lt;código&gt;" (solicitação) e
        /// "R&lt;código&gt;" (RDDV).
        /// </summary>
        private static async Task<List<string>> ListarVisaoFinanceiraAsync(HttpClient client)
        {
            var resposta = await client.GetAsync($"/api/listing?viewMode={(int)PaymentListView.FinancialView}&page=1&pageSize=1000");
            resposta.StatusCode.Should().Be(HttpStatusCode.OK);

            using var json = JsonDocument.Parse(await resposta.Content.ReadAsStringAsync());
            json.RootElement.GetProperty("success").GetBoolean().Should().BeTrue();

            return json.RootElement.GetProperty("data").EnumerateArray()
                .Select(t => (t.GetProperty("idTipo").GetInt32() == 0 ? "S" : "R") + t.GetProperty("codigo").GetInt32())
                .ToList();
        }

        private static readonly string[] TodosOsAprovados =
        [
            "S" + IdSolicitacaoAtribuidaAoFinanceiro, "S" + IdSolicitacaoAtribuidaAContabilidade, "S" + IdSolicitacaoSemAtribuicao,
            "R" + IdRddvAtribuidoAoFinanceiro, "R" + IdRddvAtribuidoAContabilidade
        ];

        [Fact]
        public async Task VisaoFinanceira_Administrador_VeOsAprovadosAtribuidosAOutroUsuario()
        {
            var titulos = await ListarVisaoFinanceiraAsync(_factory.GetAuthenticatedClient(1, "Admin", "ADM"));

            titulos.Should().BeEquivalentTo(TodosOsAprovados,
                "o Administrador vê na aba Financeiro/Contábil o mesmo conjunto que o Financeiro, inclusive o que está atribuído a outro usuário");
        }

        [Fact]
        public async Task VisaoFinanceira_Financeiro_VeTodosOsAprovados()
        {
            var titulos = await ListarVisaoFinanceiraAsync(_factory.GetAuthenticatedClient(4, "Financeiro", "FIN"));

            titulos.Should().BeEquivalentTo(TodosOsAprovados,
                "o Financeiro vê todos os títulos aprovados pelo gestor, sem rascunho e sem cancelado");
        }

        [Fact]
        public async Task VisaoFinanceira_Contabilidade_SoVeOsAtribuidosAEla()
        {
            var titulos = await ListarVisaoFinanceiraAsync(_factory.GetAuthenticatedClient(5, "Contabilidade", "CON"));

            titulos.Should().BeEquivalentTo(
                new[] { "S" + IdSolicitacaoAtribuidaAContabilidade, "R" + IdRddvAtribuidoAContabilidade },
                "sem perfil Financeiro nem Administrador, a aba mostra só os títulos atribuídos ao usuário");
        }
    }
}
