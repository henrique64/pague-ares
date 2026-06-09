using Ares.PagueAres.API.Controllers;
using Ares.PagueAres.Application.Authentication;
using Ares.PagueAres.Application.Exceptions;
using Ares.PagueAres.Domain;
using Ares.PagueAres.Domain.Dtos;
using Ares.PagueAres.Domain.Dtos.Authentication;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Caching.Memory;
using Moq;

namespace Ares.PagueAres.Tests.Unit.Authentication
{
    public class AuthControllerUnitTests
    {
        private static AuthController CreateController(
            IAuthenticationService authService,
            IMemoryCache? cache = null)
        {
            cache ??= new MemoryCache(new MemoryCacheOptions());
            var controller = new AuthController(authService, cache);
            controller.ControllerContext = new ControllerContext
            {
                HttpContext = new DefaultHttpContext()
            };
            return controller;
        }

        [Fact]
        public async Task Authenticate_CredenciaisValidas_RetornaToken()
        {
            var mockAuth = new Mock<IAuthenticationService>();
            var user = new UsuarioDto
            {
                IdUsuario = 1, Nome = "Test", Email = "t@t.com", Ativo = true,
                Senha = "", Funcoes = [new FuncaoDto { Alias = "USR", Nome = "Usuário" }]
            };
            mockAuth.Setup(s => s.TryAuthenticate("user", "pass")).ReturnsAsync(user);
            mockAuth.Setup(s => s.GenerateAuthenticationToken(user))
                .Returns(new GenericResponse<TokenDto>
                {
                    Success = true, Data = new TokenDto { Token = "jwt-token", Expires = DateTime.UtcNow.AddHours(12) }
                });

            var ctrl = CreateController(mockAuth.Object);
            var result = await ctrl.Authenticate(new UserLoginDto { Username = "user", Password = "pass" });

            var ok = result.Result.Should().BeOfType<OkObjectResult>().Subject;
            var response = ok.Value.Should().BeAssignableTo<GenericResponse<TokenDto>>().Subject;
            response.Success.Should().BeTrue();
            response.Data!.Token.Should().Be("jwt-token");
        }

        [Fact]
        public async Task Authenticate_UsuarioInativo_RetornaFalha()
        {
            var mockAuth = new Mock<IAuthenticationService>();
            var inactiveUser = new UsuarioDto
            {
                IdUsuario = 2, Nome = "Inativo", Email = "i@t.com", Ativo = false, Senha = "", Funcoes = []
            };
            mockAuth.Setup(s => s.TryAuthenticate("inativo", "pass")).ReturnsAsync(inactiveUser);

            var ctrl = CreateController(mockAuth.Object);
            var result = await ctrl.Authenticate(new UserLoginDto { Username = "inativo", Password = "pass" });

            var ok = result.Result.Should().BeOfType<OkObjectResult>().Subject;
            var response = ok.Value.Should().BeAssignableTo<GenericResponse<object>>().Subject;
            response.Success.Should().BeFalse();
            response.Message.Should().Contain("inativo");
        }

        [Fact]
        public async Task Authenticate_FalhaAbaixoDoLimite_NaoBloqueiaProximaTentativa()
        {
            var mockAuth = new Mock<IAuthenticationService>();
            mockAuth.Setup(s => s.TryAuthenticate(It.IsAny<string>(), It.IsAny<string>()))
                .ThrowsAsync(new LoginFailedException());

            var cache = new MemoryCache(new MemoryCacheOptions());
            var ctrl = CreateController(mockAuth.Object, cache);

            // 9 tentativas com falha
            for (int i = 0; i < 9; i++)
                await ctrl.Authenticate(new UserLoginDto { Username = "u", Password = "wrong" });

            // A 10ª ainda deve tentar autenticar (não retornar lockout)
            mockAuth.Verify(s => s.TryAuthenticate("u", "wrong"), Times.Exactly(9));
            // DefaultHttpContext tem RemoteIpAddress = null, então ip = "unknown"
            cache.TryGetValue("login_failures_unknown", out int failures);
            failures.Should().Be(9);
        }

        [Fact]
        public async Task Authenticate_FalhaAcimaDeLimite_RetornaMensagemDeBloqueio()
        {
            var mockAuth = new Mock<IAuthenticationService>();
            mockAuth.Setup(s => s.TryAuthenticate(It.IsAny<string>(), It.IsAny<string>()))
                .ThrowsAsync(new LoginFailedException());

            var cache = new MemoryCache(new MemoryCacheOptions());
            var ctrl = CreateController(mockAuth.Object, cache);

            // 10 tentativas com falha para atingir o limite
            for (int i = 0; i < 10; i++)
                await ctrl.Authenticate(new UserLoginDto { Username = "u", Password = "wrong" });

            // 11ª tentativa deve ser bloqueada sem chamar TryAuthenticate
            var result = await ctrl.Authenticate(new UserLoginDto { Username = "u", Password = "wrong" });
            var ok = result.Result.Should().BeOfType<OkObjectResult>().Subject;
            var response = ok.Value.Should().BeAssignableTo<GenericResponse<object>>().Subject;
            response.Success.Should().BeFalse();
            response.Message.Should().Contain("Muitas tentativas");

            // TryAuthenticate foi chamado exatamente 10 vezes (a 11ª foi bloqueada)
            mockAuth.Verify(s => s.TryAuthenticate(It.IsAny<string>(), It.IsAny<string>()), Times.Exactly(10));
        }

        [Fact]
        public async Task Authenticate_LoginBemSucedidoAposFalhas_ZeraContador()
        {
            var mockAuth = new Mock<IAuthenticationService>();
            var user = new UsuarioDto
            {
                IdUsuario = 1, Nome = "Test", Email = "t@t.com", Ativo = true, Senha = "", Funcoes = []
            };
            mockAuth.SetupSequence(s => s.TryAuthenticate(It.IsAny<string>(), It.IsAny<string>()))
                .ThrowsAsync(new LoginFailedException())
                .ThrowsAsync(new LoginFailedException())
                .ReturnsAsync(user);
            mockAuth.Setup(s => s.GenerateAuthenticationToken(user))
                .Returns(new GenericResponse<TokenDto>
                {
                    Success = true, Data = new TokenDto { Token = "t", Expires = DateTime.UtcNow.AddHours(1) }
                });

            var cache = new MemoryCache(new MemoryCacheOptions());
            var ctrl = CreateController(mockAuth.Object, cache);

            // 2 falhas
            await ctrl.Authenticate(new UserLoginDto { Username = "u", Password = "w" });
            await ctrl.Authenticate(new UserLoginDto { Username = "u", Password = "w" });

            // Login bem-sucedido deve zerar o contador
            await ctrl.Authenticate(new UserLoginDto { Username = "u", Password = "certa" });

            cache.TryGetValue("login_failures_unknown", out int failures);
            failures.Should().Be(0);
        }
    }
}
