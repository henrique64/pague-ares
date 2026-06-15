using Ares.PagueAres.Infrastructure;
using Ares.PagueAres.Tests.Integration.Infrastructure;
using Microsoft.Extensions.DependencyInjection;

namespace Ares.PagueAres.Tests.Integration.Controllers
{
    /// <summary>
    /// Garante que os endpoints de listagem "crua" (GET /api/request e GET /api/rddv sem id),
    /// que vazavam todos os registros (rascunhos, cancelados e de outros usuários) para
    /// qualquer usuário autenticado, permanecem removidos. A listagem oficial é o /api/listing.
    /// </summary>
    public class EndpointExposureTests : IClassFixture<PagueAresWebAppFactory>
    {
        private readonly PagueAresWebAppFactory _factory;

        public EndpointExposureTests(PagueAresWebAppFactory factory)
        {
            _factory = factory;
            _factory.SeedDatabase(ctx =>
            {
                if (!ctx.Solicitacaos.Any())
                {
                    PagueAresWebAppFactory.SeedBaseData(ctx);
                    PagueAresWebAppFactory.SeedSolicitacoes(ctx);
                    PagueAresWebAppFactory.SeedRddvs(ctx);
                }
            });
        }

        [Fact]
        public async Task GetRequest_ListaCrua_NaoExpoeRegistros()
        {
            var client = _factory.GetAuthenticatedClient(3, "Solicitante", "USR");

            var response = await client.GetAsync("/api/request");

            // Endpoint removido: não pode responder com sucesso (e portanto não lista nada).
            response.IsSuccessStatusCode.Should().BeFalse();
        }

        [Fact]
        public async Task GetRddv_ListaCrua_NaoExpoeRegistros()
        {
            var client = _factory.GetAuthenticatedClient(3, "Solicitante", "USR");

            var response = await client.GetAsync("/api/rddv");

            response.IsSuccessStatusCode.Should().BeFalse();
        }
    }
}
