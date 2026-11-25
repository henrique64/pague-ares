using System.Collections.Concurrent;
using System.Text;
using System.Text.Json;
using Ares.PagueAres.Application.Configuration;
using Ares.PagueAres.Application.Exportacao;
using Ares.PagueAres.Application.Extensions;
using Ares.PagueAres.Domain.Dtos.Exportacao;
using Ares.PagueAres.Domain.Enums;
using Ares.PagueAres.Infrastructure;
using Ares.PagueAres.Infrastructure.Models;
using Ares.PagueAres.Tests.Integration.Infrastructure;
using Microsoft.AspNetCore.Hosting;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;

namespace Ares.PagueAres.Tests.Integration.Exportacao
{
    /// <summary>
    /// Fila, worker e limpeza da exportação (contrato C9) com EF InMemory, uma pasta temporária própria e um gerador
    /// falso controlado por <see cref="TaskCompletionSource"/>. Cada "processo" é um <see cref="ServiceProvider"/>
    /// separado sobre o mesmo banco InMemory (mesma <see cref="InMemoryDatabaseRoot"/>), o que permite simular dois
    /// executores disputando o mesmo job, como dois processos da API apontando para o mesmo banco.
    /// </summary>
    public sealed class ExportacaoWorkerTests : IAsyncLifetime
    {
        private const string MensagemInterrupcao =
            "A exportação foi interrompida por um reinício do servidor. Gere a exportação de novo.";

        private const string MensagemGenerica =
            "Não foi possível gerar a exportação. Gere de novo; se o erro continuar, acione o suporte.";

        private const int IdUsuarioFicticio = 4;

        private static readonly TimeSpan Prazo = TimeSpan.FromSeconds(20);
        private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

        private static readonly StatusExportacao[] StatusFinais =
        [
            StatusExportacao.Concluido, StatusExportacao.Falhou, StatusExportacao.Expirado, StatusExportacao.Cancelado
        ];

        private readonly InMemoryDatabaseRoot _raiz = new();
        private readonly string _nomeBanco = "ExportacaoWorker_" + Guid.NewGuid();
        private readonly string _pasta =
            Path.Combine(Path.GetTempPath(), "PagueAresTestes", "ExportacaoWorker_" + Guid.NewGuid().ToString("N"));
        private readonly GeradorFalso _gerador = new();
        private readonly List<ServiceProvider> _provedores = [];
        private readonly List<IHostedService> _iniciados = [];

        // -----------------------------------------------------------------------------------------------------
        // Cenário 1: um job por vez no mesmo processo
        // -----------------------------------------------------------------------------------------------------

        [Fact]
        public async Task DoisJobsNaFila_GeradorNuncaRodaDoisAoMesmoTempo_EAmbosConcluem()
        {
            _gerador.Segurar = true;
            var processo = await IniciarProcessoAsync();
            var primeiro = CriarJob();
            var segundo = CriarJob();

            processo.Fila.Enfileirar(primeiro);
            processo.Fila.Enfileirar(segundo);

            await _gerador.Iniciou(primeiro).WaitAsync(Prazo);
            await Task.Delay(300);
            _gerador.Iniciou(segundo).IsCompleted.Should()
                .BeFalse("o segundo job só pode começar depois que o primeiro terminar");
            Status(LerJob(segundo)).Should().Be(StatusExportacao.Pendente);

            _gerador.Liberar(primeiro);
            await _gerador.Iniciou(segundo).WaitAsync(Prazo);
            _gerador.Liberar(segundo);

            await AguardarStatusAsync(primeiro, StatusExportacao.Concluido);
            await AguardarStatusAsync(segundo, StatusExportacao.Concluido);
            _gerador.MaximoSimultaneo.Should().Be(1, "o worker processa um job por vez");
        }

        // -----------------------------------------------------------------------------------------------------
        // Cenário 2: dois executores sobre o mesmo banco
        // -----------------------------------------------------------------------------------------------------

