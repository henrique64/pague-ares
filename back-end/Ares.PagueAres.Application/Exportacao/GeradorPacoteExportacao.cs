using System.Globalization;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using Ares.PagueAres.Application.Configuration;
using Ares.PagueAres.Domain.Dtos.Exportacao;
using Ares.PagueAres.Domain.Enums;
using Ares.PagueAres.Domain.Exportacao;
using Microsoft.Extensions.Options;

namespace Ares.PagueAres.Application.Exportacao
{
    /// <summary>
    /// Gera o pacote ZIP da exportação (contrato C2) direto no stream de destino: primeiro os anexos, lidos um a um
    /// da fonte e decodificados em blocos (com tamanho e SHA-256 calculados na escrita); depois a planilha
    /// <c>titulos.xlsx</c>, os índices <c>indice.csv</c> e <c>indice.json</c> e o <c>LEIA-ME.txt</c>. Em memória
    /// ficam só os metadados e um anexo por vez.
    /// </summary>
    public sealed class GeradorPacoteExportacao : IGeradorPacoteExportacao
    {
        public const string ArquivoPlanilha = "titulos.xlsx";
        public const string ArquivoIndiceCsv = "indice.csv";
        public const string ArquivoIndiceJson = "indice.json";
        public const string ArquivoLeiaMe = "LEIA-ME.txt";

        private static readonly byte[] BomUtf8 = [0xEF, 0xBB, 0xBF];

        private readonly IFonteExportacao _fonte;
        private readonly ExportacaoSettings _settings;

        public GeradorPacoteExportacao(IFonteExportacao fonte, IOptions<ExportacaoSettings> settings)
        {
            _fonte = fonte;
            _settings = settings.Value;
        }

        private sealed record TituloPlanejado(string Codigo, TipoTituloExportacao Tipo, int Id);

        private sealed record AnexoPlanejado(AnexoExportacao Anexo, string TipoDocumento, string Arquivo, string Caminho);

