using Ares.PagueAres.Infrastructure;
using Ares.PagueAres.Infrastructure.Models;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.IdentityModel.Tokens;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;

namespace Ares.PagueAres.Tests.Integration.Infrastructure
{
    public class PagueAresWebAppFactory : WebApplicationFactory<Program>
    {
        public const string JwtSecret = "45AE44EA-8470-44C2-9A61-8283410570A6";

        private readonly string _dbName = "TestDb_" + Guid.NewGuid();

        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.ConfigureServices(services =>
            {
                // Remove all DbContext and EF Core registrations to avoid provider conflicts
                var descriptorsToRemove = services
                    .Where(d =>
                        d.ServiceType == typeof(DbContextOptions<PagueAresContext>) ||
                        d.ServiceType == typeof(PagueAresContext) ||
                        d.ServiceType.FullName?.StartsWith("Microsoft.EntityFrameworkCore") == true)
                    .ToList();

                foreach (var d in descriptorsToRemove)
                    services.Remove(d);

                services.AddDbContext<PagueAresContext>(opts =>
                    opts.UseInMemoryDatabase(_dbName)
                        .ConfigureWarnings(w => w.Ignore(InMemoryEventId.TransactionIgnoredWarning)));
            });

            builder.UseSetting("Smtp:SmtpHost", "");
            builder.UseSetting("RunMigrations", "false");
            builder.UseSetting("ASPNETCORE_ENVIRONMENT", "Test");
        }

