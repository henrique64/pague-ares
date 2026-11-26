using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Ares.PagueAres.Application.Configuration;
using Ares.PagueAres.Application.Exportacao;
using Ares.PagueAres.Domain.Dtos.Exportacao;
using Ares.PagueAres.Domain.Enums;
using ClosedXML.Excel;
using FluentAssertions;
using Microsoft.Extensions.Options;

namespace Ares.PagueAres.Tests.Unit.Exportacao
{
    public class GeradorPacoteTests
    {
        private static readonly string[] EntradasDaRaiz = ["LEIA-ME.txt", "titulos.xlsx", "indice.csv", "indice.json"];

        private static readonly Regex EntradaDeTitulo = new(@"^(SOL|RDDV)-\d{6,}/[^/]+$", RegexOptions.CultureInvariant);

        private sealed class ProgressoSincrono : IProgress<ProgressoExportacao>
        {
            public List<ProgressoExportacao> Relatos { get; } = [];
            public void Report(ProgressoExportacao value) => Relatos.Add(value);
        }

        private sealed class Pacote(byte[] bytes, ResultadoPacoteExportacao resultado) : IDisposable
        {
            public ZipArchive Zip { get; } = new(new MemoryStream(bytes), ZipArchiveMode.Read);
            public ResultadoPacoteExportacao Resultado { get; } = resultado;

            public byte[] Ler(string entrada)
            {
                using var fluxo = Zip.GetEntry(entrada)!.Open();
                using var copia = new MemoryStream();
                fluxo.CopyTo(copia);
                return copia.ToArray();
            }

            public XLWorkbook Planilha() => new(new MemoryStream(Ler("titulos.xlsx")));

            public void Dispose() => Zip.Dispose();
        }

        private static GeradorPacoteExportacao CriarGerador(IFonteExportacao fonte, ExportacaoSettings? settings = null) =>
            new(fonte, Options.Create(settings ?? new ExportacaoSettings()));

        private static async Task<Pacote> GerarAsync(FonteExportacaoFalsa fonte, FiltroExportacaoDto? filtro = null,
            IProgress<ProgressoExportacao>? progresso = null, int idExportacao = 42)
        {
            using var destino = new MemoryStream();
            var resultado = await CriarGerador(fonte).GerarAsync(idExportacao, filtro ?? new FiltroExportacaoDto(), destino, progresso, CancellationToken.None);
            return new Pacote(destino.ToArray(), resultado);
        }

        private static byte[] Bytes(int tamanho, int semente)
        {
            var bytes = new byte[tamanho];
            new Random(semente).NextBytes(bytes);
            return bytes;
        }

        /// <summary>Duas solicitações (uma sem anexo), um RDDV com despesas e anexos com nomes hostis.</summary>
        private static FonteExportacaoFalsa FonteCompleta()
        {
            var fonte = new FonteExportacaoFalsa();
            fonte.Solicitacoes.Add(FonteExportacaoFalsa.Solicitacao(123));
            fonte.Solicitacoes.Add(FonteExportacaoFalsa.Solicitacao(124));
            fonte.Rddvs.Add(FonteExportacaoFalsa.Rddv(45));
            fonte.Despesas.Add(FonteExportacaoFalsa.Despesa(900, 45));
            fonte.Despesas.Add(FonteExportacaoFalsa.Despesa(901, 45));

            fonte.AdicionarAnexo(TipoTituloExportacao.Solicitacao, 123, 1, "Nota Fiscal", "boleto março.pdf", Bytes(1000, 1));
            fonte.AdicionarAnexo(TipoTituloExportacao.Solicitacao, 123, 2, "Nota Fiscal", "../../etc/passwd", Bytes(10, 2));
            fonte.AdicionarAnexo(TipoTituloExportacao.Solicitacao, 123, 3, "Comprovante", "C:\\Windows\\x.exe", Bytes(20, 3));
            fonte.AdicionarAnexo(TipoTituloExportacao.Solicitacao, 123, 4, "Comprovante", "a/b\\c;d.pdf", Bytes(30, 4));
            fonte.AdicionarAnexo(TipoTituloExportacao.Solicitacao, 123, 5, "", "", Bytes(0, 5));
            fonte.AdicionarAnexoBase64(TipoTituloExportacao.Rddv, 45, 6, "Recibo", "recibo.jpg",
                "data:image/jpeg;base64," + Convert.ToBase64String(Bytes(5000, 6)));
            fonte.AdicionarAnexo(TipoTituloExportacao.Rddv, 45, 7, "Recibo", "RECIBO.JPG", Bytes(40, 7));
            return fonte;
        }

        [Fact]
        public async Task Gerar_EntradasFicamNaRaizOuDentroDeUmaPastaDeTitulo()
        {
            using var pacote = await GerarAsync(FonteCompleta());

            var nomes = pacote.Zip.Entries.Select(e => e.FullName).ToList();

            nomes.Where(n => !n.Contains('/')).Should().BeEquivalentTo(EntradasDaRaiz);
            nomes.Where(n => !EntradasDaRaiz.Contains(n)).Should().NotBeEmpty()
                .And.AllSatisfy(n => EntradaDeTitulo.IsMatch(n).Should().BeTrue($"a entrada '{n}' deve ficar dentro da pasta do título"));
            nomes.Should().NotContain(n => n.Contains("..") || n.Contains('\\') || n.Contains(':'));
            nomes.Should().OnlyHaveUniqueItems(n => n.ToUpperInvariant());
        }