        public async Task<ResultadoPacoteExportacao> GerarAsync(int idExportacao, FiltroExportacaoDto filtro, Stream destino,
            IProgress<ProgressoExportacao>? progresso, CancellationToken ct)
        {
            ArgumentNullException.ThrowIfNull(filtro);
            ArgumentNullException.ThrowIfNull(destino);

            var agora = DateTimeOffset.Now;
            var geradoEm = new DateTimeOffset(agora.Year, agora.Month, agora.Day, agora.Hour, agora.Minute, agora.Second, agora.Offset);

            IReadOnlyList<SolicitacaoExportacao> solicitacoes = filtro.TipoTitulo == TipoTituloExportacao.Rddv
                ? []
                : await _fonte.ListarSolicitacoesAsync(filtro, ct);
            IReadOnlyList<RddvExportacao> rddvs = filtro.TipoTitulo == TipoTituloExportacao.Solicitacao
                ? []
                : await _fonte.ListarRddvsAsync(filtro, ct);

            var idsRddv = rddvs.Select(r => r.Id).Distinct().ToList();
            IReadOnlyList<DespesaRddvExportacao> despesas = idsRddv.Count == 0
                ? []
                : await _fonte.ListarDespesasAsync(idsRddv, ct);

            // O código (e, com ele, o nome da pasta) é sempre recalculado a partir do tipo e do id, para que o caminho
            // das entradas não dependa de texto vindo da fonte. A ordem é a dos índices: por código.
            var titulos = solicitacoes.Select(s => (Tipo: TipoTituloExportacao.Solicitacao, s.Id))
                .Concat(rddvs.Select(r => (Tipo: TipoTituloExportacao.Rddv, r.Id)))
                .Distinct()
                .Select(t => new TituloPlanejado(CodigoTitulo.Formatar(t.Tipo, t.Id), t.Tipo, t.Id))
                .OrderBy(t => t.Codigo, StringComparer.Ordinal)
                .ToList();

            var anexosPorTitulo = filtro.IncluirAnexos
                ? await PlanejarAnexosAsync(titulos, ct)
                : new Dictionary<string, List<AnexoPlanejado>>(StringComparer.Ordinal);

            var arquivos = new List<ArquivoPacoteExportacao>();
            var quantidadePorTitulo = new Dictionary<string, int>(StringComparer.Ordinal);
            long bytesAnexos = 0;
            long limiteBytes = _settings.MaxBytesPorExportacao;
            int processados = 0;

            progresso?.Report(new ProgressoExportacao(0, titulos.Count));

            using (var zip = new ZipArchive(destino, ZipArchiveMode.Create, leaveOpen: true))
            {
                using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);

                foreach (var titulo in titulos)
                {
                    if (anexosPorTitulo.TryGetValue(titulo.Codigo, out var planejados))
                    {
                        foreach (var planejado in planejados)
                        {
                            ct.ThrowIfCancellationRequested();

                            var arquivo = await EscreverAnexoAsync(zip, titulo, planejado, hash, ct);
                            if (arquivo is null)
                            {
                                continue;
                            }

                            bytesAnexos += arquivo.TamanhoBytes;
                            if (limiteBytes > 0 && bytesAnexos > limiteBytes)
                            {
                                throw new ExportacaoException(
                                    $"A exportação passou do limite de {FormatarTamanho(limiteBytes)} de anexos por pacote. " +
                                    "Gere de novo com filtros menores, por exemplo um período mais curto.");
                            }

                            arquivos.Add(arquivo);
                        }
                    }

                    processados++;
                    progresso?.Report(new ProgressoExportacao(processados, titulos.Count));
                }

                ct.ThrowIfCancellationRequested();

                // O ClosedXML monta a planilha inteira em memória (só metadados, sem anexos) e precisa de um stream
                // com posicionamento para salvar; a entrada do ZIP não tem, então a planilha passa por um buffer.
                using (var planilha = new MemoryStream())
                {
                    PlanilhaExportacao.Escrever(planilha, solicitacoes, rddvs, despesas, arquivos);
                    planilha.Position = 0;
                    await using var entrada = zip.CreateEntry(ArquivoPlanilha, CompressionLevel.Fastest).Open();
                    await planilha.CopyToAsync(entrada, ct);
                }

                await using (var entrada = zip.CreateEntry(ArquivoIndiceCsv, CompressionLevel.Fastest).Open())
                {
                    await IndiceExportacao.EscreverCsvAsync(entrada, arquivos, ct);
                }

                var totais = new TotaisPacoteExportacao(solicitacoes.Count, rddvs.Count, despesas.Count, arquivos.Count, bytesAnexos);
                foreach (var arquivo in arquivos)
                {
                    quantidadePorTitulo[arquivo.CodigoTitulo] = quantidadePorTitulo.GetValueOrDefault(arquivo.CodigoTitulo) + 1;
                }
                var titulosIndice = titulos
                    .Select(t => new TituloPacoteExportacao(t.Codigo, t.Tipo, t.Id, quantidadePorTitulo.GetValueOrDefault(t.Codigo)))
                    .ToList();

                await using (var entrada = zip.CreateEntry(ArquivoIndiceJson, CompressionLevel.Fastest).Open())
                {
                    await IndiceExportacao.EscreverJsonAsync(entrada, idExportacao, geradoEm, filtro, totais, titulosIndice, arquivos, ct);
                }

                await using (var entrada = zip.CreateEntry(ArquivoLeiaMe, CompressionLevel.Fastest).Open())
                {
                    await entrada.WriteAsync(BomUtf8, ct);
                    await entrada.WriteAsync(new UTF8Encoding(false).GetBytes(MontarLeiaMe(idExportacao, geradoEm, filtro, totais)), ct);
                }
            }

            return new ResultadoPacoteExportacao(titulos.Count, arquivos.Count, bytesAnexos);
        }

        /// <summary>
        /// Lista os metadados dos anexos e define o nome de cada um dentro da pasta do título: monta pelo rótulo e
        /// pelo nome original (na ordem de envio, para o primeiro ficar sem sufixo), deduplica e ordena pelo nome final.
        /// </summary>
        private async Task<Dictionary<string, List<AnexoPlanejado>>> PlanejarAnexosAsync(List<TituloPlanejado> titulos, CancellationToken ct)
        {
            var resultado = new Dictionary<string, List<AnexoPlanejado>>(StringComparer.Ordinal);

            foreach (var tipo in new[] { TipoTituloExportacao.Solicitacao, TipoTituloExportacao.Rddv })
            {
                var ids = titulos.Where(t => t.Tipo == tipo).Select(t => t.Id).ToList();
                if (ids.Count == 0)
                {
                    continue;
                }

                var idsExportados = ids.ToHashSet();
                var anexos = await _fonte.ListarAnexosAsync(tipo, ids, ct);

                foreach (var grupo in anexos.Where(a => idsExportados.Contains(a.IdTitulo)).DistinctBy(a => a.IdAnexo).GroupBy(a => a.IdTitulo))
                {
                    var codigo = CodigoTitulo.Formatar(tipo, grupo.Key);
                    var ordenados = grupo.OrderBy(a => a.IdAnexo).ToList();
                    var nomes = NomesPacote.Deduplicar(
                        ordenados.Select(a => NomesPacote.MontarNomeAnexo(a.TipoDocumento, a.NomeOriginal, a.IdAnexo)));

                    resultado[codigo] = ordenados
                        .Select((a, i) => new AnexoPlanejado(a, RotuloTipoDocumento(a.TipoDocumento), nomes[i], NomesPacote.CaminhoEntrada(codigo, nomes[i])))
                        .OrderBy(p => p.Arquivo, StringComparer.Ordinal)
                        .ToList();
                }
            }

            return resultado;
        }

