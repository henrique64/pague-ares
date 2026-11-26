using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;
using Ares.PagueAres.API.Extensions;
using Ares.PagueAres.API.Services;
using Ares.PagueAres.Application.Configuration;
using Ares.PagueAres.Application.Exportacao;
using Ares.PagueAres.Domain;
using Ares.PagueAres.Domain.Dtos.Exportacao;
using Ares.PagueAres.Domain.Enums;
using Ares.PagueAres.Infrastructure;
using Ares.PagueAres.Infrastructure.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Ares.PagueAres.API.Controllers
{
    /// <summary>
    /// Exportação de títulos com anexos (contrato C8). Só FIN e ADM, pelo papel do JWT; cada usuário só enxerga e
    /// mexe nos próprios jobs, e a identidade vem sempre do token, nunca do corpo ou da query. O pacote é gerado em
    /// segundo plano pelo <see cref="ExportacaoWorker"/> e baixado por um link de uso único
    /// (<see cref="TokensDownloadExportacao"/>), porque o navegador baixa por navegação direta, sem o cabeçalho
    /// <c>Authorization</c>.
    /// <para>
    /// Sem <c>[ApiController]</c> de propósito: a resposta automática de modelo inválido devolveria a mensagem do
    /// serializador (com nome de tipo e posição no JSON). Aqui o corpo inválido vira um 400 no envelope
    /// <see cref="GenericResponse{T}"/> com mensagem amigável.
    /// </para>
    /// </summary>
    [Route("api/exportacao")]
    [Authorize]
    public class ExportacaoController(
        PagueAresContext dbContext,
        IFonteExportacao fonte,
        ExportacaoFila fila,
        TokensDownloadExportacao tokens,
        IOptions<ExportacaoSettings> opcoes,
        IHostEnvironment ambiente,
        FusoExportacao fuso,
        ILogger<ExportacaoController> logger) : ControllerBase
    {
        private const string MensagemErroGenerica = "Não foi possível concluir a operação. Tente de novo.";
        private const string MensagemIndisponivel =
            "A exportação está indisponível neste servidor por falta de configuração. Acione o suporte.";
        private const string MensagemUsuarioNaoIdentificado = "Usuário não identificado.";
        private const string MensagemSemPermissao = "Exportação permitida apenas para os perfis Financeiro e Administrador.";
        private const string MensagemFiltroInvalido = "Filtro de exportação inválido.";
        private const string MensagemNaoEncontrada = "Exportação não encontrada.";
        private const string MensagemNaoConcluida = "Esta exportação não está concluída, então não há arquivo para baixar.";
        private const string MensagemArquivoIndisponivel =
            "O arquivo desta exportação expirou ou não está mais disponível. Gere a exportação de novo.";
        private const string MensagemArquivoExcluido =
            "O arquivo desta exportação foi excluído. Gere a exportação de novo.";
        private const string MensagemLinkInvalido =
            "Link de download inválido, já usado ou vencido. Volte à tela de exportações e baixe de novo.";
        private const string MensagemJaEncerrada = "Esta exportação já foi encerrada.";
        private const string MensagemSoEncerradasSaemDaLista =
            "Só exportações encerradas (Falhou, Cancelada, Expirada ou Excluída) podem ser removidas da lista.";
        private const string MensagemEstadoMudou =
            "A exportação mudou de estado durante a operação. Atualize a lista e tente de novo.";

        /// <summary>Janela da lista de jobs do usuário.</summary>
        private const int DiasNaLista = 30;

        /// <summary>
        /// Releituras de um job alterado por outra gravação (worker ou limpeza) no meio do cancelamento ou da remoção da
        /// lista.
        /// </summary>
        private const int TentativasDeGravacao = 5;

        /// <summary>Mesmo formato que o <see cref="ExportacaoWorker"/> lê: camelCase, enum por nome ou por número.</summary>
        private static readonly JsonSerializerOptions OpcoesFiltro = new(JsonSerializerDefaults.Web)
        {
            Converters = { new JsonStringEnumConverter() },
        };

        /// <summary>
        /// Serializa, neste processo, a conferência do limite de jobs ativos e a gravação do job novo, para que dois
        /// pedidos simultâneos do mesmo usuário não passem os dois pelo limite.
        /// </summary>
        private static readonly SemaphoreSlim TravaCriacao = new(1, 1);

        private readonly ExportacaoSettings _settings = opcoes.Value;

        // -----------------------------------------------------------------------------------------------------
        // POST api/exportacao
        // -----------------------------------------------------------------------------------------------------

        [HttpPost]
        public async Task<ActionResult> Criar([FromBody] FiltroExportacaoDto? filtro, CancellationToken ct)
        {
            try
            {
                var recusa = VerificarAcesso(out var idUsuario);
                if (recusa is not null)
                    return recusa;

                // Sem pasta própria fora de Development o worker não sobe: um job criado agora ficaria Pendente
                // para sempre. Recusa com 503 até a configuração ser corrigida.
                if (ArquivosExportacao.PastaObrigatoriaAusente(_settings, ambiente))
                {
                    logger.LogError("Exportação recusada: Exportacao:Pasta não está configurada no ambiente {Ambiente}.",
                        ambiente.EnvironmentName);
                    return StatusCode(StatusCodes.Status503ServiceUnavailable, Erro(MensagemIndisponivel));
                }

                if (!ModelState.IsValid || filtro is null)
                    return BadRequest(Erro(MensagemFiltroInvalido));

                var invalido = ValidarFiltro(filtro);
                if (invalido is not null)
                    return BadRequest(Erro(invalido));

                var totalTitulos = await fonte.ContarTitulosAsync(filtro, ct);
                if (totalTitulos == 0)
                    return BadRequest(Erro("Nenhum título encontrado com os filtros informados."));

                if (totalTitulos > _settings.MaxTitulosPorExportacao)
                {
                    return BadRequest(Erro(
                        $"Os filtros trazem {totalTitulos} títulos, e cada exportação aceita no máximo " +
                        $"{_settings.MaxTitulosPorExportacao}. Refine os filtros, por exemplo por período."));
                }

                ExportacaoJob job;
                await TravaCriacao.WaitAsync(ct);
                try
                {
                    var ativos = await dbContext.ExportacaoJobs.AsNoTracking()
                        .CountAsync(j => j.IdUsuario == idUsuario
                                         && (j.Status == (int)StatusExportacao.Pendente
                                             || j.Status == (int)StatusExportacao.Processando), ct);

                    if (ativos >= _settings.MaxJobsAtivosPorUsuario)
                    {
                        return StatusCode(StatusCodes.Status429TooManyRequests, Erro(
                            $"Você já tem {ativos} exportações em andamento, o máximo permitido. " +
                            "Aguarde uma terminar ou cancele uma delas."));
                    }

                    job = new ExportacaoJob
                    {
                        IdUsuario = idUsuario,
                        Status = (int)StatusExportacao.Pendente,
                        FiltrosJson = JsonSerializer.Serialize(filtro, OpcoesFiltro),
                        DataCriacao = DateTime.UtcNow,
                        TotalTitulos = totalTitulos,
                    };
                    dbContext.ExportacaoJobs.Add(job);
                    await dbContext.SaveChangesAsync(ct);
                }
                finally
                {
                    TravaCriacao.Release();
                }

                fila.Enfileirar(job.IdExportacaoJob);
                logger.LogInformation("Exportação {IdExportacao} criada pelo usuário {IdUsuario} com {Titulos} títulos.",
                    job.IdExportacaoJob, idUsuario, totalTitulos);

                return AcceptedAtAction(nameof(Obter), new { id = job.IdExportacaoJob },
                    Sucesso(ParaDto(job), "Exportação solicitada."));
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                logger.LogError(ex, "Falha ao criar a exportação.");
                return StatusCode(StatusCodes.Status500InternalServerError, Erro(MensagemErroGenerica));
            }
        }

        // -----------------------------------------------------------------------------------------------------
        // GET api/exportacao
        // -----------------------------------------------------------------------------------------------------

        /// <summary>Jobs do usuário criados nos últimos <see cref="DiasNaLista"/> dias, menos os que ele removeu da lista.</summary>
        [HttpGet]
        public async Task<ActionResult> Listar(CancellationToken ct)
        {
            try
            {
                var recusa = VerificarAcesso(out var idUsuario);
                if (recusa is not null)
                    return recusa;

                var desde = DateTime.UtcNow.AddDays(-DiasNaLista);
                var jobs = await dbContext.ExportacaoJobs.AsNoTracking()
                    .Where(j => j.IdUsuario == idUsuario && j.DataCriacao >= desde && j.DataRemocaoDaLista == null)
                    .OrderByDescending(j => j.DataCriacao)
                    .ThenByDescending(j => j.IdExportacaoJob)
                    .ToListAsync(ct);

                var resposta = Sucesso(jobs.Select(ParaDto).ToList(), "Registros obtidos com sucesso.");
                resposta.Records = jobs.Count;
                return Ok(resposta);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                logger.LogError(ex, "Falha ao listar as exportações.");
                return StatusCode(StatusCodes.Status500InternalServerError, Erro(MensagemErroGenerica));
            }
        }

        // -----------------------------------------------------------------------------------------------------
        // GET api/exportacao/{id}
        // -----------------------------------------------------------------------------------------------------

        [HttpGet("{id:int}")]
        public async Task<ActionResult> Obter(int id, CancellationToken ct)
        {
            try
            {
                var recusa = VerificarAcesso(out var idUsuario);
                if (recusa is not null)
                    return recusa;

                var job = await BuscarDoUsuarioAsync(id, idUsuario, ct);
                if (job is null)
                    return NotFound(Erro(MensagemNaoEncontrada));

                return Ok(Sucesso(ParaDto(job), "Registro obtido com sucesso."));
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                logger.LogError(ex, "Falha ao obter a exportação {IdExportacao}.", id);
                return StatusCode(StatusCodes.Status500InternalServerError, Erro(MensagemErroGenerica));
            }
        }

        // -----------------------------------------------------------------------------------------------------
        // POST api/exportacao/{id}/link-download
        // -----------------------------------------------------------------------------------------------------

        [HttpPost("{id:int}/link-download")]
        public async Task<ActionResult> GerarLinkDownload(int id, CancellationToken ct)
        {
            try
            {
                var recusa = VerificarAcesso(out var idUsuario);
                if (recusa is not null)
                    return recusa;

                var job = await BuscarDoUsuarioAsync(id, idUsuario, ct);
                if (job is null)
                    return NotFound(Erro(MensagemNaoEncontrada));

                var indisponivel = VerificarPacoteDisponivel(job, out _);
                if (indisponivel is not null)
                    return indisponivel;

                var link = tokens.Emitir(job.IdExportacaoJob, idUsuario);
                return Ok(Sucesso(link, "Link de download gerado."));
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                logger.LogError(ex, "Falha ao gerar o link de download da exportação {IdExportacao}.", id);
                return StatusCode(StatusCodes.Status500InternalServerError, Erro(MensagemErroGenerica));
            }
        }

        // -----------------------------------------------------------------------------------------------------
        // GET api/exportacao/download?token=
        // -----------------------------------------------------------------------------------------------------

        /// <summary>
        /// Download por navegação direta: anônimo na autenticação, mas só com um token de uso único emitido para o dono
        /// de um job Concluido. O arquivo é sempre a pasta configurada mais o nome gravado no job; nada da requisição
        /// além do token entra no caminho.
        /// </summary>
        [HttpGet("download")]
        [AllowAnonymous]
        public async Task<ActionResult> Baixar([FromQuery] string? token, CancellationToken ct)
        {
            try
            {
                var registro = tokens.Consumir(token);
                if (registro is null)
                    return Unauthorized(Erro(MensagemLinkInvalido));

                var job = await dbContext.ExportacaoJobs.AsNoTracking()
                    .SingleOrDefaultAsync(j => j.IdExportacaoJob == registro.IdExportacaoJob, ct);

                if (job is null)
                    return StatusCode(StatusCodes.Status410Gone, Erro(MensagemArquivoIndisponivel));

                if (job.IdUsuario != registro.IdUsuario)
                    return Unauthorized(Erro(MensagemLinkInvalido));

                if (job.Status == (int)StatusExportacao.Excluido)
                    return StatusCode(StatusCodes.Status410Gone, Erro(MensagemArquivoExcluido));

                if (job.Status != (int)StatusExportacao.Concluido)
                    return StatusCode(StatusCodes.Status410Gone, Erro(MensagemArquivoIndisponivel));

                var indisponivel = VerificarPacoteDisponivel(job, out var caminho);
                if (indisponivel is not null)
                    return indisponivel;

                logger.LogInformation("Exportação {IdExportacao} baixada pelo usuário {IdUsuario}.",
                    job.IdExportacaoJob, registro.IdUsuario);

                return PhysicalFile(caminho!, "application/zip", NomeDownload(job), enableRangeProcessing: false);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                logger.LogError(ex, "Falha ao iniciar o download de uma exportação.");
                return StatusCode(StatusCodes.Status500InternalServerError, Erro(MensagemErroGenerica));
            }
        }

        // -----------------------------------------------------------------------------------------------------
        // DELETE api/exportacao/{id}
        // -----------------------------------------------------------------------------------------------------

        /// <summary>
        /// Pendente vira Cancelado na hora (o worker pula o id que continua na fila). Processando grava o pedido de
        /// cancelamento e avisa a fila; o worker grava Cancelado quando para (se o job roda em outro processo, ele
        /// percebe o pedido no banco). Concluido vira Excluido e o arquivo é apagado; <c>DataExpiracao</c> fica como
        /// estava, para a lista continuar mostrando o prazo original. Falhou, Cancelado, Expirado e Excluido já estão
        /// encerrados e respondem 409. Se o worker ou a limpeza gravarem o job no meio, a gravação falha pelo token
        /// <c>Versao</c> e a regra é reaplicada sobre o estado relido.
        /// </summary>
        [HttpDelete("{id:int}")]
        public async Task<ActionResult> Cancelar(int id, CancellationToken ct)
        {
            try
            {
                var recusa = VerificarAcesso(out var idUsuario);
                if (recusa is not null)
                    return recusa;

                for (var tentativa = 1; tentativa <= TentativasDeGravacao; tentativa++)
                {
                    dbContext.ChangeTracker.Clear();
                    var job = await dbContext.ExportacaoJobs
                        .SingleOrDefaultAsync(j => j.IdExportacaoJob == id && j.IdUsuario == idUsuario, ct);

                    if (job is null)
                        return NotFound(Erro(MensagemNaoEncontrada));

                    var agora = DateTime.UtcNow;
                    var status = (StatusExportacao)job.Status;
                    string? arquivoParaApagar = null;

                    switch (status)
                    {
                        case StatusExportacao.Pendente:
                            job.Status = (int)StatusExportacao.Cancelado;
                            job.DataConclusao = agora;
                            break;

                        case StatusExportacao.Processando:
                            job.CancelamentoSolicitado = true;
                            break;

                        case StatusExportacao.Concluido:
                            // O status muda antes de o arquivo sumir, para que o download nunca seja oferecido sem ele.
                            // A limpeza não mexe em job Excluido: se o arquivo ficar preso, ele sai como órfão.
                            arquivoParaApagar = ArquivosExportacao.CaminhoPacote(
                                ArquivosExportacao.ResolverPasta(_settings), job.NomeArquivo);
                            job.Status = (int)StatusExportacao.Excluido;
                            break;

                        default:
                            return Conflict(Erro(MensagemJaEncerrada));
                    }

                    try
                    {
                        await dbContext.SaveChangesAsync(ct);
                    }
                    catch (DbUpdateConcurrencyException)
                    {
                        // Outra gravação no meio: relê e reaplica a regra.
                        continue;
                    }

                    if (status == StatusExportacao.Processando)
                        fila.SolicitarCancelamento(id);

                    if (arquivoParaApagar is not null)
                        TentarApagar(arquivoParaApagar);

                    logger.LogInformation("Exportação {IdExportacao} ({Status}) cancelada pelo usuário {IdUsuario}.",
                        id, status, idUsuario);
                    return NoContent();
                }

                return Conflict(Erro(MensagemEstadoMudou));
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                logger.LogError(ex, "Falha ao cancelar a exportação {IdExportacao}.", id);
                return StatusCode(StatusCodes.Status500InternalServerError, Erro(MensagemErroGenerica));
            }
        }

        // -----------------------------------------------------------------------------------------------------
        // DELETE api/exportacao/{id}/lista
        // -----------------------------------------------------------------------------------------------------

        /// <summary>
        /// Tira da lista do usuário (<c>GET api/exportacao</c>) um job encerrado: Falhou, Cancelado, Expirado ou
        /// Excluido. Só grava <c>DataRemocaoDaLista</c>: a situação não muda e o job continua em
        /// <c>GET api/exportacao/{id}</c>. Repetir o pedido num job já removido responde 204 sem gravar de novo.
        /// Pendente, Processando e Concluido respondem 409. Se o worker ou a limpeza gravarem o job no meio, a gravação
        /// falha pelo token <c>Versao</c> e a regra é reaplicada sobre o estado relido, como no cancelamento.
        /// </summary>
        [HttpDelete("{id:int}/lista")]
        public async Task<ActionResult> RemoverDaLista(int id, CancellationToken ct)
        {
            try
            {
                var recusa = VerificarAcesso(out var idUsuario);
                if (recusa is not null)
                    return recusa;

                for (var tentativa = 1; tentativa <= TentativasDeGravacao; tentativa++)
                {
                    dbContext.ChangeTracker.Clear();
                    var job = await dbContext.ExportacaoJobs
                        .SingleOrDefaultAsync(j => j.IdExportacaoJob == id && j.IdUsuario == idUsuario, ct);

                    if (job is null)
                        return NotFound(Erro(MensagemNaoEncontrada));

                    var status = (StatusExportacao)job.Status;
                    if (!EstaEncerrado(status))
                        return Conflict(Erro(MensagemSoEncerradasSaemDaLista));

                    if (job.DataRemocaoDaLista is not null)
                        return NoContent();

                    job.DataRemocaoDaLista = DateTime.UtcNow;

                    try
                    {
                        await dbContext.SaveChangesAsync(ct);
                    }
                    catch (DbUpdateConcurrencyException)
                    {
                        // Outra gravação no meio: relê e reaplica a regra.
                        continue;
                    }

                    logger.LogInformation("Exportação {IdExportacao} ({Status}) removida da lista pelo usuário {IdUsuario}.",
                        id, status, idUsuario);
                    return NoContent();
                }

                return Conflict(Erro(MensagemEstadoMudou));
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                logger.LogError(ex, "Falha ao remover da lista a exportação {IdExportacao}.", id);
                return StatusCode(StatusCodes.Status500InternalServerError, Erro(MensagemErroGenerica));
            }
        }

        // -----------------------------------------------------------------------------------------------------
        // Apoio
        // -----------------------------------------------------------------------------------------------------

        /// <summary>
        /// Usuário e papel vêm só do JWT (posto em <c>HttpContext.Items["User"]</c> pelo <c>JwtMiddleware</c>).
        /// Devolve a resposta de recusa (401 ou 403), ou <c>null</c> quando o usuário é FIN ou ADM.
        /// </summary>
        private ActionResult? VerificarAcesso(out int idUsuario)
        {
            var usuario = this.GetUserFromToken();
            if (usuario is null)
            {
                idUsuario = 0;
                return Unauthorized(Erro(MensagemUsuarioNaoIdentificado));
            }

            idUsuario = usuario.IdUsuario;
            if (!this.CurrentUserIsInRole(Funcoes.Financeiro, Funcoes.Administrador))
                return StatusCode(StatusCodes.Status403Forbidden, Erro(MensagemSemPermissao));

            return null;
        }

        /// <summary>O job só é encontrado pelo dono: job de outro usuário responde como inexistente.</summary>
        private Task<ExportacaoJob?> BuscarDoUsuarioAsync(int id, int idUsuario, CancellationToken ct) =>
            dbContext.ExportacaoJobs.AsNoTracking()
                .SingleOrDefaultAsync(j => j.IdExportacaoJob == id && j.IdUsuario == idUsuario, ct);

        /// <summary>
        /// 409 se o job não está Concluido (salvo Expirado e Excluido, que são 410); 410 se o prazo venceu ou o arquivo
        /// sumiu. Devolve <c>null</c> e o caminho do pacote quando ele pode ser baixado.
        /// </summary>
        private ActionResult? VerificarPacoteDisponivel(ExportacaoJob job, out string? caminho)
        {
            caminho = null;
            var status = (StatusExportacao)job.Status;

            if (status == StatusExportacao.Excluido)
                return StatusCode(StatusCodes.Status410Gone, Erro(MensagemArquivoExcluido));

            if (status == StatusExportacao.Expirado)
                return StatusCode(StatusCodes.Status410Gone, Erro(MensagemArquivoIndisponivel));

            if (status != StatusExportacao.Concluido)
                return Conflict(Erro(MensagemNaoConcluida));

            if (job.DataExpiracao is not null && job.DataExpiracao <= DateTime.UtcNow)
                return StatusCode(StatusCodes.Status410Gone, Erro(MensagemArquivoIndisponivel));

            caminho = ArquivosExportacao.CaminhoPacote(ArquivosExportacao.ResolverPasta(_settings), job.NomeArquivo);
            if (caminho is null || !System.IO.File.Exists(caminho))
            {
                caminho = null;
                return StatusCode(StatusCodes.Status410Gone, Erro(MensagemArquivoIndisponivel));
            }

            return null;
        }

        /// <summary>Validações de C5 que o binding não cobre: tipo de título fora do enum e período com fim antes do início.</summary>
        private static string? ValidarFiltro(FiltroExportacaoDto filtro)
        {
            if (!Enum.IsDefined(filtro.TipoTitulo))
                return "Tipo de título inválido.";

            if (FimAntesDoInicio(filtro.DataSolicitacaoInicio, filtro.DataSolicitacaoFim))
                return "A data final da solicitação é anterior à data inicial.";

            if (FimAntesDoInicio(filtro.DataVencimentoInicio, filtro.DataVencimentoFim))
                return "A data final de vencimento é anterior à data inicial.";

            if (FimAntesDoInicio(filtro.DataAprovacaoFinanceiroInicio, filtro.DataAprovacaoFinanceiroFim))
                return "A data final de aprovação do financeiro é anterior à data inicial.";

            return null;
        }

        /// <summary>O fim vale pelo dia inteiro (C5), então a comparação é por data.</summary>
        private static bool FimAntesDoInicio(DateTime? inicio, DateTime? fim) =>
            inicio is not null && fim is not null && fim.Value.Date < inicio.Value.Date;

        /// <summary>Nome sugerido no download (C2), com a hora da conclusão no fuso configurado (padrão: Brasília).</summary>
        private string NomeDownload(ExportacaoJob job)
        {
            var conclusao = fuso.ParaLocal(ComoUtc(job.DataConclusao ?? job.DataCriacao));
            return string.Create(CultureInfo.InvariantCulture,
                $"exportacao-titulos-{job.IdExportacaoJob}-{conclusao:yyyyMMdd-HHmm}.zip");
        }

        private static ExportacaoJobDto ParaDto(ExportacaoJob job)
        {
            var status = (StatusExportacao)job.Status;
            return new ExportacaoJobDto
            {
                Id = job.IdExportacaoJob,
                Status = status,
                StatusDescricao = DescreverStatus(status),
                DataCriacao = ComoUtc(job.DataCriacao),
                DataInicio = ComoUtc(job.DataInicio),
                DataConclusao = ComoUtc(job.DataConclusao),
                DataExpiracao = ComoUtc(job.DataExpiracao),
                TotalTitulos = job.TotalTitulos,
                TitulosProcessados = job.TitulosProcessados,
                Percentual = CalcularPercentual(job, status),
                TotalAnexos = job.TotalAnexos,
                TamanhoBytes = job.TamanhoBytes,
                MensagemErro = job.MensagemErro,
                Filtros = LerFiltros(job.FiltrosJson),
            };
        }

        private static string DescreverStatus(StatusExportacao status) => status switch
        {
            StatusExportacao.Pendente => "Pendente",
            StatusExportacao.Processando => "Em processamento",
            StatusExportacao.Concluido => "Concluída",
            StatusExportacao.Falhou => "Falhou",
            StatusExportacao.Expirado => "Expirada",
            StatusExportacao.Cancelado => "Cancelada",
            StatusExportacao.Excluido => "Excluída",
            _ => "Desconhecido",
        };

        /// <summary>Situações finais: nenhuma gravação do worker, da limpeza ou da API tira o job delas.</summary>
        private static bool EstaEncerrado(StatusExportacao status) =>
            status is StatusExportacao.Falhou or StatusExportacao.Cancelado
                or StatusExportacao.Expirado or StatusExportacao.Excluido;

        private static int CalcularPercentual(ExportacaoJob job, StatusExportacao status)
        {
            if (status is StatusExportacao.Concluido or StatusExportacao.Expirado or StatusExportacao.Excluido)
                return 100;

            if (job.TotalTitulos <= 0)
                return 0;

            return (int)Math.Clamp(job.TitulosProcessados * 100L / job.TotalTitulos, 0, 100);
        }

        private static FiltroExportacaoDto LerFiltros(string filtrosJson)
        {
            try
            {
                return JsonSerializer.Deserialize<FiltroExportacaoDto>(filtrosJson, OpcoesFiltro) ?? new FiltroExportacaoDto();
            }
            catch (JsonException)
            {
                return new FiltroExportacaoDto();
            }
        }

        /// <summary>As datas do job são gravadas em UTC; o banco as devolve sem o tipo, e o JSON precisa do "Z".</summary>
        private static DateTime ComoUtc(DateTime data) => DateTime.SpecifyKind(data, DateTimeKind.Utc);

        private static DateTime? ComoUtc(DateTime? data) => data is null ? null : ComoUtc(data.Value);

        /// <summary>Um arquivo preso (por exemplo, num download em curso) vira órfão e sai numa varredura da limpeza.</summary>
        private void TentarApagar(string caminho)
        {
            try
            {
                System.IO.File.Delete(caminho);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                logger.LogWarning(ex, "Não foi possível apagar o arquivo de exportação {Arquivo}.", Path.GetFileName(caminho));
            }
        }

        private static GenericResponse<T> Sucesso<T>(T dados, string mensagem) where T : class => new()
        {
            Success = true,
            Message = mensagem,
            Data = dados,
            Records = 1,
            Pages = 1,
            Page = 1,
        };

        private static GenericResponse<object> Erro(string mensagem) => new()
        {
            Success = false,
            Message = mensagem,
            Data = null,
            Records = 0,
            Pages = 1,
            Page = 1,
        };
    }
}