        [Fact]
        public async Task Gerar_NomesDosAnexosSeguemOContrato()
        {
            using var pacote = await GerarAsync(FonteCompleta());

            pacote.Zip.Entries.Select(e => e.FullName).Where(n => n.Contains('/')).Should().BeEquivalentTo(
                "SOL-000123/Nota Fiscal - boleto março.pdf",
                "SOL-000123/Nota Fiscal - anexo-2.etcpasswd",
                "SOL-000123/Comprovante - CWindowsx.exe",
                "SOL-000123/Comprovante - abc;d.pdf",
                "SOL-000123/Documento - anexo-5",
                "RDDV-000045/Recibo - recibo.jpg",
                "RDDV-000045/Recibo - RECIBO (2).JPG");
        }

        [Fact]
        public async Task Gerar_EscreveAnexosAntesDosArquivosDaRaizNaOrdemDoContrato()
        {
            using var pacote = await GerarAsync(FonteCompleta());

            var nomes = pacote.Zip.Entries.Select(e => e.FullName).ToList();

            nomes.TakeLast(4).Should().Equal("titulos.xlsx", "indice.csv", "indice.json", "LEIA-ME.txt");
        }

        [Fact]
        public async Task Gerar_TamanhoEShaDoIndiceBatemComOConteudo()
        {
            var fonte = FonteCompleta();
            using var pacote = await GerarAsync(fonte);

            using var json = JsonDocument.Parse(pacote.Ler("indice.json"));
            var arquivos = json.RootElement.GetProperty("arquivos").EnumerateArray().ToList();
            arquivos.Should().HaveCount(7);

            foreach (var arquivo in arquivos)
            {
                var caminho = arquivo.GetProperty("caminhoRelativo").GetString()!;
                var conteudo = pacote.Ler(caminho);

                arquivo.GetProperty("tamanhoBytes").GetInt64().Should().Be(conteudo.LongLength);
                arquivo.GetProperty("sha256").GetString().Should().Be(Convert.ToHexStringLower(SHA256.HashData(conteudo)));
            }

            var linhasCsv = LinhasCsv(pacote.Ler("indice.csv"));
            foreach (var linha in linhasCsv.Skip(1))
            {
                var campos = DividirCsv(linha);
                var conteudo = pacote.Ler(campos[5]);
                campos[6].Should().Be(conteudo.LongLength.ToString());
                campos[7].Should().Be(Convert.ToHexStringLower(SHA256.HashData(conteudo)));
            }

            pacote.Resultado.TotalAnexos.Should().Be(7);
            pacote.Resultado.BytesAnexos.Should().Be(1000 + 10 + 20 + 30 + 0 + 5000 + 40);
            pacote.Resultado.TotalTitulos.Should().Be(3);
        }

        [Fact]
        public async Task Gerar_ConteudoDecodificadoEhIgualAoOriginal_InclusiveComPrefixoData()
        {
            using var pacote = await GerarAsync(FonteCompleta());

            pacote.Ler("SOL-000123/Nota Fiscal - boleto março.pdf").Should().Equal(Bytes(1000, 1));
            pacote.Ler("RDDV-000045/Recibo - recibo.jpg").Should().Equal(Bytes(5000, 6));
            pacote.Ler("SOL-000123/Documento - anexo-5").Should().BeEmpty();
        }

        [Fact]
        public async Task Gerar_Base64ComQuebrasDeLinhaSemPreenchimentoEEmVariosBlocos_DecodificaIgual()
        {
            var original = Bytes(3 * DecodificadorBase64.TamanhoBloco + 1234, 99);
            var base64 = Convert.ToBase64String(original, Base64FormattingOptions.InsertLineBreaks).TrimEnd('=');
            var fonte = new FonteExportacaoFalsa();
            fonte.Solicitacoes.Add(FonteExportacaoFalsa.Solicitacao(1));
            fonte.AdicionarAnexoBase64(TipoTituloExportacao.Solicitacao, 1, 1, "Nota Fiscal", "grande.bin", base64);

            using var pacote = await GerarAsync(fonte);

            pacote.Ler("SOL-000001/Nota Fiscal - grande.bin").Should().Equal(original);
        }

        [Theory]
        [InlineData("isto não é base64!")]
        [InlineData("QUJD$")]
        [InlineData("QUJDRA==QUJD")]
        [InlineData("data:application/pdf;base64")]
        [InlineData("Q")]
        public async Task Gerar_Base64Invalido_LancaExportacaoExceptionComMensagemAmigavel(string base64)
        {
            var fonte = new FonteExportacaoFalsa();
            fonte.Solicitacoes.Add(FonteExportacaoFalsa.Solicitacao(123));
            fonte.AdicionarAnexoBase64(TipoTituloExportacao.Solicitacao, 123, 1, "Nota Fiscal", "boleto.pdf", base64);

            var acao = async () => await GerarAsync(fonte);

            var excecao = (await acao.Should().ThrowExactlyAsync<ExportacaoException>()).Which;
            excecao.Message.Should().Be("O anexo boleto.pdf do título SOL-000123 está corrompido e não pôde ser exportado.");
            excecao.InnerException.Should().BeNull();
            excecao.Message.Should().NotContainAny("Base-64", "base-64", "FormatException", "System.");
        }