        /// <summary>
        /// Lê um anexo da fonte e o escreve no ZIP. Devolve <c>null</c> quando o anexo deixou de existir entre a listagem
        /// e a leitura (ele fica fora do pacote, dos índices e da planilha).
        /// </summary>
        private async Task<ArquivoPacoteExportacao?> EscreverAnexoAsync(ZipArchive zip, TituloPlanejado titulo, AnexoPlanejado planejado,
            IncrementalHash hash, CancellationToken ct)
        {
            var conteudo = await _fonte.LerConteudoBase64Async(titulo.Tipo, planejado.Anexo.IdAnexo, ct);
            if (conteudo is null)
            {
                return null;
            }

            long tamanho;
            await using (var entrada = zip.CreateEntry(planejado.Caminho, CompressionLevel.Fastest).Open())
            {
                try
                {
                    tamanho = await DecodificadorBase64.DecodificarAsync(conteudo, entrada, hash, ct);
                }
                catch (FormatException)
                {
                    throw new ExportacaoException(
                        $"O anexo {NomeParaMensagem(planejado)} do título {titulo.Codigo} está corrompido e não pôde ser exportado.");
                }
            }

            return new ArquivoPacoteExportacao(
                titulo.Codigo, titulo.Tipo, planejado.TipoDocumento, planejado.Anexo.NomeOriginal ?? "",
                planejado.Arquivo, planejado.Caminho, tamanho, Convert.ToHexStringLower(hash.GetHashAndReset()),
                planejado.Anexo.DataCriacao);
        }

        private static string RotuloTipoDocumento(string? tipoDocumento) =>
            string.IsNullOrWhiteSpace(tipoDocumento) ? NomesPacote.RotuloPadrao : tipoDocumento.Trim();

        private static string NomeParaMensagem(AnexoPlanejado planejado)
        {
            var nome = NomesPacote.Sanitizar(planejado.Anexo.NomeOriginal);
            if (nome.Length == 0)
            {
                nome = planejado.Arquivo;
            }

            if (nome.Length > NomesPacote.TamanhoMaximoNome)
            {
                int corte = char.IsHighSurrogate(nome[NomesPacote.TamanhoMaximoNome - 1]) ? NomesPacote.TamanhoMaximoNome - 1 : NomesPacote.TamanhoMaximoNome;
                nome = nome[..corte];
            }

            return nome;
        }

        private static string FormatarTamanho(long bytes)
        {
            const double Gib = 1024d * 1024 * 1024;
            const double Mib = 1024d * 1024;

            static string Numero(double valor) => valor.ToString("0.#", CultureInfo.InvariantCulture).Replace('.', ',');

            if (bytes >= Gib)
            {
                return Numero(bytes / Gib) + " GB";
            }

            if (bytes >= Mib)
            {
                return Numero(bytes / Mib) + " MB";
            }

            return bytes.ToString(CultureInfo.InvariantCulture) + " bytes";
        }

        private static string MontarLeiaMe(int idExportacao, DateTimeOffset geradoEm, FiltroExportacaoDto filtro, TotaisPacoteExportacao totais)
        {
            var deslocamento = geradoEm.Offset;
            var sinal = deslocamento < TimeSpan.Zero ? "-" : "+";
            var fuso = $"UTC{sinal}{deslocamento.Duration():hh\\:mm}";

            var texto = new StringBuilder();
            texto.Append("PagueAres - Exportação de títulos com anexos\r\n");
            texto.Append(CultureInfo.InvariantCulture, $"Exportação nº {idExportacao}, gerada em {geradoEm:dd/MM/yyyy HH:mm} (horário do servidor, {fuso}).\r\n");
            texto.Append("\r\n");
            texto.Append("IMPORTANTE: extraia o arquivo ZIP inteiro para uma pasta antes de abrir o Excel. Os links da\r\n");
            texto.Append("planilha apontam para as pastas e os anexos extraídos e só funcionam depois da extração.\r\n");
            texto.Append("\r\n");
            texto.Append("O que há neste pacote:\r\n");
            texto.Append("- titulos.xlsx: planilha com as abas Solicitações, RDDVs, Despesas dos RDDVs e Anexos.\r\n");
            texto.Append("- indice.csv: lista dos anexos (separada por ponto e vírgula), com o tamanho e o SHA-256 de cada arquivo.\r\n");
            texto.Append("- indice.json: o mesmo índice em JSON, com os filtros usados, os totais e todos os títulos exportados.\r\n");
            texto.Append("- Pastas SOL-xxxxxx (solicitações) e RDDV-xxxxxx (RDDVs): uma pasta por título com anexos; o nome de\r\n");
            texto.Append("  cada anexo começa pelo tipo de documento.\r\n");
            texto.Append("- LEIA-ME.txt: este arquivo.\r\n");
            texto.Append("\r\n");
            texto.Append("Para conferir a integridade de um anexo, compare o SHA-256 do índice com o do arquivo\r\n");
            texto.Append("(no Windows: certutil -hashfile \"<arquivo>\" SHA256).\r\n");
            texto.Append("\r\n");
            texto.Append(CultureInfo.InvariantCulture,
                $"Totais: {totais.Solicitacoes} solicitação(ões), {totais.Rddvs} RDDV(s), {totais.Despesas} despesa(s) de RDDV, " +
                $"{totais.Anexos} anexo(s) ({FormatarTamanho(totais.BytesAnexos)}).\r\n");
            texto.Append("\r\n");
            texto.Append("Filtros usados:\r\n");
            foreach (var linha in DescreverFiltros(filtro))
            {
                texto.Append("- ").Append(linha).Append("\r\n");
            }

            return texto.ToString();
        }

