using Ares.PagueAres.Domain.Enums;
using Ares.PagueAres.Infrastructure;
using Ares.PagueAres.Infrastructure.Models;
using Ares.PagueAres.Tests.Integration.Infrastructure;
using ClosedXML.Excel;
using System.IO.Compression;
using System.Net;
using System.Text.Json;

namespace Ares.PagueAres.Tests.Integration.Controllers
{
    /// <summary>
    /// Segurança dos endpoints legados de exportação (ExportController) e da emissão de
    /// chaves de visualização (PreviewController).
    ///
    /// Regras verificadas:
    /// - a chave de exportação só é emitida para os papéis FIN e ADM do JWT e vale uma única vez;
    /// - o escopo do Excel é sempre o de FIN/ADM (não rascunho, não cancelado e aprovado pelo
    ///   gestor), independente do userId enviado na query;
    /// - os ZIPs por título recusam chave de outro tipo ou de outro id;
    /// - rascunho só gera chave para o dono;
    /// - nenhuma resposta de erro expõe texto de exceção;
    /// - o preview de anexo só entrega anexo do próprio título da chave (anexo de outro título é 404);
    /// - as entradas dos ZIPs por título nunca saem da pasta de extração (zip-slip) e não se repetem.
    ///
    /// Testes com [Trait("Category", "Controle")] confirmam o caminho legítimo e devem passar
    /// antes e depois da correção.
    /// </summary>
    public class ExportLegadoSecurityTests : IClassFixture<PagueAresWebAppFactory>
    {
        // Títulos e anexos próprios desta classe. Solicitação 53 e RDDV 103 existem para que o
        // mesmo id exista nos dois tipos de título, como acontece em produção.
        private const int IdSolicitacaoAtribuidaAoFinanceiro = 300;
        private const int IdSolicitacaoAnexoCorrompido = 310;
        private const int IdRddvAnexoCorrompido = 311;
        private const int IdSolicitacaoMesmoIdDeRddv = 53;
        private const int IdRddvMesmoIdDeSolicitacao = 103;

        private const int IdAnexoSolicitacao104 = 9104;
        private const int IdAnexoSolicitacao53 = 9053;
        private const int IdAnexoSolicitacaoCorrompido = 9310;
        private const int IdAnexoRddv54 = 9054;
        private const int IdAnexoRddv103 = 9103;
        private const int IdAnexoRddvCorrompido = 9311;

        // Preview de anexo: o usuário 6 (SOL) é dono de um título e pede a chave dele; o título vizinho é
        // rascunho do usuário 3. A chave do título do usuário 6 não pode abrir o anexo do rascunho.
        private const int IdSolicitacaoDoUsuario6 = 7400;
        private const int IdSolicitacaoRascunhoDeOutroUsuario = 7401;
        private const int IdRddvDoUsuario6 = 7410;
        private const int IdRddvRascunhoDeOutroUsuario = 7411;

        private const int IdAnexoSolicitacaoDoUsuario6 = 97400;
        private const int IdAnexoSolicitacaoRascunhoDeOutroUsuario = 97401;
        private const int IdAnexoRddvDoUsuario6 = 97410;
        private const int IdAnexoRddvRascunhoDeOutroUsuario = 97411;
        private const int IdAnexoInexistente = 99999;

        // ZIPs por título com nomes de anexo maliciosos (zip-slip) e repetidos.
        private const int IdSolicitacaoNomesMaliciosos = 7420;
        private const int IdRddvNomesMaliciosos = 7430;
        private const int PrimeiroIdAnexoSolicitacaoNomesMaliciosos = 97420;
        private const int PrimeiroIdAnexoRddvNomesMaliciosos = 97430;

        private static readonly string[] NomesDeAnexoMaliciosos =
        [
            @"..\..\fora.pdf",
            "../../fora2.pdf",
            @"C:\Windows\fora3.pdf",
            "/fora4.pdf",
            "boleto.pdf",
            "boleto.pdf",
            "BOLETO.pdf"
        ];