        [Fact]
        public async Task Gerar_Planilha_TemAsQuatroAbasNaOrdemComOsCabecalhosDoContrato()
        {
            using var pacote = await GerarAsync(FonteCompleta());
            using var planilha = pacote.Planilha();

            planilha.Worksheets.Select(w => w.Name).Should().Equal("Solicitações", "RDDVs", "Despesas dos RDDVs", "Anexos");

            Cabecalho(planilha.Worksheet("Solicitações")).Should().Equal(
                "Código", "Id", "Tipo de solicitação", "Tipo de pagamento", "Forma de pagamento", "Departamento", "Solicitante",
                "Atribuído a", "Gestor", "Data da solicitação", "Data do documento", "Data de vencimento", "Nº do documento",
                "Descrição", "Valor", "Parcelado", "Parcelas", "Centro de custo", "Projeto", "PCA", "Parceiro",
                "Documento do parceiro", "Código do parceiro", "Banco", "Agência", "Conta", "Observação", "Status gestor",
                "Data aprovação gestor", "Observação do gestor", "Status financeiro", "Data aprovação financeiro",
                "Observação do setor", "Status contábil", "Data aprovação contábil", "Qtde. de anexos");

            Cabecalho(planilha.Worksheet("RDDVs")).Should().Equal(
                "Código", "Id", "Tipo de relatório", "Funcionário", "CPF", "Departamento", "Solicitante", "Atribuído a", "Gestor",
                "Finalidade", "Tipo de viagem", "Local da viagem", "Data início", "Data fim", "Data de cadastro", "Data de vencimento",
                "Moeda", "Diária de viagem", "Diárias", "Valor da diária", "Valor do km", "Adiantamento", "Valor do adiantamento",
                "Saldo cartão VTM", "Valor líquido do RDDV", "Centro de custo", "Projeto", "PCA", "Banco", "Agência", "Conta",
                "Observação", "Status gestor", "Data aprovação gestor", "Observação do gestor", "Status financeiro",
                "Data aprovação financeiro", "Observação do setor", "Status contábil", "Data aprovação contábil", "Qtde. de anexos");

            Cabecalho(planilha.Worksheet("Despesas dos RDDVs")).Should().Equal(
                "Código do RDDV", "Id da despesa", "Data da despesa", "Tipo de despesa", "Moeda", "Quantidade", "Valor");

            Cabecalho(planilha.Worksheet("Anexos")).Should().Equal(
                "Código do título", "Tipo do título", "Tipo de documento", "Nome original", "Arquivo no pacote", "Tamanho (bytes)",
                "SHA-256", "Data de envio");

            foreach (var aba in planilha.Worksheets)
            {
                aba.Cell(1, 1).Style.Font.Bold.Should().BeTrue($"o cabeçalho da aba {aba.Name} é negrito");
                aba.SheetView.SplitRow.Should().Be(1, $"o cabeçalho da aba {aba.Name} fica congelado");
                aba.AutoFilter.IsEnabled.Should().BeTrue($"a aba {aba.Name} tem filtro automático");
            }
        }