        [Fact]
        public async Task DoisWorkersNoMesmoBanco_JobPendenteEGeradoExatamenteUmaVez()
        {
            var a = await IniciarProcessoAsync();
            var b = await IniciarProcessoAsync();

            // Vários jobs disputados aumentam a chance de as duas reivindicações lerem o mesmo Pendente.
            var disputados = Enumerable.Range(0, 8).Select(_ => CriarJob()).ToList();
            foreach (var id in disputados)
            {
                a.Fila.Enfileirar(id);
                b.Fila.Enfileirar(id);
            }

            // Cada worker recebe uma sentinela só dele: quando ela conclui, o worker já tentou todos os anteriores.
            var sentinelaA = CriarJob();
            var sentinelaB = CriarJob();
            a.Fila.Enfileirar(sentinelaA);
            b.Fila.Enfileirar(sentinelaB);
            await AguardarStatusAsync(sentinelaA, StatusExportacao.Concluido);
            await AguardarStatusAsync(sentinelaB, StatusExportacao.Concluido);

            foreach (var id in disputados)
            {
                await AguardarStatusAsync(id, StatusExportacao.Concluido);
                _gerador.Chamadas(id).Should().Be(1, $"o job {id} foi enfileirado nos dois workers e só um pode gerá-lo");
            }
        }

        // -----------------------------------------------------------------------------------------------------
        // Cenário 3: recuperação no início
        // -----------------------------------------------------------------------------------------------------

        [Fact]
        public async Task Inicio_ProcessandoViraFalhou_ParciaisApagados_PendentesProcessadosPorDataDeCriacao()
        {
            var agora = DateTime.UtcNow;
            var interrompido = CriarJob(StatusExportacao.Processando, j =>
            {
                j.DataCriacao = agora.AddMinutes(-30);
                j.DataInicio = agora.AddMinutes(-20);
                j.TotalTitulos = 10;
                j.TitulosProcessados = 3;
            });
            // O id menor foi criado depois: a ordem de retomada é a da DataCriacao, não a do id.
            var pendenteRecente = CriarJob(StatusExportacao.Pendente, j => j.DataCriacao = agora.AddMinutes(-1));
            var pendenteAntigo = CriarJob(StatusExportacao.Pendente, j => j.DataCriacao = agora.AddMinutes(-10));

            Directory.CreateDirectory(_pasta);
            File.WriteAllText(Parcial(interrompido), "pacote pela metade");
            var sobra = Path.Combine(_pasta, "exportacao-999999.zip.parcial");
            File.WriteAllText(sobra, "sobra de outro reinício");
            var outroArquivo = Path.Combine(_pasta, "exportacao-888888.zip");
            File.WriteAllText(outroArquivo, "pacote concluído de outro job");

            await IniciarProcessoAsync();

            var falhou = await AguardarStatusAsync(interrompido, StatusExportacao.Falhou);
            falhou.MensagemErro.Should().Be(MensagemInterrupcao);
            File.Exists(Parcial(interrompido)).Should().BeFalse("o .parcial do job interrompido é apagado no início");
            File.Exists(sobra).Should().BeFalse("todo .parcial da pasta é apagado no início");
            File.Exists(outroArquivo).Should().BeTrue("o início do worker só apaga .parcial");

            await AguardarStatusAsync(pendenteAntigo, StatusExportacao.Concluido);
            await AguardarStatusAsync(pendenteRecente, StatusExportacao.Concluido);
            _gerador.Chamadas(interrompido).Should().Be(0);
            _gerador.Ordem.Should().Equal([pendenteAntigo, pendenteRecente], "os Pendente voltam à fila por DataCriacao");
        }

        // -----------------------------------------------------------------------------------------------------
        // Cenário 4: cancelamento
        // -----------------------------------------------------------------------------------------------------

