using Ares.PagueAres.Application.Authentication;
using Ares.PagueAres.Application.Configuration;
using Ares.PagueAres.Application.Exportacao;
using Ares.PagueAres.Application.Services;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Ares.PagueAres.Application.Extensions
{
    public static class ApplicationServiceExtensions
    {

        public static IServiceCollection RegisterApplicationServices(this IServiceCollection service, IConfiguration configuration)
        {
            service.AddScoped<IAuthenticationService, AuthenticationService>();
            service.AddScoped<EmailService>();

            // Exportação de títulos com anexos: fonte e gerador por escopo (um por job), fila única no processo.
            service.AddScoped<IFonteExportacao, FonteExportacaoEf>();
            service.AddScoped<IGeradorPacoteExportacao, GeradorPacoteExportacao>();
            service.AddSingleton<ExportacaoFila>();
            service.TryAddSingleton(TimeProvider.System);
            service.AddSingleton<FusoExportacao>();
            service.AddSingleton<IMedidorEspacoLivre, MedidorEspacoLivreUnidade>();

            // Com WorkerHabilitado = false nenhum dos dois serviços sobe e os jobs ficam Pendente.
            var exportacao = configuration.GetSection("Exportacao").Get<ExportacaoSettings>() ?? new ExportacaoSettings();
            if (exportacao.WorkerHabilitado)
            {
                service.AddHostedService<ExportacaoWorker>();
                service.AddHostedService<ExportacaoLimpeza>();
            }

            return service;
        }

    }
}
