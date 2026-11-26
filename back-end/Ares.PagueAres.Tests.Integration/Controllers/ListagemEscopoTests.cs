using Ares.PagueAres.Infrastructure;
using Ares.PagueAres.Infrastructure.Models;
using Ares.PagueAres.Tests.Integration.Infrastructure;
using System.Net;
using System.Text.Json;

namespace Ares.PagueAres.Tests.Integration.Controllers
{
    /// <summary>
    /// Escopo da listagem de pagamentos (<c>GET /api/listing</c>) por visão e por perfil:
    /// <list type="bullet">
    /// <item>sem <c>viewMode</c>, ou com <c>viewMode=0</c> (Undefined), a lista vale como "Minhas Solicitações": cada
    /// usuário recebe só os próprios títulos, qualquer que seja o perfil;</item>
    /// <item>o rascunho de outro usuário nunca aparece, em nenhuma visão e para nenhum perfil;</item>
    /// <item>o Dashboard (fila de títulos aprovados pelo gestor e sem atribuição) só devolve títulos para Financeiro,
    /// Contabilidade e Administrador, os perfis que veem a tela inicial;</item>
    /// <item>cada perfil recebe, em cada visão, exatamente o conjunto do escopo dele: a visão do gestor só traz os títulos
    /// de que o usuário é gestor; a visão financeira traz todos os aprovados pelo gestor ao Financeiro e ao
    /// Administrador, só os atribuídos a ela à Contabilidade, e nada aos demais perfis, mesmo com título atribuído a
    /// eles;</item>
    /// <item>um <c>viewMode</c> fora do enum é recusado com 400.</item>
    /// </list>
    /// </summary>
    public class ListagemEscopoTests : IClassFixture<PagueAresWebAppFactory>
    {
        // Usuários do SeedBaseData: 1 = ADM, 2 = GES, 3 = solicitante, 4 = FIN, 5 = CON, 6 = outro solicitante.
        private const int IdSolicitacaoDoSolicitanteAprovada = 900;
        private const int IdSolicitacaoDoSolicitanteRascunho = 901;
        private const int IdSolicitacaoDeOutroAprovada = 902;
        private const int IdSolicitacaoDeOutroRascunho = 903;
        private const int IdSolicitacaoDoGestor = 904;
        private const int IdSolicitacaoDaContabilidade = 905;
        private const int IdSolicitacaoDoAdministrador = 906;
        private const int IdSolicitacaoDeOutroAtribuidaAoSolicitante = 907;
        private const int IdSolicitacaoDeOutroAtribuidaAoGestor = 908;
        private const int IdSolicitacaoDeOutroCancelada = 909;
        private const int IdRddvDoSolicitanteAprovado = 910;
        private const int IdRddvDeOutroRascunho = 911;
        private const int IdRddvDoFinanceiro = 912;
        private const int IdRddvDeOutroComOutroGestor = 913;

        /// <summary>Títulos de cada usuário, como "S&lt;código&gt;" (solicitação) e "R&lt;código&gt;" (RDDV).</summary>
        private static readonly Dictionary<int, string[]> TitulosDoProprioUsuario = new()
        {
            [1] = ["S" + IdSolicitacaoDoAdministrador],
            [2] = ["S" + IdSolicitacaoDoGestor],
            [3] = ["S" + IdSolicitacaoDoSolicitanteAprovada, "S" + IdSolicitacaoDoSolicitanteRascunho, "R" + IdRddvDoSolicitanteAprovado],
            [4] = ["R" + IdRddvDoFinanceiro],
            [5] = ["S" + IdSolicitacaoDaContabilidade],
            [6] =
            [
                "S" + IdSolicitacaoDeOutroAprovada, "S" + IdSolicitacaoDeOutroRascunho, "S" + IdSolicitacaoDeOutroAtribuidaAoSolicitante,
                "S" + IdSolicitacaoDeOutroAtribuidaAoGestor, "S" + IdSolicitacaoDeOutroCancelada, "R" + IdRddvDeOutroRascunho,
                "R" + IdRddvDeOutroComOutroGestor
            ]
        };

        /// <summary>
        /// Visão do gestor 2: os títulos de que ele é gestor, menos rascunhos (S901, S903, R911) e cancelados (S909). O
        /// R913 é do gestor 1 e fica de fora.
        /// </summary>
        private static readonly string[] TitulosDoGestor2 =
        [
            "S" + IdSolicitacaoDoSolicitanteAprovada, "S" + IdSolicitacaoDeOutroAprovada, "S" + IdSolicitacaoDoGestor,
            "S" + IdSolicitacaoDaContabilidade, "S" + IdSolicitacaoDoAdministrador, "S" + IdSolicitacaoDeOutroAtribuidaAoSolicitante,
            "S" + IdSolicitacaoDeOutroAtribuidaAoGestor, "R" + IdRddvDoSolicitanteAprovado, "R" + IdRddvDoFinanceiro
        ];

