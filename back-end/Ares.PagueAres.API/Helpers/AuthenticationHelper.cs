using System.Linq;
using Ares.PagueAres.Domain.Dtos;

namespace Ares.PagueAres.API.Helpers
{
    public static class AuthenticationHelper
    {
        public static UsuarioDto? GetUserFromToken(HttpContext context)
        {
            var contextUser = context.Items["User"];

            if (contextUser is null) return null;

            return contextUser as UsuarioDto;
        }

        public static bool CurrentUserIsInRole(HttpContext context, params string[] roles)
        {
            var user = GetUserFromToken(context);

            if (user is null) return false;

            return user.Funcoes
                .Any(f => roles.Contains(f.Alias));
        }
    }
}
