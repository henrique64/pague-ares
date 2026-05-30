namespace Ares.PagueAres.API.Dtos
{
    public class ListFilterDto
    {
        public string? DocumentNumber { get; set; }
        public DateTime? RequestStart { get; set; }
        public DateTime? RequestEnd { get; set; }
        public DateTime? DueStart { get; set; }
        public DateTime? DueEnd { get; set; }
        public int? RequesterId { get; set; }
        public int? ManagerStatus { get; set; }
        public int? FinanceStatus { get; set; }
        public int? AccountingStatus { get; set; }
        public int? AssigneeId { get; set; }
        public int? DocumentId { get; set; }
        public string? PartnerName { get; set; }
        public string? PartnerDocument { get; set; }
    }
}