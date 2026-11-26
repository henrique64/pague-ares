using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using Ares.PagueAres.Domain.Dtos.Exportacao;
using Ares.PagueAres.Domain.Enums;

namespace Ares.PagueAres.Application.Exportacao
{
    /// <summary>Anexo já escrito no pacote, com tamanho e SHA-256 calculados durante a escrita.</summary>
    public sealed record ArquivoPacoteExportacao(
        string CodigoTitulo, TipoTituloExportacao TipoTitulo, string TipoDocumento, string NomeOriginal,
        string Arquivo, string CaminhoRelativo, long TamanhoBytes, string Sha256, DateTime DataCriacao);

    /// <summary>Título exportado, com a quantidade de anexos que entraram no pacote.</summary>
    public sealed record TituloPacoteExportacao(string Codigo, TipoTituloExportacao Tipo, int Id, int QuantidadeAnexos);

    /// <summary>Totais do pacote, gravados no <c>indice.json</c>.</summary>
    public sealed record TotaisPacoteExportacao(int Solicitacoes, int Rddvs, int Despesas, int Anexos, long BytesAnexos);

    /// <summary>Índices do pacote (contrato C4): <c>indice.csv</c> e <c>indice.json</c>.</summary>
    public static class IndiceExportacao
    {
        public const string CabecalhoCsv = "titulo;tipo_titulo;tipo_documento;nome_original;arquivo;caminho_relativo;tamanho_bytes;sha256";

        private static readonly byte[] BomUtf8 = [0xEF, 0xBB, 0xBF];

        private static readonly JsonSerializerOptions OpcoesJson = new()
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            WriteIndented = true,
            NewLine = "\n",
            // O arquivo não é HTML: acentos e aspas saem legíveis, sem escape \uXXXX.
            Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
        };

        /// <summary>Nome do tipo de título nos índices, o mesmo da aba Anexos da planilha: <c>Solicitação</c> ou <c>RDDV</c>.</summary>
        public static string NomeTipoTitulo(TipoTituloExportacao tipo) => tipo switch
        {
            TipoTituloExportacao.Solicitacao => "Solicitação",
            TipoTituloExportacao.Rddv => "RDDV",
            _ => throw new ArgumentOutOfRangeException(nameof(tipo), tipo, "O tipo de título deve ser Solicitacao ou Rddv.")
        };

        /// <summary>
        /// Escreve o <c>indice.csv</c>: UTF-8 com BOM, separador <c>;</c>, linhas terminadas em CRLF, uma linha por
        /// anexo na ordem recebida (o gerador já entrega ordenado por título e depois por arquivo).
        /// </summary>
        public static async Task EscreverCsvAsync(Stream destino, IEnumerable<ArquivoPacoteExportacao> arquivos, CancellationToken ct)
        {
            ArgumentNullException.ThrowIfNull(destino);
            ArgumentNullException.ThrowIfNull(arquivos);

            await destino.WriteAsync(BomUtf8, ct);

            await using var escritor = new StreamWriter(destino, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false), 64 * 1024, leaveOpen: true)
            {
                NewLine = "\r\n"
            };