        [Fact]
        public async Task CancelamentoDePendente_ViraCanceladoSemChamarOGerador()
        {
            _gerador.Segurar = true;
            var processo = await IniciarProcessoAsync();

            var ocupado = CriarJob();
            processo.Fila.Enfileirar(ocupado);
            await _gerador.Iniciou(ocupado).WaitAsync(Prazo);

            // Enquanto o worker está ocupado, dois jobs na fila são cancelados ainda Pendente:
            // um pela API deste processo (que já grava Cancelado) e outro só com o pedido gravado no banco.
            var canceladoPelaApi = CriarJob();
            var comPedidoNoBanco = CriarJob();
            processo.Fila.Enfileirar(canceladoPelaApi);
            processo.Fila.Enfileirar(comPedidoNoBanco);
            AlterarJob(canceladoPelaApi, j => j.Status = (int)StatusExportacao.Cancelado);
            AlterarJob(comPedidoNoBanco, j => j.CancelamentoSolicitado = true);

            var sentinela = CriarJob();
            processo.Fila.Enfileirar(sentinela);
            _gerador.Liberar(ocupado);
            _gerador.Liberar(sentinela);
            await AguardarStatusAsync(sentinela, StatusExportacao.Concluido);

            Status(LerJob(canceladoPelaApi)).Should().Be(StatusExportacao.Cancelado);
            await AguardarStatusAsync(comPedidoNoBanco, StatusExportacao.Cancelado);
            _gerador.Chamadas(canceladoPelaApi).Should().Be(0);
            _gerador.Chamadas(comPedidoNoBanco).Should().Be(0);
            File.Exists(Parcial(comPedidoNoBanco)).Should().BeFalse();
            File.Exists(Final(comPedidoNoBanco)).Should().BeFalse();
        }

        [Fact]
        public async Task CancelamentoEmProcessando_PelaFila_ViraCanceladoEApagaParcial()
        {
            _gerador.Segurar = true;
            var processo = await IniciarProcessoAsync();
            var id = CriarJob();
            processo.Fila.Enfileirar(id);

            await _gerador.Iniciou(id).WaitAsync(Prazo);
            Status(LerJob(id)).Should().Be(StatusExportacao.Processando);
            File.Exists(Parcial(id)).Should().BeTrue("o gerador já escreveu parte do pacote");

            // O que a API faz no DELETE de um job Processando: grava o pedido e avisa a fila.
            AlterarJob(id, j => j.CancelamentoSolicitado = true);
            processo.Fila.SolicitarCancelamento(id).Should().BeTrue("o job está em execução neste processo");

            var job = await AguardarStatusAsync(id, StatusExportacao.Cancelado);
            File.Exists(Parcial(id)).Should().BeFalse();
            File.Exists(Final(id)).Should().BeFalse();
            job.NomeArquivo.Should().BeNull();
            job.TamanhoBytes.Should().BeNull();
        }

        [Fact]
        public async Task CancelamentoEmProcessando_SoPeloBanco_ViraCanceladoEApagaParcial()
        {
            // Pedido feito em outro processo da API: só a coluna CancelamentoSolicitado muda.
            _gerador.Segurar = true;
            var processo = await IniciarProcessoAsync();
            var id = CriarJob();
            processo.Fila.Enfileirar(id);
            await _gerador.Iniciou(id).WaitAsync(Prazo);

            AlterarJob(id, j => j.CancelamentoSolicitado = true);

            await AguardarStatusAsync(id, StatusExportacao.Cancelado);
            File.Exists(Parcial(id)).Should().BeFalse();
            File.Exists(Final(id)).Should().BeFalse();
        }

        // -----------------------------------------------------------------------------------------------------
        // Cenário 5: parada do host
        // -----------------------------------------------------------------------------------------------------

        [Fact]
        public async Task ParadaDoHostDuranteAGeracao_ViraFalhouComMensagemDeInterrupcao()
        {
            _gerador.Segurar = true;
            var processo = await IniciarProcessoAsync();
            var id = CriarJob();
            processo.Fila.Enfileirar(id);
            await _gerador.Iniciou(id).WaitAsync(Prazo);
            File.Exists(Parcial(id)).Should().BeTrue();

            await processo.Worker.StopAsync(CancellationToken.None).WaitAsync(Prazo);

            var job = LerJob(id);
            Status(job).Should().Be(StatusExportacao.Falhou);
            job.MensagemErro.Should().Be(MensagemInterrupcao);
            File.Exists(Parcial(id)).Should().BeFalse();
            File.Exists(Final(id)).Should().BeFalse();
        }

        // -----------------------------------------------------------------------------------------------------
        // Cenário 6: falhas do gerador
        // -----------------------------------------------------------------------------------------------------

