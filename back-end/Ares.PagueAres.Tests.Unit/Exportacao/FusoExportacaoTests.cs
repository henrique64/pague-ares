using Ares.PagueAres.Application.Configuration;
using Ares.PagueAres.Application.Exportacao;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace Ares.PagueAres.Tests.Unit.Exportacao
{
    /// <summary>Relógio que sempre devolve o mesmo instante, para os testes de data e hora.</summary>
    public sealed class RelogioFixo(DateTimeOffset instante) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => instante.ToUniversalTime();
    }

    /// <summary>Fábrica do <see cref="FusoExportacao"/> para os testes, com o instante UTC fixo da conversa do dono.</summary>
    public static class FusoDeTeste
    {
        /// <summary>2026-11-26 15:30 UTC, 12:30 em Brasília.</summary>
        public static readonly DateTimeOffset InstanteFixo = new(2026, 11, 26, 15, 30, 0, TimeSpan.Zero);

        public static FusoExportacao Criar(string? fusoHorario = null, ILogger<FusoExportacao>? logger = null)
        {
            var settings = new ExportacaoSettings();
            if (fusoHorario is not null)
            {
                settings.FusoHorario = fusoHorario;
            }

            return new FusoExportacao(Options.Create(settings), new RelogioFixo(InstanteFixo), logger ?? NullLogger<FusoExportacao>.Instance);
        }
    }

    public class FusoExportacaoTests
    {
        [Fact]
        public void Padrao_ConverteInstanteUtcParaBrasilia()
        {
            var fuso = FusoDeTeste.Criar();

            var local = fuso.ParaLocal(new DateTime(2026, 11, 26, 15, 30, 0, DateTimeKind.Utc));

            local.Hour.Should().Be(12);
            local.Minute.Should().Be(30);
            local.Offset.Should().Be(TimeSpan.FromHours(-3));
        }

        [Fact]
        public void Padrao_AgoraUsaORelogioInjetadoNoFusoDeBrasilia()
        {
            var agora = FusoDeTeste.Criar().Agora();

            agora.ToString("dd/MM/yyyy HH:mm").Should().Be("26/11/2026 12:30");
            agora.Offset.Should().Be(TimeSpan.FromHours(-3));
        }

        [Fact]
        public void Padrao_RotuloDizHorarioDeBrasiliaEODeslocamento()
        {
            var fuso = FusoDeTeste.Criar();

            fuso.Rotulo(fuso.Agora()).Should().Be("horário de Brasília, UTC-03:00");
        }

        [Fact]
        public void IdDoWindows_TambemResolveBrasilia()
        {
            var fuso = FusoDeTeste.Criar("E. South America Standard Time");

            fuso.Agora().ToString("HH:mm").Should().Be("12:30");
            fuso.Rotulo(fuso.Agora()).Should().Be("horário de Brasília, UTC-03:00");
        }

        [Fact]
        public void FusoInvalido_CaiNoPadraoSemExcecaoERegistraAviso()
        {
            var logger = new LoggerDeAvisos<FusoExportacao>();

            var fuso = FusoDeTeste.Criar("Marte/Cratera-Gale", logger);

            fuso.Agora().ToString("HH:mm").Should().Be("12:30");
            fuso.Rotulo(fuso.Agora()).Should().Be("horário de Brasília, UTC-03:00");
            logger.Avisos.Should().ContainSingle().Which.Should().Contain("Marte/Cratera-Gale");
        }

        [Theory]
        [InlineData("")]
        [InlineData("   ")]
        public void FusoVazio_CaiNoPadraoSemExcecao(string configurado)
        {
            var fuso = FusoDeTeste.Criar(configurado);

            fuso.Agora().ToString("HH:mm").Should().Be("12:30");
        }

        [Fact]
        public void OutroFuso_RotuloTraOIdEODeslocamento()
        {
            var fuso = FusoDeTeste.Criar("UTC");

            fuso.Agora().ToString("HH:mm").Should().Be("15:30");
            fuso.Rotulo(fuso.Agora()).Should().Be("fuso UTC, UTC+00:00");
        }

        [Fact]
        public void ParaLocal_TrataDataSemKindComoUtc()
        {
            var fuso = FusoDeTeste.Criar();

            var local = fuso.ParaLocal(new DateTime(2026, 11, 25, 17, 4, 0, DateTimeKind.Unspecified));

            local.ToString("yyyyMMdd-HHmm").Should().Be("20261125-1404");
        }

        private sealed class LoggerDeAvisos<T> : ILogger<T>
        {
            public List<string> Avisos { get; } = [];

            public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

            public bool IsEnabled(LogLevel logLevel) => true;

            public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
            {
                if (logLevel == LogLevel.Warning)
                {
                    Avisos.Add(formatter(state, exception));
                }
            }
        }
    }
}