        /// <summary>
        /// Visão financeira do Financeiro e do Administrador: todos os aprovados pelo gestor, menos rascunhos (S903, R911)
        /// e cancelados (S909), com ou sem atribuição.
        /// </summary>
        private static readonly string[] AprovadosDoFluxo =
        [
            "S" + IdSolicitacaoDoSolicitanteAprovada, "S" + IdSolicitacaoDeOutroAprovada, "S" + IdSolicitacaoDaContabilidade,
            "S" + IdSolicitacaoDeOutroAtribuidaAoSolicitante, "S" + IdSolicitacaoDeOutroAtribuidaAoGestor, "R" + IdRddvDoSolicitanteAprovado
        ];

        /// <summary>
        /// Visão financeira da Contabilidade (5): só o aprovado atribuído a ela. Os outros atribuídos a ela são rascunho
        /// (S903, R911) ou cancelado (S909).
        /// </summary>
        private static readonly string[] AtribuidosAContabilidade =
        [
            "S" + IdSolicitacaoDaContabilidade
        ];

        /// <summary>Fila do Dashboard: aprovados pelo gestor, sem atribuição, não negados, sem rascunho e sem cancelado.</summary>
        private static readonly string[] FilaDoDashboard =
        [
            "S" + IdSolicitacaoDoSolicitanteAprovada, "S" + IdSolicitacaoDeOutroAprovada, "R" + IdRddvDoSolicitanteAprovado
        ];

        private static readonly string[] RascunhosDoOutroSolicitante =
        [
            "S" + IdSolicitacaoDeOutroRascunho, "R" + IdRddvDeOutroRascunho
        ];

        private readonly PagueAresWebAppFactory _factory;

        public ListagemEscopoTests(PagueAresWebAppFactory factory)
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

