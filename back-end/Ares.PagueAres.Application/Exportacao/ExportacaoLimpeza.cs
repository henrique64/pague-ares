using Ares.PagueAres.Application.Configuration;
using Ares.PagueAres.Domain.Enums;
using Ares.PagueAres.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Ares.PagueAres.Application.Exportacao
{
    /// <summary>
    /// Serviço em segundo plano que, ao subir e depois a cada <c>IntervaloLimpezaMinutos</c>, apaga os pacotes vencidos
    /// (contrato C9): jobs Concluido com <c>DataExpiracao</c> vencida viram Expirado e perdem o arquivo; arquivos
    /// <c>exportacao-*.zip</c> sem job Concluido correspondente e mais velhos que <c>RetencaoHoras</c> são apagados.
    /// </summary>
    public sealed class ExportacaoLimpeza : BackgroundService
    {
        private readonly IServiceScopeFactory _scopeFactory;
        private readonly ExportacaoSettings _settings;
        private readonly ILogger<ExportacaoLimpeza> _logger;

        public ExportacaoLimpeza(IServiceScopeFactory scopeFactory, IOptions<ExportacaoSettings> settings,
            ILogger<ExportacaoLimpeza> logger)
        {
            _scopeFactory = scopeFactory;
            _settings = settings.Value;
            _logger = logger;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            // Não segura a subida do host com a primeira varredura.
            await Task.Yield();

            var intervalo = TimeSpan.FromMinutes(Math.Max(1, _settings.IntervaloLimpezaMinutos));
            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    await LimparAsync(stoppingToken);
                }
                catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
                {
                    return;
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Falha na limpeza dos pacotes de exportação.");
                }

                try
                {
                    await Task.Delay(intervalo, stoppingToken);
                }
                catch (OperationCanceledException)
                {
                    return;
                }
            }
        }

        /// <summary>Uma varredura completa: expira os jobs vencidos e apaga os arquivos órfãos.</summary>
        public async Task LimparAsync(CancellationToken ct)
        {
            var pasta = ArquivosExportacao.ResolverPasta(_settings);
            var agora = DateTime.UtcNow;

            await ExpirarVencidosAsync(pasta, agora, ct);
            await ApagarOrfaosAsync(pasta, agora, ct);
        }

        private async Task ExpirarVencidosAsync(string pasta, DateTime agora, CancellationToken ct)
        {
            List<int> vencidos;
            using (var scope = _scopeFactory.CreateScope())
            {
                var db = scope.ServiceProvider.GetRequiredService<PagueAresContext>();
                vencidos = await db.ExportacaoJobs.AsNoTracking()
                    .Where(j => j.Status == (int)StatusExportacao.Concluido
                                && j.DataExpiracao != null
                                && j.DataExpiracao <= agora)
                    .Select(j => j.IdExportacaoJob)
                    .ToListAsync(ct);
            }

            foreach (var id in vencidos)
            {
                // O status muda antes de o arquivo sumir, para que o download nunca seja oferecido sem arquivo. Se o
                // arquivo estiver preso (um download em curso), ele vira órfão e sai numa próxima varredura.
                string? nomeArquivo = null;
                var expirou = await GravacaoExportacaoJob.AtualizarAsync(_scopeFactory, id, job =>
                {
                    if (job.Status != (int)StatusExportacao.Concluido || job.DataExpiracao is null || job.DataExpiracao > agora)
                        return false;

                    nomeArquivo = job.NomeArquivo;
                    job.Status = (int)StatusExportacao.Expirado;
                    return true;
                }, ct);

                if (!expirou)
                    continue;

                var caminho = ArquivosExportacao.CaminhoPacote(pasta, nomeArquivo);
                if (caminho is not null)
                    ArquivosExportacao.TentarApagar(caminho, _logger);

                _logger.LogInformation("Exportação {IdExportacao} expirou e o pacote foi apagado.", id);
            }
        }

        private async Task ApagarOrfaosAsync(string pasta, DateTime agora, CancellationToken ct)
        {
            if (!Directory.Exists(pasta))
                return;

            HashSet<string> validos;
            using (var scope = _scopeFactory.CreateScope())
            {
                var db = scope.ServiceProvider.GetRequiredService<PagueAresContext>();
                var nomes = await db.ExportacaoJobs.AsNoTracking()
                    .Where(j => j.Status == (int)StatusExportacao.Concluido && j.NomeArquivo != null)
                    .Select(j => j.NomeArquivo!)
                    .ToListAsync(ct);
                validos = new HashSet<string>(nomes, StringComparer.OrdinalIgnoreCase);
            }

            var limite = agora.AddHours(-_settings.RetencaoHoras);
            foreach (var caminho in Directory.EnumerateFiles(pasta, ArquivosExportacao.PrefixoArquivo + "*"))
            {
                var nome = Path.GetFileName(caminho);
                if (!ArquivosExportacao.EhPacote(nome) || validos.Contains(nome))
                    continue;

                DateTime ultimaGravacao;
                try
                {
                    ultimaGravacao = File.GetLastWriteTimeUtc(caminho);
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                {
                    continue;
                }

                if (ultimaGravacao > limite)
                    continue;

                ArquivosExportacao.TentarApagar(caminho, _logger);
                _logger.LogInformation("Pacote de exportação órfão {Arquivo} apagado.", nome);
            }
        }
    }
}