        [Fact]
        public async Task Gerar_Planilha_CelulasTemOsTiposDoContrato()
        {
            using var pacote = await GerarAsync(FonteCompleta());
            using var planilha = pacote.Planilha();

            var sol = planilha.Worksheet("Solicitações");
            var linha123 = LinhaPorValor(sol, "SOL-000123");
            Celula(sol, linha123, "Id").DataType.Should().Be(XLDataType.Number);
            Celula(sol, linha123, "Id").GetValue<int>().Should().Be(123);
            Celula(sol, linha123, "Data da solicitação").DataType.Should().Be(XLDataType.DateTime);
            Celula(sol, linha123, "Data da solicitação").GetDateTime().Should().Be(new DateTime(2026, 11, 2));
            Celula(sol, linha123, "Data da solicitação").Style.NumberFormat.Format.Should().Be("dd/mm/yyyy");
            Celula(sol, linha123, "Data aprovação gestor").DataType.Should().Be(XLDataType.DateTime);
            Celula(sol, linha123, "Data aprovação gestor").GetDateTime().Should().Be(new DateTime(2026, 11, 2, 14, 5, 0));
            Celula(sol, linha123, "Data aprovação gestor").Style.NumberFormat.Format.Should().Be("dd/mm/yyyy hh:mm");
            Celula(sol, linha123, "Valor").DataType.Should().Be(XLDataType.Number);
            Celula(sol, linha123, "Valor").GetDouble().Should().Be(1234.56);
            Celula(sol, linha123, "Valor").Style.NumberFormat.Format.Should().Be("#,##0.00");
            Celula(sol, linha123, "Documento do parceiro").DataType.Should().Be(XLDataType.Text);
            Celula(sol, linha123, "Documento do parceiro").GetString().Should().Be("00.000.000/0000-00");
            Celula(sol, linha123, "Agência").DataType.Should().Be(XLDataType.Text);
            Celula(sol, linha123, "Agência").GetString().Should().Be("0001");
            Celula(sol, linha123, "Nº do documento").DataType.Should().Be(XLDataType.Text);
            Celula(sol, linha123, "Nº do documento").GetString().Should().Be("000123");
            Celula(sol, linha123, "Parcelado").GetString().Should().Be("Não");
            Celula(sol, linha123, "Parcelas").IsEmpty().Should().BeTrue("valor nulo sai como célula vazia");
            Celula(sol, linha123, "Data aprovação financeiro").IsEmpty().Should().BeTrue();
            Celula(sol, linha123, "Qtde. de anexos").DataType.Should().Be(XLDataType.Number);
            Celula(sol, linha123, "Qtde. de anexos").GetValue<int>().Should().Be(5);
            Celula(sol, LinhaPorValor(sol, "SOL-000124"), "Qtde. de anexos").GetValue<int>().Should().Be(0);

            var rddv = planilha.Worksheet("RDDVs");
            var linha45 = LinhaPorValor(rddv, "RDDV-000045");
            Celula(rddv, linha45, "CPF").DataType.Should().Be(XLDataType.Text);
            Celula(rddv, linha45, "CPF").GetString().Should().Be("000.000.000-00");
            Celula(rddv, linha45, "Data início").DataType.Should().Be(XLDataType.DateTime);
            Celula(rddv, linha45, "Data de cadastro").GetDateTime().Should().Be(new DateTime(2026, 10, 23));
            Celula(rddv, linha45, "Diárias").DataType.Should().Be(XLDataType.Number);
            Celula(rddv, linha45, "Valor líquido do RDDV").GetDouble().Should().Be(875.40);
            Celula(rddv, linha45, "Diária de viagem").GetString().Should().Be("Sim");
            Celula(rddv, linha45, "Adiantamento").IsEmpty().Should().BeTrue();
            Celula(rddv, linha45, "Data de vencimento").IsEmpty().Should().BeTrue();
            Celula(rddv, linha45, "Data aprovação contábil").DataType.Should().Be(XLDataType.DateTime);
            Celula(rddv, linha45, "Qtde. de anexos").GetValue<int>().Should().Be(2);

            var despesas = planilha.Worksheet("Despesas dos RDDVs");
            despesas.LastRowUsed()!.RowNumber().Should().Be(3);
            Celula(despesas, 2, "Código do RDDV").GetString().Should().Be("RDDV-000045");
            Celula(despesas, 2, "Id da despesa").DataType.Should().Be(XLDataType.Number);
            Celula(despesas, 2, "Data da despesa").DataType.Should().Be(XLDataType.DateTime);
            Celula(despesas, 2, "Valor").DataType.Should().Be(XLDataType.Number);
            Celula(despesas, 2, "Quantidade").DataType.Should().Be(XLDataType.Number);

            var anexos = planilha.Worksheet("Anexos");
            anexos.LastRowUsed()!.RowNumber().Should().Be(8);
            var linhaBoleto = LinhaPorValor(anexos, "SOL-000123/Nota Fiscal - boleto março.pdf", "Arquivo no pacote");
            Celula(anexos, linhaBoleto, "Tipo do título").GetString().Should().Be("Solicitação");
            Celula(anexos, linhaBoleto, "Tipo de documento").GetString().Should().Be("Nota Fiscal");
            Celula(anexos, linhaBoleto, "Nome original").GetString().Should().Be("boleto março.pdf");
            Celula(anexos, linhaBoleto, "Tamanho (bytes)").DataType.Should().Be(XLDataType.Number);
            Celula(anexos, linhaBoleto, "Tamanho (bytes)").GetValue<long>().Should().Be(1000);
            Celula(anexos, linhaBoleto, "SHA-256").GetString().Should().Be(Convert.ToHexStringLower(SHA256.HashData(Bytes(1000, 1))));
            Celula(anexos, linhaBoleto, "Data de envio").DataType.Should().Be(XLDataType.DateTime);
            Celula(anexos, linhaBoleto, "Data de envio").GetDateTime().Should().Be(new DateTime(2026, 11, 3, 10, 15, 0));
            var linhaSemTipo = LinhaPorValor(anexos, "SOL-000123/Documento - anexo-5", "Arquivo no pacote");
            Celula(anexos, linhaSemTipo, "Tipo de documento").GetString().Should().Be("Documento");
            Celula(anexos, LinhaPorValor(anexos, "RDDV-000045/Recibo - recibo.jpg", "Arquivo no pacote"), "Tipo do título")
                .GetString().Should().Be("RDDV");
        }

        [Fact]
        public async Task Gerar_Planilha_HiperlinksApontamParaEntradasExistentes()
        {
            using var pacote = await GerarAsync(FonteCompleta());
            using var planilha = pacote.Planilha();
            var entradas = pacote.Zip.Entries.Select(e => e.FullName).ToHashSet();

            var anexos = planilha.Worksheet("Anexos");
            var ultima = anexos.LastRowUsed()!.RowNumber();
            for (int linha = 2; linha <= ultima; linha++)
            {
                var celula = Celula(anexos, linha, "Arquivo no pacote");
                celula.HasHyperlink.Should().BeTrue();
                var hiperlink = celula.GetHyperlink();
                hiperlink.IsExternal.Should().BeTrue();
                hiperlink.ExternalAddress.IsAbsoluteUri.Should().BeFalse("o link é relativo à pasta extraída");
                var destino = Uri.UnescapeDataString(hiperlink.ExternalAddress.OriginalString);
                entradas.Should().Contain(destino);
                destino.Should().Be(celula.GetString());
            }

            var sol = planilha.Worksheet("Solicitações");
            var codigoComAnexos = Celula(sol, LinhaPorValor(sol, "SOL-000123"), "Código");
            codigoComAnexos.HasHyperlink.Should().BeTrue();
            var pasta = codigoComAnexos.GetHyperlink().ExternalAddress.OriginalString;
            pasta.Should().Be("SOL-000123/");
            entradas.Should().Contain(e => e.StartsWith(pasta, StringComparison.Ordinal));
            Celula(sol, LinhaPorValor(sol, "SOL-000124"), "Código").HasHyperlink.Should().BeFalse("título sem anexo não ganha pasta");

            var rddv = planilha.Worksheet("RDDVs");
            Celula(rddv, LinhaPorValor(rddv, "RDDV-000045"), "Código").GetHyperlink().ExternalAddress.OriginalString
                .Should().Be("RDDV-000045/");
        }