        // "%PDF-1.4\n" em base64, com o prefixo data URI que o front-end grava.
        private const string ArquivoValido = "data:application/pdf;base64,JVBERi0xLjQK";
        private const string ArquivoCorrompido = "data:application/pdf;base64,@@@ isto nao e um arquivo @@@";

        private const string MensagemErroGenerica = "Não foi possível concluir a operação. Tente de novo.";
        private const string MensagemFiltroInvalido = "Filtro de exportação inválido.";

        private readonly PagueAresWebAppFactory _factory;

        public ExportLegadoSecurityTests(PagueAresWebAppFactory factory)
        {
            _factory = factory;
            _factory.SeedDatabase(ctx =>
            {
                if (!ctx.Solicitacaos.Any())
                {
                    PagueAresWebAppFactory.SeedBaseData(ctx);
                    PagueAresWebAppFactory.SeedSolicitacoes(ctx);
                    PagueAresWebAppFactory.SeedRddvs(ctx);
                    SeedCenarioExportacao(ctx);
                }
            });
        }

        // ─── Semeadura ───────────────────────────────────────────────────────────

        private static Solicitacao NovaSolicitacaoAprovadaPeloGestor(int id, int idUsuario, int? idUsuarioAtribuido = null)
        {
            return new Solicitacao
            {
                IdSolicitacao = id,
                IdUsuario = idUsuario,
                IdUsuarioAtribuido = idUsuarioAtribuido,
                IdDepartamento = 1,
                DataSolicitacao = DateTime.Now,
                NumeroDocumento = "NF-" + id,
                Descricao = "Teste de exportação",
                Valor = 250m,
                FormaPagamento = 1,
                CentroCusto = "",
                Projeto = "",
                Pca = "",
                NumDocParceiro = "",
                CodigoParceiro = "",
                NomeParceiro = "Parceiro Fictício",
                BancoParceiro = "",
                AgenciaParceiro = "",
                ContaParceiro = "",
                Observacao = "",
                ObservacaoGestor = "",
                ObservacaoSetor = "",
                TipoAutorizacao = 0,
                TipoSolicitacao = 1,
                TipoPagamento = 1,
                Rascunho = false,
                Cancelado = false,
                StatusGestor = 2,
                StatusFinanceiro = 1,
                StatusContabilidade = 1,
                AprovadoGestor = true
            };
        }

        private static Rddv NovoRddvAprovadoPeloGestor(int id, int idUsuario)
        {
            return new Rddv
            {
                IdRelatorio = id,
                IdUsuario = idUsuario,
                IdDepartamento = 1,
                NomeFuncionario = "Funcionário Fictício",
                CPF = "00000000000",
                Finalidade = "Teste de exportação",
                TipoRelatorio = 1,
                Moeda = "BRL",
                TipoViagem = 1,
                LocalViagem = "Cidade Fictícia",
                CentroCusto = "",
                Projeto = "",
                PCA = "",
                Observacao = "",
                ObservacaoGestor = "",
                ObservacaoSetor = "",
                Banco = "",
                Agencia = "",
                Conta = "",
                TipoAutorizacao = 0,
                DataCadastro = DateTime.Now,
                Rascunho = false,
                Cancelado = false,
                StatusGestor = 2,
                StatusFinanceiro = 1,
                StatusContabilidade = 1,
                AprovadoGestor = true
            };
        }

        private static DocumentoSolicitacao NovoAnexoSolicitacao(int idAnexo, int idSolicitacao, string arquivo, string? nomeArquivo = null)
        {
            return new DocumentoSolicitacao
            {
                IdDocumentoSolicitacao = idAnexo,
                IdSolicitacao = idSolicitacao,
                IdTipoDocumento = 1,
                IdUsuario = 3,
                DataCriacao = DateTime.Now,
                Indice = 1,
                NomeArquivo = nomeArquivo ?? $"anexo-{idAnexo}.pdf",
                CaminhoArquivo = "application/pdf",
                Arquivo = arquivo
            };
        }

