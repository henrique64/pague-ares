using Ares.PagueAres.Infrastructure;
using Ares.PagueAres.Infrastructure.Models;
using Ares.PagueAres.Tests.Integration.Infrastructure;
using System.Net;
using System.Text.Json;

namespace Ares.PagueAres.Tests.Integration.Controllers
{
    /// <summary>
    /// Atribuição de um título a um usuário, a lista "Atribuir" da tela inicial (<c>GET /api/request/{id}/user?userId=</c>
    /// e <c>GET /api/rddv/{id}/user?userId=</c>):
    /// <list type="bullet">
    /// <item>só Financeiro e Administrador atribuem. Os demais perfis recebem 403 e nada é gravado: o título continua
    /// fora da visão financeira de quem tentou e na fila do Dashboard. O 403 vem antes da busca do título, para que um id
    /// inexistente não se distinga de um existente;</item>
    /// <item>o destino tem de ser um usuário ativo com perfil Financeiro ou Contabilidade, o mesmo conjunto que a tela
    /// oferece (<c>GET /api/users/list?roles=FIN,CON</c>). Outro destino recebe 400 com mensagem, e nada é gravado;</item>
    /// <item>título inexistente recebe 404.</item>
    /// </list>
    /// Cada cenário usa um título próprio, porque a atribuição bem-sucedida muda o banco compartilhado da classe.
    /// </summary>
    public class AtribuicaoTests : IClassFixture<PagueAresWebAppFactory>
    {
        // Usuários do SeedBaseData: 1 = ADM, 2 = GES, 3 = solicitante, 4 = FIN, 5 = CON, 6 = outro solicitante,
        // 7 = inativo sem perfil. Este cenário acrescenta o 8, da Contabilidade e inativo.
        private const int IdContabilidadeInativa = 8;

        // Títulos do usuário 6, aprovados pelo gestor e sem atribuição: estão na fila do Dashboard.
        private const int IdSolicitacaoAlvoSemPermissao = 720;
        private const int IdSolicitacaoAtribuidaPeloFinanceiro = 721;
        private const int IdSolicitacaoAtribuidaPeloAdministrador = 722;
        private const int IdSolicitacaoAlvoDestinoInvalido = 723;
        private const int IdRddvAlvoSemPermissao = 730;
        private const int IdRddvAtribuidoPeloFinanceiro = 731;
        private const int IdRddvAtribuidoPeloAdministrador = 732;
        private const int IdRddvAlvoDestinoInvalido = 733;

        private const int IdInexistente = 99999;

        private const string MensagemSemPermissao = "Atribuição permitida apenas para os perfis Financeiro e Administrador.";
        private const string MensagemDestinoInvalido =
            "O usuário informado não pode receber a atribuição: escolha um usuário ativo com perfil Financeiro ou Contabilidade.";

        private readonly PagueAresWebAppFactory _factory;

        public AtribuicaoTests(PagueAresWebAppFactory factory)
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

        private static Solicitacao NovaSolicitacao(int id)
        {
            return new Solicitacao
            {
                IdSolicitacao = id,
                IdUsuario = 6,
                IdGestor = 2,
                IdDepartamento = 1,
                DataSolicitacao = DateTime.Now,
                NumeroDocumento = "NF-" + id,
                Descricao = "Teste da atribuição",
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
                Rascunho = false,
                Cancelado = false,
                StatusGestor = 2,
                StatusFinanceiro = 1,
                StatusContabilidade = 1,
                AprovadoGestor = true
            };
        }

