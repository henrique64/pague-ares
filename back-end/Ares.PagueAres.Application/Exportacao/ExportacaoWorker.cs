using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using Ares.PagueAres.Application.Configuration;
using Ares.PagueAres.Domain.Dtos.Exportacao;
using Ares.PagueAres.Domain.Enums;
using Ares.PagueAres.Infrastructure;
using Ares.PagueAres.Infrastructure.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Ares.PagueAres.Application.Exportacao
{
    /// <summary>
    /// Serviço em segundo plano que gera os pacotes da exportação (contrato C9), um job por vez.
    /// <para>
    /// Concorrência: o job é reivindicado (Pendente → Processando) com o token <c>Versao</c> da tabela
    /// <c>ExportacaoJob</c>; se outro executor gravou antes (<see cref="DbUpdateConcurrencyException"/>) ou o job já não
    /// está Pendente, ele é pulado sem gerar. Toda gravação posterior relê o job e só vale enquanto ele continuar
    /// Processando, então um executor que perdeu o job nunca sobrescreve o estado gravado por outro.
    /// </para>
    /// <para>
    /// O worker é singleton: cada job ganha um escopo de DI próprio (fonte e gerador), e cada gravação de progresso ou
    /// de estado usa outro escopo, curto.
    /// </para>
    /// </summary>
    public sealed class ExportacaoWorker : BackgroundService
    {
        public const string MensagemInterrupcao =
            "A exportação foi interrompida por um reinício do servidor. Gere a exportação de novo.";

        public const string MensagemErroGenerico =
            "Não foi possível gerar a exportação. Gere de novo; se o erro continuar, acione o suporte.";

        public const string MensagemEspacoInsuficiente =
            "O servidor está sem espaço em disco suficiente para gerar a exportação agora. " +
            "Tente de novo mais tarde; se o erro continuar, acione o suporte.";

        /// <summary>Intervalo máximo entre duas gravações de progresso (e entre duas conferências de cancelamento).</summary>
        private static readonly TimeSpan IntervaloProgresso = TimeSpan.FromSeconds(2);

        /// <summary>Quantidade de títulos que antecipa a gravação do progresso, antes de completar o intervalo.</summary>
        private const int TitulosPorGravacaoDeProgresso = 10;

        private static readonly JsonSerializerOptions OpcoesFiltro = new(JsonSerializerDefaults.Web)
        {
            Converters = { new JsonStringEnumConverter() },
        };

        private readonly IServiceScopeFactory _scopeFactory;
        private readonly ExportacaoFila _fila;
        private readonly ExportacaoSettings _settings;
        private readonly IMedidorEspacoLivre _medidorEspaco;
        private readonly ILogger<ExportacaoWorker> _logger;

        public ExportacaoWorker(IServiceScopeFactory scopeFactory, ExportacaoFila fila,
            IOptions<ExportacaoSettings> settings, IMedidorEspacoLivre medidorEspaco, ILogger<ExportacaoWorker> logger)
        {
            _scopeFactory = scopeFactory;
            _fila = fila;
            _settings = settings.Value;
            _medidorEspaco = medidorEspaco;
            _logger = logger;
        }

        /// <summary>
        /// Faz a recuperação antes de começar a ler a fila, para que ela termine antes de qualquer reivindicação deste
        /// processo: um job que este worker acabou de pegar nunca é confundido com um job interrompido.
        /// </summary>
        public override async Task StartAsync(CancellationToken cancellationToken)
        {
            await RecuperarAsync(cancellationToken);
            await base.StartAsync(cancellationToken);
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            try
            {
                await foreach (var id in _fila.LerAsync(stoppingToken))
                    await ProcessarAsync(id, stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                // Parada do host: os jobs ainda Pendente voltam à fila no próximo início.
            }
        }

        // -----------------------------------------------------------------------------------------------------
        // Recuperação no início
        // -----------------------------------------------------------------------------------------------------

        private async Task RecuperarAsync(CancellationToken ct)
        {
            var pasta = ArquivosExportacao.ResolverPasta(_settings);

            try
            {
                List<int> interrompidos;
                using (var scope = _scopeFactory.CreateScope())
                {
                    var db = scope.ServiceProvider.GetRequiredService<PagueAresContext>();
                    interrompidos = await db.ExportacaoJobs.AsNoTracking()
                        .Where(j => j.Status == (int)StatusExportacao.Processando)
                        .Select(j => j.IdExportacaoJob)
                        .ToListAsync(ct);
                }

                foreach (var id in interrompidos)
                {
                    if (await GravacaoExportacaoJob.AtualizarAsync(_scopeFactory, id,
                            job => MarcarFalhou(job, MensagemInterrupcao), ct))
                    {
                        _logger.LogWarning("Exportação {IdExportacao} estava em processamento no início do serviço e foi marcada como falha.", id);
                    }
                }
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger.LogError(ex, "Falha ao marcar como interrompidas as exportações que estavam em processamento.");
            }

            try
            {
                Directory.CreateDirectory(pasta);
                foreach (var caminho in Directory.EnumerateFiles(pasta, ArquivosExportacao.PrefixoArquivo + "*"))
                {
                    if (ArquivosExportacao.EhParcial(Path.GetFileName(caminho)))
                        ArquivosExportacao.TentarApagar(caminho, _logger);
                }
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                _logger.LogError(ex, "Falha ao preparar a pasta de exportação {Pasta}.", pasta);
            }

            try
            {
                List<int> pendentes;
                using (var scope = _scopeFactory.CreateScope())
                {
                    var db = scope.ServiceProvider.GetRequiredService<PagueAresContext>();
                    pendentes = await db.ExportacaoJobs.AsNoTracking()
                        .Where(j => j.Status == (int)StatusExportacao.Pendente)
                        .OrderBy(j => j.DataCriacao)
                        .ThenBy(j => j.IdExportacaoJob)
                        .Select(j => j.IdExportacaoJob)
                        .ToListAsync(ct);
                }

                foreach (var id in pendentes)
                    _fila.Enfileirar(id);

                if (pendentes.Count > 0)
                    _logger.LogInformation("{Quantidade} exportações pendentes voltaram para a fila.", pendentes.Count);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger.LogError(ex, "Falha ao reenfileirar as exportações pendentes.");
            }
        }

        // -----------------------------------------------------------------------------------------------------
        // Processamento de um job
        // -----------------------------------------------------------------------------------------------------

        private async Task ProcessarAsync(int id, CancellationToken stoppingToken)
        {
            try
            {
                using var cts = CancellationTokenSource.CreateLinkedTokenSource(stoppingToken);
                using var execucao = await _fila.IniciarExecucaoAsync(id, cts, stoppingToken);

                var filtrosJson = await ReivindicarAsync(id, stoppingToken);
                if (filtrosJson is null)
                    return;

                await GerarAsync(id, filtrosJson, execucao, cts, stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                // Parada antes da reivindicação gravar: o job continua Pendente e volta à fila no próximo início.
            }
            catch (Exception ex)
            {
                // Só chega aqui uma falha ao gravar o estado final (por exemplo, banco fora do ar). O job fica
                // Processando e vira Falhou na recuperação do próximo início.
                _logger.LogError(ex, "Falha inesperada ao processar a exportação {IdExportacao}.", id);
            }
        }

        /// <summary>
        /// Pendente → Processando com o token <c>Versao</c>. Devolve os filtros do job reivindicado, ou <c>null</c> quando
        /// o job deve ser pulado (não existe, não está Pendente, teve o cancelamento pedido ou outro executor o pegou).
        /// </summary>
        private async Task<string?> ReivindicarAsync(int id, CancellationToken ct)
        {
            using var scope = _scopeFactory.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<PagueAresContext>();

            var job = await db.ExportacaoJobs.SingleOrDefaultAsync(j => j.IdExportacaoJob == id, ct);
            if (job is null || job.Status != (int)StatusExportacao.Pendente)
                return null;

            var agora = DateTime.UtcNow;
            var cancelar = job.CancelamentoSolicitado;
            if (cancelar)
            {
                job.Status = (int)StatusExportacao.Cancelado;
                job.DataConclusao = agora;
            }
            else
            {
                job.Status = (int)StatusExportacao.Processando;
                job.DataInicio = agora;
                job.TitulosProcessados = 0;
                job.MensagemErro = null;
            }

            try
            {
                await db.SaveChangesAsync(ct);
            }
            catch (DbUpdateConcurrencyException)
            {
                _logger.LogInformation("Exportação {IdExportacao} foi alterada por outro executor; pulada.", id);
                return null;
            }

            if (cancelar)
            {
                _logger.LogInformation("Exportação {IdExportacao} cancelada antes de começar.", id);
                return null;
            }

            return job.FiltrosJson;
        }

        private async Task GerarAsync(int id, string filtrosJson, ExecucaoExportacao execucao,
            CancellationTokenSource cts, CancellationToken stoppingToken)
        {
            var pasta = ArquivosExportacao.ResolverPasta(_settings);
            var nomeFinal = ArquivosExportacao.NomeArquivo(id);
            var caminhoFinal = Path.Combine(pasta, nomeFinal);
            var caminhoParcial = Path.Combine(pasta, ArquivosExportacao.NomeArquivoParcial(id));

            ResultadoPacoteExportacao? resultado = null;
            string? mensagemFalha = null;
            var cancelado = false;

            var progresso = new ProgressoJob();
            using var fimAcompanhamento = new CancellationTokenSource();
            var acompanhamento = AcompanharAsync(id, progresso, execucao, fimAcompanhamento.Token);

            try
            {
                var filtro = JsonSerializer.Deserialize<FiltroExportacaoDto>(filtrosJson, OpcoesFiltro)
                             ?? throw new JsonException("Filtros da exportação vazios.");

                Directory.CreateDirectory(pasta);
                if (!HaEspacoLivre(pasta))
                {
                    mensagemFalha = MensagemEspacoInsuficiente;
                }
                else
                {
                    using var scope = _scopeFactory.CreateScope();
                    var gerador = scope.ServiceProvider.GetRequiredService<IGeradorPacoteExportacao>();

                    await using (var arquivo = new FileStream(caminhoParcial, FileMode.Create, FileAccess.Write,
                                     FileShare.None, bufferSize: 1 << 16))
                    {
                        resultado = await gerador.GerarAsync(id, filtro, arquivo, progresso, cts.Token);
                        await arquivo.FlushAsync(cts.Token);
                    }

                    // Um cancelamento pedido no fim da geração ainda vale: o pacote não é publicado.
                    cts.Token.ThrowIfCancellationRequested();
                    File.Move(caminhoParcial, caminhoFinal, overwrite: true);
                }
            }
            catch (OperationCanceledException) when (cts.IsCancellationRequested)
            {
                resultado = null;
                if (execucao.CancelamentoSolicitado)
                    cancelado = true;
                else if (stoppingToken.IsCancellationRequested)
                    mensagemFalha = MensagemInterrupcao;
                else
                    _logger.LogWarning("Exportação {IdExportacao} deixou de estar em processamento no banco; geração interrompida.", id);
            }
            catch (ExportacaoException ex)
            {
                resultado = null;
                mensagemFalha = ex.Message;
                _logger.LogWarning("Exportação {IdExportacao} recusada pelo gerador: {Mensagem}", id, ex.Message);
            }
            catch (Exception ex)
            {
                resultado = null;
                mensagemFalha = MensagemErroGenerico;
                _logger.LogError(ex, "Falha ao gerar a exportação {IdExportacao}.", id);
            }
            finally
            {
                fimAcompanhamento.Cancel();
                await acompanhamento;
            }

            if (resultado is not null && !execucao.PossePerdida)
            {
                if (!await ConcluirAsync(id, nomeFinal, new FileInfo(caminhoFinal).Length, resultado))
                    ArquivosExportacao.TentarApagar(caminhoFinal, _logger);
                return;
            }

            // Em toda falha ou cancelamento o pacote incompleto é apagado.
            ArquivosExportacao.TentarApagar(caminhoParcial, _logger);
            ArquivosExportacao.TentarApagar(caminhoFinal, _logger);

            if (cancelado)
            {
                await GravacaoExportacaoJob.AtualizarAsync(_scopeFactory, id, MarcarCancelado, CancellationToken.None);
                _logger.LogInformation("Exportação {IdExportacao} cancelada durante a geração.", id);
            }
            else if (mensagemFalha is not null)
            {
                await GravacaoExportacaoJob.AtualizarAsync(_scopeFactory, id,
                    job => MarcarFalhou(job, mensagemFalha), CancellationToken.None);
            }
        }

        /// <summary>
        /// Grava a conclusão. Devolve <c>false</c> quando o pacote não deve ficar disponível: o job deixou de estar
        /// Processando (outro executor o mudou) ou o cancelamento foi pedido no banco enquanto ele terminava.
        /// </summary>
        private async Task<bool> ConcluirAsync(int id, string nomeArquivo, long tamanhoBytes, ResultadoPacoteExportacao resultado)
        {
            var cancelado = false;
            var gravou = await GravacaoExportacaoJob.AtualizarAsync(_scopeFactory, id, job =>
            {
                cancelado = job.CancelamentoSolicitado;
                if (cancelado)
                    return MarcarCancelado(job);

                if (job.Status != (int)StatusExportacao.Processando)
                    return false;

                var agora = DateTime.UtcNow;
                job.Status = (int)StatusExportacao.Concluido;
                job.NomeArquivo = nomeArquivo;
                job.TamanhoBytes = tamanhoBytes;
                job.TotalTitulos = resultado.TotalTitulos;
                job.TitulosProcessados = resultado.TotalTitulos;
                job.TotalAnexos = resultado.TotalAnexos;
                job.DataConclusao = agora;
                job.DataExpiracao = agora.AddHours(_settings.RetencaoHoras);
                job.MensagemErro = null;
                return true;
            }, CancellationToken.None);

            if (gravou && !cancelado)
            {
                _logger.LogInformation("Exportação {IdExportacao} concluída: {Titulos} títulos, {Anexos} anexos, {Bytes} bytes.",
                    id, resultado.TotalTitulos, resultado.TotalAnexos, tamanhoBytes);
                return true;
            }

            return false;
        }

        private static bool MarcarFalhou(ExportacaoJob job, string mensagem)
        {
            if (job.Status != (int)StatusExportacao.Processando)
                return false;

            job.Status = (int)StatusExportacao.Falhou;
            job.MensagemErro = mensagem.Length <= 500 ? mensagem : mensagem[..500];
            job.DataConclusao = DateTime.UtcNow;
            job.NomeArquivo = null;
            job.TamanhoBytes = null;
            return true;
        }

        private static bool MarcarCancelado(ExportacaoJob job)
        {
            if (job.Status != (int)StatusExportacao.Processando)
                return false;

            job.Status = (int)StatusExportacao.Cancelado;
            job.DataConclusao = DateTime.UtcNow;
            job.NomeArquivo = null;
            job.TamanhoBytes = null;
            return true;
        }

        // -----------------------------------------------------------------------------------------------------
        // Progresso e conferência de cancelamento
        // -----------------------------------------------------------------------------------------------------

        /// <summary>
        /// Enquanto o job gera, grava o progresso a cada <see cref="IntervaloProgresso"/> ou a cada
        /// <see cref="TitulosPorGravacaoDeProgresso"/> títulos, o que vier primeiro. A cada volta relê o job: um pedido de
        /// cancelamento gravado no banco (inclusive por outro processo) ou a perda do status Processando interrompe a
        /// geração. Nunca lança: uma falha aqui não derruba a geração.
        /// </summary>
        private async Task AcompanharAsync(int id, ProgressoJob progresso, ExecucaoExportacao execucao, CancellationToken fim)
        {
            ProgressoExportacao? gravado = null;
            while (true)
            {
                try
                {
                    await progresso.AguardarAsync(IntervaloProgresso, fim);
                }
                catch (OperationCanceledException)
                {
                    return;
                }

                try
                {
                    gravado = await GravarProgressoAsync(id, progresso.Atual, gravado, execucao);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Falha ao gravar o progresso da exportação {IdExportacao}.", id);
                }
            }
        }

        private async Task<ProgressoExportacao?> GravarProgressoAsync(int id, ProgressoExportacao? atual,
            ProgressoExportacao? gravado, ExecucaoExportacao execucao)
        {
            using var scope = _scopeFactory.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<PagueAresContext>();

            var job = await db.ExportacaoJobs.SingleOrDefaultAsync(j => j.IdExportacaoJob == id);
            if (job is null || job.Status != (int)StatusExportacao.Processando)
            {
                execucao.PerderPosse();
                return gravado;
            }

            if (job.CancelamentoSolicitado)
            {
                execucao.Cancelar();
                return gravado;
            }

            if (atual is null || atual == gravado)
                return gravado;

            job.TitulosProcessados = atual.TitulosProcessados;
            job.TotalTitulos = atual.TotalTitulos;
            try
            {
                await db.SaveChangesAsync();
                return atual;
            }
            catch (DbUpdateConcurrencyException)
            {
                // Outra gravação no meio (por exemplo, o pedido de cancelamento): a próxima volta relê o job.
                return gravado;
            }
        }

        /// <summary>
        /// Recebe o progresso do gerador (na mesma thread da geração) e sinaliza o acompanhamento a cada
        /// <see cref="TitulosPorGravacaoDeProgresso"/> títulos.
        /// </summary>
        private sealed class ProgressoJob : IProgress<ProgressoExportacao>
        {
            private readonly SemaphoreSlim _sinal = new(0);
            private ProgressoExportacao? _atual;
            private int _ultimoSinalizado;

            public ProgressoExportacao? Atual => Volatile.Read(ref _atual);

            public void Report(ProgressoExportacao value)
            {
                Volatile.Write(ref _atual, value);
                if (value.TitulosProcessados - _ultimoSinalizado >= TitulosPorGravacaoDeProgresso)
                {
                    _ultimoSinalizado = value.TitulosProcessados;
                    if (_sinal.CurrentCount == 0)
                        _sinal.Release();
                }
            }

            public Task<bool> AguardarAsync(TimeSpan intervalo, CancellationToken ct) => _sinal.WaitAsync(intervalo, ct);
        }

        // -----------------------------------------------------------------------------------------------------
        // Espaço em disco
        // -----------------------------------------------------------------------------------------------------

        private bool HaEspacoLivre(string pasta)
        {
            var minimoMb = _settings.EspacoLivreMinimoMb;
            if (minimoMb <= 0)
                return true;

            var livreBytes = _medidorEspaco.EspacoLivreBytes(pasta);
            if (livreBytes is null)
            {
                _logger.LogWarning("Não foi possível medir o espaço livre da pasta de exportação {Pasta}; a geração segue sem a verificação.", pasta);
                return true;
            }

            var livreMb = livreBytes.Value / (1024 * 1024);
            if (livreMb >= minimoMb)
                return true;

            _logger.LogWarning("Espaço livre insuficiente para exportar em {Pasta}: {LivreMb} MB livres, mínimo {MinimoMb} MB.",
                pasta, livreMb, minimoMb);
            return false;
        }
    }

    /// <summary>
    /// Nomes e caminhos dos arquivos da exportação na pasta configurada (contrato C2). O caminho de um pacote é sempre
    /// a pasta mais um nome no formato <c>exportacao-&lt;id&gt;.zip</c>, nunca algo vindo de fora.
    /// </summary>
    public static class ArquivosExportacao
    {
        public const string PrefixoArquivo = "exportacao-";

        private static readonly Regex PadraoPacote =
            new(@"\Aexportacao-[0-9]+\.zip\z", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

        private static readonly Regex PadraoParcial =
            new(@"\Aexportacao-[0-9]+\.zip\.parcial\z", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

        /// <summary>Pasta configurada em <c>Exportacao:Pasta</c>; vazia, a pasta temporária do processo.</summary>
        public static string ResolverPasta(ExportacaoSettings settings) =>
            string.IsNullOrWhiteSpace(settings.Pasta)
                ? Path.Combine(Path.GetTempPath(), "PagueAres", "Exportacao")
                : Path.GetFullPath(settings.Pasta);

        public static string NomeArquivo(int idExportacao) => $"{PrefixoArquivo}{idExportacao}.zip";

        public static string NomeArquivoParcial(int idExportacao) => NomeArquivo(idExportacao) + ".parcial";

        /// <summary>
        /// Caminho do pacote a partir do nome gravado no job, ou <c>null</c> se o nome não for de um pacote (o que
        /// impede que um valor adulterado no banco aponte para fora da pasta).
        /// </summary>
        public static string? CaminhoPacote(string pasta, string? nomeArquivo) =>
            nomeArquivo is not null && EhPacote(nomeArquivo) ? Path.Combine(pasta, nomeArquivo) : null;

        public static bool EhPacote(string nomeArquivo) => PadraoPacote.IsMatch(nomeArquivo);

        public static bool EhParcial(string nomeArquivo) => PadraoParcial.IsMatch(nomeArquivo);

        /// <summary>Apaga o arquivo se existir; um arquivo preso (por exemplo, num download em curso) fica para depois.</summary>
        internal static void TentarApagar(string caminho, ILogger logger)
        {
            try
            {
                File.Delete(caminho);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                logger.LogWarning(ex, "Não foi possível apagar o arquivo de exportação {Arquivo}.", Path.GetFileName(caminho));
            }
        }
    }

    /// <summary>Gravação de estado de um job com o token de concorrência <c>Versao</c>.</summary>
    internal static class GravacaoExportacaoJob
    {
        private const int Tentativas = 5;

        /// <summary>
        /// Relê o job num escopo novo, aplica <paramref name="alterar"/> e grava. Se outra gravação acontecer no meio
        /// (<see cref="DbUpdateConcurrencyException"/>), relê e tenta de novo, para que a regra de
        /// <paramref name="alterar"/> seja sempre avaliada sobre o estado atual. Devolve <c>false</c> quando o job não
        /// existe, quando <paramref name="alterar"/> recusa a mudança ou quando as tentativas se esgotam.
        /// </summary>
        public static async Task<bool> AtualizarAsync(IServiceScopeFactory scopeFactory, int id,
            Func<ExportacaoJob, bool> alterar, CancellationToken ct)
        {
            for (var tentativa = 1; tentativa <= Tentativas; tentativa++)
            {
                using var scope = scopeFactory.CreateScope();
                var db = scope.ServiceProvider.GetRequiredService<PagueAresContext>();

                var job = await db.ExportacaoJobs.SingleOrDefaultAsync(j => j.IdExportacaoJob == id, ct);
                if (job is null || !alterar(job))
                    return false;

                try
                {
                    await db.SaveChangesAsync(ct);
                    return true;
                }
                catch (DbUpdateConcurrencyException) when (tentativa < Tentativas)
                {
                    // Releitura na próxima volta.
                }
                catch (DbUpdateConcurrencyException)
                {
                    return false;
                }
            }

            return false;
        }
    }
}
