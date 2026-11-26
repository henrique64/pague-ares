using System.Collections.Concurrent;
using System.Net;
using System.Text;
using System.Text.Json;
using Ares.PagueAres.Application.Configuration;
using Ares.PagueAres.Application.Exportacao;
using Ares.PagueAres.Application.Extensions;
using Ares.PagueAres.Domain;
using Ares.PagueAres.Domain.Dtos.Exportacao;
using Ares.PagueAres.Domain.Enums;
using Ares.PagueAres.Infrastructure;
using Ares.PagueAres.Infrastructure.Models;
using Ares.PagueAres.Tests.Integration.Infrastructure;
using FluentAssertions.Execution;
using Microsoft.AspNetCore.Hosting;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
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
            StatusExportacao.Concluido, StatusExportacao.Falhou, StatusExportacao.Expirado, StatusExportacao.Cancelado,
            StatusExportacao.Excluido
        ];

        /// <summary>
        /// Tempos curtos da reconciliação periódica, lidos da seção <c>Exportacao</c> como no appsettings: uma volta a
        /// cada 1 s, e um job Processando sem sinal de vida por <see cref="LimiteSemSinalCurto"/> é tido como órfão. São
        /// os menores valores que o worker aceita.
        /// </summary>
        private static readonly Dictionary<string, string?> TemposCurtos = new()
        {
            ["Exportacao:IntervaloReconciliacaoSegundos"] = "1",
            ["Exportacao:LimiteSemSinalDeVidaSegundos"] = "10",
        };

        private static readonly TimeSpan LimiteSemSinalCurto = TimeSpan.FromSeconds(10);

        private readonly InMemoryDatabaseRoot _raiz = new();
        private readonly string _nomeBanco = "ExportacaoWorker_" + Guid.NewGuid();
        private readonly string _pasta =
            Path.Combine(Path.GetTempPath(), "PagueAresTestes", "ExportacaoWorker_" + Guid.NewGuid().ToString("N"));
        private readonly GeradorFalso _gerador = new();
        private readonly MedidorFalso _medidor = new();
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
            File.Exists(outroArquivo).Should().BeTrue("o início do worker não apaga o pacote de um job que ele não encerrou");

            await AguardarStatusAsync(pendenteAntigo, StatusExportacao.Concluido);
            await AguardarStatusAsync(pendenteRecente, StatusExportacao.Concluido);
            _gerador.Chamadas(interrompido).Should().Be(0);
            _gerador.Ordem.Should().Equal([pendenteAntigo, pendenteRecente], "os Pendente voltam à fila por DataCriacao");
        }

        [Fact]
        public async Task Inicio_ProcessandoComPacoteJaPublicado_ViraFalhouEApagaOPacote()
        {
            // O executor publicou o .zip, mas a gravação da conclusão ficou sem resposta e o processo parou: o job
            // continuou Processando com o pacote na pasta. A recuperação do início o encerra e apaga o pacote, que
            // nunca vale para um job Falhou.
            var interrompido = CriarJob(StatusExportacao.Processando, j => j.DataInicio = DateTime.UtcNow.AddMinutes(-5));
            Directory.CreateDirectory(_pasta);
            File.WriteAllText(Final(interrompido), "pacote publicado antes da parada");

            await IniciarProcessoAsync();

            var job = LerJob(interrompido);
            Status(job).Should().Be(StatusExportacao.Falhou);
            job.MensagemErro.Should().Be(MensagemInterrupcao);
            File.Exists(Final(interrompido)).Should().BeFalse("o pacote do job encerrado no início é apagado");
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

        [Fact]
        public async Task JobExcluidoNaFila_NaoEReivindicadoNemGravadoPeloWorker()
        {
            // Um id que ficou na fila de um processo depois de o usuário excluir o pacote em outro.
            var processo = await IniciarProcessoAsync();
            var agora = DateTime.UtcNow;
            var excluido = CriarJob(StatusExportacao.Excluido, j =>
            {
                j.DataConclusao = agora.AddMinutes(-10);
                j.DataExpiracao = agora.AddHours(47);
            });
            var antes = LerJob(excluido);

            processo.Fila.Enfileirar(excluido);
            var sentinela = CriarJob();
            processo.Fila.Enfileirar(sentinela);
            await AguardarStatusAsync(sentinela, StatusExportacao.Concluido);

            _gerador.Chamadas(excluido).Should().Be(0);
            var depois = LerJob(excluido);
            Status(depois).Should().Be(StatusExportacao.Excluido);
            depois.DataExpiracao.Should().Be(antes.DataExpiracao);
            depois.Versao.Should().Be(antes.Versao, "o worker não grava um job Excluido");
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

        [Fact]
        public async Task Limpeza_JobExcluidoComPrazoVencido_NaoEAlterado_EArquivoQueSobrouSaiComoOrfao()
        {
            var processo = NovoProcesso(s => s.RetencaoHoras = 48);
            var agora = DateTime.UtcNow;
            Directory.CreateDirectory(_pasta);

            // O usuário excluiu o pacote, mas o arquivo estava preso e ficou na pasta. O prazo original já venceu:
            // se a limpeza tratasse a Excluída como Concluída, ela viraria Expirada.
            var excluido = CriarJob(StatusExportacao.Excluido, j =>
            {
                j.DataConclusao = agora.AddHours(-50);
                j.DataExpiracao = agora.AddHours(-2);
            });
            AlterarJob(excluido, j => j.NomeArquivo = $"exportacao-{excluido}.zip");
            var antes = LerJob(excluido);
            CriarArquivo(Final(excluido), agora.AddHours(-72));

            await processo.Provedor.GetRequiredService<ExportacaoLimpeza>().LimparAsync(CancellationToken.None);

            File.Exists(Final(excluido)).Should().BeFalse("o arquivo de uma Excluída é órfão e sai depois da retenção");
            var depois = LerJob(excluido);
            Status(depois).Should().Be(StatusExportacao.Excluido, "a limpeza só expira job Concluido");
            depois.DataExpiracao.Should().Be(antes.DataExpiracao, "o \"Expira em\" original fica gravado");
            depois.Versao.Should().Be(antes.Versao, "a limpeza não grava o job Excluido");
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
        // Revisão, achado 2: job preso em Processando (gravação do estado final e reconciliação periódica)
        // -----------------------------------------------------------------------------------------------------

        [Fact]
        public async Task GravacaoDaConclusaoFalhaNasPrimeirasTentativas_RepeteEGravaConcluido()
        {
            var falhas = new FalhaAoGravarStatus(StatusExportacao.Concluido, vezes: 2);
            var processo = await IniciarProcessoAsync(interceptor: falhas);
            var id = CriarJob();
            processo.Fila.Enfileirar(id);

            var job = await AguardarStatusAsync(id, StatusExportacao.Concluido);

            falhas.Disparadas.Should().Be(2, "as duas primeiras gravações da conclusão falham e a terceira vale");
            job.NomeArquivo.Should().Be($"exportacao-{id}.zip");
            File.Exists(Final(id)).Should().BeTrue("o pacote continua publicado depois da gravação repetida");
            job.TamanhoBytes.Should().Be(new FileInfo(Final(id)).Length);
        }

        [Fact]
        public async Task GravacaoDaFalhaFalhaNasPrimeirasTentativas_RepeteEGravaFalhou()
        {
            const string mensagem = "Mensagem fictícia de uma falha prevista do gerador.";
            _gerador.Comportamento = (_, _, _, _) => throw new ExportacaoException(mensagem);
            var falhas = new FalhaAoGravarStatus(StatusExportacao.Falhou, vezes: 2);
            var processo = await IniciarProcessoAsync(interceptor: falhas);
            var id = CriarJob();
            processo.Fila.Enfileirar(id);

            var job = await AguardarStatusAsync(id, StatusExportacao.Falhou);

            falhas.Disparadas.Should().Be(2);
            job.MensagemErro.Should().Be(mensagem);
            File.Exists(Parcial(id)).Should().BeFalse();
        }

        [Fact]
        public async Task ProcessandoSemExecutor_ViraFalhouNaReconciliacao_EComPedidoDeCancelamentoViraCancelado()
        {
            var processo = await IniciarProcessoAsync(configuracao: TemposCurtos);

            // Gravados depois do início do worker: a recuperação do início não os vê, só a reconciliação periódica.
            // Nenhum executor grava neles (é o que sobra quando a gravação do estado final falha de vez).
            var reivindicadoHaPouco = DateTime.UtcNow.AddMinutes(-10);
            var orfao = CriarJob(StatusExportacao.Processando, j => j.DataInicio = reivindicadoHaPouco);
            var orfaoComPedido = CriarJob(StatusExportacao.Processando, j =>
            {
                j.DataInicio = reivindicadoHaPouco;
                j.CancelamentoSolicitado = true;
            });
            Directory.CreateDirectory(_pasta);
            File.WriteAllText(Parcial(orfao), "pacote pela metade de um executor que sumiu");

            var falhou = await AguardarStatusAsync(orfao, StatusExportacao.Falhou);
            falhou.MensagemErro.Should().Be(MensagemInterrupcao);
            await AguardarStatusAsync(orfaoComPedido, StatusExportacao.Cancelado);
            File.Exists(Parcial(orfao)).Should().BeFalse("o pacote incompleto do job órfão é apagado");
            _gerador.Chamadas(orfao).Should().Be(0);
            _gerador.Chamadas(orfaoComPedido).Should().Be(0);
        }

        [Fact]
        public async Task JobEmExecucaoNesteProcesso_NaoEReconciliadoMesmoPassandoOLimite()
        {
            // O executor não consegue dar sinal de vida (toda gravação que não muda o Status falha), então a Versao do
            // job fica parada por mais que o limite: só a guarda "em execução neste processo" impede a reconciliação
            // deste mesmo processo de tomá-lo por órfão.
            _gerador.Segurar = true;
            var semSinalDeVida = new FalhaNoSinalDeVida();
            var processo = await IniciarProcessoAsync(configuracao: TemposCurtos, interceptor: semSinalDeVida);
            var id = CriarJob();
            processo.Fila.Enfileirar(id);
            await _gerador.Iniciou(id).WaitAsync(Prazo);
            var versao = LerJob(id).Versao;

            // Várias voltas da reconciliação, passando do limite sem sinal de vida.
            await Task.Delay(LimiteSemSinalCurto * 1.5);

            semSinalDeVida.Disparadas.Should().BeGreaterThan(0, "o executor tentou dar sinal de vida e a gravação falhou");
            var job = LerJob(id);
            Status(job).Should().Be(StatusExportacao.Processando, "o job está em execução neste processo");
            job.Versao.Should().Be(versao, "sem sinal de vida a Versao do job não muda durante a geração");
            _gerador.Liberar(id);
            await AguardarStatusAsync(id, StatusExportacao.Concluido);
        }

        [Fact]
        public async Task JobEmExecucaoEmOutroProcessoComSinalDeVida_NaoEReconciliado()
        {
            // Dois processos sobre o mesmo banco, os dois no ar antes do job existir (a recuperação do início de um
            // processo que sobe depois encerraria o job do outro, que é o contrato do reinício). Um executa o job e o
            // outro só reconcilia. O gerador não informa progresso: o que impede o segundo de tomar o job por órfão é
            // só o sinal de vida que o executor grava.
            _gerador.Segurar = true;
            var executor = await IniciarProcessoAsync(configuracao: TemposCurtos);
            await IniciarProcessoAsync(configuracao: TemposCurtos);
            var id = CriarJob();
            executor.Fila.Enfileirar(id);
            await _gerador.Iniciou(id).WaitAsync(Prazo);

            await Task.Delay(LimiteSemSinalCurto * 1.5);

            Status(LerJob(id)).Should().Be(StatusExportacao.Processando, "o executor do outro processo continua vivo");
            _gerador.Liberar(id);
            await AguardarStatusAsync(id, StatusExportacao.Concluido);
            _gerador.Chamadas(id).Should().Be(1);
        }

        [Fact]
        public async Task PendenteForaDaFila_EReenfileiradoPelaReconciliacaoEProcessado()
        {
            var processo = await IniciarProcessoAsync(configuracao: TemposCurtos);

            // Pendente gravado depois do início e nunca posto na fila deste processo (por exemplo, a recuperação do
            // início falhou ao reenfileirar).
            var id = CriarJob();

            await AguardarStatusAsync(id, StatusExportacao.Concluido);
            _gerador.Chamadas(id).Should().Be(1);
        }

        [Fact]
        public async Task ReivindicacaoFalha_PendenteVoltaAFilaPelaReconciliacao()
        {
            var falhas = new FalhaAoGravarStatus(StatusExportacao.Processando, vezes: 1);
            var processo = await IniciarProcessoAsync(configuracao: TemposCurtos, interceptor: falhas);
            var id = CriarJob();
            processo.Fila.Enfileirar(id);

            await AguardarStatusAsync(id, StatusExportacao.Concluido);

            falhas.Disparadas.Should().Be(1, "a primeira reivindicação falhou (banco fora do ar) e o job ficou Pendente");
            _gerador.Chamadas(id).Should().Be(1);
        }

        // -----------------------------------------------------------------------------------------------------
        // Reconferência da F2, achado 1: configuração da reconciliação fora da faixa aceita
        // -----------------------------------------------------------------------------------------------------

        [Theory]
        [InlineData("5000000", "NaN", true, true)]
        [InlineData("NaN", "5000000", true, true)]
        [InlineData("60", "NaN", false, true)]
        public async Task ConfiguracaoDaReconciliacaoForaDaFaixa_WorkerProcessaNormalmente_EParaRapido(
            string intervalo, string limite, bool ajustaIntervalo, bool ajustaLimite)
        {
            // 5000000 s passa do maior intervalo que o Task.Delay aceita (cerca de 49,7 dias) e NaN não é um tempo.
            var logs = new ColetorDeAvisos();
            var processo = await IniciarProcessoAsync(configuracao: new Dictionary<string, string?>
            {
                ["Exportacao:IntervaloReconciliacaoSegundos"] = intervalo,
                ["Exportacao:LimiteSemSinalDeVidaSegundos"] = limite,
            }, logs: logs);
            var id = CriarJob();
            processo.Fila.Enfileirar(id);

            await AguardarStatusAsync(id, StatusExportacao.Concluido);

            await processo.Worker.StopAsync(CancellationToken.None).WaitAsync(TimeSpan.FromSeconds(5));
            processo.Worker.ExecuteTask.Should().NotBeNull();
            processo.Worker.ExecuteTask!.IsCompleted.Should().BeTrue("a parada do host encerra o worker, inclusive a reconciliação");
            logs.Avisos.Any(m => m.Contains("Exportacao:IntervaloReconciliacaoSegundos")).Should()
                .Be(ajustaIntervalo, "o ajuste de um valor fora da faixa sai como aviso no log");
            logs.Avisos.Any(m => m.Contains("Exportacao:LimiteSemSinalDeVidaSegundos")).Should()
                .Be(ajustaLimite, "o ajuste de um valor fora da faixa sai como aviso no log");
        }

        // -----------------------------------------------------------------------------------------------------
        // Reconferência da F2, achado 3: resultado desconhecido da gravação da conclusão não apaga o pacote
        // -----------------------------------------------------------------------------------------------------

        [Fact]
        public async Task GravacaoDaConclusaoSemRespostaEmTodasAsTentativas_ComConcluidoNoBanco_MantemOPacote()
        {
            // A primeira gravação da conclusão chega ao banco, mas a resposta se perde (exceção depois de gravar); nas
            // tentativas seguintes o banco nem responde à leitura do job. Para o worker o resultado é desconhecido, e o
            // banco tem Concluido: apagar o pacote deixaria um job Concluído sem arquivo para baixar.
            var respostaPerdida = new ConclusaoSemResposta();
            var processo = await IniciarProcessoAsync(interceptor: respostaPerdida);
            var id = CriarJob();
            respostaPerdida.Alvo = id;
            processo.Fila.Enfileirar(id);

            // Um job por vez: quando a sentinela conclui, o worker já desistiu de gravar a conclusão do primeiro job
            // (são 5 tentativas, com esperas de 1, 2, 4 e 8 s).
            var sentinela = CriarJob();
            processo.Fila.Enfileirar(sentinela);
            await AguardarStatusAsync(sentinela, StatusExportacao.Concluido, TimeSpan.FromSeconds(45));

            respostaPerdida.LeiturasRecusadas.Should().BeGreaterThan(0, "as tentativas seguintes não conseguem ler o job");
            var job = LerJob(id);
            Status(job).Should().Be(StatusExportacao.Concluido, "a primeira gravação chegou ao banco");
            job.NomeArquivo.Should().Be($"exportacao-{id}.zip");
            File.Exists(Final(id)).Should().BeTrue("com o resultado da gravação desconhecido o pacote publicado não é apagado");
            job.TamanhoBytes.Should().Be(new FileInfo(Final(id)).Length);
        }

        [Fact]
        public async Task ConclusaoRecusadaPorqueOJobDeixouDeEstarProcessando_ApagaOPacote()
        {
            // Controle: quando a conclusão é recusada (outro executor encerrou o job durante a geração, por exemplo a
            // recuperação do início de outro processo), o pacote nunca vai valer e é apagado.
            var alvo = 0;
            _gerador.Comportamento = async (idJob, destino, _, ct) =>
            {
                await destino.WriteAsync(new byte[2048], ct);
                if (idJob == Volatile.Read(ref alvo))
                {
                    AlterarJob(idJob, j =>
                    {
                        j.Status = (int)StatusExportacao.Falhou;
                        j.MensagemErro = MensagemInterrupcao;
                        j.DataConclusao = DateTime.UtcNow;
                    });
                }

                return new ResultadoPacoteExportacao(3, 2, 2048);
            };
            var processo = await IniciarProcessoAsync();
            var id = CriarJob();
            Volatile.Write(ref alvo, id);
            processo.Fila.Enfileirar(id);
            var sentinela = CriarJob();
            processo.Fila.Enfileirar(sentinela);

            await AguardarStatusAsync(sentinela, StatusExportacao.Concluido);

            var job = LerJob(id);
            Status(job).Should().Be(StatusExportacao.Falhou, "a conclusão foi recusada e vale o estado gravado pelo outro executor");
            job.MensagemErro.Should().Be(MensagemInterrupcao);
            File.Exists(Final(id)).Should().BeFalse("o pacote de uma conclusão recusada é apagado");
            File.Exists(Parcial(id)).Should().BeFalse();
            File.Exists(Final(sentinela)).Should().BeTrue();
        }

        // -----------------------------------------------------------------------------------------------------
        // Reconferência da F2, achado 4: falha ao encerrar um órfão não recomeça a contagem nem para a volta
        // -----------------------------------------------------------------------------------------------------

        [Fact]
        public async Task EncerramentoDeOrfaoFalhaUmaVez_RepeteNaVoltaSeguinteSemEsperarOLimite_EOutroOrfaoDaMesmaVoltaEEncerrado()
        {
            // Uma volta a cada 2 s e limite de 10 s: "mesma volta" e "volta seguinte" ficam bem separadas no tempo.
            var falhas = new FalhaAoGravarStatus(StatusExportacao.Falhou, vezes: 1);
            await IniciarProcessoAsync(configuracao: new Dictionary<string, string?>
            {
                ["Exportacao:IntervaloReconciliacaoSegundos"] = "2",
                ["Exportacao:LimiteSemSinalDeVidaSegundos"] = "10",
            }, interceptor: falhas);

            // Dois órfãos gravados juntos, depois do início: a reconciliação vê os dois pela primeira vez na mesma volta,
            // e os dois passam do limite na mesma volta. O primeiro encerramento dessa volta falha (banco fora do ar).
            var orfaos = CriarJobs(2, StatusExportacao.Processando, j => j.DataInicio = DateTime.UtcNow.AddMinutes(-10));

            await AguardarAsync(() => falhas.Disparadas == 1, "o encerramento do primeiro órfão deveria falhar uma vez",
                TimeSpan.FromSeconds(30));
            var falhouEm = falhas.MomentoDaPrimeiraFalha!.Value;
            var comFalha = falhas.IdDaPrimeiraFalha!.Value;
            orfaos.Should().Contain(comFalha);
            var outro = orfaos.Single(id => id != comFalha);

            var encerradoComFalha = await AguardarStatusAsync(comFalha, StatusExportacao.Falhou, TimeSpan.FromSeconds(30));
            var encerradoOutro = await AguardarStatusAsync(outro, StatusExportacao.Falhou);

            using (new AssertionScope())
            {
                encerradoOutro.DataConclusao.Should().NotBeNull().And.BeOnOrBefore(falhouEm.AddSeconds(1),
                    "o outro órfão é encerrado na mesma volta em que o encerramento do primeiro falhou");
                encerradoComFalha.DataConclusao.Should().NotBeNull().And.BeOnOrBefore(falhouEm.AddSeconds(5),
                    "o órfão cujo encerramento falhou é encerrado na volta seguinte, sem esperar de novo o limite de 10 s");
                encerradoComFalha.MensagemErro.Should().Be(MensagemInterrupcao);
            }
        }

        // -----------------------------------------------------------------------------------------------------
        // Revisão, achado 5, e reconferência da F2, pergunta 5: pasta obrigatória fora de Development
        // -----------------------------------------------------------------------------------------------------

        [Theory]
        [InlineData("Production")]
        [InlineData("Test")]
        public async Task PastaVaziaForaDeDesenvolvimento_WorkerELimpezaNaoSobem_EJobFicaPendente(string ambiente)
        {
            var processo = NovoProcesso(s => s.Pasta = "", ambiente: ambiente);

            await IniciarAsync(processo.Worker);
            processo.Worker.ExecuteTask.Should()
                .BeNull("sem Exportacao:Pasta fora de Development o worker não pode usar a pasta temporária compartilhada");

            var limpeza = processo.Provedor.GetRequiredService<ExportacaoLimpeza>();
            await IniciarAsync(limpeza);
            limpeza.ExecuteTask.Should().BeNull("a limpeza também não sobe, para não apagar pacotes de outra instância");

            var id = CriarJob();
            processo.Fila.Enfileirar(id);
            await Task.Delay(300);
            Status(LerJob(id)).Should().Be(StatusExportacao.Pendente);
            _gerador.Chamadas(id).Should().Be(0);
        }

        [Theory]
        [InlineData("Development", "", false)]
        [InlineData("Test", "", true)]
        [InlineData("Staging", "", true)]
        [InlineData("Production", "   ", true)]
        [InlineData("Production", @"D:\PagueAres\Exportacao", false)]
        [InlineData("Test", @"D:\PagueAres\Exportacao", false)]
        public void PastaObrigatoriaAusente_SoDevelopmentAceitaPastaVazia(string ambiente, string pasta, bool ausente)
        {
            var settings = new ExportacaoSettings { Pasta = pasta };

            ArquivosExportacao.PastaObrigatoriaAusente(settings, new AmbienteFalso(ambiente)).Should().Be(ausente);
        }

        [Fact]
        public async Task PastaVaziaForaDeDesenvolvimento_ApiRecusaNovaExportacaoCom503()
        {
            using var factory = new FactoryPastaVaziaEmProducao();
            factory.SeedDatabase(ctx =>
            {
                PagueAresWebAppFactory.SeedBaseData(ctx);
                PagueAresWebAppFactory.SeedSolicitacoes(ctx);
                PagueAresWebAppFactory.SeedRddvs(ctx);
            });
            var client = factory.GetAuthenticatedClient(4, "Financeiro", Funcoes.Financeiro);

            var resposta = await client.PostAsJsonAsync("/api/exportacao", new FiltroExportacaoDto());

            resposta.StatusCode.Should().Be(HttpStatusCode.ServiceUnavailable, await resposta.Content.ReadAsStringAsync());
            var envelope = await resposta.Content.ReadFromJsonAsync<GenericResponse<object>>(Json);
            envelope!.Success.Should().BeFalse();
            envelope.Message.Should().NotBeNullOrWhiteSpace().And.Contain("suporte").And.NotContain("Exportacao:Pasta");
            var jobs = -1;
            factory.SeedDatabase(ctx => jobs = ctx.ExportacaoJobs.Count());
            jobs.Should().Be(0, "nenhum job é criado enquanto a pasta não estiver configurada");
        }

        /// <summary>Ambiente Production com <c>Exportacao:Pasta</c> vazio; worker desligado (o teste olha só a API).</summary>
        private sealed class FactoryPastaVaziaEmProducao : PagueAresWebAppFactory
        {
            protected override void ConfigureWebHost(IWebHostBuilder builder)
            {
                base.ConfigureWebHost(builder);
                builder.UseEnvironment(Environments.Production);
                builder.UseSetting("Exportacao:Pasta", "");
                builder.UseSetting("Exportacao:WorkerHabilitado", "false");
            }
        }

        // -----------------------------------------------------------------------------------------------------
        // Revisão, achado 6: espaço em disco conferido durante a geração
        // -----------------------------------------------------------------------------------------------------

        [Fact]
        public async Task EspacoCaiAbaixoDoMinimoDuranteAGeracao_ViraFalhouComMensagemDeEspacoEApagaParcial()
        {
            _gerador.Segurar = true;
            _medidor.BytesLivres = 50L * 1024 * 1024 * 1024; // 50 GB livres no início
            var processo = await IniciarProcessoAsync(s => s.EspacoLivreMinimoMb = 2048);
            var id = CriarJob();
            processo.Fila.Enfileirar(id);
            await _gerador.Iniciou(id).WaitAsync(Prazo);
            File.Exists(Parcial(id)).Should().BeTrue("o gerador já escreveu parte do pacote");

            _medidor.BytesLivres = 100L * 1024 * 1024; // o disco enche durante a geração: sobram 100 MB

            var job = await AguardarStatusAsync(id, StatusExportacao.Falhou);
            job.MensagemErro.Should().Be(ExportacaoWorker.MensagemEspacoInsuficiente);
            File.Exists(Parcial(id)).Should().BeFalse("o pacote parcial é apagado");
            File.Exists(Final(id)).Should().BeFalse();
        }

        [Fact]
        public async Task DiscoCheioDuranteAGeracao_ViraFalhouComMensagemDeEspaco()
        {
            const int erroDiscoCheio = unchecked((int)0x80070070); // ERROR_DISK_FULL
            _gerador.Comportamento = (_, _, _, _) =>
                throw new IOException("Simulação: não há espaço suficiente no disco.", erroDiscoCheio);
            var processo = await IniciarProcessoAsync();
            var id = CriarJob();
            processo.Fila.Enfileirar(id);

            var job = await AguardarStatusAsync(id, StatusExportacao.Falhou);

            job.MensagemErro.Should().Be(ExportacaoWorker.MensagemEspacoInsuficiente);
            File.Exists(Parcial(id)).Should().BeFalse();
        }

        [Fact]
        public async Task EspacoNaoMensuravel_GeracaoSegueEConclui()
        {
            // Compartilhamento de rede (UNC): a medição não é possível e a geração segue, só com aviso no log.
            _medidor.BytesLivres = null;
            var processo = await IniciarProcessoAsync(s => s.EspacoLivreMinimoMb = 2048);
            var id = CriarJob();
            processo.Fila.Enfileirar(id);

            await AguardarStatusAsync(id, StatusExportacao.Concluido);
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

        /// <summary>
        /// Monta um "processo": a seção <c>Exportacao</c> lida de <paramref name="configuracao"/> (como no appsettings),
        /// depois a pasta própria do teste, o espaço mínimo zerado e os ajustes do teste. O ambiente padrão é Production,
        /// com a pasta configurada, como num servidor de produção.
        /// </summary>
        private Processo NovoProcesso(Action<ExportacaoSettings>? ajustar = null,
            IReadOnlyDictionary<string, string?>? configuracao = null, string ambiente = "Production",
            IInterceptor? interceptor = null, ILoggerProvider? logs = null)
        {
            var services = new ServiceCollection();
            services.AddLogging(b =>
            {
                if (logs is not null)
                    b.AddProvider(logs);
            });
            services.AddDbContext<PagueAresContext>(o =>
            {
                o.UseInMemoryDatabase(_nomeBanco, _raiz);
                if (interceptor is not null)
                    o.AddInterceptors(interceptor);
            });
            services.AddSingleton<IGeradorPacoteExportacao>(_gerador);
            services.AddSingleton<IHostEnvironment>(new AmbienteFalso(ambiente));

            var secao = new ConfigurationBuilder()
                .AddInMemoryCollection(configuracao ?? new Dictionary<string, string?>())
                .Build()
                .GetSection("Exportacao");
            services.Configure<ExportacaoSettings>(secao);
            services.Configure<ExportacaoSettings>(s =>
            {
                s.Pasta = _pasta;
                s.EspacoLivreMinimoMb = 0;
                ajustar?.Invoke(s);
            });
            services.AddSingleton<ExportacaoFila>();
            services.AddSingleton<IMedidorEspacoLivre>(_medidor);
            services.AddSingleton<ExportacaoWorker>();
            services.AddSingleton<ExportacaoLimpeza>();

            var provedor = services.BuildServiceProvider();
            _provedores.Add(provedor);
            return new Processo(provedor, provedor.GetRequiredService<ExportacaoFila>(), provedor.GetRequiredService<ExportacaoWorker>());
        }

        private async Task<Processo> IniciarProcessoAsync(Action<ExportacaoSettings>? ajustar = null,
            IReadOnlyDictionary<string, string?>? configuracao = null, IInterceptor? interceptor = null,
            ILoggerProvider? logs = null)
        {
            var processo = NovoProcesso(ajustar, configuracao, interceptor: interceptor, logs: logs);
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

        /// <summary>Cria vários jobs numa só gravação, para que apareçam juntos para quem consulta o banco.</summary>
        private List<int> CriarJobs(int quantidade, StatusExportacao status, Action<ExportacaoJob>? ajustar = null)
        {
            using var ctx = NovoContexto();
            var jobs = Enumerable.Range(0, quantidade).Select(_ =>
            {
                var job = new ExportacaoJob
                {
                    IdUsuario = IdUsuarioFicticio,
                    Status = (int)status,
                    FiltrosJson = JsonSerializer.Serialize(new FiltroExportacaoDto(), Json),
                    DataCriacao = DateTime.UtcNow,
                };
                ajustar?.Invoke(job);
                return job;
            }).ToList();
            ctx.ExportacaoJobs.AddRange(jobs);
            ctx.SaveChanges();
            return jobs.Select(j => j.IdExportacaoJob).ToList();
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

        private async Task<ExportacaoJob> AguardarStatusAsync(int id, StatusExportacao esperado, TimeSpan? prazo = null)
        {
            var limite = DateTime.UtcNow + (prazo ?? Prazo);
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

        private static async Task AguardarAsync(Func<bool> condicao, string motivo, TimeSpan? prazo = null)
        {
            var limite = DateTime.UtcNow + (prazo ?? Prazo);
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

        /// <summary>
        /// Medição de espaço controlada pelo teste. Enquanto <see cref="BytesLivres"/> não for atribuído, mede a unidade
        /// de verdade; depois devolve o valor atribuído (<c>null</c> simula um compartilhamento de rede sem medição).
        /// </summary>
        private sealed class MedidorFalso : IMedidorEspacoLivre
        {
            private readonly MedidorEspacoLivreUnidade _real = new();
            private readonly object _trava = new();
            private bool _simulado;
            private long? _bytesLivres;

            public long? BytesLivres
            {
                get { lock (_trava) return _bytesLivres; }
                set
                {
                    lock (_trava)
                    {
                        _bytesLivres = value;
                        _simulado = true;
                    }
                }
            }

            public long? EspacoLivreBytes(string pasta)
            {
                lock (_trava)
                {
                    if (_simulado)
                        return _bytesLivres;
                }

                return _real.EspacoLivreBytes(pasta);
            }
        }

        private sealed class AmbienteFalso(string nome) : IHostEnvironment
        {
            public string EnvironmentName { get; set; } = nome;
            public string ApplicationName { get; set; } = "Ares.PagueAres.Tests.Integration";
            public string ContentRootPath { get; set; } = AppContext.BaseDirectory;
            public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
        }

        /// <summary>
        /// Simula o banco fora do ar nas <paramref name="vezes"/> primeiras gravações que levam um job ao status
        /// <paramref name="alvo"/>; as seguintes passam. Só vale para os contextos do processo em que foi registrado.
        /// </summary>
        private sealed class FalhaAoGravarStatus(StatusExportacao alvo, int vezes) : SaveChangesInterceptor
        {
            private readonly object _trava = new();
            private int _tentativas;
            private int _disparadas;
            private int? _idDaPrimeiraFalha;
            private DateTime? _momentoDaPrimeiraFalha;

            public int Disparadas => Volatile.Read(ref _disparadas);

            /// <summary>Job da primeira gravação que falhou.</summary>
            public int? IdDaPrimeiraFalha { get { lock (_trava) return _idDaPrimeiraFalha; } }

            /// <summary>Momento (UTC) da primeira gravação que falhou.</summary>
            public DateTime? MomentoDaPrimeiraFalha { get { lock (_trava) return _momentoDaPrimeiraFalha; } }

            public override InterceptionResult<int> SavingChanges(DbContextEventData eventData, InterceptionResult<int> result)
            {
                Verificar(eventData.Context);
                return base.SavingChanges(eventData, result);
            }

            public override ValueTask<InterceptionResult<int>> SavingChangesAsync(DbContextEventData eventData,
                InterceptionResult<int> result, CancellationToken cancellationToken = default)
            {
                Verificar(eventData.Context);
                return base.SavingChangesAsync(eventData, result, cancellationToken);
            }

            private void Verificar(DbContext? contexto)
            {
                var entrada = contexto?.ChangeTracker.Entries<ExportacaoJob>().FirstOrDefault(e =>
                    e.State == EntityState.Modified
                    && e.Property(j => j.Status).IsModified
                    && e.Entity.Status == (int)alvo);
                if (entrada is null || Interlocked.Increment(ref _tentativas) > vezes)
                    return;

                lock (_trava)
                {
                    if (_idDaPrimeiraFalha is null)
                    {
                        _idDaPrimeiraFalha = entrada.Entity.IdExportacaoJob;
                        _momentoDaPrimeiraFalha = DateTime.UtcNow;
                    }
                }

                Interlocked.Increment(ref _disparadas);
                throw new DbUpdateException("Simulação: o banco de dados está fora do ar.");
            }
        }

        /// <summary>
        /// Simula o banco recusando as gravações de job que não mudam o <c>Status</c>, ou seja, o progresso e o sinal de
        /// vida do executor; a reivindicação e os estados finais passam. Sem sinal de vida, a <c>Versao</c> do job em
        /// execução fica parada.
        /// </summary>
        private sealed class FalhaNoSinalDeVida : SaveChangesInterceptor
        {
            private int _disparadas;

            public int Disparadas => Volatile.Read(ref _disparadas);

            public override InterceptionResult<int> SavingChanges(DbContextEventData eventData, InterceptionResult<int> result)
            {
                Verificar(eventData.Context);
                return base.SavingChanges(eventData, result);
            }

            public override ValueTask<InterceptionResult<int>> SavingChangesAsync(DbContextEventData eventData,
                InterceptionResult<int> result, CancellationToken cancellationToken = default)
            {
                Verificar(eventData.Context);
                return base.SavingChangesAsync(eventData, result, cancellationToken);
            }

            private void Verificar(DbContext? contexto)
            {
                var semMudarStatus = contexto?.ChangeTracker.Entries<ExportacaoJob>().Any(e =>
                    e.State == EntityState.Modified && !e.Property(j => j.Status).IsModified) == true;
                if (!semMudarStatus)
                    return;

                Interlocked.Increment(ref _disparadas);
                throw new DbUpdateException("Simulação: o banco recusou a gravação do sinal de vida.");
            }
        }

        /// <summary>
        /// Simula a resposta perdida da gravação que conclui o job <see cref="Alvo"/>: a gravação chega ao banco, mas o
        /// worker recebe uma exceção; dali em diante toda leitura desse job neste processo falha, como com o banco fora
        /// do ar. As leituras feitas pelo teste (contextos sem o interceptor) continuam funcionando.
        /// </summary>
        private sealed class ConclusaoSemResposta : SaveChangesInterceptor, IMaterializationInterceptor
        {
            private int _alvo;
            private int _bancoFora;
            private int _leiturasRecusadas;

            public int Alvo
            {
                get => Volatile.Read(ref _alvo);
                set => Volatile.Write(ref _alvo, value);
            }

            public int LeiturasRecusadas => Volatile.Read(ref _leiturasRecusadas);

            public override int SavedChanges(SaveChangesCompletedEventData eventData, int result)
            {
                Verificar(eventData.Context);
                return base.SavedChanges(eventData, result);
            }

            public override ValueTask<int> SavedChangesAsync(SaveChangesCompletedEventData eventData, int result,
                CancellationToken cancellationToken = default)
            {
                Verificar(eventData.Context);
                return base.SavedChangesAsync(eventData, result, cancellationToken);
            }

            public object InitializedInstance(MaterializationInterceptionData materializationData, object entity)
            {
                if (Volatile.Read(ref _bancoFora) == 1 && entity is ExportacaoJob job && job.IdExportacaoJob == Alvo)
                {
                    Interlocked.Increment(ref _leiturasRecusadas);
                    throw new InvalidOperationException("Simulação: o banco de dados não responde.");
                }

                return entity;
            }

            private void Verificar(DbContext? contexto)
            {
                var concluiu = contexto?.ChangeTracker.Entries<ExportacaoJob>().Any(e =>
                    e.Entity.IdExportacaoJob == Alvo && e.Entity.Status == (int)StatusExportacao.Concluido) == true;
                if (!concluiu || Interlocked.Exchange(ref _bancoFora, 1) == 1)
                    return;

                throw new DbUpdateException("Simulação: a gravação chegou ao banco, mas a resposta se perdeu.");
            }
        }

        /// <summary>Guarda as mensagens de aviso do log (até um limite, para não crescer sem fim).</summary>
        private sealed class ColetorDeAvisos : ILoggerProvider
        {
            private const int Maximo = 1000;
            private readonly ConcurrentQueue<string> _avisos = new();

            public IReadOnlyList<string> Avisos => [.. _avisos];

            public ILogger CreateLogger(string categoria) => new Registrador(this);

            public void Dispose()
            {
                // Compartilhado entre os processos do teste: nada a liberar.
            }

            private sealed class Registrador(ColetorDeAvisos coletor) : ILogger
            {
                public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

                public bool IsEnabled(LogLevel logLevel) => logLevel == LogLevel.Warning;

                public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception,
                    Func<TState, Exception?, string> formatter)
                {
                    if (logLevel == LogLevel.Warning && coletor._avisos.Count < Maximo)
                        coletor._avisos.Enqueue(formatter(state, exception));
                }
            }
        }
    }
}