        [Fact]
        public async Task ExportacaoException_ViraFalhouComAMensagemDela()
        {
            const string mensagem = "A exportação passou do limite de 20 GB de anexos por pacote. Gere de novo com filtros menores.";
            _gerador.Comportamento = (_, _, _, _) => throw new ExportacaoException(mensagem);
            var processo = await IniciarProcessoAsync();
            var id = CriarJob();
            processo.Fila.Enfileirar(id);

            var job = await AguardarStatusAsync(id, StatusExportacao.Falhou);

            job.MensagemErro.Should().Be(mensagem);
            File.Exists(Parcial(id)).Should().BeFalse();
            File.Exists(Final(id)).Should().BeFalse();
            job.NomeArquivo.Should().BeNull();
        }

        [Fact]
        public async Task ExcecaoGenerica_ViraFalhouComMensagemGenericaSemOTextoDaExcecao()
        {
            _gerador.Comportamento = (_, _, _, _) =>
                throw new InvalidOperationException("Timeout na conexão com servidor-ficticio:1433 (detalhe interno)");
            var processo = await IniciarProcessoAsync();
            var id = CriarJob();
            processo.Fila.Enfileirar(id);

            var job = await AguardarStatusAsync(id, StatusExportacao.Falhou);

            job.MensagemErro.Should().Be(MensagemGenerica);
            job.MensagemErro.Should().NotContain("servidor-ficticio").And.NotContain("Timeout").And.NotContain("Exception");
            File.Exists(Parcial(id)).Should().BeFalse();
            File.Exists(Final(id)).Should().BeFalse();
        }

        // -----------------------------------------------------------------------------------------------------
        // Cenário 7: espaço em disco
        // -----------------------------------------------------------------------------------------------------

        [Fact]
        public async Task EspacoLivreAbaixoDoMinimo_ViraFalhouComMensagemAmigavelSemChamarOGerador()
        {
            var processo = await IniciarProcessoAsync(s => s.EspacoLivreMinimoMb = long.MaxValue / 4);
            var id = CriarJob();
            processo.Fila.Enfileirar(id);

            var job = await AguardarStatusAsync(id, StatusExportacao.Falhou);

            job.MensagemErro.Should().NotBeNullOrWhiteSpace();
            job.MensagemErro.Should().Contain("espaço");
            job.MensagemErro.Should().NotBe(MensagemGenerica);
            job.MensagemErro!.Length.Should().BeLessThanOrEqualTo(500);
            _gerador.Chamadas(id).Should().Be(0);
            File.Exists(Parcial(id)).Should().BeFalse();
        }

        // -----------------------------------------------------------------------------------------------------
        // Cenário 8: limpeza
        // -----------------------------------------------------------------------------------------------------

        [Fact]
        public async Task Limpeza_ExpiraConcluidoVencido_ApagaOrfaoVelho_MantemArquivoDeJobValido()
        {
            var processo = NovoProcesso(s => s.RetencaoHoras = 48);
            var agora = DateTime.UtcNow;
            Directory.CreateDirectory(_pasta);

            var vencido = CriarJob(StatusExportacao.Concluido, j =>
            {
                j.DataConclusao = agora.AddHours(-50);
                j.DataExpiracao = agora.AddHours(-2);
            });
            AlterarJob(vencido, j => j.NomeArquivo = $"exportacao-{vencido}.zip");
            var valido = CriarJob(StatusExportacao.Concluido, j =>
            {
                j.DataConclusao = agora.AddHours(-8);
                j.DataExpiracao = agora.AddHours(40);
            });
            AlterarJob(valido, j => j.NomeArquivo = $"exportacao-{valido}.zip");

            var velho = agora.AddHours(-72);
            CriarArquivo(Final(vencido), velho);
            CriarArquivo(Final(valido), velho);       // velho, mas o job Concluido ainda vale
            var orfaoVelho = CriarArquivo(Path.Combine(_pasta, "exportacao-777777.zip"), velho);
            var orfaoRecente = CriarArquivo(Path.Combine(_pasta, "exportacao-777778.zip"), agora.AddHours(-1));
            var naoExportacao = CriarArquivo(Path.Combine(_pasta, "outro-arquivo.txt"), velho);

            await IniciarAsync(processo.Provedor.GetRequiredService<ExportacaoLimpeza>());

            await AguardarAsync(
                () => Status(LerJob(vencido)) == StatusExportacao.Expirado
                      && !File.Exists(Final(vencido))
                      && !File.Exists(orfaoVelho),
                "a limpeza deveria expirar o job vencido, apagar o arquivo dele e o órfão velho");

            File.Exists(Final(valido)).Should().BeTrue("o arquivo de um job Concluido dentro da validade é mantido");
            Status(LerJob(valido)).Should().Be(StatusExportacao.Concluido);
            File.Exists(orfaoRecente).Should().BeTrue("um órfão mais novo que a retenção é mantido");
            File.Exists(naoExportacao).Should().BeTrue("a limpeza só considera exportacao-*.zip");
        }

