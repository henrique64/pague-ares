using Ares.PagueAres.Domain;
using Ares.PagueAres.Domain.Dtos;
using Ares.PagueAres.Infrastructure;
using Ares.PagueAres.Tests.Integration.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.IdentityModel.Tokens;
using System.IdentityModel.Tokens.Jwt;
using System.Net;
using System.Net.Http.Headers;
using System.Security.Claims;
using System.Text;
using System.Text.Json;

namespace Ares.PagueAres.Tests.Integration.Controllers
{
    /// <summary>
    /// Testes de segurança: autenticação, segregação de dados e escalada de privilégios.
    ///
    /// Testes marcados com [Trait("Category", "SecurityGap")] documentam vulnerabilidades
    /// conhecidas nos controllers e irão FALHAR até que os controles de acesso sejam
    /// implementados. Eles servem como lista de pendências de segurança.
    ///
    /// Testes marcados com [Trait("Category", "SecurityControl")] verificam proteções
    /// que já funcionam corretamente e devem sempre PASSAR.
    /// </summary>
    public class SecurityTests : IClassFixture<PagueAresWebAppFactory>
    {
        private readonly PagueAresWebAppFactory _factory;
        private static readonly JsonSerializerOptions JsonOpts = new() { PropertyNameCaseInsensitive = true };

        public SecurityTests(PagueAresWebAppFactory factory)
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

        // ─── Helpers ─────────────────────────────────────────────────────────────

        private static string GenerateExpiredJwt(int userId, string userName, string[] roles)
        {
            var key = new SymmetricSecurityKey(Encoding.ASCII.GetBytes(PagueAresWebAppFactory.JwtSecret));
            var creds = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);
            var claims = new[]
            {
                new Claim(JwtRegisteredClaimNames.Sub, userName),
                new Claim(JwtRegisteredClaimNames.NameId, userId.ToString()),
                new Claim("Roles", string.Join(",", roles))
            };
            var token = new JwtSecurityToken(
                issuer: "pagueares.local",
                audience: "pagueares.local",
                claims: claims,
                expires: DateTime.UtcNow.AddHours(-2),
                signingCredentials: creds);
            return new JwtSecurityTokenHandler().WriteToken(token);
        }

        private static string GenerateTamperedJwt(int userId, string userName, string[] roles)
        {
            // assina com chave errada — verificação de assinatura vai rejeitar
            var wrongKey = new SymmetricSecurityKey(Encoding.ASCII.GetBytes("CHAVE_ERRADA_NAO_CORRESPONDE_AO_SEGREDO_DO_SISTEMA"));
            var creds = new SigningCredentials(wrongKey, SecurityAlgorithms.HmacSha256);
            var claims = new[]
            {
                new Claim(JwtRegisteredClaimNames.Sub, userName),
                new Claim(JwtRegisteredClaimNames.NameId, userId.ToString()),
                new Claim("Roles", string.Join(",", roles))
            };
            var token = new JwtSecurityToken(
                issuer: "pagueares.local",
                audience: "pagueares.local",
                claims: claims,
                expires: DateTime.UtcNow.AddHours(1),
                signingCredentials: creds);
            return new JwtSecurityTokenHandler().WriteToken(token);
        }

        private HttpClient GetClientWithToken(string token)
        {
            var client = _factory.CreateClient();
            client.DefaultRequestHeaders.Authorization =
                new AuthenticationHeaderValue("Bearer", token);
            return client;
        }

        private SolicitacaoDto GetSolicitacaoDto(int id)
        {
            using var scope = _factory.Services.CreateScope();
            var ctx = scope.ServiceProvider.GetRequiredService<PagueAresContext>();
            var s = ctx.Solicitacaos.AsNoTracking().First(x => x.IdSolicitacao == id);
            return new SolicitacaoDto
            {
                IdSolicitacao = s.IdSolicitacao,
                IdUsuario = s.IdUsuario,
                IdDepartamento = s.IdDepartamento,
                DataSolicitacao = s.DataSolicitacao,
                Descricao = s.Descricao,
                Valor = s.Valor,
                FormaPagamento = s.FormaPagamento,
                CentroCusto = s.CentroCusto ?? "",
                Projeto = s.Projeto ?? "",
                Pca = s.Pca ?? "",
                NumDocParceiro = s.NumDocParceiro ?? "",
                CodigoParceiro = s.CodigoParceiro ?? "",
                NomeParceiro = s.NomeParceiro ?? "",
                BancoParceiro = s.BancoParceiro ?? "",
                AgenciaParceiro = s.AgenciaParceiro ?? "",
                ContaParceiro = s.ContaParceiro ?? "",
                Observacao = s.Observacao ?? "",
                ObservacaoGestor = s.ObservacaoGestor ?? "",
                ObservacaoSetor = s.ObservacaoSetor ?? "",
                TipoAutorizacao = s.TipoAutorizacao,
                TipoSolicitacao = s.TipoSolicitacao,
                TipoPagamento = s.TipoPagamento,
                Rascunho = s.Rascunho,
                Cancelado = s.Cancelado,
                StatusGestor = s.StatusGestor,
                StatusFinanceiro = s.StatusFinanceiro,
                StatusContabilidade = s.StatusContabilidade,
                AprovadoGestor = s.AprovadoGestor,
                AprovadoSetor = s.AprovadoSetor,
                Documentos = []
            };
        }

