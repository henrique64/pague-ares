using Ares.PagueAres.Application.Configuration;
using Ares.PagueAres.Application.Exportacao;
using Ares.PagueAres.Domain.Dtos.Exportacao;
using Ares.PagueAres.Domain.Enums;
using FluentAssertions;
using Microsoft.Extensions.Options;
using Xunit.Abstractions;

namespace Ares.PagueAres.Tests.Unit.Exportacao
{
    /// <summary>
    /// A medição usa a memória gerenciada do processo inteiro; a coleção roda sozinha, sem outros testes em paralelo.
    /// </summary>
    [CollectionDefinition(Nome, DisableParallelization = true)]
    public class ColecaoMedicaoMemoria
    {
        public const string Nome = "Medição de memória";
    }

    [Collection(ColecaoMedicaoMemoria.Nome)]
    public class GeradorPacoteMemoriaTests(ITestOutputHelper saida)
    {
        private const int QuantidadeAnexos = 50;
        private const int TamanhoAnexo = 5 * 1024 * 1024;
        private const long LimitePicoBytes = 150L * 1024 * 1024;

        [Fact]
        public async Task Gerar_50AnexosDe5MB_PicoDeMemoriaFicaAbaixoDe150MBAcimaDaLinhaDeBase()
        {
            var fonte = new FonteExportacaoFalsa();
            for (int i = 1; i <= QuantidadeAnexos; i++)
            {
                fonte.Solicitacoes.Add(FonteExportacaoFalsa.Solicitacao(i));
                fonte.Anexos.Add(new AnexoExportacao(i, TipoTituloExportacao.Solicitacao, i, $"SOL-{i:D6}", 1, "Nota Fiscal",
                    $"documento-{i}.pdf", new DateTime(2026, 11, 3)));
            }

            // Conteúdo gerado sob demanda, um anexo por leitura, como a fonte real faz: só o base64 do anexo lido
            // existe em memória a cada momento. O buffer de bytes é reaproveitado e criado antes da linha de base.
            var bytes = new byte[TamanhoAnexo];
            new Random(42).NextBytes(bytes);
            fonte.GerarConteudo = (_, idAnexo) =>
            {
                BitConverter.TryWriteBytes(bytes.AsSpan(0, sizeof(int)), idAnexo);
                return Convert.ToBase64String(bytes);
            };

            var gerador = new GeradorPacoteExportacao(fonte, Options.Create(new ExportacaoSettings()));
            var caminho = Path.Combine(Path.GetTempPath(), $"pagueares-teste-memoria-{Guid.NewGuid():N}.zip");

            GC.Collect();
            GC.WaitForPendingFinalizers();
            GC.Collect();
            long linhaDeBase = GC.GetTotalMemory(forceFullCollection: true);
            long pico = linhaDeBase;

            using var parar = new CancellationTokenSource();
            var amostrador = Task.Run(async () =>
            {
                while (!parar.IsCancellationRequested)
                {
                    pico = Math.Max(pico, GC.GetTotalMemory(forceFullCollection: false));
                    try
                    {
                        await Task.Delay(50, parar.Token);
                    }
                    catch (OperationCanceledException)
                    {
                    }
                }
            });

            ResultadoPacoteExportacao resultado;
            try
            {
                await using (var arquivo = new FileStream(caminho, FileMode.CreateNew, FileAccess.ReadWrite, FileShare.None, 81920, FileOptions.Asynchronous))
                {
                    resultado = await gerador.GerarAsync(1, new FiltroExportacaoDto(), arquivo, null, CancellationToken.None);
                }
            }
            finally
            {
                parar.Cancel();
                await amostrador;
                File.Delete(caminho);
            }

            pico = Math.Max(pico, GC.GetTotalMemory(forceFullCollection: false));
            saida.WriteLine($"Linha de base: {linhaDeBase / (1024 * 1024)} MB; pico: {pico / (1024 * 1024)} MB; " +
                $"acima da linha de base: {(pico - linhaDeBase) / (1024 * 1024)} MB; anexos: {resultado.TotalAnexos}; bytes: {resultado.BytesAnexos}.");

            resultado.TotalAnexos.Should().Be(QuantidadeAnexos);
            resultado.BytesAnexos.Should().Be((long)QuantidadeAnexos * TamanhoAnexo);
            fonte.MaximoLeiturasSimultaneas.Should().Be(1);
            File.Exists(caminho).Should().BeFalse();
            (pico - linhaDeBase).Should().BeLessThan(LimitePicoBytes,
                $"o pico ({(pico - linhaDeBase) / (1024 * 1024)} MB acima da linha de base) mostra que os anexos não se acumulam em memória");
        }
    }
}
