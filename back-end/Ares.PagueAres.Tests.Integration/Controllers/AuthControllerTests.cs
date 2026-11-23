using Ares.PagueAres.Domain;
using Ares.PagueAres.Tests.Integration.Infrastructure;
using System.Text.Json;
using System.Text;

namespace Ares.PagueAres.Tests.Integration.Controllers
{
    public class AuthControllerTests : IClassFixture<PagueAresWebAppFactory>
    {
        private readonly PagueAresWebAppFactory _factory;

        public AuthControllerTests(PagueAresWebAppFactory factory)
        {
            _factory = factory;
            _factory.SeedDatabase(ctx =>
            {
                if (!ctx.Usuarios.Any())
                    PagueAresWebAppFactory.SeedBaseData(ctx);
            });
        }

        private HttpContent BuildLoginBody(string username, string password) =>
            new StringContent(
                JsonSerializer.Serialize(new { Username = username, Password = password }),
                Encoding.UTF8,
                "application/json");

        [Fact]
        public async Task Login_CredenciaisValidas_RetornaToken()
        {
            var client = _factory.CreateClient();
            var response = await client.PostAsync("/api/auth", BuildLoginBody("adm@test.com", "senhaAdm"));

            response.EnsureSuccessStatusCode();
            var body = await response.Content.ReadAsStringAsync();
            var json = JsonDocument.Parse(body).RootElement;
            json.GetProperty("success").GetBoolean().Should().BeTrue();
            json.GetProperty("data").TryGetProperty("token", out var token);
            token.GetString().Should().NotBeNullOrEmpty();
        }

        [Fact]
        public async Task Login_SenhaErrada_RetornaFalha()
        {
            var client = _factory.CreateClient();
            var response = await client.PostAsync("/api/auth", BuildLoginBody("adm@test.com", "senhaErrada"));

            var body = await response.Content.ReadAsStringAsync();
            var result = JsonSerializer.Deserialize<GenericResponse<object>>(body,
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

            result!.Success.Should().BeFalse();
        }

        [Fact]
        public async Task Login_UsuarioInativo_RetornaFalha()
        {
            var client = _factory.CreateClient();
            var response = await client.PostAsync("/api/auth", BuildLoginBody("inativo@test.com", "senha"));

            var body = await response.Content.ReadAsStringAsync();
            var result = JsonSerializer.Deserialize<GenericResponse<object>>(body,
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

            result!.Success.Should().BeFalse();
            result.Message.Should().Contain("inativo");
        }

        [Fact]
        public async Task Login_AposDezFalhas_RetornaMensagemDeLockout()
        {
            // Usar uma nova factory isolada para não interferir com outros testes
            using var factory = new PagueAresWebAppFactory();
            factory.SeedDatabase(ctx => PagueAresWebAppFactory.SeedBaseData(ctx));

            var client = factory.CreateClient();

            // 10 tentativas com falha
            for (int i = 0; i < 10; i++)
                await client.PostAsync("/api/auth", BuildLoginBody("adm@test.com", "errada"));

            // 11ª deve ser bloqueada
            var response = await client.PostAsync("/api/auth", BuildLoginBody("adm@test.com", "errada"));
            var body = await response.Content.ReadAsStringAsync();
            var result = JsonSerializer.Deserialize<GenericResponse<object>>(body,
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

            result!.Success.Should().BeFalse();
            result.Message.Should().Contain("Muitas tentativas");
        }
    }
}
