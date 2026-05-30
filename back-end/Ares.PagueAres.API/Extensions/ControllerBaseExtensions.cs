using Ares.PagueAres.API.Helpers;
using Ares.PagueAres.Domain.Dtos;
using Microsoft.AspNetCore.Mvc;

namespace Ares.PagueAres.API.Extensions
{
    public static class ControllerBaseExtensions
    {
        public static UsuarioDto? GetUserFromToken(this ControllerBase controller)
        {
            var contextUser = controller.HttpContext.Items["User"];

            if (contextUser is null) return null;

            return contextUser as UsuarioDto;
        }

        public static bool CurrentUserIsInRole(this ControllerBase controller, params string[] roles)
        {
            return AuthenticationHelper.CurrentUserIsInRole(controller.HttpContext, roles);
        }
    }
}
