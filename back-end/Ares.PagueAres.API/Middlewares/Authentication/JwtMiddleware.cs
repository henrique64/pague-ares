using Ares.PagueAres.Application.Configuration;
using Ares.PagueAres.Domain.Dtos;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using System.IdentityModel.Tokens.Jwt;
using System.Text;

namespace Ares.PagueAres.API.Middlewares.Authentication
{
    public class JwtMiddleware(RequestDelegate next, IOptions<JwtSettings> appSettings)
    {
        private readonly JwtSettings _appSettings = appSettings.Value;

        public async Task Invoke(HttpContext context)
        {
            var token = context.Request.Headers.Authorization
                .FirstOrDefault()?.Split(" ").Last();

            if (token != null)
                AttachUserToContext(context, token);

            await next(context);
        }

        private void AttachUserToContext(HttpContext context, string token)
        {
            try
            {
                var tokenHandler = new JwtSecurityTokenHandler();
                var key = Encoding.ASCII.GetBytes(_appSettings.Secret);

                tokenHandler.ValidateToken(token, new TokenValidationParameters
                {
                    ValidateIssuerSigningKey = true,
                    IssuerSigningKey = new SymmetricSecurityKey(key),
                    ValidateIssuer = false,
                    ValidateAudience = false,
                    // set clockskew to zero so tokens expire exactly at token expiration time (instead of 5 minutes later)
                    ClockSkew = TimeSpan.Zero
                }, out SecurityToken validatedToken);

                var jwtToken = (JwtSecurityToken)validatedToken;

                var userId = jwtToken.Claims.FirstOrDefault(x => x.Type == "nameid")?.Value ?? "0";
                var userName = jwtToken.Claims.FirstOrDefault(x => x.Type == "sub")?.Value ?? string.Empty;
                var roles = jwtToken.Claims.FirstOrDefault(x => x.Type == "Roles")?.Value;

                var user = new UsuarioDto()
                {
                    IdUsuario = int.Parse(userId),
                    Nome = userName,
                    Funcoes = [.. roles?.Split(',')?.Select(r => new FuncaoDto() { Alias = r }) ?? []]
                };

                // attach user to context on successful jwt validation
                context.Items["User"] = user;
            }
            catch
            {
                // do nothing if jwt validation fails
                // user is not attached to context so request won't have access to secure routes
            }
        }
    }
}
