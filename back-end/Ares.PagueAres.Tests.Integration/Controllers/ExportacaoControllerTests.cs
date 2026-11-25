using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Ares.PagueAres.Application.Exportacao;
using Ares.PagueAres.Domain;
using Ares.PagueAres.Domain.Dtos.Exportacao;
using Ares.PagueAres.Domain.Enums;
using Ares.PagueAres.Infrastructure.Models;
using Ares.PagueAres.Tests.Integration.Infrastructure;
using Microsoft.AspNetCore.Hosting;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Ares.PagueAres.Tests.Integration.Controllers
{
    /// <summary>
    /// API de exportação de títulos com anexos (contrato C8): papel FIN/ADM vindo do JWT, posse do job pelo usuário do
    /// token, limites de criação e download por token de uso único.
    /// <para>
    /// Os testes rodam com o worker desligado (<c>Exportacao:WorkerHabilitado = false</c>): os jobs criados pela API
    /// ficam Pendente, e os demais estados são semeados direto no banco InMemory, com o pacote do job Concluido gravado
    /// à mão na pasta de exportação da factory. Cada teste usa usuários fictícios próprios, para que os jobs de um não
    /// contem no limite de jobs ativos de outro.
    /// </para>
    /// </summary>
    public class ExportacaoControllerTests : IClassFixture<ExportacaoControllerTests.FabricaSemWorker>
    {
        private const string Rota = "/api/exportacao";
        private const string RotaDownload = "/api/exportacao/download";

        private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

        // Ids dos usuários fictícios criados pelos testes (os de SeedBaseData vão de 1 a 7).
        private static int _ultimoUsuario = 1000;

        private readonly FabricaSemWorker _factory;

        public ExportacaoControllerTests(FabricaSemWorker factory)
        {
            _factory = factory;
            PrepararBanco(_factory);
        }

        // ─── Fábricas ────────────────────────────────────────────────────────────

        /// <summary>Factory com o worker e a limpeza desligados: os jobs só mudam de estado pela API ou pelo teste.</summary>
        public class FabricaSemWorker : PagueAresWebAppFactory
        {
            protected override void ConfigureWebHost(IWebHostBuilder builder)
            {
                base.ConfigureWebHost(builder);
                builder.UseSetting("Exportacao:WorkerHabilitado", "false");
            }
        }

        /// <summary>Link de download válido por 1 s e no máximo 1 título por exportação.</summary>
        public class FabricaLimitesCurtos : FabricaSemWorker
        {
            protected override void ConfigureWebHost(IWebHostBuilder builder)
            {
                base.ConfigureWebHost(builder);
                builder.UseSetting("Exportacao:ValidadeLinkDownloadSegundos", "1");
                builder.UseSetting("Exportacao:MaxTitulosPorExportacao", "1");
            }
        }

        // ─── Controle da infraestrutura ──────────────────────────────────────────

        [Fact]
        [Trait("Category", "Controle")]
        public void Fabrica_ComWorkerDesligado_NaoSobeWorkerNemLimpeza()
        {
            var servicos = _factory.Services.GetServices<IHostedService>().ToList();

            servicos.OfType<ExportacaoWorker>().Should().BeEmpty();
            servicos.OfType<ExportacaoLimpeza>().Should().BeEmpty();
        }

        // ─── 401 sem token ───────────────────────────────────────────────────────

        public static TheoryData<string, string> RotasAutenticadas => new()
        {
            { "POST", Rota },
            { "GET", Rota },
            { "GET", Rota + "/1" },
            { "POST", Rota + "/1/link-download" },
            { "DELETE", Rota + "/1" },
        };

        [Theory]
        [MemberData(nameof(RotasAutenticadas))]
        public async Task SemToken_RotaAutenticada_Responde401(string metodo, string rota)
        {
            var client = _factory.CreateClient();

            var resposta = await client.SendAsync(NovaRequisicao(metodo, rota));

            resposta.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        }

        [Theory]
        [InlineData(RotaDownload)]
        [InlineData(RotaDownload + "?token=")]
        public async Task Download_SemToken_Responde401(string rota)
        {
            var client = _factory.CreateClient();

            var resposta = await client.GetAsync(rota);

            await AfirmarErroAsync(resposta, HttpStatusCode.Unauthorized);
        }

        // ─── 403 para SOL, GES e CON ─────────────────────────────────────────────

        public static TheoryData<string, string> PapeisSemPermissaoERotas()
        {
            var dados = new TheoryData<string, string>();
            foreach (var papel in new[] { Funcoes.Solicitante, Funcoes.Gestor, Funcoes.Contabilidade })
            foreach (var rota in new[] { "POST lista", "GET lista", "GET id", "POST link-download", "DELETE id" })
                dados.Add(papel, rota);
            return dados;
        }

        [Theory]
        [MemberData(nameof(PapeisSemPermissaoERotas))]
        public async Task PapelSemFinOuAdm_Responde403ENaoAlteraNada(string papel, string rota)
        {
            // Os jobs são do próprio usuário: sem a checagem de papel, todas as rotas responderiam com sucesso.
            var idUsuario = NovoUsuario(_factory);
            var concluido = SemearConcluidoComPacote(_factory, idUsuario, out _);
            var pendente = SemearJob(_factory, idUsuario, StatusExportacao.Pendente);
            var client = _factory.GetAuthenticatedClient(idUsuario, "Usuário sem papel", papel);

            var resposta = rota switch
            {
                "POST lista" => await client.PostAsJsonAsync(Rota, new FiltroExportacaoDto()),
                "GET lista" => await client.GetAsync(Rota),
                "GET id" => await client.GetAsync($"{Rota}/{concluido}"),
                "POST link-download" => await client.PostAsync($"{Rota}/{concluido}/link-download", null),
                "DELETE id" => await client.DeleteAsync($"{Rota}/{pendente}"),
                _ => throw new ArgumentOutOfRangeException(nameof(rota)),
            };

            await AfirmarErroAsync(resposta, HttpStatusCode.Forbidden);
            JobsDoUsuario(_factory, idUsuario).Should().HaveCount(2, "nenhum job pode ser criado");
            Status(LerJob(_factory, pendente)).Should().Be(StatusExportacao.Pendente);
            Status(LerJob(_factory, concluido)).Should().Be(StatusExportacao.Concluido);
        }

        // ─── Criação ─────────────────────────────────────────────────────────────

        [Theory]
        [InlineData(Funcoes.Financeiro)]
        [InlineData(Funcoes.Administrador)]
        public async Task FinOuAdm_Post_Responde202ComJobPendente(string papel)
        {
            var idUsuario = NovoUsuario(_factory);
            var client = _factory.GetAuthenticatedClient(idUsuario, "Usuário " + papel, papel);

            var resposta = await client.PostAsJsonAsync(Rota, new FiltroExportacaoDto { TipoTitulo = TipoTituloExportacao.Solicitacao });

            resposta.StatusCode.Should().Be(HttpStatusCode.Accepted, await resposta.Content.ReadAsStringAsync());
            var envelope = await resposta.Content.ReadFromJsonAsync<GenericResponse<ExportacaoJobDto>>(Json);
            envelope!.Success.Should().BeTrue();
            envelope.Data!.Id.Should().BePositive();
            envelope.Data.Status.Should().Be(StatusExportacao.Pendente);
            envelope.Data.StatusDescricao.Should().Be("Pendente");
            envelope.Data.Filtros.TipoTitulo.Should().Be(TipoTituloExportacao.Solicitacao);

            var job = LerJob(_factory, envelope.Data.Id);
            Status(job).Should().Be(StatusExportacao.Pendente);
            job.IdUsuario.Should().Be(idUsuario);
            job.DataCriacao.Should().BeCloseTo(DateTime.UtcNow, TimeSpan.FromMinutes(1));
        }

        [Theory]
        [InlineData("idUsuario")]
        [InlineData("userId")]
        public async Task Post_IdentidadeNoCorpoOuNaQuery_EIgnorada_JobGravadoComOIdDoJwt(string campo)
        {
            var idUsuario = NovoUsuario(_factory);
            var client = _factory.GetAuthenticatedClient(idUsuario, "Financeiro", Funcoes.Financeiro);
            var corpo = new StringContent($"{{\"tipoTitulo\":0,\"incluirAnexos\":true,\"{campo}\":999}}",
                Encoding.UTF8, "application/json");

            var resposta = await client.PostAsync($"{Rota}?{campo}=999", corpo);

            resposta.StatusCode.Should().Be(HttpStatusCode.Accepted, await resposta.Content.ReadAsStringAsync());
            var envelope = await resposta.Content.ReadFromJsonAsync<GenericResponse<ExportacaoJobDto>>(Json);
            LerJob(_factory, envelope!.Data!.Id).IdUsuario.Should().Be(idUsuario);
            JobsDoUsuario(_factory, 999).Should().BeEmpty();
        }

        [Fact]
        public async Task Post_TerceiroJobAtivoDoMesmoUsuario_Responde429()
        {
            var idUsuario = NovoUsuario(_factory);
            // Um job já encerrado não conta no limite de jobs ativos (Pendente + Processando).
            SemearConcluidoComPacote(_factory, idUsuario, out _);
            var client = _factory.GetAuthenticatedClient(idUsuario, "Financeiro", Funcoes.Financeiro);

            (await client.PostAsJsonAsync(Rota, new FiltroExportacaoDto())).StatusCode.Should().Be(HttpStatusCode.Accepted);
            (await client.PostAsJsonAsync(Rota, new FiltroExportacaoDto())).StatusCode.Should().Be(HttpStatusCode.Accepted);
            var terceira = await client.PostAsJsonAsync(Rota, new FiltroExportacaoDto());

            await AfirmarErroAsync(terceira, HttpStatusCode.TooManyRequests);
            JobsDoUsuario(_factory, idUsuario).Should().HaveCount(3, "o concluído e os dois pendentes; o terceiro não é gravado");
        }

        public static TheoryData<string> PeriodosInvertidos => new()
        {
            "{\"dataSolicitacaoInicio\":\"2026-11-20\",\"dataSolicitacaoFim\":\"2026-11-10\"}",
            "{\"dataVencimentoInicio\":\"2026-11-20\",\"dataVencimentoFim\":\"2026-11-19\"}",
            "{\"dataAprovacaoFinanceiroInicio\":\"2026-02-01\",\"dataAprovacaoFinanceiroFim\":\"2026-01-31\"}",
        };

        [Theory]
        [MemberData(nameof(PeriodosInvertidos))]
        public async Task Post_FiltroComFimAntesDoInicio_Responde400(string filtroJson)
        {
            var idUsuario = NovoUsuario(_factory);
            var client = _factory.GetAuthenticatedClient(idUsuario, "Financeiro", Funcoes.Financeiro);

            var resposta = await client.PostAsync(Rota, new StringContent(filtroJson, Encoding.UTF8, "application/json"));

            await AfirmarErroAsync(resposta, HttpStatusCode.BadRequest);
            JobsDoUsuario(_factory, idUsuario).Should().BeEmpty();
        }

        [Fact]
        public async Task Post_FiltroSemTitulos_Responde400()
        {
            var idUsuario = NovoUsuario(_factory);
            var client = _factory.GetAuthenticatedClient(idUsuario, "Financeiro", Funcoes.Financeiro);

            var resposta = await client.PostAsJsonAsync(Rota, new FiltroExportacaoDto { Codigo = 987654 });

            await AfirmarErroAsync(resposta, HttpStatusCode.BadRequest);
            JobsDoUsuario(_factory, idUsuario).Should().BeEmpty();
        }

        [Theory]
        [InlineData("{\"tipoTitulo\":9}")]
        [InlineData("{\"tipoTitulo\":\"nao-existe\"}")]
        [InlineData("{\"codigo\":\"abc\"}")]
        [InlineData("{ isto não é json")]
        [InlineData("")]
        public async Task Post_CorpoInvalido_Responde400SemTextoDeExcecao(string corpo)
        {
            var idUsuario = NovoUsuario(_factory);
            var client = _factory.GetAuthenticatedClient(idUsuario, "Financeiro", Funcoes.Financeiro);

            var resposta = await client.PostAsync(Rota, new StringContent(corpo, Encoding.UTF8, "application/json"));

            await AfirmarErroAsync(resposta, HttpStatusCode.BadRequest);
            JobsDoUsuario(_factory, idUsuario).Should().BeEmpty();
        }

        // ─── Consulta e posse ────────────────────────────────────────────────────

        [Fact]
        public async Task GetLista_SoOsJobsDoProprioUsuarioDosUltimos30Dias_MaisRecentesPrimeiro()
        {
            var idFinanceiro = NovoUsuario(_factory);
            var idAdministrador = NovoUsuario(_factory);
            var agora = DateTime.UtcNow;
            var antigo = SemearJob(_factory, idFinanceiro, StatusExportacao.Falhou, j => j.DataCriacao = agora.AddDays(-31));
            var doisDias = SemearJob(_factory, idFinanceiro, StatusExportacao.Cancelado, j => j.DataCriacao = agora.AddDays(-2));
            var hoje = SemearJob(_factory, idFinanceiro, StatusExportacao.Pendente, j => j.DataCriacao = agora.AddMinutes(-5));
            var doAdministrador = SemearJob(_factory, idAdministrador, StatusExportacao.Pendente);
            var client = _factory.GetAuthenticatedClient(idFinanceiro, "Financeiro", Funcoes.Financeiro);

            var resposta = await client.GetAsync(Rota);

            resposta.StatusCode.Should().Be(HttpStatusCode.OK);
            var envelope = await resposta.Content.ReadFromJsonAsync<GenericResponse<List<ExportacaoJobDto>>>(Json);
            envelope!.Success.Should().BeTrue();
            envelope.Data!.Select(j => j.Id).Should().Equal(hoje, doisDias);
            envelope.Data.Select(j => j.Id).Should().NotContain(new[] { antigo, doAdministrador });
        }

        [Fact]
        public async Task GetPorId_DoProprioJob_Responde200ComPercentual()
        {
            var idUsuario = NovoUsuario(_factory);
            var id = SemearJob(_factory, idUsuario, StatusExportacao.Processando, j =>
            {
                j.TotalTitulos = 8;
                j.TitulosProcessados = 2;
            });
            var client = _factory.GetAuthenticatedClient(idUsuario, "Administrador", Funcoes.Administrador);

            var resposta = await client.GetAsync($"{Rota}/{id}");

            resposta.StatusCode.Should().Be(HttpStatusCode.OK);
            var envelope = await resposta.Content.ReadFromJsonAsync<GenericResponse<ExportacaoJobDto>>(Json);
            envelope!.Data!.Id.Should().Be(id);
            envelope.Data.StatusDescricao.Should().Be("Em processamento");
            envelope.Data.Percentual.Should().Be(25);
        }

        [Theory]
        [InlineData("GET id")]
        [InlineData("POST link-download")]
        [InlineData("DELETE id")]
        public async Task JobDeOutroUsuario_Responde404ENaoAlteraNada(string rota)
        {
            var idDono = NovoUsuario(_factory);
            var idOutro = NovoUsuario(_factory);
            var id = SemearConcluidoComPacote(_factory, idDono, out var caminho);
            // O outro usuário também é ADM: o papel não dá acesso a job alheio.
            var client = _factory.GetAuthenticatedClient(idOutro, "Outro administrador", Funcoes.Administrador, Funcoes.Financeiro);

            var resposta = rota switch
            {
                "GET id" => await client.GetAsync($"{Rota}/{id}"),
                "POST link-download" => await client.PostAsync($"{Rota}/{id}/link-download", null),
                "DELETE id" => await client.DeleteAsync($"{Rota}/{id}"),
                _ => throw new ArgumentOutOfRangeException(nameof(rota)),
            };

            await AfirmarErroAsync(resposta, HttpStatusCode.NotFound);
            Status(LerJob(_factory, id)).Should().Be(StatusExportacao.Concluido);
            File.Exists(caminho).Should().BeTrue();
        }

        [Fact]
        public async Task GetPorId_JobInexistente_Responde404()
        {
            var idUsuario = NovoUsuario(_factory);
            var client = _factory.GetAuthenticatedClient(idUsuario, "Financeiro", Funcoes.Financeiro);

            var resposta = await client.GetAsync($"{Rota}/987654321");

            await AfirmarErroAsync(resposta, HttpStatusCode.NotFound);
        }

        // ─── Link de download ────────────────────────────────────────────────────

        [Theory]
        [InlineData(StatusExportacao.Pendente)]
        [InlineData(StatusExportacao.Processando)]
        [InlineData(StatusExportacao.Falhou)]
        [InlineData(StatusExportacao.Cancelado)]
        public async Task LinkDownload_DeJobNaoConcluido_Responde409(StatusExportacao status)
        {
            var idUsuario = NovoUsuario(_factory);
            var id = SemearJob(_factory, idUsuario, status);
            var client = _factory.GetAuthenticatedClient(idUsuario, "Financeiro", Funcoes.Financeiro);

            var resposta = await client.PostAsync($"{Rota}/{id}/link-download", null);

            await AfirmarErroAsync(resposta, HttpStatusCode.Conflict);
        }

        [Fact]
        public async Task LinkDownload_DeJobExpirado_Responde410()
        {
            var idUsuario = NovoUsuario(_factory);
            var id = SemearJob(_factory, idUsuario, StatusExportacao.Expirado, j =>
            {
                j.NomeArquivo = ArquivosExportacao.NomeArquivo(j.IdExportacaoJob);
                j.DataConclusao = DateTime.UtcNow.AddDays(-3);
                j.DataExpiracao = DateTime.UtcNow.AddDays(-1);
            });
            var client = _factory.GetAuthenticatedClient(idUsuario, "Financeiro", Funcoes.Financeiro);

            var resposta = await client.PostAsync($"{Rota}/{id}/link-download", null);

            await AfirmarErroAsync(resposta, HttpStatusCode.Gone);
        }

        [Fact]
        public async Task LinkDownload_DeConcluidoComPrazoVencidoOuSemArquivo_Responde410()
        {
            var idUsuario = NovoUsuario(_factory);
            var vencido = SemearConcluidoComPacote(_factory, idUsuario, out _, j => j.DataExpiracao = DateTime.UtcNow.AddMinutes(-1));
            var semArquivo = SemearConcluidoComPacote(_factory, idUsuario, out var caminho);
            File.Delete(caminho);
            var client = _factory.GetAuthenticatedClient(idUsuario, "Financeiro", Funcoes.Financeiro);

            await AfirmarErroAsync(await client.PostAsync($"{Rota}/{vencido}/link-download", null), HttpStatusCode.Gone);
            await AfirmarErroAsync(await client.PostAsync($"{Rota}/{semArquivo}/link-download", null), HttpStatusCode.Gone);
        }

        [Fact]
        public async Task LinkDownload_DeConcluido_DevolveTokenDe32BytesComValidadeConfigurada()
        {
            var idUsuario = NovoUsuario(_factory);
            var id = SemearConcluidoComPacote(_factory, idUsuario, out _);
            var client = _factory.GetAuthenticatedClient(idUsuario, "Financeiro", Funcoes.Financeiro);

            var link = await GerarLinkAsync(client, id);

            link.Token.Should().MatchRegex("^[A-Za-z0-9_-]{43}$", "32 bytes em base64url, sem preenchimento");
            link.ExpiraEm.Should().BeCloseTo(DateTime.UtcNow.AddSeconds(120), TimeSpan.FromSeconds(15));
        }

        // ─── Download ────────────────────────────────────────────────────────────

        [Fact]
        public async Task Download_TokenValido_Responde200EmZipComNomeDoContratoECorpoIgualAoArquivo()
        {
            var idUsuario = NovoUsuario(_factory);
            var conclusao = new DateTime(2026, 11, 25, 17, 4, 0, DateTimeKind.Utc);
            var id = SemearConcluidoComPacote(_factory, idUsuario, out var caminho, j => j.DataConclusao = conclusao);
            var client = _factory.GetAuthenticatedClient(idUsuario, "Financeiro", Funcoes.Financeiro);
            var link = await GerarLinkAsync(client, id);

            // O download é uma navegação do navegador: sem o cabeçalho Authorization.
            var resposta = await _factory.CreateClient().GetAsync($"{RotaDownload}?token={Uri.EscapeDataString(link.Token)}");

            resposta.StatusCode.Should().Be(HttpStatusCode.OK);
            resposta.Content.Headers.ContentType!.MediaType.Should().Be("application/zip");
            var disposicao = resposta.Content.Headers.ContentDisposition!;
            disposicao.DispositionType.Should().Be("attachment");
            var nomeEsperado = $"exportacao-titulos-{id}-{conclusao.ToLocalTime():yyyyMMdd-HHmm}.zip";
            (disposicao.FileNameStar ?? disposicao.FileName!.Trim('"')).Should().Be(nomeEsperado);
            resposta.Headers.AcceptRanges.Should().BeEmpty("o download não aceita intervalos");
            (await resposta.Content.ReadAsByteArrayAsync()).Should().Equal(File.ReadAllBytes(caminho));
        }

        [Fact]
        public async Task Download_TokenUsadoDuasVezes_200EDepois401()
        {
            var idUsuario = NovoUsuario(_factory);
            var id = SemearConcluidoComPacote(_factory, idUsuario, out _);
            var client = _factory.GetAuthenticatedClient(idUsuario, "Financeiro", Funcoes.Financeiro);
            var link = await GerarLinkAsync(client, id);
            var anonimo = _factory.CreateClient();

            var primeira = await anonimo.GetAsync($"{RotaDownload}?token={Uri.EscapeDataString(link.Token)}");
            var segunda = await anonimo.GetAsync($"{RotaDownload}?token={Uri.EscapeDataString(link.Token)}");

            primeira.StatusCode.Should().Be(HttpStatusCode.OK);
            await AfirmarErroAsync(segunda, HttpStatusCode.Unauthorized);
        }

        [Fact]
        public async Task Download_DuasRequisicoesSimultaneasComOMesmoToken_ExatamenteUmaResponde200()
        {
            var idUsuario = NovoUsuario(_factory);
            var id = SemearConcluidoComPacote(_factory, idUsuario, out _);
            var client = _factory.GetAuthenticatedClient(idUsuario, "Financeiro", Funcoes.Financeiro);
            var anonimo = _factory.CreateClient();

            // Várias rodadas aumentam a chance de as duas requisições consultarem o token ao mesmo tempo.
            for (var rodada = 0; rodada < 10; rodada++)
            {
                var link = await GerarLinkAsync(client, id);
                var url = $"{RotaDownload}?token={Uri.EscapeDataString(link.Token)}";

                var respostas = await Task.WhenAll(
                    Task.Run(() => anonimo.GetAsync(url)),
                    Task.Run(() => anonimo.GetAsync(url)));

                respostas.Count(r => r.StatusCode == HttpStatusCode.OK).Should().Be(1, $"rodada {rodada}");
                respostas.Count(r => r.StatusCode == HttpStatusCode.Unauthorized).Should().Be(1, $"rodada {rodada}");
            }
        }

        [Fact]
        public async Task Download_TokenInventado_Responde401()
        {
            var anonimo = _factory.CreateClient();
            var inventado = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32))
                .TrimEnd('=').Replace('+', '-').Replace('/', '_');

            await AfirmarErroAsync(await anonimo.GetAsync($"{RotaDownload}?token={inventado}"), HttpStatusCode.Unauthorized);
            await AfirmarErroAsync(await anonimo.GetAsync($"{RotaDownload}?token=..%2F..%2Fexportacao-1.zip"), HttpStatusCode.Unauthorized);
        }

        [Fact]
        public async Task Download_JobExpiradoDepoisDoLink_Responde410()
        {
            var idUsuario = NovoUsuario(_factory);
            var id = SemearConcluidoComPacote(_factory, idUsuario, out var caminho);
            var client = _factory.GetAuthenticatedClient(idUsuario, "Financeiro", Funcoes.Financeiro);
            var link = await GerarLinkAsync(client, id);
            AlterarJob(_factory, id, j => j.Status = (int)StatusExportacao.Expirado);
            File.Delete(caminho);

            var resposta = await _factory.CreateClient().GetAsync($"{RotaDownload}?token={Uri.EscapeDataString(link.Token)}");

            await AfirmarErroAsync(resposta, HttpStatusCode.Gone);
        }

        // ─── Cancelamento e exclusão ─────────────────────────────────────────────

        [Fact]
        public async Task Delete_DePendente_Responde204EJobCancelado()
        {
            var idUsuario = NovoUsuario(_factory);
            var id = SemearJob(_factory, idUsuario, StatusExportacao.Pendente);
            var client = _factory.GetAuthenticatedClient(idUsuario, "Financeiro", Funcoes.Financeiro);

            var resposta = await client.DeleteAsync($"{Rota}/{id}");

            resposta.StatusCode.Should().Be(HttpStatusCode.NoContent);
            (await resposta.Content.ReadAsByteArrayAsync()).Should().BeEmpty();
            Status(LerJob(_factory, id)).Should().Be(StatusExportacao.Cancelado);
        }

        [Fact]
        public async Task Delete_DeProcessando_Responde204EPedeOCancelamento()
        {
            var idUsuario = NovoUsuario(_factory);
            var id = SemearJob(_factory, idUsuario, StatusExportacao.Processando);
            var client = _factory.GetAuthenticatedClient(idUsuario, "Financeiro", Funcoes.Financeiro);

            var resposta = await client.DeleteAsync($"{Rota}/{id}");

            resposta.StatusCode.Should().Be(HttpStatusCode.NoContent);
            var job = LerJob(_factory, id);
            job.CancelamentoSolicitado.Should().BeTrue();
            Status(job).Should().Be(StatusExportacao.Processando, "quem grava Cancelado é o worker, quando para");
        }

        [Fact]
        public async Task Delete_DeConcluido_Responde204ApagaOArquivoEExpira()
        {
            var idUsuario = NovoUsuario(_factory);
            var id = SemearConcluidoComPacote(_factory, idUsuario, out var caminho);
            var client = _factory.GetAuthenticatedClient(idUsuario, "Financeiro", Funcoes.Financeiro);

            var resposta = await client.DeleteAsync($"{Rota}/{id}");

            resposta.StatusCode.Should().Be(HttpStatusCode.NoContent);
            Status(LerJob(_factory, id)).Should().Be(StatusExportacao.Expirado);
            File.Exists(caminho).Should().BeFalse();
        }

        [Theory]
        [InlineData(StatusExportacao.Falhou)]
        [InlineData(StatusExportacao.Expirado)]
        [InlineData(StatusExportacao.Cancelado)]
        public async Task Delete_DeJobJaEncerrado_Responde409(StatusExportacao status)
        {
            var idUsuario = NovoUsuario(_factory);
            var id = SemearJob(_factory, idUsuario, status);
            var client = _factory.GetAuthenticatedClient(idUsuario, "Financeiro", Funcoes.Financeiro);

            var resposta = await client.DeleteAsync($"{Rota}/{id}");

            await AfirmarErroAsync(resposta, HttpStatusCode.Conflict);
            Status(LerJob(_factory, id)).Should().Be(status);
        }

        // ─── Validade curta e limite de títulos ──────────────────────────────────

        /// <summary>Cenários que dependem de uma configuração própria (validade do link de 1 s e no máximo 1 título).</summary>
        public class ComLimitesCurtos : IClassFixture<FabricaLimitesCurtos>
        {
            private readonly FabricaLimitesCurtos _factory;

            public ComLimitesCurtos(FabricaLimitesCurtos factory)
            {
                _factory = factory;
                PrepararBanco(_factory);
            }

            [Fact]
            public async Task Download_TokenVencido_Responde401()
            {
                var idUsuario = NovoUsuario(_factory);
                var id = SemearConcluidoComPacote(_factory, idUsuario, out _);
                var client = _factory.GetAuthenticatedClient(idUsuario, "Financeiro", Funcoes.Financeiro);
                var link = await GerarLinkAsync(client, id);

                await Task.Delay(TimeSpan.FromSeconds(1.5));
                var resposta = await _factory.CreateClient().GetAsync($"{RotaDownload}?token={Uri.EscapeDataString(link.Token)}");

                await AfirmarErroAsync(resposta, HttpStatusCode.Unauthorized);
            }

            [Fact]
            public async Task Post_MaisTitulosQueOLimite_Responde400()
            {
                var idUsuario = NovoUsuario(_factory);
                var client = _factory.GetAuthenticatedClient(idUsuario, "Financeiro", Funcoes.Financeiro);

                var resposta = await client.PostAsJsonAsync(Rota, new FiltroExportacaoDto());

                await AfirmarErroAsync(resposta, HttpStatusCode.BadRequest);
                JobsDoUsuario(_factory, idUsuario).Should().BeEmpty();
            }
        }

        // ─── Apoio ───────────────────────────────────────────────────────────────

        private static void PrepararBanco(PagueAresWebAppFactory factory)
        {
            // Títulos visíveis à exportação: solicitações 103 a 106 e RDDVs 53 e 54 (aprovados pelo gestor).
            factory.SeedDatabase(ctx =>
            {
                if (!ctx.Solicitacaos.Any())
                {
                    PagueAresWebAppFactory.SeedBaseData(ctx);
                    PagueAresWebAppFactory.SeedSolicitacoes(ctx);
                    PagueAresWebAppFactory.SeedRddvs(ctx);
                }
            });
        }

        private static HttpRequestMessage NovaRequisicao(string metodo, string rota)
        {
            var requisicao = new HttpRequestMessage(new HttpMethod(metodo), rota);
            if (metodo == "POST")
                requisicao.Content = new StringContent("{}", Encoding.UTF8, "application/json");
            return requisicao;
        }

        private static int NovoUsuario(PagueAresWebAppFactory factory)
        {
            var id = Interlocked.Increment(ref _ultimoUsuario);
            factory.SeedDatabase(ctx =>
            {
                ctx.Usuarios.Add(new Usuario
                {
                    IdUsuario = id,
                    Email = $"usuario{id}@exemplo.invalid",
                    Nome = $"Usuário Fictício {id}",
                    Senha = "-",
                    Origem = 0,
                    Ativo = true,
                    DataCadastro = DateTime.Now,
                    IdDepartamento = 2,
                });
                ctx.SaveChanges();
            });
            return id;
        }

        private static int SemearJob(PagueAresWebAppFactory factory, int idUsuario, StatusExportacao status,
            Action<ExportacaoJob>? ajustar = null)
        {
            var id = 0;
            factory.SeedDatabase(ctx =>
            {
                var job = new ExportacaoJob
                {
                    IdUsuario = idUsuario,
                    Status = (int)status,
                    FiltrosJson = JsonSerializer.Serialize(new FiltroExportacaoDto(), Json),
                    DataCriacao = DateTime.UtcNow,
                };
                ctx.ExportacaoJobs.Add(job);
                ctx.SaveChanges();

                if (ajustar is not null)
                {
                    ajustar(job);
                    ctx.SaveChanges();
                }

                id = job.IdExportacaoJob;
            });
            return id;
        }

        /// <summary>Job Concluido com o pacote gravado à mão na pasta de exportação da factory.</summary>
        private static int SemearConcluidoComPacote(PagueAresWebAppFactory factory, int idUsuario, out string caminho,
            Action<ExportacaoJob>? ajustar = null)
        {
            var conteudo = RandomNumberGenerator.GetBytes(4096);
            string? caminhoGravado = null;
            var id = SemearJob(factory, idUsuario, StatusExportacao.Concluido, job =>
            {
                var agora = DateTime.UtcNow;
                job.NomeArquivo = ArquivosExportacao.NomeArquivo(job.IdExportacaoJob);
                job.TamanhoBytes = conteudo.Length;
                job.TotalTitulos = 3;
                job.TitulosProcessados = 3;
                job.TotalAnexos = 2;
                job.DataInicio = agora.AddMinutes(-2);
                job.DataConclusao = agora.AddMinutes(-1);
                job.DataExpiracao = agora.AddHours(48);
                ajustar?.Invoke(job);

                Directory.CreateDirectory(factory.PastaExportacao);
                caminhoGravado = Path.Combine(factory.PastaExportacao, job.NomeArquivo);
                File.WriteAllBytes(caminhoGravado, conteudo);
            });
            caminho = caminhoGravado!;
            return id;
        }

        private static ExportacaoJob LerJob(PagueAresWebAppFactory factory, int id)
        {
            ExportacaoJob? job = null;
            factory.SeedDatabase(ctx => job = ctx.ExportacaoJobs.AsNoTracking().Single(j => j.IdExportacaoJob == id));
            return job!;
        }

        private static List<ExportacaoJob> JobsDoUsuario(PagueAresWebAppFactory factory, int idUsuario)
        {
            List<ExportacaoJob> jobs = [];
            factory.SeedDatabase(ctx => jobs = ctx.ExportacaoJobs.AsNoTracking().Where(j => j.IdUsuario == idUsuario).ToList());
            return jobs;
        }

        private static void AlterarJob(PagueAresWebAppFactory factory, int id, Action<ExportacaoJob> alterar)
        {
            factory.SeedDatabase(ctx =>
            {
                var job = ctx.ExportacaoJobs.Single(j => j.IdExportacaoJob == id);
                alterar(job);
                ctx.SaveChanges();
            });
        }

        private static StatusExportacao Status(ExportacaoJob job) => (StatusExportacao)job.Status;

        private static async Task<LinkDownloadExportacaoDto> GerarLinkAsync(HttpClient client, int id)
        {
            var resposta = await client.PostAsync($"{Rota}/{id}/link-download", null);
            resposta.StatusCode.Should().Be(HttpStatusCode.OK, await resposta.Content.ReadAsStringAsync());
            var envelope = await resposta.Content.ReadFromJsonAsync<GenericResponse<LinkDownloadExportacaoDto>>(Json);
            envelope!.Success.Should().BeTrue();
            return envelope.Data!;
        }

        /// <summary>
        /// Confere o status e que o corpo é o envelope <see cref="GenericResponse{T}"/> com <c>success = false</c> e uma
        /// mensagem amigável, sem nenhum texto de exceção (tipo, pilha ou mensagem do serializador).
        /// </summary>
        private static async Task AfirmarErroAsync(HttpResponseMessage resposta, HttpStatusCode esperado)
        {
            var corpo = await resposta.Content.ReadAsStringAsync();
            resposta.StatusCode.Should().Be(esperado, corpo);

            foreach (var trecho in new[] { "Exception", "   at ", "System.", "Ares.PagueAres", "StackTrace", "Path: $", "LineNumber", "BytePosition" })
                corpo.Should().NotContain(trecho, "erro da API não pode expor texto de exceção");

            var envelope = JsonSerializer.Deserialize<GenericResponse<object>>(corpo, Json);
            envelope.Should().NotBeNull();
            envelope!.Success.Should().BeFalse();
            envelope.Message.Should().NotBeNullOrWhiteSpace();
        }
    }
}
