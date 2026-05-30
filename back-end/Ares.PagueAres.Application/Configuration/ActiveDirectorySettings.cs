namespace Ares.PagueAres.Application.Configuration
{
    public class ActiveDirectorySettings
    {

        public bool EnableDomainAuthentication { get; set; } = false!;

        public IEnumerable<DomainServer> Servers { get; set; } = null!;

    }
}