        private static HttpContent ToJson(object obj) =>
            new StringContent(JsonSerializer.Serialize(obj), Encoding.UTF8, "application/json");

        // ═══════════════════════════════════════════════════════════════════════
        // Grupo 1 — Aplicação do requisito de autenticação
        // Todas as rotas protegidas por [Authorize] devem rejeitar tokens
        // ausentes, assinatura inválida e tokens expirados com HTTP 401.
        // ═══════════════════════════════════════════════════════════════════════

        [Fact]
        [Trait("Category", "Authentication")]
        public async Task SemToken_EndpointRequest_Retorna401()
        {
            var client = _factory.CreateClient();
            var response = await client.GetAsync("/api/request/11");
            response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        }

        [Fact]
        [Trait("Category", "Authentication")]
        public async Task SemToken_EndpointUsers_Retorna401()
        {
            var client = _factory.CreateClient();
            var response = await client.GetAsync("/api/users");
            response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        }

        [Fact]
        [Trait("Category", "Authentication")]
        public async Task SemToken_EndpointConfig_Retorna401()
        {
            var client = _factory.CreateClient();
            var response = await client.GetAsync("/api/config");
            response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        }

        [Fact]
        [Trait("Category", "Authentication")]
        public async Task JwtAssinadoComChaveErrada_EndpointProtegido_Retorna401()
        {
            var token = GenerateTamperedJwt(3, "Solicitante", ["USR"]);
            var response = await GetClientWithToken(token).GetAsync("/api/request/11");
            response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        }

        [Fact]
        [Trait("Category", "Authentication")]
        public async Task JwtExpirado_EndpointProtegido_Retorna401()
        {
            var token = GenerateExpiredJwt(3, "Solicitante", ["USR"]);
            var response = await GetClientWithToken(token).GetAsync("/api/request/11");
            response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        }

        // ═══════════════════════════════════════════════════════════════════════
        // Grupo 2 — Acesso ao gerenciamento de usuários (deve exigir ADM)
        //
        // VULNERABILIDADE: UsersController usa apenas [Authorize] na classe,
        // sem verificar o papel ADM. Qualquer usuário autenticado pode listar
        // todos os usuários, obter detalhes e até criar/modificar contas.
        //
        // Os testes deste grupo irão FALHAR até que a verificação de papel
        // seja adicionada ao UsersController.
        // ═══════════════════════════════════════════════════════════════════════

        [Fact]
        [Trait("Category", "SecurityGap")]
        public async Task ListarUsuarios_RoleUSR_DeveSerBloqueado()
        {
            var client = _factory.GetAuthenticatedClient(3, "Solicitante", "USR");
            var response = await client.GetAsync("/api/users");
            var body = await response.Content.ReadAsStringAsync();
            var result = JsonSerializer.Deserialize<GenericResponse<IEnumerable<UsuarioDto>>>(body, JsonOpts);

            // Um USR não deve poder listar todos os usuários do sistema (inclui e-mails e departamentos)
            result!.Success.Should().BeFalse("um usuário com papel USR não deve poder listar todos os usuários");
        }

        [Fact]
        [Trait("Category", "SecurityGap")]
        public async Task ObterUsuarioPorId_RoleUSR_DeveSerBloqueado()
        {
            var client = _factory.GetAuthenticatedClient(3, "Solicitante", "USR");
            var response = await client.GetAsync("/api/users/1"); // detalhes do administrador (id=1)
            var body = await response.Content.ReadAsStringAsync();
            var result = JsonSerializer.Deserialize<GenericResponse<UsuarioDto>>(body, JsonOpts);

            // Um USR não deve poder obter dados (roles, e-mail, departamento) de outros usuários
            result!.Success.Should().BeFalse("um usuário com papel USR não deve poder acessar dados de outro usuário");
        }

