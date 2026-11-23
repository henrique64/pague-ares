using Ares.PagueAres.Domain;
using Ares.PagueAres.Domain.Dtos;
using Ares.PagueAres.Tests.Integration.Infrastructure;
using System.Text.Json;

namespace Ares.PagueAres.Tests.Integration.Controllers
{
    public class DepartmentControllerTests : IClassFixture<PagueAresWebAppFactory>
    {
        private readonly PagueAresWebAppFactory _factory;

        public DepartmentControllerTests(PagueAresWebAppFactory factory)
        {
            _factory = factory;
            _factory.SeedDatabase(ctx =>
            {
                if (!ctx.Departamentos.Any())
                    PagueAresWebAppFactory.SeedBaseData(ctx);
            });
        }

        [Fact]
        public async Task GetAll_ComToken_RetornaDepartamentos()
        {
            var client = _factory.GetAuthenticatedClient(1, "Admin", "ADM");
            var response = await client.GetAsync("/api/department");

            response.EnsureSuccessStatusCode();
            var body = await response.Content.ReadAsStringAsync();
            var result = JsonSerializer.Deserialize<GenericResponse<List<DepartamentoDto>>>(body,
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

            result!.Success.Should().BeTrue();
            result.Data.Should().HaveCount(3);
        }

        [Fact]
        public async Task GetAll_SemToken_Retorna401OuNaoAutorizado()
        {
            var client = _factory.CreateClient();
            var response = await client.GetAsync("/api/department");

            // Sem token, [Authorize] retorna 401
            ((int)response.StatusCode).Should().Be(401);
        }
    }
}
