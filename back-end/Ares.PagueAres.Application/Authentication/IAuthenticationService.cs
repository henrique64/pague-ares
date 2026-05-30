using Ares.PagueAres.Domain;
using Ares.PagueAres.Domain.Dtos;
using Ares.PagueAres.Domain.Dtos.Authentication;

namespace Ares.PagueAres.Application.Authentication
{
    public interface IAuthenticationService
    {

        Task<UsuarioDto> TryAuthenticate(string username, string password);

        Task<UsuarioDto?> GetUserById(int id);

        GenericResponse<TokenDto> GenerateAuthenticationToken(UsuarioDto userData);

    }
}
