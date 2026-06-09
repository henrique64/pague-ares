using Ares.PagueAres.Application.Authentication;
using Ares.PagueAres.Application.Configuration;
using Ares.PagueAres.Application.Exceptions;
using Ares.PagueAres.Infrastructure;
using Ares.PagueAres.Infrastructure.Models;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using System.Security.Cryptography;
using System.Text;

namespace Ares.PagueAres.Tests.Unit.Authentication
{
    public class AuthenticationServiceTests
    {
        private static PagueAresContext CreateContext(string dbName)
        {
            var opts = new DbContextOptionsBuilder<PagueAresContext>()
                .UseInMemoryDatabase(dbName)
                .Options;
            return new PagueAresContext(opts);
        }

        private static AuthenticationService CreateService(PagueAresContext ctx)
        {
            var adSettings = Options.Create(new ActiveDirectorySettings
            {
                EnableDomainAuthentication = false,
                Servers = []
            });
            var jwtSettings = Options.Create(new JwtSettings
            {
                Secret = "45AE44EA-8470-44C2-9A61-8283410570A6",
                Issuer = "pagueares.local",
                Audience = "pagueares.local",
                ExpiryMinutes = 720
            });
            return new AuthenticationService(ctx, adSettings, jwtSettings);
        }

        private static string ComputeSha256(string input)
        {
            byte[] bytes = SHA256.HashData(Encoding.UTF8.GetBytes(input));
            var sb = new StringBuilder();
            foreach (var b in bytes) sb.Append(b.ToString("x2"));
            return sb.ToString();
        }

        private static void SeedUser(PagueAresContext ctx, Usuario user, string[] roles)
        {
            ctx.Usuarios.Add(user);
            int funcaoId = 1;
            foreach (var alias in roles)
            {
                var funcao = ctx.Funcaos.FirstOrDefault(f => f.Alias == alias)
                    ?? new Funcao { IdFuncao = funcaoId++, Nome = alias, Alias = alias };
                if (!ctx.Funcaos.Any(f => f.Alias == alias))
                    ctx.Funcaos.Add(funcao);
                ctx.UsuarioFuncaos.Add(new UsuarioFuncao
                {
                    IdUsuario = user.IdUsuario,
                    IdFuncao = funcao.IdFuncao
                });
            }
            ctx.SaveChanges();
        }

        [Fact]
        public async Task TryAuthenticate_ComSenhaSha256Valida_RetornaUsuario()
        {
            using var ctx = CreateContext(nameof(TryAuthenticate_ComSenhaSha256Valida_RetornaUsuario));
            var hash = ComputeSha256("senha123");
            SeedUser(ctx, new Usuario
            {
                IdUsuario = 1, Email = "user@test.com", Nome = "Usuário Teste",
                Senha = hash, Origem = 0, Ativo = true, DataCadastro = DateTime.Now,
                IdDepartamento = 1
            }, ["USR"]);

            var svc = CreateService(ctx);
            var result = await svc.TryAuthenticate("user@test.com", "senha123");

            result.Should().NotBeNull();
            result.Email.Should().Be("user@test.com");
        }

        [Fact]
        public async Task TryAuthenticate_ComSenhaBcryptValida_RetornaUsuario()
        {
            using var ctx = CreateContext(nameof(TryAuthenticate_ComSenhaBcryptValida_RetornaUsuario));
            var hash = BCrypt.Net.BCrypt.HashPassword("senhaSegura");
            SeedUser(ctx, new Usuario
            {
                IdUsuario = 1, Email = "bcrypt@test.com", Nome = "BCrypt User",
                Senha = hash, Origem = 0, Ativo = true, DataCadastro = DateTime.Now,
                IdDepartamento = 1
            }, ["USR"]);

            var svc = CreateService(ctx);
            var result = await svc.TryAuthenticate("bcrypt@test.com", "senhaSegura");

            result.Should().NotBeNull();
            result.Email.Should().Be("bcrypt@test.com");
        }

        [Fact]
        public async Task TryAuthenticate_ComSenhaSha256_MigraParaBcrypt()
        {
            using var ctx = CreateContext(nameof(TryAuthenticate_ComSenhaSha256_MigraParaBcrypt));
            var sha256Hash = ComputeSha256("migrar123");
            var usuario = new Usuario
            {
                IdUsuario = 1, Email = "migra@test.com", Nome = "Migração",
                Senha = sha256Hash, Origem = 0, Ativo = true, DataCadastro = DateTime.Now,
                IdDepartamento = 1
            };
            SeedUser(ctx, usuario, ["USR"]);

            var svc = CreateService(ctx);
            await svc.TryAuthenticate("migra@test.com", "migrar123");

            var dbUser = ctx.Usuarios.First(u => u.Email == "migra@test.com");
            dbUser.Senha.Should().StartWith("$2", "o hash deve ter sido migrado para BCrypt");
        }

        [Fact]
        public async Task TryAuthenticate_ComSenhaErrada_LancaExcecao()
        {
            using var ctx = CreateContext(nameof(TryAuthenticate_ComSenhaErrada_LancaExcecao));
            var hash = BCrypt.Net.BCrypt.HashPassword("correta");
            SeedUser(ctx, new Usuario
            {
                IdUsuario = 1, Email = "wrong@test.com", Nome = "Wrong",
                Senha = hash, Origem = 0, Ativo = true, DataCadastro = DateTime.Now,
                IdDepartamento = 1
            }, ["USR"]);

            var svc = CreateService(ctx);
            await svc.Invoking(s => s.TryAuthenticate("wrong@test.com", "errada"))
                .Should().ThrowAsync<LoginFailedException>();
        }

        [Fact]
        public async Task TryAuthenticate_UsuarioNaoEncontrado_LancaExcecao()
        {
            using var ctx = CreateContext(nameof(TryAuthenticate_UsuarioNaoEncontrado_LancaExcecao));
            var svc = CreateService(ctx);

            await svc.Invoking(s => s.TryAuthenticate("naoexiste@test.com", "qualquer"))
                .Should().ThrowAsync<LoginFailedException>();
        }

        [Fact]
        public void GenerateAuthenticationToken_UsuarioValido_TokenContemClaims()
        {
            using var ctx = CreateContext(nameof(GenerateAuthenticationToken_UsuarioValido_TokenContemClaims));
            var svc = CreateService(ctx);

            var user = new Domain.Dtos.UsuarioDto
            {
                IdUsuario = 42,
                Nome = "Admin Teste",
                Email = "adm@test.com",
                Funcoes = [new Domain.Dtos.FuncaoDto { Alias = "ADM", Nome = "Administrador" }]
            };

            var response = svc.GenerateAuthenticationToken(user);

            response.Success.Should().BeTrue();
            response.Data.Should().NotBeNull();
            response.Data!.Token.Should().NotBeNullOrEmpty();

            var handler = new System.IdentityModel.Tokens.Jwt.JwtSecurityTokenHandler();
            var jwt = handler.ReadJwtToken(response.Data.Token);

            jwt.Claims.Should().Contain(c => c.Type == "nameid" && c.Value == "42");
            jwt.Claims.Should().Contain(c => c.Type == "Roles" && c.Value.Contains("ADM"));
            jwt.Issuer.Should().Be("pagueares.local");
        }
    }
}