        private static DocumentoRddv NovoAnexoRddv(int idAnexo, int idRelatorio, string arquivo, string? nomeArquivo = null)
        {
            return new DocumentoRddv
            {
                IdDocumentoRddv = idAnexo,
                IdRelatorio = idRelatorio,
                IdTipoDocumento = 1,
                IdUsuario = 3,
                DataCriacao = DateTime.Now,
                Indice = 1,
                NomeArquivo = nomeArquivo ?? $"anexo-{idAnexo}.pdf",
                CaminhoArquivo = "application/pdf",
                Arquivo = arquivo
            };
        }

        private static Solicitacao NovaSolicitacaoRascunho(int id, int idUsuario)
        {
            var solicitacao = NovaSolicitacaoAprovadaPeloGestor(id, idUsuario);
            solicitacao.Rascunho = true;
            solicitacao.AprovadoGestor = null;
            solicitacao.StatusGestor = 1;
            return solicitacao;
        }

        private static Rddv NovoRddvRascunho(int id, int idUsuario)
        {
            var rddv = NovoRddvAprovadoPeloGestor(id, idUsuario);
            rddv.Rascunho = true;
            rddv.AprovadoGestor = null;
            rddv.StatusGestor = 1;
            return rddv;
        }

        private static void SeedCenarioExportacao(PagueAresContext ctx)
        {
            ctx.Solicitacaos.AddRange(
                // Aprovada pelo gestor e atribuída ao financeiro (id=4), não ao ADM (id=1)
                NovaSolicitacaoAprovadaPeloGestor(IdSolicitacaoAtribuidaAoFinanceiro, idUsuario: 6, idUsuarioAtribuido: 4),
                NovaSolicitacaoAprovadaPeloGestor(IdSolicitacaoAnexoCorrompido, idUsuario: 3),
                NovaSolicitacaoAprovadaPeloGestor(IdSolicitacaoMesmoIdDeRddv, idUsuario: 3));

            ctx.Rddv.AddRange(
                NovoRddvAprovadoPeloGestor(IdRddvAnexoCorrompido, idUsuario: 3),
                NovoRddvAprovadoPeloGestor(IdRddvMesmoIdDeSolicitacao, idUsuario: 3));

            ctx.DocumentoSolicitacaos.AddRange(
                NovoAnexoSolicitacao(IdAnexoSolicitacao104, 104, ArquivoValido),
                NovoAnexoSolicitacao(IdAnexoSolicitacao53, IdSolicitacaoMesmoIdDeRddv, ArquivoValido),
                NovoAnexoSolicitacao(IdAnexoSolicitacaoCorrompido, IdSolicitacaoAnexoCorrompido, ArquivoCorrompido));

            ctx.DocumentoRddv.AddRange(
                NovoAnexoRddv(IdAnexoRddv54, 54, ArquivoValido),
                NovoAnexoRddv(IdAnexoRddv103, IdRddvMesmoIdDeSolicitacao, ArquivoValido),
                NovoAnexoRddv(IdAnexoRddvCorrompido, IdRddvAnexoCorrompido, ArquivoCorrompido));

            // Preview de anexo: título do usuário 6 e rascunho vizinho do usuário 3, cada um com um anexo.
            ctx.Solicitacaos.AddRange(
                NovaSolicitacaoAprovadaPeloGestor(IdSolicitacaoDoUsuario6, idUsuario: 6),
                NovaSolicitacaoRascunho(IdSolicitacaoRascunhoDeOutroUsuario, idUsuario: 3));

            ctx.Rddv.AddRange(
                NovoRddvAprovadoPeloGestor(IdRddvDoUsuario6, idUsuario: 6),
                NovoRddvRascunho(IdRddvRascunhoDeOutroUsuario, idUsuario: 3));

            ctx.DocumentoSolicitacaos.AddRange(
                NovoAnexoSolicitacao(IdAnexoSolicitacaoDoUsuario6, IdSolicitacaoDoUsuario6, ArquivoValido),
                NovoAnexoSolicitacao(IdAnexoSolicitacaoRascunhoDeOutroUsuario, IdSolicitacaoRascunhoDeOutroUsuario, ArquivoValido));

            ctx.DocumentoRddv.AddRange(
                NovoAnexoRddv(IdAnexoRddvDoUsuario6, IdRddvDoUsuario6, ArquivoValido),
                NovoAnexoRddv(IdAnexoRddvRascunhoDeOutroUsuario, IdRddvRascunhoDeOutroUsuario, ArquivoValido));

            // ZIPs por título: um anexo para cada nome malicioso ou repetido.
            ctx.Solicitacaos.Add(NovaSolicitacaoAprovadaPeloGestor(IdSolicitacaoNomesMaliciosos, idUsuario: 3));
            ctx.Rddv.Add(NovoRddvAprovadoPeloGestor(IdRddvNomesMaliciosos, idUsuario: 3));

            for (int i = 0; i < NomesDeAnexoMaliciosos.Length; i++)
            {
                ctx.DocumentoSolicitacaos.Add(NovoAnexoSolicitacao(
                    PrimeiroIdAnexoSolicitacaoNomesMaliciosos + i, IdSolicitacaoNomesMaliciosos, ArquivoValido, NomesDeAnexoMaliciosos[i]));
                ctx.DocumentoRddv.Add(NovoAnexoRddv(
                    PrimeiroIdAnexoRddvNomesMaliciosos + i, IdRddvNomesMaliciosos, ArquivoValido, NomesDeAnexoMaliciosos[i]));
            }

            ctx.SaveChanges();
        }

