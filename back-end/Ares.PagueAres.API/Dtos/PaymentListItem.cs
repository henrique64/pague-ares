namespace Ares.PagueAres.API.Dtos
{
    public class PaymentListItem
    {
        public string Codigo { get; set; } = null!;
        public string Tipo { get; set; } = null!;
        public DateTime DataInclusao { get; set; }
        public DateTime? DataVencimento { get; set; }
        public string DocNumero { get; set; } = null!;
        public string Solicitante { get; set; } = null!;
        public string Parceiro { get; set; } = null!;
        public decimal Valor { get; set; }
        public string StatusGestor { get; set; } = null!;
        public string StatusContabil { get; set; } = null!;
        public string StatusFinanceiro { get; set; } = null!;
    }
}