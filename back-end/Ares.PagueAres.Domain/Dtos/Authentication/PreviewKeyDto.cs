using Ares.PagueAres.Domain.Enums;
using System.Text.Json.Serialization;

namespace Ares.PagueAres.Domain.Dtos.Authentication
{
    /// <param name="IdUsuario">
    /// Usuário do JWT que gerou a chave. Fica só no servidor: não vai no JSON da resposta.
    /// </param>
    public record PreviewKeyDto (
        Guid PreviewKey,
        EnumRequestType RequestType,
        int EntityId,
        [property: JsonIgnore] int IdUsuario = 0
        );

}