        [Fact]
        public async Task Gerar_Planilha_HiperlinkEscapaCerquilhaEPercentual()
        {
            var fonte = new FonteExportacaoFalsa();
            fonte.Solicitacoes.Add(FonteExportacaoFalsa.Solicitacao(1));
            fonte.AdicionarAnexo(TipoTituloExportacao.Solicitacao, 1, 1, "Nota Fiscal", "boleto #3 100%.pdf", Bytes(5, 1));

            using var pacote = await GerarAsync(fonte);
            using var planilha = pacote.Planilha();

            var celula = Celula(planilha.Worksheet("Anexos"), 2, "Arquivo no pacote");
            celula.GetString().Should().Be("SOL-000001/Nota Fiscal - boleto #3 100%.pdf");
            celula.GetHyperlink().ExternalAddress.OriginalString.Should().Be("SOL-000001/Nota Fiscal - boleto %233 100%25.pdf");
            pacote.Zip.GetEntry(Uri.UnescapeDataString(celula.GetHyperlink().ExternalAddress.OriginalString)).Should().NotBeNull();
        }

        [Fact]
        public async Task Gerar_IndiceCsv_TemBomSeparadorCabecalhoEOrdem()
        {
            using var pacote = await GerarAsync(FonteCompleta());

            var bytes = pacote.Ler("indice.csv");
            bytes.Take(3).Should().Equal(0xEF, 0xBB, 0xBF);

            var linhas = LinhasCsv(bytes);
            linhas[0].Should().Be("titulo;tipo_titulo;tipo_documento;nome_original;arquivo;caminho_relativo;tamanho_bytes;sha256");
            linhas.Should().HaveCount(8);

            var registros = linhas.Skip(1).Select(DividirCsv).ToList();
            registros.Should().AllSatisfy(r => r.Should().HaveCount(8));
            registros.Select(r => (r[0], r[4])).Should().BeInAscendingOrder(Comparer<(string, string)>.Create((a, b) =>
            {
                int porTitulo = string.CompareOrdinal(a.Item1, b.Item1);
                return porTitulo != 0 ? porTitulo : string.CompareOrdinal(a.Item2, b.Item2);
            }));

            var recibo = registros.Single(r => r[4] == "Recibo - recibo.jpg");
            recibo.Should().Equal("RDDV-000045", "Rddv", "Recibo", "recibo.jpg", "Recibo - recibo.jpg", "RDDV-000045/Recibo - recibo.jpg",
                "5000", Convert.ToHexStringLower(SHA256.HashData(Bytes(5000, 6))));

            // Campo com ";" sai entre aspas.
            linhas.Should().Contain(l => l.Contains("\"a/b\\c;d.pdf\"") && l.Contains("\"Comprovante - abc;d.pdf\"") && l.Contains("\"SOL-000123/Comprovante - abc;d.pdf\""));
            registros.Single(r => r[3] == "a/b\\c;d.pdf")[1].Should().Be("Solicitacao");
        }

        [Fact]
        public void CampoCsv_EscapaSeparadorAspasEQuebraDeLinha()
        {
            IndiceExportacao.CampoCsv("simples").Should().Be("simples");
            IndiceExportacao.CampoCsv("a;b").Should().Be("\"a;b\"");
            IndiceExportacao.CampoCsv("diz \"oi\"").Should().Be("\"diz \"\"oi\"\"\"");
            IndiceExportacao.CampoCsv("linha\nnova").Should().Be("\"linha\nnova\"");
            IndiceExportacao.CampoCsv(null).Should().BeEmpty();
        }

        [Theory]
        [InlineData("=1+1", "'=1+1")]
        [InlineData("+55 11", "'+55 11")]
        [InlineData("-2", "'-2")]
        [InlineData("@SOMA(A1)", "'@SOMA(A1)")]
        [InlineData("\tcom tab", "'\tcom tab")]
        [InlineData("normal", "normal")]
        [InlineData("meio=igual", "meio=igual")]
        public void CampoCsv_ProtegeContraInjecaoDeFormula(string valor, string esperado)
        {
            IndiceExportacao.CampoCsv(valor).Should().Be(esperado);
        }

        [Fact]
        public void CampoCsv_ValorComCrNoInicioRecebePrefixoEFicaEntreAspas()
        {
            IndiceExportacao.CampoCsv("\rabc").Should().Be("\"'\rabc\"");
        }

        [Fact]
        public void CampoCsv_PrefixoVemAntesDasAspasQuandoOValorTemSeparador()
        {
            IndiceExportacao.CampoCsv("=a;b").Should().Be("\"'=a;b\"");
        }