        private static IEnumerable<string> DescreverFiltros(FiltroExportacaoDto f)
        {
            yield return "Tipo de título: " + f.TipoTitulo switch
            {
                TipoTituloExportacao.Solicitacao => "somente solicitações",
                TipoTituloExportacao.Rddv => "somente RDDVs",
                _ => "solicitações e RDDVs"
            };

            if (f.Codigo is > 0) yield return $"Código (Id do título): {f.Codigo}";
            if (!string.IsNullOrWhiteSpace(f.NumeroDocumento)) yield return $"Nº do documento: {TextoEmLinha(f.NumeroDocumento)}";

            var periodo = Periodo(f.DataSolicitacaoInicio, f.DataSolicitacaoFim);
            if (periodo is not null) yield return "Data da solicitação (no RDDV, data de cadastro): " + periodo;
            periodo = Periodo(f.DataVencimentoInicio, f.DataVencimentoFim);
            if (periodo is not null) yield return "Data de vencimento: " + periodo;
            periodo = Periodo(f.DataAprovacaoFinanceiroInicio, f.DataAprovacaoFinanceiroFim);
            if (periodo is not null) yield return "Data de aprovação do financeiro: " + periodo;

            if (f.IdSolicitante is > 0) yield return $"Solicitante (Id do usuário): {f.IdSolicitante}";
            if (f.IdAtribuido is > 0) yield return $"Atribuído a (Id do usuário): {f.IdAtribuido}";
            if (f.IdDepartamento is > 0) yield return $"Departamento (Id): {f.IdDepartamento}";
            if (f.StatusGestor is > 0) yield return $"Status gestor (código): {f.StatusGestor}";
            if (f.StatusFinanceiro is > 0) yield return $"Status financeiro (código): {f.StatusFinanceiro}";
            if (f.StatusContabil is > 0) yield return $"Status contábil (código): {f.StatusContabil}";
            if (!string.IsNullOrWhiteSpace(f.NomeParceiro)) yield return $"Parceiro ou funcionário contém: {TextoEmLinha(f.NomeParceiro)}";
            if (!string.IsNullOrWhiteSpace(f.DocumentoParceiro)) yield return $"Documento do parceiro ou CPF contém: {TextoEmLinha(f.DocumentoParceiro)}";
            if (f.TipoSolicitacao is > 0) yield return $"Tipo de solicitação (código): {f.TipoSolicitacao}";
            if (f.TipoPagamento is > 0) yield return $"Tipo de pagamento (código): {f.TipoPagamento}";
            if (f.FormaPagamento is > 0) yield return $"Forma de pagamento (código): {f.FormaPagamento}";
            if (f.TipoRelatorio is > 0) yield return $"Tipo de relatório do RDDV (código): {f.TipoRelatorio}";

            yield return "Anexos incluídos: " + (f.IncluirAnexos ? "sim" : "não");
        }

        private static string? Periodo(DateTime? inicio, DateTime? fim)
        {
            static string Data(DateTime d) => d.ToString("dd/MM/yyyy", CultureInfo.InvariantCulture);

            return (inicio, fim) switch
            {
                ({ } i, { } f) => $"de {Data(i)} a {Data(f)}",
                ({ } i, null) => $"a partir de {Data(i)}",
                (null, { } f) => $"até {Data(f)}",
                _ => null
            };
        }

        /// <summary>Texto informado pelo usuário, numa linha só e sem caracteres de controle.</summary>
        private static string TextoEmLinha(string texto) =>
            new string(texto.Trim().Select(c => char.IsControl(c) ? ' ' : c).ToArray());
    }
}
