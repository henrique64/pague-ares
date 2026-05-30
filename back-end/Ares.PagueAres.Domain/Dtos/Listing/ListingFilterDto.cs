using Ares.PagueAres.Domain.Enums;

namespace Ares.PagueAres.Domain.Dtos.Listing
{
    public record ListingFilterDto(
        string? DocumentNumber,
        DateTime? RequestStart,
        DateTime? RequestEnd,
        DateTime? DueStart,
        DateTime? DueEnd,
        int? Type,
        string? PartnerName,
        int? RequesterId,
        int? AssigneeId,
        bool? IsAssigned,
        int? ManagerStatus,
        int? FinanceStatus,
        int? AccountingStatus,
        int? DocumentId,
        PaymentListView? ViewMode,
        string? SortField,
        string? SortOrder = "asc",
        int PageSize = 100,
        int Page = 1);
}