        // ─── Helpers ─────────────────────────────────────────────────────────────

        private HttpClient ClienteADM() => _factory.GetAuthenticatedClient(1, "Admin", "ADM");
        private HttpClient ClienteFIN() => _factory.GetAuthenticatedClient(4, "Financeiro", "FIN");

        private static Task<HttpResponseMessage> PedirChaveAsync(HttpClient client, int entityId, EnumRequestType tipo) =>
            client.PostAsync($"/api/preview/generate-preview-key?entityId={entityId}&requestType={(int)tipo}", null);

        private static async Task<JsonElement> LerJsonAsync(HttpResponseMessage response)
        {
            var body = await response.Content.ReadAsStringAsync();
            using var json = JsonDocument.Parse(body);
            return json.RootElement.Clone();
        }

        private static async Task<string> GerarChaveAsync(HttpClient client, int entityId, EnumRequestType tipo)
        {
            var response = await PedirChaveAsync(client, entityId, tipo);
            response.StatusCode.Should().Be(HttpStatusCode.OK, "o perfil usado no teste tem direito à chave");

            var json = await LerJsonAsync(response);
            json.GetProperty("success").GetBoolean().Should().BeTrue("o perfil usado no teste tem direito à chave");

            return json.GetProperty("data").GetProperty("previewKey").GetString()!;
        }

        private static string UrlExportacaoExcel(string sFilter, int userId, string key) =>
            $"/api/export/payments?sFilter={Uri.EscapeDataString(sFilter)}&userId={userId}&key={Uri.EscapeDataString(key)}";

        private HttpClient ClienteSOLUsuario6() => _factory.GetAuthenticatedClient(6, "Outro Solicitante", "SOL");

        private static async Task<List<string>> LerEntradasDoZipAsync(HttpResponseMessage response)
        {
            using var stream = new MemoryStream(await response.Content.ReadAsByteArrayAsync());
            using var zip = new ZipArchive(stream, ZipArchiveMode.Read);
            return zip.Entries.Select(e => e.FullName).ToList();
        }

        private static List<string> LerCodigosDoExcel(byte[] conteudo)
        {
            using var stream = new MemoryStream(conteudo);
            using var workbook = new XLWorkbook(stream);
            var planilha = workbook.Worksheet("Pagamentos");

            return planilha.Column(1).CellsUsed()
                .Skip(1) // cabeçalho
                .Select(c => c.GetFormattedString().Trim())
                .ToList();
        }