        // -----------------------------------------------------------------------------------------------------
        // Cenário 9: sucesso
        // -----------------------------------------------------------------------------------------------------

        [Fact]
        public async Task Sucesso_ArquivoFinalSemParcial_TamanhoEExpiracaoGravados()
        {
            var processo = await IniciarProcessoAsync(s => s.RetencaoHoras = 6);
            var id = CriarJob();
            processo.Fila.Enfileirar(id);

            var job = await AguardarStatusAsync(id, StatusExportacao.Concluido);

            job.NomeArquivo.Should().Be($"exportacao-{id}.zip");
            File.Exists(Final(id)).Should().BeTrue();
            File.Exists(Parcial(id)).Should().BeFalse();
            File.ReadAllText(Final(id)).Should().StartWith($"pacote ficticio do job {id}");
            job.TamanhoBytes.Should().Be(new FileInfo(Final(id)).Length).And.BeGreaterThan(0);
            job.DataInicio.Should().NotBeNull();
            job.DataConclusao.Should().NotBeNull().And.BeOnOrAfter(job.DataInicio!.Value);
            job.DataExpiracao.Should().Be(job.DataConclusao!.Value.AddHours(6));
            job.TotalTitulos.Should().Be(3);
            job.TitulosProcessados.Should().Be(3);
            job.TotalAnexos.Should().Be(2);
            job.MensagemErro.Should().BeNull();
        }

        [Fact]
        public async Task Progresso_EGravadoDuranteAGeracao()
        {
            var continuar = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            _gerador.Comportamento = async (_, _, progresso, ct) =>
            {
                progresso?.Report(new ProgressoExportacao(0, 25));
                for (var i = 1; i <= 12; i++)
                    progresso?.Report(new ProgressoExportacao(i, 25));
                await continuar.Task.WaitAsync(ct);
                for (var i = 13; i <= 25; i++)
                    progresso?.Report(new ProgressoExportacao(i, 25));
                return new ResultadoPacoteExportacao(25, 0, 0);
            };
            var processo = await IniciarProcessoAsync();
            var id = CriarJob();
            processo.Fila.Enfileirar(id);

            await AguardarAsync(() =>
            {
                var job = LerJob(id);
                return Status(job) == StatusExportacao.Processando && job.TotalTitulos == 25 && job.TitulosProcessados >= 10;
            }, "o progresso deveria ser gravado enquanto o job está em Processando");

            continuar.SetResult();
            var concluido = await AguardarStatusAsync(id, StatusExportacao.Concluido);
            concluido.TitulosProcessados.Should().Be(25);
            concluido.TotalTitulos.Should().Be(25);
        }

        // -----------------------------------------------------------------------------------------------------
        // Registro no DI e factory de testes
        // -----------------------------------------------------------------------------------------------------

        [Theory]
        [InlineData(true)]
        [InlineData(false)]
        public void Registro_FonteGeradorEFila_EServicosSoComWorkerHabilitado(bool habilitado)
        {
            var configuracao = new ConfigurationBuilder()
                .AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["Exportacao:WorkerHabilitado"] = habilitado ? "true" : "false",
                })
                .Build();
            var services = new ServiceCollection();

            services.RegisterApplicationServices(configuracao);