        [Fact]
        public async Task Gerar_IndiceCsv_ProtegeNomeETipoQueComecamComCaractereDeFormula_EOJsonFicaIntacto()
        {
            var fonte = new FonteExportacaoFalsa();
            fonte.Solicitacoes.Add(FonteExportacaoFalsa.Solicitacao(1));
            fonte.AdicionarAnexo(TipoTituloExportacao.Solicitacao, 1, 1, "=Tipo", "=HYPERLINK(\"http://exemplo.invalid\").pdf", Bytes(5, 1));
            fonte.AdicionarAnexo(TipoTituloExportacao.Solicitacao, 1, 2, "Nota Fiscal", "+1.pdf", Bytes(6, 2));
            fonte.AdicionarAnexo(TipoTituloExportacao.Solicitacao, 1, 3, "Nota Fiscal", "-2.pdf", Bytes(7, 3));
            fonte.AdicionarAnexo(TipoTituloExportacao.Solicitacao, 1, 4, "Nota Fiscal", "@a.pdf", Bytes(8, 4));
            fonte.AdicionarAnexo(TipoTituloExportacao.Solicitacao, 1, 5, "Nota Fiscal", "\tcom tab.pdf", Bytes(9, 5));
            fonte.AdicionarAnexo(TipoTituloExportacao.Solicitacao, 1, 6, "Nota Fiscal", "normal.pdf", Bytes(10, 6));

            using var pacote = await GerarAsync(fonte);

            var registros = LinhasCsv(pacote.Ler("indice.csv")).Skip(1).Select(DividirCsv).ToList();
            registros.Should().HaveCount(6);

            registros.Select(r => r[3]).Should().BeEquivalentTo(
                "'=HYPERLINK(\"http://exemplo.invalid\").pdf", "'+1.pdf", "'-2.pdf", "'@a.pdf", "'\tcom tab.pdf", "normal.pdf");
            registros.Single(r => r[3] == "normal.pdf")[2].Should().Be("Nota Fiscal");
            registros.Single(r => r[3].EndsWith("HYPERLINK(\"http://exemplo.invalid\").pdf"))[2].Should().Be("'=Tipo");
            registros.SelectMany(r => r).Should().OnlyContain(c => c.Length == 0 || !"=+-@\t\r".Contains(c[0]),
                "nenhuma célula do CSV pode começar com caractere que o Excel interpreta como fórmula");

            using var json = JsonDocument.Parse(pacote.Ler("indice.json"));
            json.RootElement.GetProperty("arquivos").EnumerateArray().Select(a => a.GetProperty("nomeOriginal").GetString())
                .Should().BeEquivalentTo("=HYPERLINK(\"http://exemplo.invalid\").pdf", "+1.pdf", "-2.pdf", "@a.pdf", "\tcom tab.pdf", "normal.pdf");
        }

        [Fact]
        public async Task Gerar_LeiaMe_MostraRotulosNosFiltrosDeStatusETipos()
        {
            var filtro = new FiltroExportacaoDto
            {
                StatusGestor = 2,
                StatusFinanceiro = 3,
                StatusContabil = 2,
                TipoSolicitacao = 2,
                TipoPagamento = 1,
                FormaPagamento = 2,
                TipoRelatorio = 3
            };

            using var pacote = await GerarAsync(FonteCompleta(), filtro);

            var texto = Encoding.UTF8.GetString(pacote.Ler("LEIA-ME.txt"));
            texto.Should().Contain("Status gestor: Autorizado");
            texto.Should().Contain("Status financeiro: Negado");
            texto.Should().Contain("Status contábil: Lançado");
            texto.Should().Contain("Tipo de solicitação: Pagamento");
            texto.Should().Contain("Tipo de pagamento: Nacional");
            texto.Should().Contain("Forma de pagamento: Transferência Bancária");
            texto.Should().Contain("Tipo de relatório do RDDV: Reembolso");
            texto.Should().NotContain("(código)");
        }

        [Fact]
        public async Task Gerar_LeiaMe_CodigoSemRotuloConhecidoSaiComoCodigoN()
        {
            var filtro = new FiltroExportacaoDto { StatusGestor = 9, TipoPagamento = 7 };

            using var pacote = await GerarAsync(FonteCompleta(), filtro);

            var texto = Encoding.UTF8.GetString(pacote.Ler("LEIA-ME.txt"));
            texto.Should().Contain("Status gestor: Código 9");
            texto.Should().Contain("Tipo de pagamento: Código 7");
        }