        private static Solicitacao NovaSolicitacao(int id, int idUsuario, bool rascunho = false, bool aprovadaPeloGestor = false,
            int? idUsuarioAtribuido = null, bool cancelada = false)
        {
            return new Solicitacao
            {
                IdSolicitacao = id,
                IdUsuario = idUsuario,
                IdGestor = 2,
                IdUsuarioAtribuido = idUsuarioAtribuido,
                IdDepartamento = 1,
                DataSolicitacao = DateTime.Now,
                NumeroDocumento = "NF-" + id,
                Descricao = "Teste do escopo da listagem",
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

        private static Rddv NovoRddv(int id, int idUsuario, bool rascunho = false, bool aprovadoPeloGestor = false,
            int? idUsuarioAtribuido = null, int idGestor = 2)
        {
            return new Rddv
            {
                IdRelatorio = id,
                IdUsuario = idUsuario,
                IdGestor = idGestor,
                IdUsuarioAtribuido = idUsuarioAtribuido,
                IdDepartamento = 1,
                NomeFuncionario = "Funcionário Fictício",
                CPF = "00000000000",
                Finalidade = "Teste do escopo da listagem",
                TipoRelatorio = 1,
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
            ctx.Solicitacaos.AddRange(
                NovaSolicitacao(IdSolicitacaoDoSolicitanteAprovada, idUsuario: 3, aprovadaPeloGestor: true),
                NovaSolicitacao(IdSolicitacaoDoSolicitanteRascunho, idUsuario: 3, rascunho: true),
                NovaSolicitacao(IdSolicitacaoDeOutroAprovada, idUsuario: 6, aprovadaPeloGestor: true),
                // Rascunho alheio que, sem o filtro de rascunho, entraria nas visões do gestor, do financeiro e da
                // contabilidade: aprovado pelo gestor e atribuído à Contabilidade.
                NovaSolicitacao(IdSolicitacaoDeOutroRascunho, idUsuario: 6, rascunho: true, aprovadaPeloGestor: true, idUsuarioAtribuido: 5),
                NovaSolicitacao(IdSolicitacaoDoGestor, idUsuario: 2),
                NovaSolicitacao(IdSolicitacaoDaContabilidade, idUsuario: 5, aprovadaPeloGestor: true, idUsuarioAtribuido: 5),
                NovaSolicitacao(IdSolicitacaoDoAdministrador, idUsuario: 1),
                // Títulos alheios aprovados e atribuídos a perfis de fora da área financeira (o solicitante 3 e o gestor
                // 2), como ficavam quando a atribuição não conferia o destino: a visão financeira não os mostra a eles.
                NovaSolicitacao(IdSolicitacaoDeOutroAtribuidaAoSolicitante, idUsuario: 6, aprovadaPeloGestor: true, idUsuarioAtribuido: 3),
                NovaSolicitacao(IdSolicitacaoDeOutroAtribuidaAoGestor, idUsuario: 6, aprovadaPeloGestor: true, idUsuarioAtribuido: 2),
                // Cancelada depois de aprovada e atribuída à Contabilidade: sai de todas as visões do fluxo.
                NovaSolicitacao(IdSolicitacaoDeOutroCancelada, idUsuario: 6, aprovadaPeloGestor: true, idUsuarioAtribuido: 5, cancelada: true));

            ctx.Rddv.AddRange(
                NovoRddv(IdRddvDoSolicitanteAprovado, idUsuario: 3, aprovadoPeloGestor: true),
                NovoRddv(IdRddvDeOutroRascunho, idUsuario: 6, rascunho: true, aprovadoPeloGestor: true, idUsuarioAtribuido: 5),
                NovoRddv(IdRddvDoFinanceiro, idUsuario: 4),
                // Pendente com outro gestor (o Administrador): fica fora da visão do gestor 2.
                NovoRddv(IdRddvDeOutroComOutroGestor, idUsuario: 6, idGestor: 1));

            ctx.SaveChanges();
        }

        /// <summary>
        /// Lista os títulos que <c>GET /api/listing</c> devolve ao usuário, como "S&lt;código&gt;" e "R&lt;código&gt;".
        /// <paramref name="consulta"/> é o começo da query string (por exemplo <c>viewMode=1&amp;</c>), vazio para não
        /// mandar a visão.
        /// </summary>
        private async Task<List<string>> ListarAsync(int idUsuario, string papel, string consulta)
        {
            var client = _factory.GetAuthenticatedClient(idUsuario, "Usuário " + idUsuario, papel);
            var resposta = await client.GetAsync($"/api/listing?{consulta}page=1&pageSize=1000");
            resposta.StatusCode.Should().Be(HttpStatusCode.OK);

            using var json = JsonDocument.Parse(await resposta.Content.ReadAsStringAsync());
            json.RootElement.GetProperty("success").GetBoolean().Should().BeTrue();

            return json.RootElement.GetProperty("data").EnumerateArray()
                .Select(t => (t.GetProperty("tipo").GetString()!.StartsWith("RDDV", StringComparison.Ordinal) ? "R" : "S")
                    + t.GetProperty("codigo").GetInt32())
                .ToList();
        }

        /// <summary>Consultas sem visão definida: sem o parâmetro, com o código 0 e com o nome do valor 0.</summary>
        public static TheoryData<string, int, string> SemVisaoDefinida()
        {
            var dados = new TheoryData<string, int, string>();

            foreach (var consulta in new[] { "", "viewMode=0&", "viewMode=Undefined&" })
            {
                dados.Add(consulta, 3, "SOL");
                dados.Add(consulta, 2, "GES");
                dados.Add(consulta, 5, "CON");
                dados.Add(consulta, 4, "FIN");
                dados.Add(consulta, 1, "ADM");
            }

            return dados;
        }

        [Theory]
        [MemberData(nameof(SemVisaoDefinida))]
        public async Task Listagem_SemVisaoDefinida_DevolveSoOsTitulosDoProprioUsuario(string consulta, int idUsuario, string papel)
        {
            var titulos = await ListarAsync(idUsuario, papel, consulta);

            titulos.Should().BeEquivalentTo(TitulosDoProprioUsuario[idUsuario],
                "sem visão definida a lista vale como Minhas Solicitações, com os títulos do próprio usuário, qualquer que seja o perfil");
        }

        /// <summary>Todas as visões (e a ausência dela) para cada perfil, menos o dono dos rascunhos.</summary>
        public static TheoryData<string, int, string> TodasAsVisoes()
        {
            var dados = new TheoryData<string, int, string>();

            foreach (var consulta in new[] { "", "viewMode=0&", "viewMode=1&", "viewMode=2&", "viewMode=3&", "viewMode=4&" })
            {
                dados.Add(consulta, 3, "SOL");
                dados.Add(consulta, 2, "GES");
                dados.Add(consulta, 5, "CON");
                dados.Add(consulta, 4, "FIN");
                dados.Add(consulta, 1, "ADM");
            }

            return dados;
        }

        [Theory]
        [MemberData(nameof(TodasAsVisoes))]
        public async Task Listagem_RascunhoDeOutroUsuario_NuncaAparece(string consulta, int idUsuario, string papel)
        {
            var titulos = await ListarAsync(idUsuario, papel, consulta);

            titulos.Should().NotContain(RascunhosDoOutroSolicitante,
                "o rascunho só aparece para o próprio dono, em Minhas Solicitações");
        }

        /// <summary>
        /// Conjunto exato de cada perfil nas visões Minhas Solicitações (1), Gestor (2) e Financeiro/Contábil (3). O
        /// Dashboard (4) tem os próprios testes, logo abaixo.
        /// </summary>
        public static TheoryData<string, int, string, string[]> EscopoPorVisaoEPerfil()
        {
            var dados = new TheoryData<string, int, string, string[]>();

            foreach (var (idUsuario, papel) in new[] { (3, "SOL"), (6, "SOL"), (2, "GES"), (5, "CON"), (4, "FIN"), (1, "ADM") })
            {
                dados.Add("viewMode=1&", idUsuario, papel, TitulosDoProprioUsuario[idUsuario]);
            }

            dados.Add("viewMode=2&", 3, "SOL", []);
            dados.Add("viewMode=2&", 6, "SOL", []);
            dados.Add("viewMode=2&", 2, "GES", TitulosDoGestor2);
            dados.Add("viewMode=2&", 5, "CON", []);
            dados.Add("viewMode=2&", 4, "FIN", []);
            dados.Add("viewMode=2&", 1, "ADM", ["R" + IdRddvDeOutroComOutroGestor]);

            // O S907 está atribuído ao solicitante 3 e o S908 ao gestor 2: sem perfil da área financeira, a visão vem
            // vazia assim mesmo.
            dados.Add("viewMode=3&", 3, "SOL", []);
            dados.Add("viewMode=3&", 6, "SOL", []);
            dados.Add("viewMode=3&", 2, "GES", []);
            dados.Add("viewMode=3&", 5, "CON", AtribuidosAContabilidade);
            dados.Add("viewMode=3&", 4, "FIN", AprovadosDoFluxo);
            dados.Add("viewMode=3&", 1, "ADM", AprovadosDoFluxo);

            return dados;
        }

        [Theory]
        [MemberData(nameof(EscopoPorVisaoEPerfil))]
        public async Task Listagem_PorVisaoEPerfil_DevolveExatamenteOsTitulosDoEscopo(string consulta, int idUsuario, string papel,
            string[] esperado)
        {
            var titulos = await ListarAsync(idUsuario, papel, consulta);

            titulos.Should().BeEquivalentTo(esperado,
                "cada perfil recebe, em cada visão, só os títulos do escopo dele: na visão do gestor, os de que é gestor; na " +
                "financeira, todos os aprovados para FIN e ADM, os atribuídos a ela para CON e nada para os demais perfis");
        }

        [Theory]
        [InlineData(3, "SOL")]
        [InlineData(6, "SOL")]
        [InlineData(2, "GES")]
        public async Task Listagem_Dashboard_SemPerfilDaFila_NaoDevolveTitulos(int idUsuario, string papel)
        {
            var titulos = await ListarAsync(idUsuario, papel, "viewMode=4&");

            titulos.Should().BeEmpty(
                "a fila do Dashboard traz títulos de todos os usuários e é só de Financeiro, Contabilidade e Administrador");
        }

        [Theory]
        [InlineData(4, "FIN")]
        [InlineData(5, "CON")]
        [InlineData(1, "ADM")]
        public async Task Listagem_Dashboard_ComPerfilDaFila_DevolveOsAprovadosSemAtribuicao(int idUsuario, string papel)
        {
            var titulos = await ListarAsync(idUsuario, papel, "viewMode=4&");

            titulos.Should().BeEquivalentTo(FilaDoDashboard,
                "a fila mostra os aprovados pelo gestor, sem atribuição, sem rascunho e sem cancelado");
        }

        [Theory]
        [InlineData("5")]
        [InlineData("99")]
        [InlineData("-1")]
        [InlineData("abc")]
        public async Task Listagem_VisaoForaDoEnum_Responde400(string valor)
        {
            var client = _factory.GetAuthenticatedClient(3, "Solicitante", "SOL");

            var resposta = await client.GetAsync($"/api/listing?viewMode={Uri.EscapeDataString(valor)}&page=1&pageSize=1000");

            resposta.StatusCode.Should().Be(HttpStatusCode.BadRequest,
                "uma visão que não existe é recusada, em vez de cair numa lista sem escopo");
        }
    }
}
