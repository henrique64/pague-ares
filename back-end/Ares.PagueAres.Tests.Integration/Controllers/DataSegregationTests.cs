using Ares.PagueAres.Domain;
using Ares.PagueAres.Domain.Dtos;
using Ares.PagueAres.Tests.Integration.Infrastructure;
using System.Text.Json;

namespace Ares.PagueAres.Tests.Integration.Controllers
{
    public class DataSegregationTests : IClassFixture<PagueAresWebAppFactory>
    {
        private readonly PagueAresWebAppFactory _factory;

        public DataSegregationTests(PagueAresWebAppFactory factory)
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
        public async Task GetRequest_DonoDaSolicitacao_RetornaDados()
        {
            // Solicitante (id=3) acessa sua própria solicitação (id=11)
            var client = _factory.GetAuthenticatedClient(3, "Solicitante", "USR");
            var response = await client.GetAsync("/api/request/11");

            var body = await response.Content.ReadAsStringAsync();
            var result = JsonSerializer.Deserialize<GenericResponse<SolicitacaoDto>>(body,
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

            result!.Success.Should().BeTrue();
            result.Data!.IdSolicitacao.Should().Be(11);
        }

        [Fact]
        public async Task GetRequest_OutroUsuarioSemRole_RetornaFalha()
        {
            // Usuário "outro" (id=6) tenta acessar solicitação do solicitante (id=11) sem role adequada
            var client = _factory.GetAuthenticatedClient(6, "Outro", "USR");
            var response = await client.GetAsync("/api/request/11");

            var body = await response.Content.ReadAsStringAsync();
            var result = JsonSerializer.Deserialize<GenericResponse<SolicitacaoDto>>(body,
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

            result!.Success.Should().BeFalse();
            result.Message.Should().Contain("não pertence");
        }

        [Fact]
        public async Task GetRequest_UsuarioComRoleADM_RetornaDados()
        {
            var client = _factory.GetAuthenticatedClient(1, "Admin", "ADM");
            var response = await client.GetAsync("/api/request/11");

            var body = await response.Content.ReadAsStringAsync();
            var result = JsonSerializer.Deserialize<GenericResponse<SolicitacaoDto>>(body,
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

            result!.Success.Should().BeTrue();
        }

        [Fact]
        public async Task GetRequest_UsuarioComRoleGES_RetornaDados()
        {
            var client = _factory.GetAuthenticatedClient(2, "Gestor", "GES");
            var response = await client.GetAsync("/api/request/11");

            var body = await response.Content.ReadAsStringAsync();
            var result = JsonSerializer.Deserialize<GenericResponse<SolicitacaoDto>>(body,
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

            result!.Success.Should().BeTrue();
        }

        [Fact]
        public async Task GetRequest_UsuarioComRoleFIN_RetornaDados()
        {
            var client = _factory.GetAuthenticatedClient(4, "Financeiro", "FIN");
            var response = await client.GetAsync("/api/request/11");

            var body = await response.Content.ReadAsStringAsync();
            var result = JsonSerializer.Deserialize<GenericResponse<SolicitacaoDto>>(body,
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

            result!.Success.Should().BeTrue();
        }

        [Fact]
        public async Task GetRddv_DonoDaRddv_RetornaDados()
        {
            // Solicitante (id=3) acessa seu próprio RDDV (id=51)
            var client = _factory.GetAuthenticatedClient(3, "Solicitante", "USR");
            var response = await client.GetAsync("/api/rddv/51");

            var body = await response.Content.ReadAsStringAsync();
            var result = JsonSerializer.Deserialize<GenericResponse<RddvDto>>(body,
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

            result!.Success.Should().BeTrue();
        }

        [Fact]
        public async Task GetRddv_OutroUsuarioSemRole_RetornaFalha()
        {
            // Usuário "outro" (id=6) tenta acessar RDDV do solicitante (id=51)
            var client = _factory.GetAuthenticatedClient(6, "Outro", "USR");
            var response = await client.GetAsync("/api/rddv/51");

            var body = await response.Content.ReadAsStringAsync();
            var result = JsonSerializer.Deserialize<GenericResponse<RddvDto>>(body,
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

            result!.Success.Should().BeFalse();
        }
    }
}
