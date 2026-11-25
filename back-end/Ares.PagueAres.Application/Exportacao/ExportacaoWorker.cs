using System.Diagnostics;
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
    /// Nenhum job fica preso: a gravação do estado final é repetida com espera crescente, e uma reconciliação periódica
    /// encerra os jobs Processando que ninguém executa (sem sinal de vida por <c>LimiteSemSinalDeVidaSegundos</c>) e
    /// devolve à fila os jobs Pendente que ficaram fora dela.
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

        /// <summary>
        /// Intervalo máximo entre duas gravações de progresso (e entre duas conferências de cancelamento e de espaço
        /// livre).
        /// </summary>
        private static readonly TimeSpan IntervaloProgresso = TimeSpan.FromSeconds(2);

        /// <summary>Quantidade de títulos que antecipa a gravação do progresso, antes de completar o intervalo.</summary>
        private const int TitulosPorGravacaoDeProgresso = 10;

        /// <summary>Tentativas de gravar o estado final (Concluido, Falhou ou Cancelado) antes de deixar o job para a reconciliação.</summary>
        private const int TentativasGravacaoFinal = 5;

        /// <summary>Espera antes da segunda tentativa de gravar o estado final; dobra a cada tentativa (1, 2, 4 e 8 s).</summary>
        private static readonly TimeSpan EsperaInicialGravacaoFinal = TimeSpan.FromSeconds(1);

        /// <summary>
        /// O executor dá sinal de vida a cada <c>LimiteSemSinalDeVidaSegundos</c> dividido por este número, para que um
        /// atraso ocasional nunca faça a reconciliação de outro processo tomar o job por órfão.
        /// </summary>
        private const int SinaisDeVidaPorLimite = 10;

        private static readonly JsonSerializerOptions OpcoesFiltro = new(JsonSerializerDefaults.Web)
        {
            Converters = { new JsonStringEnumConverter() },
        };

        private readonly IServiceScopeFactory _scopeFactory;
        private readonly ExportacaoFila _fila;
        private readonly ExportacaoSettings _settings;
        private readonly IMedidorEspacoLivre _medidorEspaco;
        private readonly IHostEnvironment _ambiente;
        private readonly ILogger<ExportacaoWorker> _logger;

        public ExportacaoWorker(IServiceScopeFactory scopeFactory, ExportacaoFila fila,
            IOptions<ExportacaoSettings> settings, IMedidorEspacoLivre medidorEspaco, IHostEnvironment ambiente,
            ILogger<ExportacaoWorker> logger)
        {
            _scopeFactory = scopeFactory;
            _fila = fila;
            _settings = settings.Value;
            _medidorEspaco = medidorEspaco;
            _ambiente = ambiente;
            _logger = logger;
        }

        /// <summary>Tempo sem nenhuma gravação do executor depois do qual um job Processando é tido como órfão.</summary>
        private TimeSpan LimiteSemSinalDeVida => TimeSpan.FromSeconds(Math.Max(1, _settings.LimiteSemSinalDeVidaSegundos));

        private TimeSpan IntervaloSinalDeVida => LimiteSemSinalDeVida / SinaisDeVidaPorLimite;

        private TimeSpan IntervaloReconciliacao => TimeSpan.FromSeconds(Math.Max(0.1, _settings.IntervaloReconciliacaoSegundos));

        /// <summary>
        /// Faz a recuperação antes de começar a ler a fila, para que ela termine antes de qualquer reivindicação deste
        /// processo: um job que este worker acabou de pegar nunca é confundido com um job interrompido. Sem
        /// <c>Exportacao:Pasta</c> fora de Development e Test o worker não sobe (veja
        /// <see cref="ArquivosExportacao.PastaObrigatoriaAusente"/>).
        /// </summary>
        public override async Task StartAsync(CancellationToken cancellationToken)
        {
            if (ArquivosExportacao.PastaObrigatoriaAusente(_settings, _ambiente))
            {
                _logger.LogError(
                    "O serviço de exportação não foi iniciado: Exportacao:Pasta não está configurada no ambiente {Ambiente}. " +
                    "Configure uma pasta exclusiva desta instância (variável Exportacao__Pasta); até lá a API recusa novas exportações.",
                    _ambiente.EnvironmentName);
                return;
            }

            await RecuperarAsync(cancellationToken);
            await base.StartAsync(cancellationToken);
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            var reconciliacao = ReconciliarPeriodicamenteAsync(stoppingToken);
            try
            {
                await foreach (var id in _fila.LerAsync(stoppingToken))
                    await ProcessarAsync(id, stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                // Parada do host: os jobs ainda Pendente voltam à fila no próximo início.
            }
            finally
            {
                await reconciliacao;
            }
        }

        // -----------------------------------------------------------------------------------------------------
        // Recuperação no início
        // -----------------------------------------------------------------------------------------------------

        /// <summary>
        /// Encerra os jobs que estavam Processando, apaga os <c>.parcial</c> e reenfileira os Pendente. Uma falha aqui
        /// (por exemplo, banco fora do ar na subida) só vai para o log: a reconciliação periódica refaz o trabalho.
        /// </summary>
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
                _logger.LogError(ex, "Falha ao marcar como interrompidas as exportações que estavam em processamento; a reconciliação periódica as encerra.");
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
                _logger.LogError(ex, "Falha ao reenfileirar as exportações pendentes; a reconciliação periódica as devolve à fila.");
            }
        }

        // -----------------------------------------------------------------------------------------------------
        // Reconciliação periódica
        // -----------------------------------------------------------------------------------------------------

        /// <summary>Momento em que a reconciliação viu um job Processando com esta <c>Versao</c> pela primeira vez.</summary>
        private readonly record struct ObservacaoJob(Guid Versao, long DesdeTimestamp);

        /// <summary>
        /// A cada <c>IntervaloReconciliacaoSegundos</c>, encerra os jobs Processando órfãos e reenfileira os Pendente
        /// que estão fora da fila. Cobre o que a recuperação do início não alcança: uma gravação de estado final que
        /// falhou de vez, uma reivindicação que falhou (o job continua Pendente, mas saiu da fila) e uma recuperação
        /// do início que falhou. Nunca lança.
        /// </summary>
        private async Task ReconciliarPeriodicamenteAsync(CancellationToken stoppingToken)
        {
            var observados = new Dictionary<int, ObservacaoJob>();
            while (true)
            {
                try
                {
                    await Task.Delay(IntervaloReconciliacao, stoppingToken);
                    await ReconciliarAsync(observados, stoppingToken);
                }
                catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
                {
                    return;
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Falha na reconciliação das exportações; nova tentativa na próxima volta.");
                }
            }
        }

        private async Task ReconciliarAsync(Dictionary<int, ObservacaoJob> observados, CancellationToken ct)
        {
            List<(int Id, Guid Versao)> processando;
            List<int> pendentes;
            using (var scope = _scopeFactory.CreateScope())
            {
                var db = scope.ServiceProvider.GetRequiredService<PagueAresContext>();
                processando = (await db.ExportacaoJobs.AsNoTracking()
                        .Where(j => j.Status == (int)StatusExportacao.Processando)
                        .Select(j => new { j.IdExportacaoJob, j.Versao })
                        .ToListAsync(ct))
                    .Select(j => (j.IdExportacaoJob, j.Versao))
                    .ToList();
                pendentes = await db.ExportacaoJobs.AsNoTracking()
                    .Where(j => j.Status == (int)StatusExportacao.Pendente)
                    .OrderBy(j => j.DataCriacao)
                    .ThenBy(j => j.IdExportacaoJob)
                    .Select(j => j.IdExportacaoJob)
                    .ToListAsync(ct);
            }

            await EncerrarOrfaosAsync(processando, observados, ct);

            var reenfileirados = 0;
            foreach (var id in pendentes)
            {
                if (_fila.NaFilaOuEmExecucao(id))
                    continue;

                _fila.Enfileirar(id);
                reenfileirados++;
            }

            if (reenfileirados > 0)
                _logger.LogInformation("{Quantidade} exportações pendentes estavam fora da fila e voltaram para ela.", reenfileirados);
        }

        /// <summary>
        /// Um job Processando é órfão quando não está em execução neste processo e a <c>Versao</c> dele não muda há pelo
        /// menos <see cref="LimiteSemSinalDeVida"/>. O executor de qualquer processo renova a <c>Versao</c> a cada
        /// gravação de progresso e a cada sinal de vida (<see cref="IntervaloSinalDeVida"/>), então um job em execução
        /// em outro processo nunca fica tanto tempo com a mesma <c>Versao</c>. O encerramento grava com o token
        /// <c>Versao</c> observado: se o executor gravou no meio, nada muda.
        /// </summary>
        private async Task EncerrarOrfaosAsync(List<(int Id, Guid Versao)> processando,
            Dictionary<int, ObservacaoJob> observados, CancellationToken ct)
        {
            var ids = processando.Select(j => j.Id).ToHashSet();
            foreach (var id in observados.Keys.Where(id => !ids.Contains(id)).ToList())
                observados.Remove(id);

            var agora = Stopwatch.GetTimestamp();
            foreach (var (id, versao) in processando)
            {
                if (_fila.EmExecucao(id))
                {
                    observados.Remove(id);
                    continue;
                }

                if (!observados.TryGetValue(id, out var observacao) || observacao.Versao != versao)
                {
                    observados[id] = new ObservacaoJob(versao, agora);
                    continue;
                }

                if (Stopwatch.GetElapsedTime(observacao.DesdeTimestamp, agora) < LimiteSemSinalDeVida)
                    continue;

                observados.Remove(id);
                await EncerrarOrfaoAsync(id, versao, ct);
            }
        }

        private async Task EncerrarOrfaoAsync(int id, Guid versaoObservada, CancellationToken ct)
        {
            var cancelado = false;
            var encerrou = await GravacaoExportacaoJob.AtualizarAsync(_scopeFactory, id, job =>
            {
                if (job.Versao != versaoObservada || _fila.EmExecucao(id))
                    return false;

                // O usuário pediu o cancelamento e o executor sumiu antes de atender: vale o pedido.
                cancelado = job.CancelamentoSolicitado;
                return cancelado ? MarcarCancelado(job) : MarcarFalhou(job, MensagemInterrupcao);
            }, ct);

            if (!encerrou)
                return;

            var pasta = ArquivosExportacao.ResolverPasta(_settings);
            ArquivosExportacao.TentarApagar(Path.Combine(pasta, ArquivosExportacao.NomeArquivoParcial(id)), _logger);
            ArquivosExportacao.TentarApagar(Path.Combine(pasta, ArquivosExportacao.NomeArquivo(id)), _logger);

            _logger.LogWarning(
                "Exportação {IdExportacao} estava em processamento sem executor (nenhuma gravação por {Limite}) e foi marcada como {Status}.",
                id, LimiteSemSinalDeVida, cancelado ? StatusExportacao.Cancelado : StatusExportacao.Falhou);
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
                // Em geral, falha ao reivindicar (por exemplo, banco fora do ar): o job continua Pendente, fora da fila,
                // e a reconciliação periódica o devolve a ela. Um job que tenha ficado Processando sem executor é
                // encerrado pela mesma reconciliação.
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
            long tamanhoBytes = 0;
            string? mensagemFalha = null;
            var cancelado = false;

            var progresso = new ProgressoJob();
            using var fimAcompanhamento = new CancellationTokenSource();
            var acompanhamento = AcompanharAsync(id, pasta, progresso, execucao, fimAcompanhamento.Token);

            try
            {
                var filtro = JsonSerializer.Deserialize<FiltroExportacaoDto>(filtrosJson, OpcoesFiltro)
                             ?? throw new JsonException("Filtros da exportação vazios.");

                Directory.CreateDirectory(pasta);
                if (!HaEspacoLivre(pasta, avisarSemMedicao: true))
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
                    tamanhoBytes = new FileInfo(caminhoFinal).Length;
                }
            }
            catch (OperationCanceledException) when (cts.IsCancellationRequested)
            {
                resultado = null;
                if (execucao.CancelamentoSolicitado)
                    cancelado = true;
                else if (execucao.EspacoInsuficiente)
                    mensagemFalha = MensagemEspacoInsuficiente;
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
            catch (Exception ex) when (EhDiscoCheio(ex))
            {
                resultado = null;
                mensagemFalha = MensagemEspacoInsuficiente;
                _logger.LogError(ex, "O disco da pasta de exportação {Pasta} encheu durante a geração da exportação {IdExportacao}.", pasta, id);
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
                if (!await ConcluirAsync(id, nomeFinal, tamanhoBytes, resultado, stoppingToken))
                    ArquivosExportacao.TentarApagar(caminhoFinal, _logger);
                return;
            }

            // Em toda falha ou cancelamento o pacote incompleto é apagado.
            ArquivosExportacao.TentarApagar(caminhoParcial, _logger);
            ArquivosExportacao.TentarApagar(caminhoFinal, _logger);

            if (cancelado)
            {
                if (await GravarEstadoFinalAsync(id, MarcarCancelado, stoppingToken))
                    _logger.LogInformation("Exportação {IdExportacao} cancelada durante a geração.", id);
            }
            else if (mensagemFalha is not null)
            {
                await GravarEstadoFinalAsync(id, job => MarcarFalhou(job, mensagemFalha), stoppingToken);
            }
        }

        /// <summary>
        /// Grava a conclusão. Devolve <c>false</c> quando o pacote não deve ficar disponível: o job deixou de estar
        /// Processando (outro executor o mudou), o cancelamento foi pedido no banco enquanto ele terminava, ou a
        /// gravação falhou em todas as tentativas (o job fica para a reconciliação, que o marca como Falhou).
        /// </summary>
        private async Task<bool> ConcluirAsync(int id, string nomeArquivo, long tamanhoBytes,
            ResultadoPacoteExportacao resultado, CancellationToken stoppingToken)
        {
            var cancelado = false;
            var jaConcluido = false;
            var gravou = await GravarEstadoFinalAsync(id, job =>
            {
                // Uma tentativa anterior pode ter gravado e só a resposta do banco se perdeu.
                jaConcluido = job.Status == (int)StatusExportacao.Concluido && job.NomeArquivo == nomeArquivo;
                cancelado = !jaConcluido && job.CancelamentoSolicitado;
                if (jaConcluido)
                    return false;
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
            }, stoppingToken);

            if (jaConcluido || (gravou && !cancelado))
            {
                _logger.LogInformation("Exportação {IdExportacao} concluída: {Titulos} títulos, {Anexos} anexos, {Bytes} bytes.",
                    id, resultado.TotalTitulos, resultado.TotalAnexos, tamanhoBytes);
                return true;
            }

            return false;
        }

        /// <summary>
        /// Grava um estado final com <see cref="GravacaoExportacaoJob.AtualizarAsync"/>, repetindo com espera crescente
        /// (<see cref="EsperaInicialGravacaoFinal"/>, dobrando, até <see cref="TentativasGravacaoFinal"/> tentativas)
        /// quando a gravação falha, por exemplo com o banco fora do ar. Na parada do host não repete: o job que ficar
        /// Processando é encerrado pela recuperação do próximo início. Esgotadas as tentativas, devolve <c>false</c> e o
        /// job fica para a reconciliação periódica. Nunca lança.
        /// </summary>
        private async Task<bool> GravarEstadoFinalAsync(int id, Func<ExportacaoJob, bool> alterar, CancellationToken stoppingToken)
        {
            var espera = EsperaInicialGravacaoFinal;
            for (var tentativa = 1; ; tentativa++)
            {
                try
                {
                    return await GravacaoExportacaoJob.AtualizarAsync(_scopeFactory, id, alterar, CancellationToken.None);
                }
                catch (Exception ex) when (tentativa < TentativasGravacaoFinal && !stoppingToken.IsCancellationRequested)
                {
                    _logger.LogWarning(ex,
                        "Falha ao gravar o estado final da exportação {IdExportacao} (tentativa {Tentativa} de {Tentativas}); nova tentativa em {Espera}.",
                        id, tentativa, TentativasGravacaoFinal, espera);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex,
                        "Não foi possível gravar o estado final da exportação {IdExportacao} depois de {Tentativas} tentativas; " +
                        "a reconciliação periódica a encerra.", id, tentativa);
                    return false;
                }

                try
                {
                    await Task.Delay(espera, stoppingToken);
                }
                catch (OperationCanceledException)
                {
                    _logger.LogWarning("Parada do serviço durante as tentativas de gravar o estado final da exportação {IdExportacao}.", id);
                    return false;
                }

                espera *= 2;
            }
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
        // Progresso, sinal de vida e conferências durante a geração
        // -----------------------------------------------------------------------------------------------------

        /// <summary>
        /// Enquanto o job gera, grava o progresso a cada <see cref="IntervaloProgresso"/> ou a cada
        /// <see cref="TitulosPorGravacaoDeProgresso"/> títulos, o que vier primeiro, e um sinal de vida quando passa
        /// <see cref="IntervaloSinalDeVida"/> sem gravação. A cada volta relê o job (um pedido de cancelamento gravado no
        /// banco, inclusive por outro processo, ou a perda do status Processando interrompe a geração) e confere o
        /// espaço livre da unidade da pasta (abaixo de <c>EspacoLivreMinimoMb</c>, a geração é interrompida). Nunca
        /// lança: uma falha aqui não derruba a geração.
        /// </summary>
        private async Task AcompanharAsync(int id, string pasta, ProgressoJob progresso, ExecucaoExportacao execucao,
            CancellationToken fim)
        {
            var intervaloSinal = IntervaloSinalDeVida;
            var espera = IntervaloProgresso < intervaloSinal ? IntervaloProgresso : intervaloSinal;
            var ultimaGravacao = Stopwatch.GetTimestamp(); // a reivindicação acabou de gravar
            ProgressoExportacao? gravado = null;
            while (true)
            {
                try
                {
                    await progresso.AguardarAsync(espera, fim);
                }
                catch (OperationCanceledException)
                {
                    return;
                }

                try
                {
                    var sinalDeVida = Stopwatch.GetElapsedTime(ultimaGravacao) >= intervaloSinal;
                    var (novoGravado, gravou) = await GravarProgressoAsync(id, progresso.Atual, gravado, sinalDeVida, execucao);
                    gravado = novoGravado;
                    if (gravou)
                        ultimaGravacao = Stopwatch.GetTimestamp();
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Falha ao gravar o progresso da exportação {IdExportacao}.", id);
                }

                try
                {
                    if (!execucao.EspacoInsuficiente && !HaEspacoLivre(pasta, avisarSemMedicao: false))
                        execucao.InterromperPorFaltaDeEspaco();
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Falha ao conferir o espaço livre durante a exportação {IdExportacao}.", id);
                }
            }
        }

        /// <summary>
        /// Relê o job e grava o progresso novo ou, sem progresso novo, o sinal de vida pedido. Devolve o último
        /// progresso gravado e se houve gravação.
        /// </summary>
        private async Task<(ProgressoExportacao? Gravado, bool Gravou)> GravarProgressoAsync(int id,
            ProgressoExportacao? atual, ProgressoExportacao? gravado, bool sinalDeVida, ExecucaoExportacao execucao)
        {
            using var scope = _scopeFactory.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<PagueAresContext>();

            var job = await db.ExportacaoJobs.SingleOrDefaultAsync(j => j.IdExportacaoJob == id);
            if (job is null || job.Status != (int)StatusExportacao.Processando)
            {
                execucao.PerderPosse();
                return (gravado, false);
            }

            if (job.CancelamentoSolicitado)
            {
                execucao.Cancelar();
                return (gravado, false);
            }

            var progressoNovo = atual is not null && atual != gravado;
            if (!progressoNovo && !sinalDeVida)
                return (gravado, false);

            if (progressoNovo)
            {
                job.TitulosProcessados = atual!.TitulosProcessados;
                job.TotalTitulos = atual.TotalTitulos;
            }

            // Força o UPDATE mesmo sem valor novo (o sinal de vida, ou um progresso igual ao do banco): a gravação
            // renova a Versao, que é o que a reconciliação de outro processo observa para saber que este job tem executor.
            db.Entry(job).Property(j => j.TitulosProcessados).IsModified = true;
            try
            {
                await db.SaveChangesAsync();
                return (progressoNovo ? atual : gravado, true);
            }
            catch (DbUpdateConcurrencyException)
            {
                // Outra gravação no meio (por exemplo, o pedido de cancelamento): a próxima volta relê o job.
                return (gravado, false);
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

        /// <summary>
        /// Confere o espaço livre da unidade da pasta contra <c>EspacoLivreMinimoMb</c>. Quando não dá para medir (por
        /// exemplo, compartilhamento de rede), a geração segue; o aviso no log sai só na conferência do início.
        /// </summary>
        private bool HaEspacoLivre(string pasta, bool avisarSemMedicao)
        {
            var minimoMb = _settings.EspacoLivreMinimoMb;
            if (minimoMb <= 0)
                return true;

            var livreBytes = _medidorEspaco.EspacoLivreBytes(pasta);
            if (livreBytes is null)
            {
                if (avisarSemMedicao)
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

        /// <summary>
        /// A exceção (ou uma interna dela) é de disco cheio: <c>ERROR_DISK_FULL</c> ou <c>ERROR_HANDLE_DISK_FULL</c> no
        /// Windows, <c>ENOSPC</c> no Linux.
        /// </summary>
        private static bool EhDiscoCheio(Exception? ex)
        {
            const int erroDiscoCheio = unchecked((int)0x80070070);
            const int erroHandleDiscoCheio = unchecked((int)0x80070027);
            const int enospc = 28;

            for (; ex is not null; ex = ex.InnerException)
            {
                if (ex is IOException { HResult: erroDiscoCheio or erroHandleDiscoCheio or enospc })
                    return true;
            }

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

        /// <summary>Ambiente dos testes automatizados, em que a pasta vazia também é aceita.</summary>
        public const string AmbienteTestes = "Test";

        private static readonly Regex PadraoPacote =
            new(@"\Aexportacao-[0-9]+\.zip\z", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

        private static readonly Regex PadraoParcial =
            new(@"\Aexportacao-[0-9]+\.zip\.parcial\z", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

        /// <summary>
        /// Pasta configurada em <c>Exportacao:Pasta</c>; vazia, a pasta temporária do processo (aceita só em
        /// Development e Test, veja <see cref="PastaObrigatoriaAusente"/>).
        /// </summary>
        public static string ResolverPasta(ExportacaoSettings settings) =>
            string.IsNullOrWhiteSpace(settings.Pasta)
                ? Path.Combine(Path.GetTempPath(), "PagueAres", "Exportacao")
                : Path.GetFullPath(settings.Pasta);

        /// <summary>
        /// Fora dos ambientes Development e Test, <c>Exportacao:Pasta</c> é obrigatória. A pasta temporária pode ser a
        /// mesma para duas instâncias da API no mesmo servidor (homologação e produção, por exemplo), e os nomes
        /// <c>exportacao-&lt;id&gt;.zip</c> se repetem entre bancos: uma instância sobrescreveria ou apagaria os pacotes
        /// da outra. Com a pasta ausente, o worker e a limpeza não sobem e a API recusa novas exportações com 503.
        /// </summary>
        public static bool PastaObrigatoriaAusente(ExportacaoSettings settings, IHostEnvironment ambiente) =>
            string.IsNullOrWhiteSpace(settings.Pasta)
            && !ambiente.IsDevelopment()
            && !ambiente.IsEnvironment(AmbienteTestes);

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
        /// existe, quando <paramref name="alterar"/> recusa a mudança ou quando as tentativas se esgotam. Outras falhas
        /// do banco são propagadas.
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