        [Fact]
        [Trait("Category", "SecurityGap")]
        public async Task CriarUsuario_RoleUSR_DeveSerBloqueado()
        {
            // Um USR não deve poder criar usuários — menos ainda com papel ADM (escalonamento crítico)
            var client = _factory.GetAuthenticatedClient(3, "Solicitante", "USR");
            var payload = new UsuarioDto
            {
                IdUsuario = 0,
                Email = "malicioso@test.com",
                Nome = "Conta Maliciosa",
                Origem = 0,
                Ativo = true,
                Senha = "senha123",
                IdDepartamento = 1,
                DataCadastro = DateTime.Now,
                Funcoes = [new FuncaoDto { IdFuncao = 1, Alias = "ADM", Nome = "Administrador" }]
            };

            var response = await client.PostAsync("/api/users", ToJson(payload));
            var body = await response.Content.ReadAsStringAsync();
            var result = JsonSerializer.Deserialize<GenericResponse<UsuarioDto>>(body, JsonOpts);

            // Criação de conta com papel ADM por um USR é escalada de privilégio crítica
            result!.Success.Should().BeFalse("um usuário com papel USR não deve poder criar contas no sistema");
        }

        [Fact]
        [Trait("Category", "SecurityGap")]
        public async Task ModificarUsuario_RoleGES_DeveSerBloqueado()
        {
            // Somente ADM deve poder alterar cadastros — GES não deve ter esse acesso
            var client = _factory.GetAuthenticatedClient(2, "Gestor", "GES");
            var payload = new UsuarioDto
            {
                IdUsuario = 6, // usuário existente
                Email = "alterado@test.com",
                Nome = "Nome Alterado",
                Origem = 0,
                Ativo = true,
                Senha = "",
                IdDepartamento = 1,
                DataCadastro = DateTime.Now,
                Funcoes = [new FuncaoDto { IdFuncao = 1, Alias = "ADM", Nome = "Administrador" }]
            };

            var response = await client.PostAsync("/api/users", ToJson(payload));
            var body = await response.Content.ReadAsStringAsync();
            var result = JsonSerializer.Deserialize<GenericResponse<UsuarioDto>>(body, JsonOpts);

            // GES elevando outro usuário para ADM é escalada de privilégio crítica
            result!.Success.Should().BeFalse("somente ADM deve poder modificar cadastros de usuários");
        }

        // ═══════════════════════════════════════════════════════════════════════
        // Grupo 3 — Modificação de configurações do sistema (deve exigir ADM)
        //
        // VULNERABILIDADE: ConfigController usa apenas [Authorize] na classe,
        // sem verificar o papel ADM. Qualquer usuário autenticado pode alterar
        // parâmetros como valor/km de RDDV.
        //
        // Os testes deste grupo irão FALHAR até que a verificação de papel
        // seja adicionada ao ConfigController (POST).
        // ═══════════════════════════════════════════════════════════════════════

        [Fact]
        [Trait("Category", "SecurityGap")]
        public async Task AlterarConfiguracoes_RoleUSR_DeveSerBloqueado()
        {
            var client = _factory.GetAuthenticatedClient(3, "Solicitante", "USR");
            var payload = new object[]
            {
                new { Chave = "valorKm", Valor = "999999", UsuarioAlteracao = 3 }
            };

            var response = await client.PostAsync("/api/config", ToJson(payload));
            var body = await response.Content.ReadAsStringAsync();
            var result = JsonSerializer.Deserialize<GenericResponse<object>>(body, JsonOpts);

            // Alterar valor/km inflaciona reembolsos — deve ser exclusivo de ADM
            result!.Success.Should().BeFalse("somente ADM deve poder alterar configurações do sistema");
        }

        [Fact]
        [Trait("Category", "SecurityGap")]
        public async Task AlterarConfiguracoes_RoleFIN_DeveSerBloqueado()
        {
            var client = _factory.GetAuthenticatedClient(4, "Financeiro", "FIN");
            var payload = new object[]
            {
                new { Chave = "valorKm", Valor = "0.01", UsuarioAlteracao = 4 }
            };

            var response = await client.PostAsync("/api/config", ToJson(payload));
            var body = await response.Content.ReadAsStringAsync();
            var result = JsonSerializer.Deserialize<GenericResponse<object>>(body, JsonOpts);

            result!.Success.Should().BeFalse("somente ADM deve poder alterar configurações do sistema");
        }