        // ═══════════════════════════════════════════════════════════════════════
        // Chave de exportação: só FIN e ADM, uso único
        // ═══════════════════════════════════════════════════════════════════════

        [Fact]
        [Trait("Category", "SecurityGap")]
        public async Task ChaveExportacao_PapelCON_Retorna403()
        {
            var client = _factory.GetAuthenticatedClient(5, "Contabilidade", "CON");

            var response = await PedirChaveAsync(client, 0, EnumRequestType.Exportacao);

            response.StatusCode.Should().Be(HttpStatusCode.Forbidden, "a exportação é exclusiva de FIN e ADM");
            var json = await LerJsonAsync(response);
            json.GetProperty("success").GetBoolean().Should().BeFalse();
        }

        [Fact]
        [Trait("Category", "SecurityGap")]
        public async Task ChaveExportacao_PapelSOL_Retorna403()
        {
            var client = _factory.GetAuthenticatedClient(3, "Solicitante", "SOL");

            var response = await PedirChaveAsync(client, 0, EnumRequestType.Exportacao);

            response.StatusCode.Should().Be(HttpStatusCode.Forbidden, "a exportação é exclusiva de FIN e ADM");
            var json = await LerJsonAsync(response);
            json.GetProperty("success").GetBoolean().Should().BeFalse();
        }

        [Fact]
        [Trait("Category", "SecurityGap")]
        public async Task ExportacaoExcel_ADM_IncluiSolicitacaoAprovadaNaoAtribuidaAEle()
        {
            var client = ClienteADM();
            var key = await GerarChaveAsync(client, 0, EnumRequestType.Exportacao);

            var response = await client.GetAsync(UrlExportacaoExcel("{}", userId: 1, key));

            response.StatusCode.Should().Be(HttpStatusCode.OK);
            var codigos = LerCodigosDoExcel(await response.Content.ReadAsByteArrayAsync());

            codigos.Should().Contain(IdSolicitacaoAtribuidaAoFinanceiro.ToString(),
                "ADM vê todos os títulos aprovados pelo gestor, inclusive os atribuídos a outro usuário");
            codigos.Should().NotContain("11", "rascunho nunca entra na exportação");
            codigos.Should().NotContain("10", "cancelado nunca entra na exportação");
            codigos.Should().NotContain("101", "título não aprovado pelo gestor não entra na exportação");
        }

        [Fact]
        [Trait("Category", "SecurityGap")]
        public async Task ChaveExportacao_UsadaDuasVezes_SegundaRetorna401()
        {
            var client = ClienteFIN();
            var key = await GerarChaveAsync(client, 0, EnumRequestType.Exportacao);

            var primeira = await client.GetAsync(UrlExportacaoExcel("{}", userId: 4, key));
            var segunda = await client.GetAsync(UrlExportacaoExcel("{}", userId: 4, key));

            primeira.StatusCode.Should().Be(HttpStatusCode.OK);
            segunda.StatusCode.Should().Be(HttpStatusCode.Unauthorized, "a chave de exportação é de uso único");
        }

        // ═══════════════════════════════════════════════════════════════════════
        // ZIPs por título: chave de outro tipo ou de outro id é recusada
        // ═══════════════════════════════════════════════════════════════════════

        [Fact]
        [Trait("Category", "SecurityGap")]
        public async Task ZipRddv_ChaveDeOutroRddv_Retorna401()
        {
            var client = ClienteFIN();
            var key = await GerarChaveAsync(client, 53, EnumRequestType.Rddv);

            var response = await client.GetAsync($"/api/export/rddv/attachment/54?key={key}");

            response.StatusCode.Should().Be(HttpStatusCode.Unauthorized, "a chave foi emitida para o RDDV 53");
        }

