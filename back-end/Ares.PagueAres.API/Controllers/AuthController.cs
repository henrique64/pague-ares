using Ares.PagueAres.Application.Authentication;
using Ares.PagueAres.Domain;
using Ares.PagueAres.Domain.Dtos.Authentication;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Ares.PagueAres.API.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    [AllowAnonymous]
    public class AuthController(IAuthenticationService authenticationService) : ControllerBase
    {

        [HttpPost]
        public async Task<ActionResult<GenericResponse<dynamic>>> Authenticate([FromBody] UserLoginDto login)
        {
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

                var tokenResponse = authenticationService.GenerateAuthenticationToken(userAuth);

                return Ok(tokenResponse);
            }
            catch (Exception ex)
            {
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