        // ═══════════════════════════════════════════════════════════════════════
        // Grupo 4 — Escalada horizontal via POST /api/request
        //
        // VULNERABILIDADE: GravarSolicitacao não verifica se o usuário
        // autenticado é dono do registro antes de permitir a modificação.
        // Qualquer usuário autenticado pode alterar solicitações de terceiros.
        //
        // O teste irá FALHAR até que verificação de dono seja adicionada ao POST.
        // ═══════════════════════════════════════════════════════════════════════

        [Fact]
        [Trait("Category", "SecurityGap")]
        public async Task PostSolicitacao_UsuarioAltercandoRegistroDeOutroUsuario_DeveSerBloqueado()
        {
            // Usuário 6 (outro SOL) tenta modificar solicitação 11 (pertence ao usuário 3)
            var client = _factory.GetAuthenticatedClient(6, "Outro", "USR");
            var dto = GetSolicitacaoDto(11);
            dto.Descricao = "Descrição alterada por terceiro — ataque de escalada horizontal";

            var response = await client.PostAsync("/api/request", ToJson(dto));
            var body = await response.Content.ReadAsStringAsync();
            var result = JsonSerializer.Deserialize<GenericResponse<SolicitacaoDto>>(body, JsonOpts);

            result!.Success.Should().BeFalse("um usuário não deve poder modificar solicitações que pertencem a outro usuário");
        }

        // ═══════════════════════════════════════════════════════════════════════
        // Grupo 5 — Bypass de aprovação via POST /api/request
        //
        // VULNERABILIDADE: GravarSolicitacao não verifica se o papel do chamador
        // corresponde à ação de aprovação que está sendo executada.
        // Um SOL pode definir AprovadoGestor=true (auto-aprovar) ou forçar
        // StatusContabilidade=2 (auto-lançar), pulando o fluxo de aprovação.
        //
        // Os testes irão FALHAR até que validação de papel por campo seja
        // adicionada ao POST do RequestController.
        // ═══════════════════════════════════════════════════════════════════════

        [Fact]
        [Trait("Category", "SecurityGap")]
        public async Task PostSolicitacao_SolicitanteAutoAprovandoPropriaSolicitacao_DeveSerBloqueado()
        {
            // Usuário 3 (SOL, dono) tenta definir AprovadoGestor=true na própria solicitação 101
            var client = _factory.GetAuthenticatedClient(3, "Solicitante", "USR");
            var dto = GetSolicitacaoDto(101); // StatusGestor=1, AprovadoGestor=null
            dto.AprovadoGestor = true;        // tentativa de auto-aprovação

            var response = await client.PostAsync("/api/request", ToJson(dto));
            var body = await response.Content.ReadAsStringAsync();
            var result = JsonSerializer.Deserialize<GenericResponse<SolicitacaoDto>>(body, JsonOpts);

            // Se o ataque funcionar, StatusGestor virará 2 sem intervenção do gestor
            result!.Success.Should().BeFalse(
                "um solicitante (USR) não deve poder aprovar sua própria solicitação — isso requer papel GES");
        }

        [Fact]
        [Trait("Category", "SecurityGap")]
        public async Task PostSolicitacao_SolicitanteForçandoLancamentoContabil_DeveSerBloqueado()
        {
            // Usuário 3 (SOL) tenta forçar StatusContabilidade=2 (lançado) na solicitação 105,
            // que está aguardando contabilidade (StatusContabilidade=1)
            var client = _factory.GetAuthenticatedClient(3, "Solicitante", "USR");
            var dto = GetSolicitacaoDto(105);
            dto.StatusContabilidade = 2; // tentativa de forçar o lançamento contábil

            var response = await client.PostAsync("/api/request", ToJson(dto));
            var body = await response.Content.ReadAsStringAsync();
            var result = JsonSerializer.Deserialize<GenericResponse<SolicitacaoDto>>(body, JsonOpts);

            result!.Success.Should().BeFalse(
                "somente usuários com papel CON devem poder definir StatusContabilidade — isso exige papel CON");
        }

        // ═══════════════════════════════════════════════════════════════════════
        // Grupo 6 — Controles que já funcionam corretamente
        // Estes testes devem sempre PASSAR e servem como reafirmação de que
        // as proteções existentes não foram quebradas.
        // ═══════════════════════════════════════════════════════════════════════