        [Fact]
        [Trait("Category", "SecurityGap")]
        public async Task ZipRddv_ChaveDeSolicitacaoComMesmoId_Retorna401()
        {
            var client = ClienteFIN();
            var key = await GerarChaveAsync(client, 103, EnumRequestType.Solicitacao);

            var response = await client.GetAsync($"/api/export/rddv/attachment/{IdRddvMesmoIdDeSolicitacao}?key={key}");

            response.StatusCode.Should().Be(HttpStatusCode.Unauthorized, "a chave foi emitida para a Solicitação 103, não para o RDDV 103");
        }

        [Fact]
        [Trait("Category", "SecurityGap")]
        public async Task ZipSolicitacao_ChaveDeOutraSolicitacao_Retorna401()
        {
            var client = ClienteFIN();
            var key = await GerarChaveAsync(client, 103, EnumRequestType.Solicitacao);

            var response = await client.GetAsync($"/api/export/request/attachment/104?key={key}");

            response.StatusCode.Should().Be(HttpStatusCode.Unauthorized, "a chave foi emitida para a Solicitação 103");
        }

        [Fact]
        [Trait("Category", "SecurityGap")]
        public async Task ZipSolicitacao_ChaveDeRddvComMesmoId_Retorna401()
        {
            var client = ClienteFIN();
            var key = await GerarChaveAsync(client, 53, EnumRequestType.Rddv);

            var response = await client.GetAsync($"/api/export/request/attachment/{IdSolicitacaoMesmoIdDeRddv}?key={key}");

            response.StatusCode.Should().Be(HttpStatusCode.Unauthorized, "a chave foi emitida para o RDDV 53, não para a Solicitação 53");
        }

        [Fact]
        [Trait("Category", "Controle")]
        public async Task ZipRddv_ChaveCorreta_RetornaZip()
        {
            var client = ClienteFIN();
            var key = await GerarChaveAsync(client, 54, EnumRequestType.Rddv);

            var response = await client.GetAsync($"/api/export/rddv/attachment/54?key={key}");

            response.StatusCode.Should().Be(HttpStatusCode.OK);
            response.Content.Headers.ContentType!.MediaType.Should().Be("application/zip");
        }

        // ═══════════════════════════════════════════════════════════════════════
        // Rascunho só gera chave para o dono
        // ═══════════════════════════════════════════════════════════════════════

        [Fact]
        [Trait("Category", "SecurityGap")]
        public async Task ChaveSolicitacaoRascunho_FINDeOutroUsuario_Negada()
        {
            // Solicitação 11 é rascunho do usuário 3
            var response = await PedirChaveAsync(ClienteFIN(), 11, EnumRequestType.Solicitacao);

            var json = await LerJsonAsync(response);
            json.GetProperty("success").GetBoolean().Should().BeFalse("rascunho só é visível para o dono");
        }

        [Fact]
        [Trait("Category", "SecurityGap")]
        public async Task ChaveRddvRascunho_FINDeOutroUsuario_Negada()
        {
            // RDDV 51 é rascunho do usuário 3
            var response = await PedirChaveAsync(ClienteFIN(), 51, EnumRequestType.Rddv);

            var json = await LerJsonAsync(response);
            json.GetProperty("success").GetBoolean().Should().BeFalse("rascunho só é visível para o dono");
        }

        [Fact]
        [Trait("Category", "Controle")]
        public async Task ChaveSolicitacaoRascunho_Dono_Concedida()
        {
            var client = _factory.GetAuthenticatedClient(3, "Solicitante", "SOL");

            var response = await PedirChaveAsync(client, 11, EnumRequestType.Solicitacao);

            var json = await LerJsonAsync(response);
            json.GetProperty("success").GetBoolean().Should().BeTrue("o dono vê o próprio rascunho");
        }

        [Fact]
        [Trait("Category", "Controle")]
        public async Task ChaveGerada_RespostaNaoExpoeIdUsuario()
        {
            var response = await PedirChaveAsync(ClienteFIN(), 103, EnumRequestType.Solicitacao);

            var json = await LerJsonAsync(response);
            var data = json.GetProperty("data");
            data.TryGetProperty("previewKey", out _).Should().BeTrue();
            data.TryGetProperty("idUsuario", out _).Should().BeFalse("o dono da chave fica só no servidor");
        }

