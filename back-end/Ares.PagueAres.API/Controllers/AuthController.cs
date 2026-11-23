using Ares.PagueAres.Application.Authentication;
using Ares.PagueAres.Domain;
using Ares.PagueAres.Domain.Dtos.Authentication;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Caching.Memory;

namespace Ares.PagueAres.API.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    [AllowAnonymous]
    public class AuthController(IAuthenticationService authenticationService, IMemoryCache cache) : ControllerBase
    {
        private const int MaxLoginFailures = 10;
        private static readonly TimeSpan LockoutWindow = TimeSpan.FromMinutes(5);

        [HttpPost]
        public async Task<ActionResult<GenericResponse<dynamic>>> Authenticate([FromBody] UserLoginDto login)
        {
            var ip = HttpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown";
            var cacheKey = $"login_failures_{ip}";

            if (cache.TryGetValue(cacheKey, out int failures) && failures >= MaxLoginFailures)
            {
                return Ok(new GenericResponse<object>()
                {
                    Success = false,
                    Message = "Muitas tentativas de login. Aguarde alguns minutos e tente novamente.",
                    Data = null,
                    Page = 1,
                    Pages = 1,
                    Records = 0
                });
            }

            try
            {
                var userAuth = await authenticationService.TryAuthenticate(login.Username, login.Password);

                if (!(userAuth.Ativo ?? true))
                {
                    return Ok(new GenericResponse<object>()
                    {
                        Success = false,
                        Message = "Usuário inativo.",
                        Data = null,
                        Page = 1,
                        Pages = 1,
                        Records = 0
                    });
                }

                cache.Remove(cacheKey);

                var tokenResponse = authenticationService.GenerateAuthenticationToken(userAuth);

                return Ok(tokenResponse);
            }
            catch (Exception ex)
            {
                var newCount = failures + 1;
                cache.Set(cacheKey, newCount, LockoutWindow);

                return Ok(new GenericResponse<object>()
                {
                    Success = false,
                    Message = ex.Message,
                    Data = null,
                    Page = 1,
                    Pages = 1,
                    Records = 0
                });
            }
        }

    }
}