        [Fact]
        public async Task Gerar_IndiceJson_TemOFormatoDoContrato()
        {
            var filtro = new FiltroExportacaoDto
            {
                DataSolicitacaoInicio = new DateTime(2026, 11, 1),
                NomeParceiro = "Fictício",
                StatusFinanceiro = 2
            };
            using var pacote = await GerarAsync(FonteCompleta(), filtro, idExportacao: 42);

            var bytes = pacote.Ler("indice.json");
            bytes.Take(3).Should().NotEqual(new byte[] { 0xEF, 0xBB, 0xBF }, "o JSON é UTF-8 sem BOM");
            Encoding.UTF8.GetString(bytes).Should().Contain("\n  \"versao\": 1", "o JSON é indentado");

            using var json = JsonDocument.Parse(bytes);
            var raiz = json.RootElement;
            raiz.EnumerateObject().Select(p => p.Name).Should().Equal("versao", "exportacaoId", "geradoEm", "filtros", "totais", "titulos", "arquivos");
            raiz.GetProperty("versao").GetInt32().Should().Be(1);
            raiz.GetProperty("exportacaoId").GetInt32().Should().Be(42);
            var geradoEm = raiz.GetProperty("geradoEm").GetString()!;
            geradoEm.Should().MatchRegex(@"^\d{4}-\d{2}-\d{2}T\d{2}:\d{2}:\d{2}([+-]\d{2}:\d{2}|Z)$");
            DateTimeOffset.Parse(geradoEm).Should().BeCloseTo(DateTimeOffset.Now, TimeSpan.FromMinutes(5));

            var filtros = raiz.GetProperty("filtros");
            filtros.GetProperty("tipoTitulo").GetInt32().Should().Be(0);
            filtros.GetProperty("nomeParceiro").GetString().Should().Be("Fictício");
            filtros.GetProperty("statusFinanceiro").GetInt32().Should().Be(2);
            filtros.GetProperty("incluirAnexos").GetBoolean().Should().BeTrue();
            filtros.GetProperty("dataSolicitacaoInicio").GetDateTime().Should().Be(new DateTime(2026, 11, 1));

            var totais = raiz.GetProperty("totais");
            totais.GetProperty("solicitacoes").GetInt32().Should().Be(2);
            totais.GetProperty("rddvs").GetInt32().Should().Be(1);
            totais.GetProperty("despesas").GetInt32().Should().Be(2);
            totais.GetProperty("anexos").GetInt32().Should().Be(7);
            totais.GetProperty("bytesAnexos").GetInt64().Should().Be(6100);

            var titulos = raiz.GetProperty("titulos").EnumerateArray().ToList();
            titulos.Select(t => t.GetProperty("codigo").GetString()).Should().BeEquivalentTo("SOL-000123", "SOL-000124", "RDDV-000045");
            var sol123 = titulos.Single(t => t.GetProperty("codigo").GetString() == "SOL-000123");
            sol123.GetProperty("tipo").GetString().Should().Be("Solicitacao");
            sol123.GetProperty("id").GetInt32().Should().Be(123);
            sol123.GetProperty("pasta").GetString().Should().Be("SOL-000123/");
            sol123.GetProperty("quantidadeAnexos").GetInt32().Should().Be(5);
            var sol124 = titulos.Single(t => t.GetProperty("codigo").GetString() == "SOL-000124");
            sol124.GetProperty("pasta").ValueKind.Should().Be(JsonValueKind.Null);
            sol124.GetProperty("quantidadeAnexos").GetInt32().Should().Be(0);
            titulos.Single(t => t.GetProperty("codigo").GetString() == "RDDV-000045").GetProperty("tipo").GetString().Should().Be("Rddv");

            var arquivos = raiz.GetProperty("arquivos").EnumerateArray().ToList();
            arquivos.Should().HaveCount(7);
            var boleto = arquivos.Single(a => a.GetProperty("arquivo").GetString() == "Nota Fiscal - boleto março.pdf");
            boleto.EnumerateObject().Select(p => p.Name).Should().Equal(
                "titulo", "tipoTitulo", "tipoDocumento", "nomeOriginal", "arquivo", "caminhoRelativo", "tamanhoBytes", "sha256");
            boleto.GetProperty("titulo").GetString().Should().Be("SOL-000123");
            boleto.GetProperty("tipoTitulo").GetString().Should().Be("Solicitacao");
            boleto.GetProperty("tipoDocumento").GetString().Should().Be("Nota Fiscal");
            boleto.GetProperty("nomeOriginal").GetString().Should().Be("boleto março.pdf");
            boleto.GetProperty("caminhoRelativo").GetString().Should().Be("SOL-000123/Nota Fiscal - boleto março.pdf");
            boleto.GetProperty("tamanhoBytes").GetInt64().Should().Be(1000);

            // Mesma ordem do CSV.
            var caminhosCsv = LinhasCsv(pacote.Ler("indice.csv")).Skip(1).Select(l => DividirCsv(l)[5]);
            arquivos.Select(a => a.GetProperty("caminhoRelativo").GetString()).Should().Equal(caminhosCsv);
        }

        [Fact]
        public async Task Gerar_SemAnexos_NaoCriaPastasEDeixaAnexosVazios()
        {
            var fonte = FonteCompleta();

            using var pacote = await GerarAsync(fonte, new FiltroExportacaoDto { IncluirAnexos = false });

            pacote.Zip.Entries.Select(e => e.FullName).Should().BeEquivalentTo(EntradasDaRaiz);
            fonte.ChamadasListarAnexos.Should().Be(0);
            fonte.LeiturasDeConteudo.Should().Be(0);

            using var planilha = pacote.Planilha();
            planilha.Worksheet("Anexos").LastRowUsed()!.RowNumber().Should().Be(1);
            planilha.Worksheet("Solicitações").LastRowUsed()!.RowNumber().Should().Be(3);
            var sol = planilha.Worksheet("Solicitações");
            Celula(sol, LinhaPorValor(sol, "SOL-000123"), "Código").HasHyperlink.Should().BeFalse();

            using var json = JsonDocument.Parse(pacote.Ler("indice.json"));
            json.RootElement.GetProperty("arquivos").GetArrayLength().Should().Be(0);
            json.RootElement.GetProperty("titulos").EnumerateArray()
                .Should().AllSatisfy(t => t.GetProperty("pasta").ValueKind.Should().Be(JsonValueKind.Null));
            LinhasCsv(pacote.Ler("indice.csv")).Should().HaveCount(1);
            pacote.Resultado.TotalAnexos.Should().Be(0);
            pacote.Resultado.BytesAnexos.Should().Be(0);
        }

        [Fact]
        public async Task Gerar_TipoTituloSolicitacao_NaoListaRddvs()
        {
            using var pacote = await GerarAsync(FonteCompleta(), new FiltroExportacaoDto { TipoTitulo = TipoTituloExportacao.Solicitacao });

            pacote.Zip.Entries.Should().NotContain(e => e.FullName.StartsWith("RDDV-", StringComparison.Ordinal));
            using var planilha = pacote.Planilha();
            planilha.Worksheet("RDDVs").LastRowUsed()!.RowNumber().Should().Be(1);
            planilha.Worksheet("Despesas dos RDDVs").LastRowUsed()!.RowNumber().Should().Be(1);
            pacote.Resultado.TotalTitulos.Should().Be(2);
        }

        [Fact]
        public async Task Gerar_LeAnexosUmDeCadaVez()
        {
            var fonte = FonteCompleta();

            using var pacote = await GerarAsync(fonte);

            fonte.LeiturasDeConteudo.Should().Be(7);
            fonte.MaximoLeiturasSimultaneas.Should().Be(1);
        }