        // ═══════════════════════════════════════════════════════════════════════
        // Erros sem texto de exceção na resposta
        // ═══════════════════════════════════════════════════════════════════════

        [Fact]
        [Trait("Category", "SecurityGap")]
        public async Task ExportacaoExcel_FiltroMalformado_Retorna400SemMensagemInterna()
        {
            var client = ClienteADM();
            var key = await GerarChaveAsync(client, 0, EnumRequestType.Exportacao);

            var response = await client.GetAsync(UrlExportacaoExcel("nao-e-json", userId: 1, key));
            var body = await response.Content.ReadAsStringAsync();

            response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
            body.Should().NotContain("Unexpected character", "a mensagem do Newtonsoft não pode vazar");
            body.Should().NotContain("Path '", "a mensagem do Newtonsoft não pode vazar");
            body.Should().Contain(MensagemFiltroInvalido);
        }

        [Theory]
        [Trait("Category", "SecurityGap")]
        [InlineData(EnumRequestType.Solicitacao, IdSolicitacaoAnexoCorrompido, "/api/export/request/attachment/310")]
        [InlineData(EnumRequestType.Rddv, IdRddvAnexoCorrompido, "/api/export/rddv/attachment/311")]
        [InlineData(EnumRequestType.Solicitacao, IdSolicitacaoAnexoCorrompido, "/api/preview/payment/310/9310")]
        [InlineData(EnumRequestType.Rddv, IdRddvAnexoCorrompido, "/api/preview/rddv/311/9311")]
        public async Task AnexoBase64Invalido_RespostaSemMensagemInterna(EnumRequestType tipo, int entityId, string rota)
        {
            var client = ClienteFIN();
            var key = await GerarChaveAsync(client, entityId, tipo);

            var response = await client.GetAsync($"{rota}?key={key}");
            var body = await response.Content.ReadAsStringAsync();

            body.Should().NotContainEquivalentOf("base-64", "a mensagem da FormatException não pode vazar");
            body.Should().NotContainEquivalentOf("base 64", "a mensagem da FormatException não pode vazar");
            body.Should().NotContainEquivalentOf("base64", "a mensagem da FormatException não pode vazar");
            response.StatusCode.Should().Be(HttpStatusCode.InternalServerError);
            body.Should().Contain(MensagemErroGenerica);
        }

        // ═══════════════════════════════════════════════════════════════════════
        // Preview de anexo: a chave de um título não abre anexo de outro título
        // ═══════════════════════════════════════════════════════════════════════

        [Theory]
        [Trait("Category", "SecurityGap")]
        [InlineData(EnumRequestType.Solicitacao, "payment", IdSolicitacaoDoUsuario6, IdAnexoSolicitacaoRascunhoDeOutroUsuario)]
        [InlineData(EnumRequestType.Rddv, "rddv", IdRddvDoUsuario6, IdAnexoRddvRascunhoDeOutroUsuario)]
        public async Task PreviewAnexo_ChaveDoTituloComAnexoDeOutroTitulo_Retorna404(
            EnumRequestType tipo, string rota, int idTitulo, int idAnexoDeOutroTitulo)
        {
            var client = ClienteSOLUsuario6();
            var key = await GerarChaveAsync(client, idTitulo, tipo);

            var response = await client.GetAsync($"/api/preview/{rota}/{idTitulo}/{idAnexoDeOutroTitulo}?key={key}");
            var inexistente = await client.GetAsync($"/api/preview/{rota}/{idTitulo}/{IdAnexoInexistente}?key={key}");

            response.StatusCode.Should().Be(HttpStatusCode.NotFound,
                $"o anexo {idAnexoDeOutroTitulo} é do rascunho de outro usuário, não do título {idTitulo} da chave");
            (await response.Content.ReadAsStringAsync()).Should().Be(await inexistente.Content.ReadAsStringAsync(),
                "anexo de outro título responde igual a anexo inexistente, sem confirmar que ele existe");
        }