        public HttpClient GetAuthenticatedClient(int userId, string userName, params string[] roles)
        {
            var token = GenerateJwt(userId, userName, roles);
            var client = CreateClient();
            client.DefaultRequestHeaders.Authorization =
                new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token);
            return client;
        }

        public static string GenerateJwt(int userId, string userName, string[] roles)
        {
            var key = new SymmetricSecurityKey(Encoding.ASCII.GetBytes(JwtSecret));
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
                expires: DateTime.UtcNow.AddHours(1),
                signingCredentials: creds);

            return new JwtSecurityTokenHandler().WriteToken(token);
        }

        public void SeedDatabase(Action<PagueAresContext> seed)
        {
            using var scope = Services.CreateScope();
            var ctx = scope.ServiceProvider.GetRequiredService<PagueAresContext>();
            seed(ctx);
        }

        public static void SeedBaseData(PagueAresContext ctx)
        {
            // Funções
            ctx.Funcaos.AddRange(
                new Funcao { IdFuncao = 1, Nome = "Administrador", Alias = "ADM" },
                new Funcao { IdFuncao = 2, Nome = "Gestor", Alias = "GES" },
                new Funcao { IdFuncao = 3, Nome = "Financeiro", Alias = "FIN" },
                new Funcao { IdFuncao = 4, Nome = "Contabilidade", Alias = "CON" },
                new Funcao { IdFuncao = 5, Nome = "Usuário", Alias = "USR" }
            );

            // Departamentos
            ctx.Departamentos.AddRange(
                new Departamento { IdDepartamento = 1, Nome = "TI", CodigoLancamento = "TI01" },
                new Departamento { IdDepartamento = 2, Nome = "Financeiro", CodigoLancamento = "FI01" },
                new Departamento { IdDepartamento = 3, Nome = "RH", CodigoLancamento = "RH01" }
            );

            // Usuários
            ctx.Usuarios.AddRange(
                new Usuario { IdUsuario = 1, Email = "adm@test.com", Nome = "Admin", Senha = BCrypt.Net.BCrypt.HashPassword("senhaAdm"), Origem = 0, Ativo = true, DataCadastro = DateTime.Now, IdDepartamento = 1 },
                new Usuario { IdUsuario = 2, Email = "gestor@test.com", Nome = "Gestor", Senha = BCrypt.Net.BCrypt.HashPassword("senha"), Origem = 0, Ativo = true, DataCadastro = DateTime.Now, IdDepartamento = 1 },
                new Usuario { IdUsuario = 3, Email = "solicitante@test.com", Nome = "Solicitante", Senha = BCrypt.Net.BCrypt.HashPassword("senha"), Origem = 0, Ativo = true, DataCadastro = DateTime.Now, IdDepartamento = 1 },
                new Usuario { IdUsuario = 4, Email = "financeiro@test.com", Nome = "Financeiro", Senha = BCrypt.Net.BCrypt.HashPassword("senha"), Origem = 0, Ativo = true, DataCadastro = DateTime.Now, IdDepartamento = 2 },
                new Usuario { IdUsuario = 5, Email = "contabilidade@test.com", Nome = "Contabilidade", Senha = BCrypt.Net.BCrypt.HashPassword("senha"), Origem = 0, Ativo = true, DataCadastro = DateTime.Now, IdDepartamento = 2 },
                new Usuario { IdUsuario = 6, Email = "outro@test.com", Nome = "Outro Solicitante", Senha = BCrypt.Net.BCrypt.HashPassword("senha"), Origem = 0, Ativo = true, DataCadastro = DateTime.Now, IdDepartamento = 1 },
                new Usuario { IdUsuario = 7, Email = "inativo@test.com", Nome = "Inativo", Senha = BCrypt.Net.BCrypt.HashPassword("senha"), Origem = 0, Ativo = false, DataCadastro = DateTime.Now, IdDepartamento = 1 }
            );

            ctx.UsuarioFuncaos.AddRange(
                new UsuarioFuncao { IdUsuario = 1, IdFuncao = 1 },
                new UsuarioFuncao { IdUsuario = 2, IdFuncao = 2 },
                new UsuarioFuncao { IdUsuario = 3, IdFuncao = 5 },
                new UsuarioFuncao { IdUsuario = 4, IdFuncao = 3 },
                new UsuarioFuncao { IdUsuario = 5, IdFuncao = 4 },
                new UsuarioFuncao { IdUsuario = 6, IdFuncao = 5 }
            );

            ctx.SaveChanges();
        }

        private static Solicitacao MakeSolicitacao(int id, int userId, bool cancelado = false, bool rascunho = true,
            int statusGestor = 1, int statusFinanceiro = 1, int statusContabilidade = 1,
            bool? aprovadoGestor = null, bool? aprovadoSetor = null)
        {
            return new Solicitacao
            {
                IdSolicitacao = id,
                IdUsuario = userId,
                IdDepartamento = 1,
                DataSolicitacao = DateTime.Now,
                Descricao = "Teste",
                Valor = 100m,
                FormaPagamento = 1,
                CentroCusto = "",
                Projeto = "",
                Pca = "",
                NumDocParceiro = "",
                CodigoParceiro = "",
                NomeParceiro = "Parceiro Teste",
                BancoParceiro = "",
                AgenciaParceiro = "",
                ContaParceiro = "",
                Observacao = "",
                ObservacaoGestor = "",
                ObservacaoSetor = "",
                TipoAutorizacao = 0,
                TipoSolicitacao = 1,
                TipoPagamento = 1,
                Rascunho = rascunho,
                Cancelado = cancelado,
                StatusGestor = statusGestor,
                StatusFinanceiro = statusFinanceiro,
                StatusContabilidade = statusContabilidade,
                AprovadoGestor = aprovadoGestor,
                AprovadoSetor = aprovadoSetor
            };
        }

        public static void SeedSolicitacoes(PagueAresContext ctx)
        {
            ctx.Solicitacaos.AddRange(
                // ID 10: Cancelada — para teste do guard
                MakeSolicitacao(id: 10, userId: 3, cancelado: true, rascunho: false),
                // ID 11: Rascunho — dono é solicitante (id=3), para teste de segregação
                MakeSolicitacao(id: 11, userId: 3, rascunho: true),
                // IDs 101-106: Pendentes para cada teste de fluxo de aprovação individualmente
                MakeSolicitacao(id: 101, userId: 3, rascunho: false, statusGestor: 1),           // gestor aprova
                MakeSolicitacao(id: 102, userId: 3, rascunho: false, statusGestor: 1),           // gestor rejeita
                MakeSolicitacao(id: 103, userId: 3, rascunho: false, statusGestor: 2, statusFinanceiro: 1, aprovadoGestor: true), // financeiro aprova
                MakeSolicitacao(id: 104, userId: 3, rascunho: false, statusGestor: 2, statusFinanceiro: 1, aprovadoGestor: true), // financeiro rejeita
                MakeSolicitacao(id: 105, userId: 3, rascunho: false, statusGestor: 2, statusFinanceiro: 2, statusContabilidade: 1, aprovadoGestor: true, aprovadoSetor: true), // contabilidade lança
                MakeSolicitacao(id: 106, userId: 3, rascunho: false, statusGestor: 2, statusFinanceiro: 2, statusContabilidade: 1, aprovadoGestor: true, aprovadoSetor: true), // contabilidade recusa
                // ID 20: Pertence a usuário "outro" (id=6) — para teste de segregação
                MakeSolicitacao(id: 20, userId: 6, rascunho: false, statusGestor: 1)
            );
            ctx.SaveChanges();
        }

        private static Rddv MakeRddv(int id, int userId, bool cancelado = false, bool rascunho = true,
            int statusGestor = 1, int statusFinanceiro = 1, int statusContabilidade = 1,
            bool? aprovadoGestor = null, bool? aprovadoSetor = null)
        {
            return new Rddv
            {
                IdRelatorio = id,
                IdUsuario = userId,
                IdDepartamento = 1,
                NomeFuncionario = "Funcionário",
                CPF = "12345678901",
                Finalidade = "Teste",
                TipoRelatorio = 1,
                Moeda = "BRL",
                TipoViagem = 1,
                LocalViagem = "São Paulo",
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
                Cancelado = cancelado,
                StatusGestor = statusGestor,
                StatusFinanceiro = statusFinanceiro,
                StatusContabilidade = statusContabilidade,
                AprovadoGestor = aprovadoGestor,
                AprovadoSetor = aprovadoSetor
            };
        }

        public static void SeedRddvs(PagueAresContext ctx)
        {
            ctx.Rddv.AddRange(
                // Cancelado — para teste do guard
                MakeRddv(id: 50, userId: 3, cancelado: true, rascunho: false),
                // Rascunho
                MakeRddv(id: 51, userId: 3, rascunho: true),
                // Pendente gestor
                MakeRddv(id: 52, userId: 3, rascunho: false, statusGestor: 1),
                // Aprovado pelo gestor, aprovado pelo financeiro — para dashboard
                MakeRddv(id: 53, userId: 3, rascunho: false, statusGestor: 2, statusFinanceiro: 2, aprovadoGestor: true, aprovadoSetor: true),
                MakeRddv(id: 54, userId: 3, rascunho: false, statusGestor: 2, statusFinanceiro: 2, aprovadoGestor: true, aprovadoSetor: true)
            );
            ctx.SaveChanges();
        }
    }
}
