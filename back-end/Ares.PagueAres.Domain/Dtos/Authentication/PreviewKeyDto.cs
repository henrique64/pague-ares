using Ares.PagueAres.Domain.Enums;

namespace Ares.PagueAres.Domain.Dtos.Authentication
{
    public record PreviewKeyDto (
        Guid PreviewKey,
        EnumRequestType RequestType,
        int EntityId
        );

}