        [Theory]
        [Trait("Category", "Controle")]
        [InlineData(EnumRequestType.Solicitacao, "payment", IdSolicitacaoDoUsuario6, IdAnexoSolicitacaoDoUsuario6)]
        [InlineData(EnumRequestType.Rddv, "rddv", IdRddvDoUsuario6, IdAnexoRddvDoUsuario6)]
        public async Task PreviewAnexo_ChaveEAnexoDoMesmoTitulo_Retorna200(
            EnumRequestType tipo, string rota, int idTitulo, int idAnexo)
        {
            var client = ClienteSOLUsuario6();
            var key = await GerarChaveAsync(client, idTitulo, tipo);

            var response = await client.GetAsync($"/api/preview/{rota}/{idTitulo}/{idAnexo}?key={key}");

            response.StatusCode.Should().Be(HttpStatusCode.OK, "o anexo é do próprio título da chave");
            response.Content.Headers.ContentType!.MediaType.Should().Be("application/pdf");
        }

        // ═══════════════════════════════════════════════════════════════════════
        // ZIPs por título: nome de anexo não escapa da pasta (zip-slip) nem se repete
        // ═══════════════════════════════════════════════════════════════════════

        [Theory]
        [Trait("Category", "SecurityGap")]
        [InlineData(EnumRequestType.Solicitacao, "request", IdSolicitacaoNomesMaliciosos)]
        [InlineData(EnumRequestType.Rddv, "rddv", IdRddvNomesMaliciosos)]
        public async Task ZipAnexos_NomeComCaminho_EntradaNaoSaiDaPastaDeExtracao(EnumRequestType tipo, string rota, int idTitulo)
        {
            var client = ClienteFIN();
            var key = await GerarChaveAsync(client, idTitulo, tipo);

            var response = await client.GetAsync($"/api/export/{rota}/attachment/{idTitulo}?key={key}");

            response.StatusCode.Should().Be(HttpStatusCode.OK);
            var entradas = await LerEntradasDoZipAsync(response);

            entradas.Should().HaveCount(NomesDeAnexoMaliciosos.Length, "cada anexo vira uma entrada do ZIP");
            using (new FluentAssertions.Execution.AssertionScope())
            {
                foreach (var entrada in entradas)
                {
                    entrada.Should().NotContain("..", "'..' na entrada leva a extração para fora da pasta de destino");
                    entrada.Should().NotStartWith("/", "entrada absoluta escreve fora da pasta de destino");
                    entrada.Should().NotContain("\\", "barra invertida vira separador de pasta no Windows");
                    entrada.Should().NotContain(":", "dois-pontos indica unidade (C:) ou fluxo alternativo no Windows");
                }
            }
        }

        [Theory]
        [Trait("Category", "SecurityGap")]
        [InlineData(EnumRequestType.Solicitacao, "request", IdSolicitacaoNomesMaliciosos)]
        [InlineData(EnumRequestType.Rddv, "rddv", IdRddvNomesMaliciosos)]
        public async Task ZipAnexos_AnexosComMesmoNome_EntradasDistintas(EnumRequestType tipo, string rota, int idTitulo)
        {
            var client = ClienteFIN();
            var key = await GerarChaveAsync(client, idTitulo, tipo);

            var response = await client.GetAsync($"/api/export/{rota}/attachment/{idTitulo}?key={key}");

            response.StatusCode.Should().Be(HttpStatusCode.OK);
            var entradas = await LerEntradasDoZipAsync(response);

            entradas.Should().HaveCount(NomesDeAnexoMaliciosos.Length, "cada anexo vira uma entrada do ZIP");
            entradas.Select(e => e.ToUpperInvariant()).Should().OnlyHaveUniqueItems(
                "anexos de mesmo nome (sem diferenciar caixa) se sobrescreveriam na extração");
            entradas.Should().Contain("boleto.pdf", "nome que já é seguro é mantido como está");
        }
    }
}
