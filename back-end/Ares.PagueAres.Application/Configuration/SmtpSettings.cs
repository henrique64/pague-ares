namespace Ares.PagueAres.Application.Configuration
{
    public class SmtpSettings
    {

        public bool EnableEmailService { get; set; }

        public string SmtpHost { get; set; } = null!;
        public int SmtpPort { get; set; } = 0!;
        public string SmtpUser { get; set; } = null!;
        public string SmtpPassword { get; set; } = null!;
        public string DefaultSender { get; set; } = null!;
        public string EnableSsl { get; set; } = null!;

    }
}