            await escritor.WriteLineAsync(CabecalhoCsv);
            foreach (var arquivo in arquivos)
            {
                ct.ThrowIfCancellationRequested();
                await escritor.WriteLineAsync(string.Join(';',
                    CampoCsv(arquivo.CodigoTitulo),
                    CampoCsv(NomeTipoTitulo(arquivo.TipoTitulo)),
                    CampoCsv(arquivo.TipoDocumento),
                    CampoCsv(arquivo.NomeOriginal),
                    CampoCsv(arquivo.Arquivo),
                    CampoCsv(arquivo.CaminhoRelativo),
                    arquivo.TamanhoBytes.ToString(System.Globalization.CultureInfo.InvariantCulture),
                    CampoCsv(arquivo.Sha256)));
            }
        }

        /// <summary>
        /// Caracteres que, no início de uma célula, fazem o Excel interpretar o texto como fórmula (injeção de fórmula
        /// por nomes de arquivo ou tipos de documento digitados pelo usuário).
        /// </summary>
        private static bool ComecaComoFormula(char c) => c is '=' or '+' or '-' or '@' or '\t' or '\r';

        /// <summary>
        /// Campo de texto do CSV. Valor que começa com <c>=</c>, <c>+</c>, <c>-</c>, <c>@</c>, tabulação ou CR recebe o prefixo
        /// <c>'</c> para o Excel não tratá-lo como fórmula (só no CSV: o JSON e a planilha guardam o valor original).
        /// Fica entre aspas duplas (com aspas internas dobradas) se contém <c>;</c>, <c>"</c> ou quebra de linha.
        /// </summary>
        public static string CampoCsv(string? valor)
        {
            if (string.IsNullOrEmpty(valor))
            {
                return "";
            }

            if (ComecaComoFormula(valor[0]))
            {
                valor = "'" + valor;
            }

            if (valor.AsSpan().IndexOfAny(";\"\r\n") < 0)
            {
                return valor;
            }

            return "\"" + valor.Replace("\"", "\"\"", StringComparison.Ordinal) + "\"";
        }

        /// <summary>
        /// Escreve o <c>indice.json</c> (UTF-8 sem BOM, camelCase, indentado). <paramref name="titulos"/> traz todos os
        /// títulos exportados, inclusive os sem anexo (com <c>pasta: null</c>); <paramref name="arquivos"/> vai na
        /// mesma ordem do CSV.
        /// </summary>
        public static Task EscreverJsonAsync(Stream destino, int idExportacao, DateTimeOffset geradoEm, FiltroExportacaoDto filtros,
            TotaisPacoteExportacao totais, IEnumerable<TituloPacoteExportacao> titulos, IEnumerable<ArquivoPacoteExportacao> arquivos,
            CancellationToken ct)
        {
            ArgumentNullException.ThrowIfNull(destino);
            ArgumentNullException.ThrowIfNull(filtros);
            ArgumentNullException.ThrowIfNull(totais);
            ArgumentNullException.ThrowIfNull(titulos);
            ArgumentNullException.ThrowIfNull(arquivos);

            var documento = new DocumentoIndiceJson(
                Versao: 1,
                ExportacaoId: idExportacao,
                GeradoEm: geradoEm,
                Filtros: filtros,
                Totais: totais,
                Titulos: titulos.Select(t => new TituloIndiceJson(
                    t.Codigo, NomeTipoTitulo(t.Tipo), t.Id, t.QuantidadeAnexos > 0 ? t.Codigo + "/" : null, t.QuantidadeAnexos)),
                Arquivos: arquivos.Select(a => new ArquivoIndiceJson(
                    a.CodigoTitulo, NomeTipoTitulo(a.TipoTitulo), a.TipoDocumento, a.NomeOriginal, a.Arquivo,
                    a.CaminhoRelativo, a.TamanhoBytes, a.Sha256)));

            return JsonSerializer.SerializeAsync(destino, documento, OpcoesJson, ct);
        }

        private sealed record DocumentoIndiceJson(
            int Versao, int ExportacaoId, DateTimeOffset GeradoEm, FiltroExportacaoDto Filtros, TotaisPacoteExportacao Totais,
            IEnumerable<TituloIndiceJson> Titulos, IEnumerable<ArquivoIndiceJson> Arquivos);

        private sealed record TituloIndiceJson(string Codigo, string Tipo, int Id, string? Pasta, int QuantidadeAnexos);

        private sealed record ArquivoIndiceJson(
            string Titulo, string TipoTitulo, string TipoDocumento, string NomeOriginal, string Arquivo,
            string CaminhoRelativo, long TamanhoBytes, string Sha256);
    }
}