        private static Rddv NovoRddv(int id)
        {
            return new Rddv
            {
                IdRelatorio = id,
                IdUsuario = 6,
                IdGestor = 2,
                IdDepartamento = 1,
                NomeFuncionario = "Funcionário Fictício",
                CPF = "00000000000",
                Finalidade = "Teste da atribuição",
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
            ctx.Usuarios.Add(new Usuario
            {
                IdUsuario = IdContabilidadeInativa,
                Email = "contabilidade.inativa@test.com",
                Nome = "Contabilidade Inativa",
                Senha = BCrypt.Net.BCrypt.HashPassword("senha"),
                Origem = 0,
                Ativo = false,
                DataCadastro = DateTime.Now,
                IdDepartamento = 2
            });
            ctx.UsuarioFuncaos.Add(new UsuarioFuncao { IdUsuario = IdContabilidadeInativa, IdFuncao = 4 });

            ctx.Solicitacaos.AddRange(
                NovaSolicitacao(IdSolicitacaoAlvoSemPermissao),
                NovaSolicitacao(IdSolicitacaoAtribuidaPeloFinanceiro),
                NovaSolicitacao(IdSolicitacaoAtribuidaPeloAdministrador),
                NovaSolicitacao(IdSolicitacaoAlvoDestinoInvalido));

            ctx.Rddv.AddRange(
                NovoRddv(IdRddvAlvoSemPermissao),
                NovoRddv(IdRddvAtribuidoPeloFinanceiro),
                NovoRddv(IdRddvAtribuidoPeloAdministrador),
                NovoRddv(IdRddvAlvoDestinoInvalido));

            ctx.SaveChanges();
        }

        /// <summary>Chave do título na listagem: "S&lt;código&gt;" para solicitação e "R&lt;código&gt;" para RDDV.</summary>
        private static string Chave(string recurso, int idTitulo) => (recurso == "rddv" ? "R" : "S") + idTitulo;

        private async Task<(HttpStatusCode Status, bool Success, string Message)> AtribuirAsync(
            int idUsuario, string papel, string recurso, int idTitulo, int idDestino)
        {
            var client = _factory.GetAuthenticatedClient(idUsuario, "Usuário " + idUsuario, papel);
            var resposta = await client.GetAsync($"/api/{recurso}/{idTitulo}/user?userId={idDestino}");

            using var json = JsonDocument.Parse(await resposta.Content.ReadAsStringAsync());

            return (resposta.StatusCode,
                json.RootElement.GetProperty("success").GetBoolean(),
                json.RootElement.GetProperty("message").GetString() ?? "");
        }

        /// <summary>Usuário atribuído e data da atribuição gravados no título, lidos num contexto novo.</summary>
        private (int? IdUsuarioAtribuido, DateTime? DataAtribuicao) LerAtribuicao(string recurso, int idTitulo)
        {
            (int?, DateTime?) atribuicao = default;

            _factory.SeedDatabase(ctx =>
            {
                if (recurso == "rddv")
                {
                    var rddv = ctx.Rddv.Single(r => r.IdRelatorio == idTitulo);
                    atribuicao = (rddv.IdUsuarioAtribuido, rddv.DataAtribuicao);
                }
                else
                {
                    var solicitacao = ctx.Solicitacaos.Single(s => s.IdSolicitacao == idTitulo);
                    atribuicao = (solicitacao.IdUsuarioAtribuido, solicitacao.DataAtribuicao);
                }
            });

            return atribuicao;
        }

        /// <summary>Títulos que <c>GET /api/listing</c> devolve ao usuário na visão informada.</summary>
        private async Task<List<string>> ListarAsync(int idUsuario, string papel, int viewMode)
        {
            var client = _factory.GetAuthenticatedClient(idUsuario, "Usuário " + idUsuario, papel);
            var resposta = await client.GetAsync($"/api/listing?viewMode={viewMode}&page=1&pageSize=1000");
            resposta.StatusCode.Should().Be(HttpStatusCode.OK);

            using var json = JsonDocument.Parse(await resposta.Content.ReadAsStringAsync());
            json.RootElement.GetProperty("success").GetBoolean().Should().BeTrue();

            return json.RootElement.GetProperty("data").EnumerateArray()
                .Select(t => (t.GetProperty("tipo").GetString()!.StartsWith("RDDV", StringComparison.Ordinal) ? "R" : "S")
                    + t.GetProperty("codigo").GetInt32())
                .ToList();
        }

        /// <summary>
        /// Perfis sem permissão de atribuir: o solicitante se atribuindo um título alheio (o cenário da revisão), o dono do
        /// título se atribuindo o próprio, e solicitante, gestor e contabilidade tentando um destino válido.
        /// </summary>
        public static TheoryData<string, int, int, string, int> TentativasSemPermissao()
        {
            var dados = new TheoryData<string, int, int, string, int>();

            foreach (var (recurso, idTitulo) in new[] { ("request", IdSolicitacaoAlvoSemPermissao), ("rddv", IdRddvAlvoSemPermissao) })
            {
                dados.Add(recurso, idTitulo, 3, "SOL", 3);
                dados.Add(recurso, idTitulo, 6, "SOL", 6);
                dados.Add(recurso, idTitulo, 3, "SOL", 5);
                dados.Add(recurso, idTitulo, 2, "GES", 2);
                dados.Add(recurso, idTitulo, 2, "GES", 5);
                dados.Add(recurso, idTitulo, 5, "CON", 5);
            }

            return dados;
        }

        [Theory]
        [MemberData(nameof(TentativasSemPermissao))]
        public async Task Atribuir_PerfilSemPermissao_Responde403ENaoGrava(string recurso, int idTitulo, int idUsuario, string papel,
            int idDestino)
        {
            var (status, success, message) = await AtribuirAsync(idUsuario, papel, recurso, idTitulo, idDestino);

            status.Should().Be(HttpStatusCode.Forbidden, "só Financeiro e Administrador atribuem títulos; a API respondeu: {0}", message);
            success.Should().BeFalse();
            message.Should().Be(MensagemSemPermissao);

            var (idUsuarioAtribuido, dataAtribuicao) = LerAtribuicao(recurso, idTitulo);
            idUsuarioAtribuido.Should().BeNull("a tentativa recusada não grava o usuário atribuído");
            dataAtribuicao.Should().BeNull("a tentativa recusada não grava a data da atribuição");

            (await ListarAsync(idUsuario, papel, viewMode: 3)).Should().NotContain(Chave(recurso, idTitulo),
                "o título continua fora da visão financeira de quem tentou se atribuir");
            (await ListarAsync(4, "FIN", viewMode: 4)).Should().Contain(Chave(recurso, idTitulo),
                "o título continua na fila do Dashboard, sem atribuição");
        }

        [Theory]
        [InlineData("request", 3, "SOL")]
        [InlineData("request", 2, "GES")]
        [InlineData("request", 5, "CON")]
        [InlineData("rddv", 3, "SOL")]
        [InlineData("rddv", 2, "GES")]
        [InlineData("rddv", 5, "CON")]
        public async Task Atribuir_PerfilSemPermissao_TituloInexistente_Responde403(string recurso, int idUsuario, string papel)
        {
            var (status, success, _) = await AtribuirAsync(idUsuario, papel, recurso, IdInexistente, idUsuario);

            status.Should().Be(HttpStatusCode.Forbidden,
                "o papel é conferido antes da busca, para que quem não pode atribuir não descubra quais ids existem");
            success.Should().BeFalse();
        }

        [Theory]
        [InlineData("request", 4, "FIN", "Solicitação não encontrada.")]
        [InlineData("request", 1, "ADM", "Solicitação não encontrada.")]
        [InlineData("rddv", 4, "FIN", "Relatório não encontrado.")]
        [InlineData("rddv", 1, "ADM", "Relatório não encontrado.")]
        public async Task Atribuir_TituloInexistente_Responde404(string recurso, int idUsuario, string papel, string mensagem)
        {
            var (status, success, message) = await AtribuirAsync(idUsuario, papel, recurso, IdInexistente, 5);

            status.Should().Be(HttpStatusCode.NotFound, "o título não existe; a API respondeu: {0}", message);
            success.Should().BeFalse();
            message.Should().Be(mensagem);
        }

        /// <summary>
        /// Destinos que a tela não oferece: o solicitante (3), o outro solicitante (6), o gestor (2), o administrador sem
        /// perfil Financeiro nem Contabilidade (1), o inativo sem perfil (7), a Contabilidade inativa (8), um id que não
        /// existe e o zero (parâmetro ausente).
        /// </summary>
        public static TheoryData<string, int, int> DestinosInvalidos()
        {
            var dados = new TheoryData<string, int, int>();

            foreach (var (recurso, idTitulo) in new[] { ("request", IdSolicitacaoAlvoDestinoInvalido), ("rddv", IdRddvAlvoDestinoInvalido) })
            {
                foreach (var idDestino in new[] { 3, 6, 2, 1, 7, IdContabilidadeInativa, IdInexistente, 0 })
                {
                    dados.Add(recurso, idTitulo, idDestino);
                }
            }

            return dados;
        }

        [Theory]
        [MemberData(nameof(DestinosInvalidos))]
        public async Task Atribuir_DestinoInvalido_Responde400ComMensagemENaoGrava(string recurso, int idTitulo, int idDestino)
        {
            var (status, success, message) = await AtribuirAsync(4, "FIN", recurso, idTitulo, idDestino);

            status.Should().Be(HttpStatusCode.BadRequest,
                "o destino precisa ser um usuário ativo com perfil Financeiro ou Contabilidade; a API respondeu: {0}", message);
            success.Should().BeFalse();
            message.Should().Be(MensagemDestinoInvalido);

            var (idUsuarioAtribuido, dataAtribuicao) = LerAtribuicao(recurso, idTitulo);
            idUsuarioAtribuido.Should().BeNull("o destino recusado não é gravado");
            dataAtribuicao.Should().BeNull();
        }

        [Theory]
        [InlineData("request", IdSolicitacaoAtribuidaPeloFinanceiro, 4, "FIN", 5, "CON")]
        [InlineData("request", IdSolicitacaoAtribuidaPeloAdministrador, 1, "ADM", 4, "FIN")]
        [InlineData("rddv", IdRddvAtribuidoPeloFinanceiro, 4, "FIN", 5, "CON")]
        [InlineData("rddv", IdRddvAtribuidoPeloAdministrador, 1, "ADM", 4, "FIN")]
        public async Task Atribuir_FinanceiroOuAdministradorParaDestinoValido_GravaETiraDaFila(string recurso, int idTitulo,
            int idUsuario, string papel, int idDestino, string papelDestino)
        {
            var (status, success, message) = await AtribuirAsync(idUsuario, papel, recurso, idTitulo, idDestino);

            status.Should().Be(HttpStatusCode.OK, "a API respondeu: {0}", message);
            success.Should().BeTrue();
            message.Should().Be("Usuário atribuído com sucesso");

            var (idUsuarioAtribuido, dataAtribuicao) = LerAtribuicao(recurso, idTitulo);
            idUsuarioAtribuido.Should().Be(idDestino);
            dataAtribuicao.Should().NotBeNull();

            (await ListarAsync(idDestino, papelDestino, viewMode: 3)).Should().Contain(Chave(recurso, idTitulo),
                "o título atribuído aparece na visão financeira do usuário que o recebeu");
            (await ListarAsync(4, "FIN", viewMode: 4)).Should().NotContain(Chave(recurso, idTitulo),
                "o título atribuído sai da fila do Dashboard");
        }
    }
}