        [Fact]
        public async Task Gerar_AnexoRemovidoDepoisDaListagem_FicaForaDoPacote()
        {
            var fonte = FonteCompleta();
            fonte.Conteudos[2] = null;

            using var pacote = await GerarAsync(fonte);

            pacote.Zip.GetEntry("SOL-000123/Nota Fiscal - anexo-2.etcpasswd").Should().BeNull();
            pacote.Resultado.TotalAnexos.Should().Be(6);
            using var json = JsonDocument.Parse(pacote.Ler("indice.json"));
            json.RootElement.GetProperty("arquivos").GetArrayLength().Should().Be(6);
            LinhasCsv(pacote.Ler("indice.csv")).Should().HaveCount(7);
        }

        [Fact]
        public async Task Gerar_ReportaProgressoAposCadaTitulo()
        {
            var progresso = new ProgressoSincrono();

            using var pacote = await GerarAsync(FonteCompleta(), progresso: progresso);

            progresso.Relatos.Should().Equal(
                new ProgressoExportacao(0, 3), new ProgressoExportacao(1, 3), new ProgressoExportacao(2, 3), new ProgressoExportacao(3, 3));
        }

        [Fact]
        public async Task Gerar_SomaDeBytesAcimaDoLimite_LancaExportacaoException()
        {
            var fonte = FonteCompleta();
            var gerador = CriarGerador(fonte, new ExportacaoSettings { MaxBytesPorExportacao = 3000 });

            var acao = async () => await gerador.GerarAsync(1, new FiltroExportacaoDto(), new MemoryStream(), null, CancellationToken.None);

            var excecao = (await acao.Should().ThrowExactlyAsync<ExportacaoException>()).Which;
            excecao.Message.Should().Contain("limite").And.Contain("filtros menores");
            excecao.InnerException.Should().BeNull();
        }

        [Fact]
        public async Task Gerar_CancelamentoEntreAnexos_Interrompe()
        {
            var fonte = FonteCompleta();
            using var cts = new CancellationTokenSource();
            fonte.AposLeitura = _ => cts.Cancel();

            var acao = async () => await CriarGerador(fonte).GerarAsync(1, new FiltroExportacaoDto(), new MemoryStream(), null, cts.Token);

            await acao.Should().ThrowAsync<OperationCanceledException>();
            fonte.LeiturasDeConteudo.Should().Be(1);
        }

        [Fact]
        public async Task Gerar_LeiaMe_ExplicaAExtracaoEListaOsFiltros()
        {
            var filtro = new FiltroExportacaoDto
            {
                TipoTitulo = TipoTituloExportacao.Solicitacao,
                DataVencimentoInicio = new DateTime(2026, 11, 1),
                DataVencimentoFim = new DateTime(2026, 11, 30),
                NomeParceiro = "Fictício"
            };

            using var pacote = await GerarAsync(FonteCompleta(), filtro, idExportacao: 7);

            var bytes = pacote.Ler("LEIA-ME.txt");
            bytes.Take(3).Should().Equal(0xEF, 0xBB, 0xBF);
            var texto = Encoding.UTF8.GetString(bytes);
            texto.Should().Contain("extraia o arquivo ZIP");
            texto.Should().Contain("antes de abrir o Excel");
            texto.Should().Contain("Exportação nº 7");
            texto.Should().Contain("titulos.xlsx").And.Contain("indice.csv").And.Contain("indice.json");
            texto.Should().Contain("somente solicitações");
            texto.Should().Contain("Data de vencimento: de 01/11/2026 a 30/11/2026");
            texto.Should().Contain("Parceiro ou funcionário contém: Fictício");
            texto.Should().MatchRegex(@"gerada em \d{2}/\d{2}/\d{4} \d{2}:\d{2}");
        }

        private static List<string> Cabecalho(IXLWorksheet aba) =>
            aba.Row(1).CellsUsed().Select(c => c.GetString()).ToList();

        private static IXLCell Celula(IXLWorksheet aba, int linha, string coluna)
        {
            var indice = Cabecalho(aba).IndexOf(coluna);
            indice.Should().BeGreaterThanOrEqualTo(0, $"a coluna '{coluna}' existe na aba {aba.Name}");
            return aba.Cell(linha, indice + 1);
        }

        private static int LinhaPorValor(IXLWorksheet aba, string valor, string coluna = "Código")
        {
            var indice = Cabecalho(aba).IndexOf(coluna) + 1;
            var ultima = aba.LastRowUsed()!.RowNumber();
            for (int linha = 2; linha <= ultima; linha++)
            {
                if (aba.Cell(linha, indice).GetString() == valor)
                {
                    return linha;
                }
            }

            throw new InvalidOperationException($"Valor '{valor}' não encontrado na coluna '{coluna}' da aba {aba.Name}.");
        }

        private static List<string> LinhasCsv(byte[] bytes)
        {
            var texto = new UTF8Encoding(false).GetString(bytes, 3, bytes.Length - 3);
            return texto.Split("\r\n", StringSplitOptions.RemoveEmptyEntries).ToList();
        }

        private static List<string> DividirCsv(string linha)
        {
            var campos = new List<string>();
            var atual = new StringBuilder();
            bool entreAspas = false;
            for (int i = 0; i < linha.Length; i++)
            {
                char c = linha[i];
                if (entreAspas)
                {
                    if (c == '"' && i + 1 < linha.Length && linha[i + 1] == '"')
                    {
                        atual.Append('"');
                        i++;
                    }
                    else if (c == '"')
                    {
                        entreAspas = false;
                    }
                    else
                    {
                        atual.Append(c);
                    }
                }
                else if (c == '"')
                {
                    entreAspas = true;
                }
                else if (c == ';')
                {
                    campos.Add(atual.ToString());
                    atual.Clear();
                }
                else
                {
                    atual.Append(c);
                }
            }

            campos.Add(atual.ToString());
            return campos;
        }
    }
}