        [Fact]
        [Trait("Category", "SecurityControl")]
        public async Task GetSolicitacao_UsuarioProprioRegistro_Permitido()
        {
            var client = _factory.GetAuthenticatedClient(3, "Solicitante", "USR");
            var response = await client.GetAsync("/api/request/11");
            var body = await response.Content.ReadAsStringAsync();
            var result = JsonSerializer.Deserialize<GenericResponse<SolicitacaoDto>>(body, JsonOpts);

            result!.Success.Should().BeTrue("o dono deve poder acessar a própria solicitação");
            result.Data!.IdSolicitacao.Should().Be(11);
        }

        [Fact]
        [Trait("Category", "SecurityControl")]
        public async Task GetSolicitacao_OutroUsuarioSemRoleAdequada_Bloqueado()
        {
            var client = _factory.GetAuthenticatedClient(6, "Outro", "USR");
            var response = await client.GetAsync("/api/request/11");
            var body = await response.Content.ReadAsStringAsync();
            var result = JsonSerializer.Deserialize<GenericResponse<SolicitacaoDto>>(body, JsonOpts);

            result!.Success.Should().BeFalse("USR sem role adequada não deve acessar GET de solicitação alheia");
        }

        [Fact]
        [Trait("Category", "SecurityControl")]
        public async Task GetRddv_OutroUsuarioSemRoleAdequada_Bloqueado()
        {
            var client = _factory.GetAuthenticatedClient(6, "Outro", "USR");
            var response = await client.GetAsync("/api/rddv/51");
            var body = await response.Content.ReadAsStringAsync();
            var result = JsonSerializer.Deserialize<GenericResponse<RddvDto>>(body, JsonOpts);

            result!.Success.Should().BeFalse("USR sem role adequada não deve acessar GET de RDDV alheio");
        }

        [Fact]
        [Trait("Category", "SecurityControl")]
        public async Task PostSolicitacao_RegistroCancelado_Bloqueado()
        {
            // O guard de cancelado (requestData.Cancelado) já funciona corretamente
            var client = _factory.GetAuthenticatedClient(3, "Solicitante", "USR");
            var dto = GetSolicitacaoDto(10); // id=10 está cancelado
            dto.Descricao = "Tentativa de reeditar registro cancelado";

            var response = await client.PostAsync("/api/request", ToJson(dto));
            var body = await response.Content.ReadAsStringAsync();
            var result = JsonSerializer.Deserialize<GenericResponse<SolicitacaoDto>>(body, JsonOpts);

            result!.Success.Should().BeFalse("registro cancelado não deve poder ser alterado");
            result.Message.Should().Contain("cancelado");
        }

        [Fact]
        [Trait("Category", "SecurityControl")]
        public async Task GetSolicitacao_UsuarioComRoleADM_AcessaQualquerRegistro()
        {
            // ADM deve poder acessar o GET de qualquer solicitação
            var client = _factory.GetAuthenticatedClient(1, "Admin", "ADM");
            var response = await client.GetAsync("/api/request/20"); // pertence ao usuário 6
            var body = await response.Content.ReadAsStringAsync();
            var result = JsonSerializer.Deserialize<GenericResponse<SolicitacaoDto>>(body, JsonOpts);

            result!.Success.Should().BeTrue("ADM deve poder acessar qualquer solicitação");
        }

        [Fact]
        [Trait("Category", "SecurityControl")]
        public async Task Users_RoleADM_PodeCadastrarNovoUsuario()
        {
            // Verificação de que ADM terá acesso após o gap ser corrigido
            var client = _factory.GetAuthenticatedClient(1, "Admin", "ADM");
            var payload = new UsuarioDto
            {
                IdUsuario = 0,
                Email = "novoadm@test.com",
                Nome = "Usuário Criado por ADM",
                Origem = 0,
                Ativo = true,
                Senha = "senha123",
                IdDepartamento = 1,
                DataCadastro = DateTime.Now,
                Funcoes = [new FuncaoDto { IdFuncao = 5, Alias = "USR", Nome = "Usuário" }]
            };

            var response = await client.PostAsync("/api/users", ToJson(payload));
            var body = await response.Content.ReadAsStringAsync();
            var result = JsonSerializer.Deserialize<GenericResponse<UsuarioDto>>(body, JsonOpts);

            result!.Success.Should().BeTrue("ADM deve poder criar usuários");
        }
    }
}
