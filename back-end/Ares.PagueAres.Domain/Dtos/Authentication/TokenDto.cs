namespace Ares.PagueAres.Domain.Dtos.Authentication
{
    public class TokenDto
    {
        public string Token { get; set; } = null!;
        public DateTime Expires { get; set; }
    }
}
