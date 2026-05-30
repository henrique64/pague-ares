using Ares.PagueAres.Application.Authentication;
using Ares.PagueAres.Application.Services;
using Microsoft.Extensions.DependencyInjection;

namespace Ares.PagueAres.Application.Extensions
{
    public static class ApplicationServiceExtensions
    {

        public static IServiceCollection RegisterApplicationServices(this IServiceCollection service)
        {
            service.AddScoped<IAuthenticationService, AuthenticationService>();
            service.AddScoped<EmailService>();

            return service;
        }

    }
}