            services.Should().Contain(d => d.ServiceType == typeof(IFonteExportacao)
                                           && d.ImplementationType == typeof(FonteExportacaoEf)
                                           && d.Lifetime == ServiceLifetime.Scoped);
            services.Should().Contain(d => d.ServiceType == typeof(IGeradorPacoteExportacao)
                                           && d.ImplementationType == typeof(GeradorPacoteExportacao)
                                           && d.Lifetime == ServiceLifetime.Scoped);
            services.Should().Contain(d => d.ServiceType == typeof(ExportacaoFila) && d.Lifetime == ServiceLifetime.Singleton);

            var hospedados = services.Where(d => d.ServiceType == typeof(IHostedService))
                .Select(d => d.ImplementationType)
                .ToList();
            if (habilitado)
                hospedados.Should().Contain([typeof(ExportacaoWorker), typeof(ExportacaoLimpeza)]);
            else
                hospedados.Should().NotContain(typeof(ExportacaoWorker)).And.NotContain(typeof(ExportacaoLimpeza));
        }

        [Fact]
        public void Factory_RegistraWorkerELimpeza_UsaPastaPropria_EApagaAPastaNoDispose()
        {
            string pasta;
            using (var factory = new PagueAresWebAppFactory())
            {
                pasta = factory.PastaExportacao;
                pasta.Should().NotBeNullOrWhiteSpace();
                pasta.Should().NotBe(Path.Combine(Path.GetTempPath(), "PagueAres", "Exportacao"));

                var hospedados = factory.Services.GetServices<IHostedService>().ToList();
                hospedados.OfType<ExportacaoWorker>().Should().ContainSingle();
                hospedados.OfType<ExportacaoLimpeza>().Should().ContainSingle();
                factory.Services.GetRequiredService<IOptions<ExportacaoSettings>>().Value.Pasta.Should().Be(pasta);

                using var scope = factory.Services.CreateScope();
                scope.ServiceProvider.GetRequiredService<IFonteExportacao>().Should().BeOfType<FonteExportacaoEf>();
                scope.ServiceProvider.GetRequiredService<IGeradorPacoteExportacao>().Should().BeOfType<GeradorPacoteExportacao>();

                Directory.CreateDirectory(pasta);
                File.WriteAllText(Path.Combine(pasta, "exportacao-1.zip"), "pacote");
            }

            Directory.Exists(pasta).Should().BeFalse("a factory apaga a pasta de exportação dela no Dispose");
        }

        [Fact]
        public void FactoryComWorkerDesabilitado_NaoRegistraWorkerNemLimpeza()
        {
            using var factory = new FactorySemWorker();

            var hospedados = factory.Services.GetServices<IHostedService>().ToList();

            hospedados.OfType<ExportacaoWorker>().Should().BeEmpty();
            hospedados.OfType<ExportacaoLimpeza>().Should().BeEmpty();
            factory.Services.GetRequiredService<ExportacaoFila>().Should().NotBeNull();
        }

        private sealed class FactorySemWorker : PagueAresWebAppFactory
        {
            protected override void ConfigureWebHost(IWebHostBuilder builder)
            {
                base.ConfigureWebHost(builder);
                builder.UseSetting("Exportacao:WorkerHabilitado", "false");
            }
        }

        // -----------------------------------------------------------------------------------------------------
        // Infraestrutura de teste
        // -----------------------------------------------------------------------------------------------------

        private sealed record Processo(ServiceProvider Provedor, ExportacaoFila Fila, ExportacaoWorker Worker);

        public Task InitializeAsync()
        {
            using var ctx = NovoContexto();
            ctx.Usuarios.Add(new Usuario
            {
                IdUsuario = IdUsuarioFicticio,
                Email = "financeiro.ficticio@exemplo.invalid",
                Nome = "Financeiro Fictício",
                Senha = "hash-ficticio",
                IdDepartamento = 1,
                Ativo = true,
            });
            ctx.SaveChanges();
            return Task.CompletedTask;
        }

        public async Task DisposeAsync()
        {
            _gerador.LiberarTodos();
            foreach (var servico in _iniciados)
            {
                using var limite = new CancellationTokenSource(TimeSpan.FromSeconds(10));
                try
                {
                    await servico.StopAsync(limite.Token);
                }
                catch (OperationCanceledException)
                {
                    // O serviço não parou a tempo; o descarte abaixo encerra o que restar.
                }

                (servico as IDisposable)?.Dispose();
            }

            foreach (var provedor in _provedores)
                await provedor.DisposeAsync();

            try
            {
                if (Directory.Exists(_pasta))
                    Directory.Delete(_pasta, recursive: true);
            }
            catch (IOException)
            {
                // Pasta temporária: se algo ainda a segurar, o sistema a limpa depois.
            }
        }

        private Processo NovoProcesso(Action<ExportacaoSettings>? ajustar = null)
        {
            var services = new ServiceCollection();
            services.AddLogging();
            services.AddDbContext<PagueAresContext>(o => o.UseInMemoryDatabase(_nomeBanco, _raiz));
            services.AddSingleton<IGeradorPacoteExportacao>(_gerador);
            services.Configure<ExportacaoSettings>(s =>
            {
                s.Pasta = _pasta;
                s.EspacoLivreMinimoMb = 0;
                ajustar?.Invoke(s);
            });
            services.AddSingleton<ExportacaoFila>();
            services.AddSingleton<ExportacaoWorker>();
            services.AddSingleton<ExportacaoLimpeza>();

            var provedor = services.BuildServiceProvider();
            _provedores.Add(provedor);
            return new Processo(provedor, provedor.GetRequiredService<ExportacaoFila>(), provedor.GetRequiredService<ExportacaoWorker>());
        }

        private async Task<Processo> IniciarProcessoAsync(Action<ExportacaoSettings>? ajustar = null)
        {
            var processo = NovoProcesso(ajustar);
            await IniciarAsync(processo.Worker);
            return processo;
        }

        private async Task IniciarAsync(IHostedService servico)
        {
            _iniciados.Add(servico);
            await servico.StartAsync(CancellationToken.None);
        }

        private PagueAresContext NovoContexto() =>
            new(new DbContextOptionsBuilder<PagueAresContext>().UseInMemoryDatabase(_nomeBanco, _raiz).Options);

        private int CriarJob(StatusExportacao status = StatusExportacao.Pendente, Action<ExportacaoJob>? ajustar = null)
        {
            using var ctx = NovoContexto();
            var job = new ExportacaoJob
            {
                IdUsuario = IdUsuarioFicticio,
                Status = (int)status,
                FiltrosJson = JsonSerializer.Serialize(new FiltroExportacaoDto(), Json),
                DataCriacao = DateTime.UtcNow,
            };
            ajustar?.Invoke(job);
            ctx.ExportacaoJobs.Add(job);
            ctx.SaveChanges();
            return job.IdExportacaoJob;
        }

        private ExportacaoJob LerJob(int id)
        {
            using var ctx = NovoContexto();
            return ctx.ExportacaoJobs.AsNoTracking().Single(j => j.IdExportacaoJob == id);
        }

        /// <summary>Altera o job como a API faria: relê e tenta de novo se o worker gravou no meio.</summary>
        private void AlterarJob(int id, Action<ExportacaoJob> alterar)
        {
            for (var tentativa = 1; ; tentativa++)
            {
                using var ctx = NovoContexto();
                var job = ctx.ExportacaoJobs.Single(j => j.IdExportacaoJob == id);
                alterar(job);
                try
                {
                    ctx.SaveChanges();
                    return;
                }
                catch (DbUpdateConcurrencyException) when (tentativa < 10)
                {
                    // Outro executor gravou entre a leitura e a gravação; relê e tenta de novo.
                }
            }
        }

        private static StatusExportacao Status(ExportacaoJob job) => (StatusExportacao)job.Status;

        private async Task<ExportacaoJob> AguardarStatusAsync(int id, StatusExportacao esperado)
        {
            var limite = DateTime.UtcNow + Prazo;
            while (true)
            {
                var job = LerJob(id);
                var atual = Status(job);
                if (atual == esperado)
                    return job;

                // Um status final diferente do esperado não muda mais: falha na hora, com a mensagem gravada.
                if (StatusFinais.Contains(atual) || DateTime.UtcNow > limite)
                {
                    atual.Should().Be(esperado,
                        $"o job {id} deveria chegar a {esperado} (mensagem gravada: \"{job.MensagemErro}\")");
                }

                await Task.Delay(25);
            }
        }

        private static async Task AguardarAsync(Func<bool> condicao, string motivo)
        {
            var limite = DateTime.UtcNow + Prazo;
            while (!condicao())
            {
                if (DateTime.UtcNow > limite)
                    condicao().Should().BeTrue(motivo);
                await Task.Delay(25);
            }
        }

        private string Parcial(int id) => Path.Combine(_pasta, $"exportacao-{id}.zip.parcial");

        private string Final(int id) => Path.Combine(_pasta, $"exportacao-{id}.zip");

        private static string CriarArquivo(string caminho, DateTime ultimaGravacaoUtc)
        {
            File.WriteAllText(caminho, "conteúdo fictício");
            File.SetLastWriteTimeUtc(caminho, ultimaGravacaoUtc);
            return caminho;
        }

        /// <summary>
        /// Gerador falso: escreve um começo de pacote no destino, avisa que começou e, conforme o teste, termina na hora,
        /// espera ser liberado (sempre respeitando o CancellationToken) ou executa um comportamento próprio.
        /// </summary>
        private sealed class GeradorFalso : IGeradorPacoteExportacao
        {
            private readonly ConcurrentDictionary<int, int> _chamadas = new();
            private readonly ConcurrentDictionary<int, TaskCompletionSource> _iniciados = new();
            private readonly ConcurrentDictionary<int, TaskCompletionSource> _liberacoes = new();
            private readonly ConcurrentQueue<int> _ordem = new();
            private int _emExecucao;
            private int _maximoSimultaneo;

            /// <summary>Quando verdadeiro, cada geração espera <see cref="Liberar"/> antes de terminar.</summary>
            public bool Segurar { get; set; }

            public Func<int, Stream, IProgress<ProgressoExportacao>?, CancellationToken, Task<ResultadoPacoteExportacao>>? Comportamento { get; set; }

            public int MaximoSimultaneo => Volatile.Read(ref _maximoSimultaneo);

            public IReadOnlyList<int> Ordem => [.. _ordem];

            public int Chamadas(int id) => _chamadas.TryGetValue(id, out var n) ? n : 0;

            public Task Iniciou(int id) => Tcs(_iniciados, id).Task;

            public void Liberar(int id) => Tcs(_liberacoes, id).TrySetResult();

            public void LiberarTodos()
            {
                Segurar = false;
                foreach (var tcs in _liberacoes.Values)
                    tcs.TrySetResult();
            }

            public async Task<ResultadoPacoteExportacao> GerarAsync(int idExportacao, FiltroExportacaoDto filtro, Stream destino,
                IProgress<ProgressoExportacao>? progresso, CancellationToken ct)
            {
                _chamadas.AddOrUpdate(idExportacao, 1, (_, n) => n + 1);
                _ordem.Enqueue(idExportacao);
                var simultaneos = Interlocked.Increment(ref _emExecucao);
                AtualizarMaximo(simultaneos);
                try
                {
                    await destino.WriteAsync(Encoding.UTF8.GetBytes($"pacote ficticio do job {idExportacao}\n"), ct);
                    await destino.FlushAsync(ct);
                    Tcs(_iniciados, idExportacao).TrySetResult();

                    if (Comportamento is not null)
                        return await Comportamento(idExportacao, destino, progresso, ct);

                    if (Segurar)
                        await Tcs(_liberacoes, idExportacao).Task.WaitAsync(ct);
                    else
                        await Task.Delay(50, ct); // janela em que uma segunda geração simultânea seria percebida

                    await destino.WriteAsync(new byte[1000 + idExportacao], ct);
                    progresso?.Report(new ProgressoExportacao(3, 3));
                    return new ResultadoPacoteExportacao(3, 2, 2048);
                }
                finally
                {
                    Interlocked.Decrement(ref _emExecucao);
                }
            }

            private void AtualizarMaximo(int valor)
            {
                int atual;
                while (valor > (atual = Volatile.Read(ref _maximoSimultaneo)))
                {
                    if (Interlocked.CompareExchange(ref _maximoSimultaneo, valor, atual) == atual)
                        return;
                }
            }

            private static TaskCompletionSource Tcs(ConcurrentDictionary<int, TaskCompletionSource> mapa, int id) =>
                mapa.GetOrAdd(id, _ => new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously));
        }
    }
}
