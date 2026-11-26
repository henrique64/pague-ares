using Ares.PagueAres.Application.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Ares.PagueAres.Application.Exportacao
{
    /// <summary>
    /// Fuso horário do nome do ZIP baixado e da hora do <c>LEIA-ME.txt</c> (<c>Exportacao:FusoHorario</c>, padrão
    /// <c>America/Sao_Paulo</c>). Sem ele, a hora dependeria de onde a API roda: UTC no contêiner e a hora do Windows no
    /// IIS, e a mesma exportação teria nomes e horas diferentes conforme o servidor.
    /// <para>
    /// A resolução é tolerante: tenta o id configurado (IANA ou Windows), depois a conversão entre os dois formatos, depois o
    /// padrão de Brasília nos dois formatos; se nenhum existir no sistema, registra aviso e usa UTC. Nunca lança exceção,
    /// para uma configuração errada não derrubar a exportação.
    /// </para>
    /// </summary>
    public sealed class FusoExportacao
    {
        public const string IdPadraoIana = "America/Sao_Paulo";
        public const string IdPadraoWindows = "E. South America Standard Time";

        private readonly TimeProvider _relogio;
        private readonly TimeZoneInfo _fuso;

        public FusoExportacao(IOptions<ExportacaoSettings> settings, TimeProvider relogio, ILogger<FusoExportacao> logger)
        {
            ArgumentNullException.ThrowIfNull(settings);
            ArgumentNullException.ThrowIfNull(relogio);
            ArgumentNullException.ThrowIfNull(logger);

            _relogio = relogio;

            var configurado = settings.Value.FusoHorario;
            var fuso = Resolver(configurado, out var usouAlternativa);
            if (fuso is null)
            {
                logger.LogWarning(
                    "O fuso horário \"{Fuso}\" (Exportacao:FusoHorario) e os padrões de Brasília não existem neste sistema; " +
                    "o nome do ZIP e o LEIA-ME usarão UTC. Instale a base de fusos (tzdata) ou ajuste a configuração.", configurado);
                fuso = TimeZoneInfo.Utc;
            }
            else if (usouAlternativa)
            {
                logger.LogWarning(
                    "O fuso horário \"{Fuso}\" (Exportacao:FusoHorario) não foi encontrado neste sistema; usando \"{Usado}\".",
                    configurado, fuso.Id);
            }

            _fuso = fuso;
        }

        /// <summary>Id do fuso em uso, como o sistema o conhece.</summary>
        public string Id => _fuso.Id;

        /// <summary>Hora local do fuso configurado para um instante UTC (<c>DateTime</c> sem <c>Kind</c> vale como UTC).</summary>
        public DateTimeOffset ParaLocal(DateTime instanteUtc)
        {
            var utc = new DateTimeOffset(DateTime.SpecifyKind(instanteUtc, DateTimeKind.Utc));
            return TimeZoneInfo.ConvertTime(utc, _fuso);
        }

        /// <summary>A hora de agora, no fuso configurado, truncada em segundos.</summary>
        public DateTimeOffset Agora()
        {
            var local = TimeZoneInfo.ConvertTime(_relogio.GetUtcNow(), _fuso);
            return new DateTimeOffset(local.Year, local.Month, local.Day, local.Hour, local.Minute, local.Second, local.Offset);
        }

        /// <summary>
        /// Texto entre parênteses do LEIA-ME: <c>horário de Brasília, UTC-03:00</c> quando o fuso é o de São Paulo; nos demais,
        /// <c>fuso &lt;id&gt;, UTC±hh:mm</c>.
        /// </summary>
        public string Rotulo(DateTimeOffset instante)
        {
            var deslocamento = _fuso.GetUtcOffset(instante);
            var sinal = deslocamento < TimeSpan.Zero ? "-" : "+";
            var utc = $"UTC{sinal}{deslocamento.Duration():hh\\:mm}";
            return EhBrasilia(_fuso) ? $"horário de Brasília, {utc}" : $"fuso {_fuso.Id}, {utc}";
        }

        private static bool EhBrasilia(TimeZoneInfo fuso)
        {
            if (fuso.Id is IdPadraoIana or IdPadraoWindows)
            {
                return true;
            }

            return TimeZoneInfo.TryConvertWindowsIdToIanaId(fuso.Id, out var iana) && iana == IdPadraoIana;
        }

        private static TimeZoneInfo? Resolver(string? configurado, out bool usouAlternativa)
        {
            usouAlternativa = false;

            if (!string.IsNullOrWhiteSpace(configurado))
            {
                var id = configurado.Trim();
                var direto = Procurar(id);
                if (direto is not null)
                {
                    return direto;
                }

                if (TimeZoneInfo.TryConvertIanaIdToWindowsId(id, out var windows) && Procurar(windows) is { } convertido)
                {
                    return convertido;
                }

                if (TimeZoneInfo.TryConvertWindowsIdToIanaId(id, out var iana) && Procurar(iana) is { } convertidoParaIana)
                {
                    return convertidoParaIana;
                }
            }

            usouAlternativa = true;
            return Procurar(IdPadraoIana) ?? Procurar(IdPadraoWindows);
        }

        private static TimeZoneInfo? Procurar(string id)
        {
            try
            {
                return TimeZoneInfo.FindSystemTimeZoneById(id);
            }
            catch (Exception ex) when (ex is TimeZoneNotFoundException or InvalidTimeZoneException or ArgumentException)
            {
                return null;
            }
        }
    }
}
