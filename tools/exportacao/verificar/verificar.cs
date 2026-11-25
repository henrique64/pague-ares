#:property PublishAot=false
#:package ClosedXML@0.105.0

// Verificador do pacote de exportação de títulos com anexos (contratos C1 a C4 do plano).
//
// Uso:  dotnet run tools/exportacao/verificar/verificar.cs -- <zip> [--relatorio <json>] [--previa-html <html>]
//       dotnet run tools/exportacao/verificar/verificar.cs -- --ajuda
//
// Saída: 0 pacote aprovado; 1 pacote reprovado (cada falha é listada); 2 uso incorreto.
// Os anexos são lidos um a um, em streaming; só a planilha e os índices são carregados em memória.

using System.Globalization;
using System.IO.Compression;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using ClosedXML.Excel;

string? caminhoZip = null;
string? caminhoRelatorio = null;
string? caminhoPrevia = null;

for (int i = 0; i < args.Length; i++)
{
    switch (args[i])
    {
        case "--ajuda":
        case "-h":
        case "--help":
            Console.WriteLine("""
                verificar.cs - confere o pacote ZIP da exportação contra os contratos C1 a C4.

                Uso:
                  dotnet run tools/exportacao/verificar/verificar.cs -- <zip> [--relatorio <json>] [--previa-html <html>]

                  <zip>                  pacote a verificar
                  --relatorio <json>     grava o resultado (regras, falhas, avisos) em JSON
                  --previa-html <html>   grava uma prévia HTML: 10 primeiras linhas de cada aba e a árvore de pastas
                  --ajuda                mostra este texto

                Saída: 0 aprovado; 1 reprovado (falhas listadas); 2 uso incorreto.
                """);
            return 0;
        case "--relatorio":
            if (++i >= args.Length) { Console.Error.WriteLine("Falta o valor de --relatorio."); return 2; }
            caminhoRelatorio = args[i];
            break;
        case "--previa-html":
            if (++i >= args.Length) { Console.Error.WriteLine("Falta o valor de --previa-html."); return 2; }
            caminhoPrevia = args[i];
            break;
        default:
            if (args[i].StartsWith("--", StringComparison.Ordinal)) { Console.Error.WriteLine($"Argumento desconhecido: {args[i]}. Use --ajuda."); return 2; }
            if (caminhoZip != null) { Console.Error.WriteLine("Informe só um arquivo ZIP."); return 2; }
            caminhoZip = args[i];
            break;
    }
}

if (caminhoZip == null) { Console.Error.WriteLine("Informe o caminho do ZIP. Use --ajuda."); return 2; }
if (!File.Exists(caminhoZip)) { Console.Error.WriteLine($"Arquivo não encontrado: {caminhoZip}"); return 2; }

var res = new Resultado();
var verificador = new Verificador(res, caminhoZip);
verificador.Executar();

if (caminhoPrevia != null)
{
    try
    {
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(caminhoPrevia))!);
        File.WriteAllText(caminhoPrevia, verificador.GerarPreviaHtml(), new UTF8Encoding(false));
        Console.WriteLine($"Prévia HTML gravada em {caminhoPrevia}");
    }
    catch (Exception ex)
    {
        Console.Error.WriteLine($"Não foi possível gravar a prévia HTML: {ex.Message}");
    }
}

if (caminhoRelatorio != null)
{
    Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(caminhoRelatorio))!);
    File.WriteAllText(caminhoRelatorio, res.ParaJson(Path.GetFileName(caminhoZip), new FileInfo(caminhoZip).Length, verificador.Resumo), new UTF8Encoding(false));
    Console.WriteLine($"Relatório gravado em {caminhoRelatorio}");
}

res.Imprimir();
return res.Aprovado ? 0 : 1;

// =====================================================================================================
// Resultado
// =====================================================================================================

sealed record Falha(string Regra, string Detalhe);

sealed class Resultado
{
    // Regras verificadas, na ordem em que aparecem no relatório.
    public static readonly (string Id, string Descricao)[] Regras =
    [
        ("zip-legivel", "O arquivo é um ZIP legível"),
        ("raiz", "As quatro entradas da raiz existem (LEIA-ME.txt, titulos.xlsx, indice.csv, indice.json)"),
        ("estrutura", "Toda entrada fora da raiz casa ^(SOL|RDDV)-\\d{6,}/[^/]+$"),
        ("nomes-seguros", "Nenhum nome tem .., \\, : ou caractere inválido no Windows, nem nome reservado ou fim com espaço/ponto"),
        ("nomes-unicos", "Sem entradas repetidas nem nomes repetidos (sem diferenciar caixa) na mesma pasta"),
        ("indice-json", "indice.json tem o formato de C4 (UTF-8 sem BOM, camelCase, indentado)"),
        ("indice-arquivos", "Cada item do índice tem entrada no ZIP com o mesmo tamanho e o mesmo SHA-256"),
        ("indice-completo", "Cada entrada de anexo tem item no índice"),
        ("indice-totais", "Totais e títulos do índice batem com os arquivos"),
        ("csv-json", "indice.csv (UTF-8 com BOM, separador ;) e indice.json têm o mesmo conteúdo na mesma ordem"),
        ("planilha-abas", "titulos.xlsx tem as quatro abas, nesta ordem e com estes nomes"),
        ("planilha-cabecalhos", "Os cabeçalhos de cada aba são os de C3"),
        ("planilha-tipos", "Colunas de data, data-hora, valor e inteiro têm células DateTime/Number; texto e sim/não são texto"),
        ("planilha-hiperlinks", "Hiperlinks de Código e de Arquivo no pacote apontam para entradas existentes"),
        ("planilha-totais", "Linhas das abas batem com os totais do índice e entre si"),
        ("leia-me", "LEIA-ME.txt existe, é UTF-8 e descreve o pacote"),
    ];

    readonly List<Falha> _falhas = [];
    readonly List<string> _avisos = [];

    public bool Aprovado => _falhas.Count == 0;
    public IReadOnlyList<Falha> Falhas => _falhas;

    public void Falhar(string regra, string detalhe) => _falhas.Add(new Falha(regra, detalhe));
    public void Avisar(string aviso) => _avisos.Add(aviso);

    public void Imprimir()
    {
        Console.WriteLine();
        Console.WriteLine("Regras:");
        bool ilegivel = _falhas.Any(f => f.Regra == "zip-legivel");
        foreach (var (id, descricao) in Regras)
        {
            int n = _falhas.Count(f => f.Regra == id);
            if (ilegivel && id != "zip-legivel")
            {
                Console.WriteLine($"  [nao verificada] {id}: {descricao}");
                continue;
            }
            Console.WriteLine($"  [{(n == 0 ? "ok" : "FALHOU")}] {id}: {descricao}{(n == 0 ? "" : $" ({n} falha(s))")}");
        }

        foreach (var grupo in _falhas.GroupBy(f => f.Regra))
        {
            Console.WriteLine();
            Console.WriteLine($"Falhas em {grupo.Key}:");
            foreach (var f in grupo.Take(25)) Console.WriteLine($"  - {f.Detalhe}");
            if (grupo.Count() > 25) Console.WriteLine($"  ... e mais {grupo.Count() - 25}");
        }

        if (_avisos.Count > 0)
        {
            Console.WriteLine();
            Console.WriteLine($"Avisos ({_avisos.Count}, não reprovam):");
            foreach (var a in _avisos.Take(15)) Console.WriteLine($"  - {a}");
            if (_avisos.Count > 15) Console.WriteLine($"  ... e mais {_avisos.Count - 15}");
        }

        Console.WriteLine();
        Console.WriteLine(Aprovado ? "RESULTADO: APROVADO" : $"RESULTADO: REPROVADO ({_falhas.Count} falha(s))");
    }

    public string ParaJson(string nomeZip, long tamanhoZip, Dictionary<string, object?> resumo)
    {
        var verificacoes = Regras.Select(r => new
        {
            regra = r.Id,
            descricao = r.Descricao,
            verificada = r.Id == "zip-legivel" || !_falhas.Any(f => f.Regra == "zip-legivel"),
            ok = r.Id == "zip-legivel" ? !_falhas.Any(f => f.Regra == r.Id) : !_falhas.Any(f => f.Regra == "zip-legivel") && !_falhas.Any(f => f.Regra == r.Id),
            falhas = _falhas.Count(f => f.Regra == r.Id),
        });
        var objeto = new
        {
            zip = nomeZip,
            tamanhoZipBytes = tamanhoZip,
            verificadoEm = DateTimeOffset.Now.ToString("o", CultureInfo.InvariantCulture),
            aprovado = Aprovado,
            resumo,
            verificacoes,
            falhas = _falhas.Take(500).Select(f => new { regra = f.Regra, detalhe = f.Detalhe }),
            falhasTotal = _falhas.Count,
            avisos = _avisos.Take(200),
        };
        return JsonSerializer.Serialize(objeto, new JsonSerializerOptions { WriteIndented = true, Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping });
    }
}

// =====================================================================================================
// Verificador
// =====================================================================================================

enum Tipo { Texto, Inteiro, Data, DataHora, Valor, SimNao }

sealed record Coluna(string Cabecalho, Tipo Tipo, string[]? Alternativas = null);

sealed record ItemArquivo(string Titulo, string TipoTitulo, string TipoDocumento, string NomeOriginal, string Arquivo, string CaminhoRelativo, long TamanhoBytes, string Sha256);
sealed record ItemTitulo(string Codigo, string Tipo, int Id, string? Pasta, int QuantidadeAnexos);

sealed class Verificador(Resultado res, string caminhoZip)
{
    static readonly string[] EntradasRaiz = ["LEIA-ME.txt", "titulos.xlsx", "indice.csv", "indice.json"];
    static readonly Regex PadraoEntrada = new(@"^(SOL|RDDV)-\d{6,}/[^/]+$", RegexOptions.CultureInvariant);
    static readonly Regex PadraoCodigo = new(@"^(SOL|RDDV)-\d{6,}$", RegexOptions.CultureInvariant);
    static readonly string[] Reservados = ["CON", "PRN", "AUX", "NUL", "COM1", "COM2", "COM3", "COM4", "COM5", "COM6", "COM7", "COM8", "COM9", "LPT1", "LPT2", "LPT3", "LPT4", "LPT5", "LPT6", "LPT7", "LPT8", "LPT9"];
    const string CabCsv = "titulo;tipo_titulo;tipo_documento;nome_original;arquivo;caminho_relativo;tamanho_bytes;sha256";

    // ---- Abas de C3 -----------------------------------------------------------------------------------
    static readonly string[] NomesAbas = ["Solicitações", "RDDVs", "Despesas dos RDDVs", "Anexos"];

    static readonly Coluna[] ColSolicitacoes =
    [
        new("Código", Tipo.Texto), new("Id", Tipo.Inteiro), new("Tipo de solicitação", Tipo.Texto), new("Tipo de pagamento", Tipo.Texto),
        new("Forma de pagamento", Tipo.Texto), new("Departamento", Tipo.Texto), new("Solicitante", Tipo.Texto), new("Atribuído a", Tipo.Texto),
        new("Gestor", Tipo.Texto), new("Data da solicitação", Tipo.Data), new("Data do documento", Tipo.Data), new("Data de vencimento", Tipo.Data),
        new("Nº do documento", Tipo.Texto), new("Descrição", Tipo.Texto), new("Valor", Tipo.Valor), new("Parcelado", Tipo.SimNao),
        new("Parcelas", Tipo.Inteiro), new("Centro de custo", Tipo.Texto), new("Projeto", Tipo.Texto), new("PCA", Tipo.Texto),
        // O contrato escreve "Parceiro (nome)": aceita-se também "Parceiro", pois o parêntese pode ser só uma explicação do tipo.
        new("Parceiro (nome)", Tipo.Texto, ["Parceiro"]), new("Documento do parceiro", Tipo.Texto), new("Código do parceiro", Tipo.Texto),
        new("Banco", Tipo.Texto), new("Agência", Tipo.Texto), new("Conta", Tipo.Texto), new("Observação", Tipo.Texto),
        new("Status gestor", Tipo.Texto), new("Data aprovação gestor", Tipo.DataHora), new("Observação do gestor", Tipo.Texto),
        new("Status financeiro", Tipo.Texto), new("Data aprovação financeiro", Tipo.DataHora), new("Observação do setor", Tipo.Texto),
        new("Status contábil", Tipo.Texto), new("Data aprovação contábil", Tipo.DataHora), new("Qtde. de anexos", Tipo.Inteiro),
    ];

    static readonly Coluna[] ColRddvs =
    [
        new("Código", Tipo.Texto), new("Id", Tipo.Inteiro), new("Tipo de relatório", Tipo.Texto), new("Funcionário", Tipo.Texto),
        new("CPF", Tipo.Texto), new("Departamento", Tipo.Texto), new("Solicitante", Tipo.Texto), new("Atribuído a", Tipo.Texto),
        new("Gestor", Tipo.Texto), new("Finalidade", Tipo.Texto), new("Tipo de viagem", Tipo.Texto), new("Local da viagem", Tipo.Texto),
        new("Data início", Tipo.Data), new("Data fim", Tipo.Data), new("Data de cadastro", Tipo.Data), new("Data de vencimento", Tipo.Data),
        new("Moeda", Tipo.Texto), new("Diária de viagem", Tipo.SimNao), new("Diárias", Tipo.Valor), new("Valor da diária", Tipo.Valor),
        new("Valor do km", Tipo.Valor), new("Adiantamento", Tipo.SimNao), new("Valor do adiantamento", Tipo.Valor), new("Saldo cartão VTM", Tipo.Valor),
        new("Valor líquido do RDDV", Tipo.Valor), new("Centro de custo", Tipo.Texto), new("Projeto", Tipo.Texto), new("PCA", Tipo.Texto),
        new("Banco", Tipo.Texto), new("Agência", Tipo.Texto), new("Conta", Tipo.Texto), new("Observação", Tipo.Texto),
        new("Status gestor", Tipo.Texto), new("Data aprovação gestor", Tipo.DataHora), new("Observação do gestor", Tipo.Texto),
        new("Status financeiro", Tipo.Texto), new("Data aprovação financeiro", Tipo.DataHora), new("Observação do setor", Tipo.Texto),
        new("Status contábil", Tipo.Texto), new("Data aprovação contábil", Tipo.DataHora), new("Qtde. de anexos", Tipo.Inteiro),
    ];

    static readonly Coluna[] ColDespesas =
    [
        new("Código do RDDV", Tipo.Texto), new("Id da despesa", Tipo.Inteiro), new("Data da despesa", Tipo.Data), new("Tipo de despesa", Tipo.Texto),
        new("Moeda", Tipo.Texto), new("Quantidade", Tipo.Valor), new("Valor", Tipo.Valor),
    ];

    static readonly Coluna[] ColAnexos =
    [
        new("Código do título", Tipo.Texto), new("Tipo do título", Tipo.Texto), new("Tipo de documento", Tipo.Texto), new("Nome original", Tipo.Texto),
        new("Arquivo no pacote", Tipo.Texto), new("Tamanho (bytes)", Tipo.Inteiro), new("SHA-256", Tipo.Texto), new("Data de envio", Tipo.DataHora),
    ];

    // ---- Estado --------------------------------------------------------------------------------------
    public Dictionary<string, object?> Resumo { get; } = [];

    ZipArchive? _zip;
    List<ZipArchiveEntry> _entradas = [];
    HashSet<string> _nomesEntradas = new(StringComparer.Ordinal);
    HashSet<string> _pastas = new(StringComparer.Ordinal);
    List<ItemArquivo> _arquivos = [];
    List<ItemTitulo> _titulos = [];
    JsonElement? _totais;
    XLWorkbook? _pasta;
    MemoryStream? _planilhaBytes;

    public void Executar()
    {
        Console.WriteLine($"Verificando {Path.GetFileName(caminhoZip)} ({new FileInfo(caminhoZip).Length:N0} bytes)...");
        try
        {
            using var fs = new FileStream(caminhoZip, FileMode.Open, FileAccess.Read, FileShare.Read, 81920, FileOptions.SequentialScan);
            using var zip = new ZipArchive(fs, ZipArchiveMode.Read);
            _zip = zip;
            _entradas = [.. zip.Entries];
            _nomesEntradas = new HashSet<string>(_entradas.Select(e => e.FullName), StringComparer.Ordinal);
            Resumo["entradas"] = _entradas.Count;

            VerificarRaiz();
            VerificarNomes();
            VerificarIndiceJson();
            VerificarArquivosDoIndice();
            VerificarEntradasSemItem();
            VerificarCsv();
            VerificarPlanilha();
            VerificarLeiaMe();
            VerificarTotais();

            // A prévia usa a planilha já carregada; mantém-na viva até aqui.
            _previaPronta = MontarPrevia();
        }
        catch (InvalidDataException ex)
        {
            res.Falhar("zip-legivel", $"O arquivo não é um ZIP válido: {ex.Message}");
        }
        catch (IOException ex)
        {
            res.Falhar("zip-legivel", $"Não foi possível ler o arquivo: {ex.Message}");
        }
        finally
        {
            _zip = null;
            _pasta?.Dispose();
        }
    }

    string? _previaPronta;
    public string GerarPreviaHtml() => _previaPronta ?? "<html><body><p>Não foi possível gerar a prévia: o pacote não pôde ser lido.</p></body></html>";

    // ---- C2: raiz e estrutura --------------------------------------------------------------------------

    void VerificarRaiz()
    {
        foreach (var nome in EntradasRaiz)
            if (!_nomesEntradas.Contains(nome))
                res.Falhar("raiz", $"Falta a entrada da raiz '{nome}'.");

        var raiz = new HashSet<string>(EntradasRaiz, StringComparer.Ordinal);
        foreach (var e in _entradas)
        {
            if (raiz.Contains(e.FullName)) continue;
            if (!PadraoEntrada.IsMatch(e.FullName))
                res.Falhar("estrutura", $"Entrada fora do padrão (SOL|RDDV)-NNNNNN/arquivo: '{Exibir(e.FullName)}'.");
            else
                _pastas.Add(e.FullName[..e.FullName.IndexOf('/')]);
        }
    }

    // ---- C1: segurança e unicidade dos nomes -----------------------------------------------------------

    void VerificarNomes()
    {
        var vistos = new HashSet<string>(StringComparer.Ordinal);
        var porPasta = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var e in _entradas)
        {
            var nome = e.FullName;
            foreach (var motivo in MotivosNomeInseguro(nome))
                res.Falhar("nomes-seguros", $"'{Exibir(nome)}': {motivo}.");

            if (!vistos.Add(nome))
                res.Falhar("nomes-unicos", $"Entrada repetida: '{Exibir(nome)}'.");
            else if (!porPasta.Add(nome))
                res.Falhar("nomes-unicos", $"Nome repetido sem diferenciar caixa: '{Exibir(nome)}'.");

            // C1: nome final do anexo com no máximo 120 caracteres.
            int barra = nome.IndexOf('/');
            if (barra > 0 && PadraoEntrada.IsMatch(nome) && nome.Length - barra - 1 > 120)
                res.Falhar("nomes-seguros", $"'{Exibir(nome)}': nome do arquivo passa de 120 caracteres.");
        }
    }

    static IEnumerable<string> MotivosNomeInseguro(string nome)
    {
        if (nome.Contains("..", StringComparison.Ordinal)) yield return "contém '..'";
        if (nome.Contains('\\')) yield return "contém barra invertida";
        if (nome.Contains(':')) yield return "contém ':'";
        if (nome.StartsWith('/')) yield return "começa com '/'";
        if (nome.Any(c => c < 32 || c == 127)) yield return "contém caractere de controle";
        if (nome.IndexOfAny(['<', '>', '"', '|', '?', '*']) >= 0) yield return "contém caractere inválido no Windows (< > \" | ? *)";

        foreach (var segmento in nome.Split('/'))
        {
            if (segmento.Length == 0) continue;
            if (segmento.EndsWith(' ') || segmento.EndsWith('.')) yield return $"o trecho '{Exibir(segmento)}' termina com espaço ou ponto";
            var raiz = segmento.Contains('.') ? segmento[..segmento.IndexOf('.')] : segmento;
            if (Reservados.Contains(raiz.TrimEnd(), StringComparer.OrdinalIgnoreCase)) yield return $"o trecho '{Exibir(segmento)}' é nome reservado do Windows";
        }
    }

    static string Exibir(string s)
    {
        var sb = new StringBuilder();
        foreach (var c in s) sb.Append(c < 32 ? $"\\x{(int)c:X2}" : c.ToString());
        return sb.Length > 160 ? sb.ToString(0, 160) + "..." : sb.ToString();
    }

    // ---- C4: indice.json --------------------------------------------------------------------------------

    string? LerTexto(string nomeEntrada, out bool temBom)
    {
        temBom = false;
        var entrada = _zip!.GetEntry(nomeEntrada);
        if (entrada == null) return null;
        using var s = entrada.Open();
        using var ms = new MemoryStream();
        s.CopyTo(ms);
        var bytes = ms.ToArray();
        temBom = bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF;
        try
        {
            return new UTF8Encoding(false, true).GetString(bytes, temBom ? 3 : 0, bytes.Length - (temBom ? 3 : 0));
        }
        catch (DecoderFallbackException)
        {
            res.Falhar(nomeEntrada == "LEIA-ME.txt" ? "leia-me" : nomeEntrada == "indice.csv" ? "csv-json" : "indice-json", $"{nomeEntrada} não é UTF-8 válido.");
            return null;
        }
    }

    void VerificarIndiceJson()
    {
        var texto = LerTexto("indice.json", out var bom);
        if (texto == null) return;
        if (bom) res.Falhar("indice-json", "indice.json tem BOM; o contrato pede UTF-8 sem BOM.");
        if (!texto.Contains("\n  \"")) res.Falhar("indice-json", "indice.json não está indentado.");

        JsonDocument doc;
        try { doc = JsonDocument.Parse(texto); }
        catch (JsonException ex) { res.Falhar("indice-json", $"indice.json não é um JSON válido: {ex.Message}"); return; }

        using (doc)
        {
            var raiz = doc.RootElement;
            if (raiz.ValueKind != JsonValueKind.Object) { res.Falhar("indice-json", "A raiz de indice.json não é um objeto."); return; }

            if (!raiz.TryGetProperty("versao", out var versao) || versao.ValueKind != JsonValueKind.Number || versao.GetInt32() != 1)
                res.Falhar("indice-json", "'versao' ausente ou diferente de 1.");
            if (!raiz.TryGetProperty("exportacaoId", out var expId) || expId.ValueKind != JsonValueKind.Number)
                res.Falhar("indice-json", "'exportacaoId' ausente ou não numérico.");
            if (!raiz.TryGetProperty("geradoEm", out var gerado) || gerado.ValueKind != JsonValueKind.String || !DateTimeOffset.TryParse(gerado.GetString(), CultureInfo.InvariantCulture, DateTimeStyles.None, out _))
                res.Falhar("indice-json", "'geradoEm' ausente ou não é uma data ISO com fuso.");
            if (!raiz.TryGetProperty("filtros", out var filtros) || filtros.ValueKind != JsonValueKind.Object)
                res.Falhar("indice-json", "'filtros' ausente ou não é objeto.");

            if (raiz.TryGetProperty("totais", out var totais) && totais.ValueKind == JsonValueKind.Object)
            {
                _totais = totais.Clone();
                foreach (var campo in new[] { "solicitacoes", "rddvs", "despesas", "anexos", "bytesAnexos" })
                    if (!totais.TryGetProperty(campo, out var v) || v.ValueKind != JsonValueKind.Number)
                        res.Falhar("indice-json", $"'totais.{campo}' ausente ou não numérico.");
            }
            else res.Falhar("indice-json", "'totais' ausente ou não é objeto.");

            if (raiz.TryGetProperty("titulos", out var titulos) && titulos.ValueKind == JsonValueKind.Array)
            {
                int n = 0;
                foreach (var t in titulos.EnumerateArray())
                {
                    n++;
                    if (!TryStr(t, "codigo", out var codigo) || !TryStr(t, "tipo", out var tipo) ||
                        !t.TryGetProperty("id", out var id) || id.ValueKind != JsonValueKind.Number ||
                        !t.TryGetProperty("pasta", out var pasta) || (pasta.ValueKind != JsonValueKind.String && pasta.ValueKind != JsonValueKind.Null) ||
                        !t.TryGetProperty("quantidadeAnexos", out var qtd) || qtd.ValueKind != JsonValueKind.Number)
                    {
                        res.Falhar("indice-json", $"titulos[{n - 1}] sem os campos codigo, tipo, id, pasta e quantidadeAnexos.");
                        continue;
                    }
                    _titulos.Add(new ItemTitulo(codigo!, tipo!, id.GetInt32(), pasta.ValueKind == JsonValueKind.Null ? null : pasta.GetString(), qtd.GetInt32()));
                }
            }
            else res.Falhar("indice-json", "'titulos' ausente ou não é lista.");

            if (raiz.TryGetProperty("arquivos", out var arquivos) && arquivos.ValueKind == JsonValueKind.Array)
            {
                int n = 0;
                foreach (var a in arquivos.EnumerateArray())
                {
                    n++;
                    if (!TryStr(a, "titulo", out var titulo) || !TryStr(a, "tipoTitulo", out var tipoTitulo) || !TryStr(a, "tipoDocumento", out var tipoDoc) ||
                        !TryStr(a, "nomeOriginal", out var nomeOrig) || !TryStr(a, "arquivo", out var arquivo) || !TryStr(a, "caminhoRelativo", out var caminho) ||
                        !a.TryGetProperty("tamanhoBytes", out var tam) || tam.ValueKind != JsonValueKind.Number || !TryStr(a, "sha256", out var sha))
                    {
                        res.Falhar("indice-json", $"arquivos[{n - 1}] sem todos os campos de C4.");
                        continue;
                    }
                    _arquivos.Add(new ItemArquivo(titulo!, tipoTitulo!, tipoDoc!, nomeOrig!, arquivo!, caminho!, tam.GetInt64(), sha!));
                }
            }
            else res.Falhar("indice-json", "'arquivos' ausente ou não é lista.");
        }

        // Consistência dos títulos listados.
        var codigos = new HashSet<string>(StringComparer.Ordinal);
        foreach (var t in _titulos)
        {
            if (!codigos.Add(t.Codigo)) res.Falhar("indice-totais", $"Título repetido em 'titulos': {t.Codigo}.");
            if (!PadraoCodigo.IsMatch(t.Codigo)) res.Falhar("indice-totais", $"Código de título inválido: '{Exibir(t.Codigo)}'.");
            var esperado = (t.Tipo == "Solicitacao" ? "SOL-" : "RDDV-") + t.Id.ToString("D6", CultureInfo.InvariantCulture);
            if (t.Tipo is not ("Solicitacao" or "Rddv")) res.Falhar("indice-totais", $"Tipo de título inválido em {t.Codigo}: '{t.Tipo}'.");
            else if (t.Codigo != esperado) res.Falhar("indice-totais", $"Código {t.Codigo} não corresponde ao id {t.Id} (esperado {esperado}).");
            var esperadaPasta = t.QuantidadeAnexos > 0 ? t.Codigo + "/" : null;
            if (t.Pasta != esperadaPasta) res.Falhar("indice-totais", $"{t.Codigo}: pasta '{t.Pasta ?? "null"}' não bate com quantidadeAnexos={t.QuantidadeAnexos}.");
        }
    }

    static bool TryStr(JsonElement e, string nome, out string? valor)
    {
        valor = null;
        if (!e.TryGetProperty(nome, out var p) || p.ValueKind != JsonValueKind.String) return false;
        valor = p.GetString();
        return true;
    }

    // ---- C4: tamanho e SHA-256 de cada arquivo (streaming) --------------------------------------------

    void VerificarArquivosDoIndice()
    {
        long bytes = 0;
        var caminhos = new HashSet<string>(StringComparer.Ordinal);
        var buffer = new byte[81920];
        int verificados = 0;

        foreach (var item in _arquivos)
        {
            bytes += item.TamanhoBytes;
            if (!caminhos.Add(item.CaminhoRelativo))
                res.Falhar("indice-arquivos", $"Caminho repetido no índice: '{Exibir(item.CaminhoRelativo)}'.");

            if (item.CaminhoRelativo != item.Titulo + "/" + item.Arquivo)
                res.Falhar("indice-arquivos", $"'{Exibir(item.CaminhoRelativo)}' não é <título>/<arquivo> ({item.Titulo}/{Exibir(item.Arquivo)}).");

            var titulo = _titulos.FirstOrDefault(t => t.Codigo == item.Titulo);
            if (titulo == null)
                res.Falhar("indice-arquivos", $"'{Exibir(item.CaminhoRelativo)}' pertence a um título que não está em 'titulos' ({item.Titulo}).");
            else if ((titulo.Tipo != item.TipoTitulo))
                res.Falhar("indice-arquivos", $"'{Exibir(item.CaminhoRelativo)}': tipoTitulo '{item.TipoTitulo}' difere do título ({titulo.Tipo}).");

            if (!Regex.IsMatch(item.Sha256, "^[0-9a-f]{64}$"))
                res.Falhar("indice-arquivos", $"'{Exibir(item.CaminhoRelativo)}': sha256 não é hexadecimal minúsculo de 64 caracteres.");

            var entrada = _zip!.GetEntry(item.CaminhoRelativo);
            if (entrada == null || entrada.FullName != item.CaminhoRelativo)
            {
                res.Falhar("indice-arquivos", $"Item do índice sem entrada no ZIP: '{Exibir(item.CaminhoRelativo)}'.");
                continue;
            }

            using var hash = SHA256.Create();
            long lidos = 0;
            using (var s = entrada.Open())
            {
                int n;
                while ((n = s.Read(buffer, 0, buffer.Length)) > 0)
                {
                    hash.TransformBlock(buffer, 0, n, null, 0);
                    lidos += n;
                }
            }
            hash.TransformFinalBlock([], 0, 0);
            var sha = Convert.ToHexStringLower(hash.Hash!);

            if (lidos != item.TamanhoBytes)
                res.Falhar("indice-arquivos", $"Tamanho diferente em '{Exibir(item.CaminhoRelativo)}': índice {item.TamanhoBytes}, ZIP {lidos}.");
            if (!string.Equals(sha, item.Sha256, StringComparison.Ordinal))
                res.Falhar("indice-arquivos", $"SHA-256 diferente em '{Exibir(item.CaminhoRelativo)}': índice {item.Sha256}, calculado {sha}.");

            if (++verificados % 200 == 0) Console.WriteLine($"  {verificados}/{_arquivos.Count} anexos conferidos...");
        }

        Resumo["anexos"] = _arquivos.Count;
        Resumo["bytesAnexos"] = bytes;
    }

    void VerificarEntradasSemItem()
    {
        var noIndice = new HashSet<string>(_arquivos.Select(a => a.CaminhoRelativo), StringComparer.Ordinal);
        var raiz = new HashSet<string>(EntradasRaiz, StringComparer.Ordinal);
        foreach (var e in _entradas)
            if (!raiz.Contains(e.FullName) && !noIndice.Contains(e.FullName))
                res.Falhar("indice-completo", $"Entrada sem item no índice: '{Exibir(e.FullName)}'.");
    }

    void VerificarTotais()
    {
        // Contagem de anexos por título e totais do índice.
        var porTitulo = _arquivos.GroupBy(a => a.Titulo).ToDictionary(g => g.Key, g => g.Count());
        foreach (var t in _titulos)
        {
            porTitulo.TryGetValue(t.Codigo, out var n);
            if (n != t.QuantidadeAnexos)
                res.Falhar("indice-totais", $"{t.Codigo}: quantidadeAnexos={t.QuantidadeAnexos}, mas o índice tem {n} arquivo(s).");
        }

        if (_totais is JsonElement tot)
        {
            long Num(string campo) => tot.TryGetProperty(campo, out var v) && v.ValueKind == JsonValueKind.Number ? v.GetInt64() : -1;
            long somaBytes = _arquivos.Sum(a => a.TamanhoBytes);
            if (Num("anexos") != _arquivos.Count) res.Falhar("indice-totais", $"totais.anexos={Num("anexos")}, mas 'arquivos' tem {_arquivos.Count}.");
            if (Num("bytesAnexos") != somaBytes) res.Falhar("indice-totais", $"totais.bytesAnexos={Num("bytesAnexos")}, mas a soma dos arquivos é {somaBytes}.");
            int sol = _titulos.Count(t => t.Tipo == "Solicitacao"), rddv = _titulos.Count(t => t.Tipo == "Rddv");
            if (Num("solicitacoes") != sol) res.Falhar("indice-totais", $"totais.solicitacoes={Num("solicitacoes")}, mas 'titulos' tem {sol}.");
            if (Num("rddvs") != rddv) res.Falhar("indice-totais", $"totais.rddvs={Num("rddvs")}, mas 'titulos' tem {rddv}.");
            Resumo["solicitacoes"] = Num("solicitacoes");
            Resumo["rddvs"] = Num("rddvs");
            Resumo["despesas"] = Num("despesas");
        }
        Resumo["titulosNoIndice"] = _titulos.Count;
        Resumo["pastasNoZip"] = _pastas.Count;

        // Ordem do índice: títulos agrupados e, dentro de cada um, arquivos em ordem alfabética.
        var fechados = new HashSet<string>(StringComparer.Ordinal);
        string? atual = null;
        string? anterior = null;
        foreach (var a in _arquivos)
        {
            if (a.Titulo != atual)
            {
                if (atual != null) fechados.Add(atual);
                if (fechados.Contains(a.Titulo)) res.Falhar("indice-totais", $"Os arquivos de {a.Titulo} não estão agrupados no índice.");
                atual = a.Titulo;
                anterior = null;
            }
            if (anterior != null && string.Compare(anterior, a.Arquivo, StringComparison.OrdinalIgnoreCase) > 0 && string.CompareOrdinal(anterior, a.Arquivo) > 0)
                res.Falhar("indice-totais", $"Arquivos de {a.Titulo} fora de ordem: '{Exibir(anterior)}' antes de '{Exibir(a.Arquivo)}'.");
            anterior = a.Arquivo;
        }
    }

    // ---- C4: indice.csv ----------------------------------------------------------------------------------

    void VerificarCsv()
    {
        var texto = LerTexto("indice.csv", out var bom);
        if (texto == null) return;
        if (!bom) res.Falhar("csv-json", "indice.csv não tem BOM UTF-8.");

        List<List<string>> linhas;
        try { linhas = ParseCsv(texto); }
        catch (FormatException ex) { res.Falhar("csv-json", $"indice.csv malformado: {ex.Message}"); return; }

        if (linhas.Count == 0) { res.Falhar("csv-json", "indice.csv está vazio."); return; }
        var cab = string.Join(';', linhas[0]);
        if (cab != CabCsv) res.Falhar("csv-json", $"Cabeçalho do CSV diferente de C4: '{Exibir(cab)}'.");

        var dados = linhas.Skip(1).ToList();
        if (dados.Count != _arquivos.Count)
            res.Falhar("csv-json", $"O CSV tem {dados.Count} linha(s) de dados e o JSON tem {_arquivos.Count} arquivo(s).");

        for (int i = 0; i < Math.Min(dados.Count, _arquivos.Count); i++)
        {
            var a = _arquivos[i];
            string[] esperado = [a.Titulo, a.TipoTitulo, a.TipoDocumento, a.NomeOriginal, a.Arquivo, a.CaminhoRelativo,
                a.TamanhoBytes.ToString(CultureInfo.InvariantCulture), a.Sha256];
            var linha = dados[i];
            if (linha.Count != 8) { res.Falhar("csv-json", $"Linha {i + 2} do CSV tem {linha.Count} colunas (esperado 8)."); continue; }
            for (int c = 0; c < 8; c++)
            {
                if (!string.Equals(linha[c], esperado[c], StringComparison.Ordinal))
                {
                    res.Falhar("csv-json", $"Linha {i + 2}, coluna {c + 1} ({CabCsv.Split(';')[c]}): CSV '{Exibir(linha[c])}' x JSON '{Exibir(esperado[c])}'.");
                    break;
                }
            }
        }
    }

    // Leitor CSV mínimo (separador ';', aspas duplas, "" como aspas literais, quebras CRLF ou LF dentro de campos entre aspas).
    static List<List<string>> ParseCsv(string texto)
    {
        var linhas = new List<List<string>>();
        var linha = new List<string>();
        var campo = new StringBuilder();
        bool entreAspas = false, campoComAspas = false;

        void FimCampo() { linha.Add(campo.ToString()); campo.Clear(); campoComAspas = false; }
        void FimLinha()
        {
            FimCampo();
            linhas.Add(linha);
            linha = [];
        }

        for (int i = 0; i < texto.Length; i++)
        {
            char c = texto[i];
            if (entreAspas)
            {
                if (c == '"')
                {
                    if (i + 1 < texto.Length && texto[i + 1] == '"') { campo.Append('"'); i++; }
                    else entreAspas = false;
                }
                else campo.Append(c);
                continue;
            }
            switch (c)
            {
                case '"':
                    if (campo.Length != 0 || campoComAspas) throw new FormatException("aspas no meio de um campo");
                    entreAspas = true; campoComAspas = true;
                    break;
                case ';': FimCampo(); break;
                case '\r': break;
                case '\n': FimLinha(); break;
                default: campo.Append(c); break;
            }
        }
        if (entreAspas) throw new FormatException("aspas não fechadas");
        if (campo.Length > 0 || campoComAspas || linha.Count > 0) FimLinha();
        return linhas;
    }

    // ---- LEIA-ME -----------------------------------------------------------------------------------------

    void VerificarLeiaMe()
    {
        var texto = LerTexto("LEIA-ME.txt", out _);
        if (texto == null) return;
        if (string.IsNullOrWhiteSpace(texto)) { res.Falhar("leia-me", "LEIA-ME.txt está vazio."); return; }
        foreach (var nome in new[] { "titulos.xlsx", "indice.csv", "indice.json" })
            if (!texto.Contains(nome, StringComparison.OrdinalIgnoreCase))
                res.Falhar("leia-me", $"LEIA-ME.txt não descreve '{nome}'.");
        if (!texto.Contains("extraia", StringComparison.OrdinalIgnoreCase) && !texto.Contains("extrair", StringComparison.OrdinalIgnoreCase))
            res.Falhar("leia-me", "LEIA-ME.txt não orienta a extrair o ZIP antes de abrir o Excel.");
    }

    // ---- C3: planilha -------------------------------------------------------------------------------------


    void VerificarPlanilha()
    {
        var entrada = _zip!.GetEntry("titulos.xlsx");
        if (entrada == null) return;

        _planilhaBytes = new MemoryStream();
        using (var s = entrada.Open()) s.CopyTo(_planilhaBytes);
        _planilhaBytes.Position = 0;

        try { _pasta = new XLWorkbook(_planilhaBytes); }
        catch (Exception ex)
        {
            res.Falhar("planilha-abas", $"titulos.xlsx não pôde ser aberto: {ex.GetType().Name}: {ex.Message}");
            return;
        }

        var abas = _pasta.Worksheets.Select(w => w.Name).ToArray();
        if (!abas.SequenceEqual(NomesAbas, StringComparer.Ordinal))
            res.Falhar("planilha-abas", $"Abas encontradas [{string.Join(" | ", abas)}]; esperado [{string.Join(" | ", NomesAbas)}].");

        var wsSol = Aba("Solicitações");
        var wsRddv = Aba("RDDVs");
        var wsDesp = Aba("Despesas dos RDDVs");
        var wsAnexos = Aba("Anexos");

        var cSol = wsSol == null ? null : VerificarAba(wsSol, ColSolicitacoes);
        var cRddv = wsRddv == null ? null : VerificarAba(wsRddv, ColRddvs);
        var cDesp = wsDesp == null ? null : VerificarAba(wsDesp, ColDespesas);
        var cAnexos = wsAnexos == null ? null : VerificarAba(wsAnexos, ColAnexos);

        int linhasSol = cSol?.Linhas ?? 0, linhasRddv = cRddv?.Linhas ?? 0;
        Resumo["linhasSolicitacoes"] = linhasSol;
        Resumo["linhasRddvs"] = linhasRddv;
        Resumo["linhasDespesas"] = cDesp?.Linhas ?? 0;
        Resumo["linhasAnexos"] = cAnexos?.Linhas ?? 0;

        // Totais do JSON.
        if (_totais is JsonElement tot)
        {
            long soma = (tot.TryGetProperty("solicitacoes", out var s1) ? s1.GetInt64() : 0) + (tot.TryGetProperty("rddvs", out var s2) ? s2.GetInt64() : 0);
            if (wsSol != null && wsRddv != null && linhasSol + linhasRddv != soma)
                res.Falhar("planilha-totais", $"Solicitações ({linhasSol}) + RDDVs ({linhasRddv}) = {linhasSol + linhasRddv}, mas totais do índice somam {soma}.");
            if (tot.TryGetProperty("solicitacoes", out var so) && wsSol != null && linhasSol != so.GetInt64())
                res.Falhar("planilha-totais", $"Aba Solicitações tem {linhasSol} linha(s); totais.solicitacoes={so.GetInt64()}.");
            if (tot.TryGetProperty("rddvs", out var rd) && wsRddv != null && linhasRddv != rd.GetInt64())
                res.Falhar("planilha-totais", $"Aba RDDVs tem {linhasRddv} linha(s); totais.rddvs={rd.GetInt64()}.");
            if (tot.TryGetProperty("despesas", out var de) && wsDesp != null && (cDesp?.Linhas ?? 0) != de.GetInt64())
                res.Falhar("planilha-totais", $"Aba Despesas dos RDDVs tem {cDesp?.Linhas} linha(s); totais.despesas={de.GetInt64()}.");
        }
        if (wsAnexos != null && (cAnexos?.Linhas ?? 0) != _arquivos.Count)
            res.Falhar("planilha-totais", $"Aba Anexos tem {cAnexos?.Linhas} linha(s); o índice tem {_arquivos.Count} arquivo(s).");

        // Códigos das abas x títulos do índice.
        if (cSol != null && cRddv != null)
        {
            var codigosAbas = new HashSet<string>(cSol.Codigos.Concat(cRddv.Codigos), StringComparer.Ordinal);
            var codigosIndice = new HashSet<string>(_titulos.Select(t => t.Codigo), StringComparer.Ordinal);
            foreach (var c in codigosIndice.Except(codigosAbas).Take(20)) res.Falhar("planilha-totais", $"Título {c} está no índice mas não nas abas Solicitações/RDDVs.");
            foreach (var c in codigosAbas.Except(codigosIndice).Take(20)) res.Falhar("planilha-totais", $"Título {c} está nas abas mas não no índice.");
            if (cSol.Codigos.Concat(cRddv.Codigos).GroupBy(x => x).Any(g => g.Count() > 1))
                res.Falhar("planilha-totais", "Há códigos repetidos nas abas Solicitações/RDDVs.");

            foreach (var c in cSol.Codigos)
                if (!c.StartsWith("SOL-", StringComparison.Ordinal)) res.Falhar("planilha-totais", $"Código '{Exibir(c)}' na aba Solicitações não começa com SOL-.");
            foreach (var c in cRddv.Codigos)
                if (!c.StartsWith("RDDV-", StringComparison.Ordinal)) res.Falhar("planilha-totais", $"Código '{Exibir(c)}' na aba RDDVs não começa com RDDV-.");

            // Código da despesa precisa existir em RDDVs.
            if (cDesp != null)
                foreach (var c in cDesp.Codigos.Distinct().Where(c => !cRddv.Codigos.Contains(c)).Take(20))
                    res.Falhar("planilha-totais", $"Despesa com código de RDDV inexistente na aba RDDVs: '{Exibir(c)}'.");

            // Hiperlinks de pasta: título com anexos precisa de link; link precisa apontar para pasta existente.
            VerificarLinksDePasta(wsSol!, cSol, "Solicitações");
            VerificarLinksDePasta(wsRddv!, cRddv, "RDDVs");
        }

        if (wsAnexos != null && cAnexos != null) VerificarAbaAnexos(wsAnexos);
    }

    IXLWorksheet? Aba(string nome) => _pasta!.Worksheets.FirstOrDefault(w => w.Name == nome);

    sealed class InfoAba
    {
        public int Linhas;
        public List<string> Codigos = [];
        public List<int> LinhasPlanilha = [];
    }

    InfoAba VerificarAba(IXLWorksheet ws, Coluna[] colunas)
    {
        var info = new InfoAba();
        int ultimaLinha = ws.LastRowUsed()?.RowNumber() ?? 0;
        int ultimaColuna = ws.LastColumnUsed()?.ColumnNumber() ?? 0;

        // Cabeçalhos.
        if (ultimaColuna != colunas.Length)
            res.Falhar("planilha-cabecalhos", $"Aba '{ws.Name}' tem {ultimaColuna} coluna(s); esperado {colunas.Length}.");
        for (int c = 1; c <= colunas.Length; c++)
        {
            var obtido = ws.Cell(1, c).GetString();
            var esperada = colunas[c - 1];
            if (obtido != esperada.Cabecalho && !(esperada.Alternativas?.Contains(obtido) ?? false))
                res.Falhar("planilha-cabecalhos", $"Aba '{ws.Name}', coluna {c}: cabeçalho '{Exibir(obtido)}'; esperado '{esperada.Cabecalho}'.");
        }

        // Formatação do cabeçalho (C3): só aviso.
        try
        {
            if (!ws.Cell(1, 1).Style.Font.Bold) res.Avisar($"Aba '{ws.Name}': cabeçalho sem negrito.");
            if (!ws.AutoFilter.IsEnabled) res.Avisar($"Aba '{ws.Name}': sem filtro automático.");
        }
        catch (Exception) { /* informações de formatação são opcionais */ }

        // Linhas de dados.
        int falhasTipo = 0;
        for (int r = 2; r <= ultimaLinha; r++)
        {
            bool linhaVazia = true;
            for (int c = 1; c <= colunas.Length; c++)
            {
                var cel = ws.Cell(r, c);
                if (cel.IsEmpty()) continue;
                linhaVazia = false;

                var tipo = colunas[c - 1].Tipo;
                var dt = cel.DataType;
                string? problema = tipo switch
                {
                    Tipo.Data or Tipo.DataHora when dt != XLDataType.DateTime => $"esperado DateTime, veio {dt}",
                    Tipo.Valor or Tipo.Inteiro when dt != XLDataType.Number => $"esperado Number, veio {dt}",
                    Tipo.Texto when dt != XLDataType.Text => $"esperado texto, veio {dt}",
                    Tipo.SimNao when dt != XLDataType.Text || (cel.GetString() is not ("Sim" or "Não")) => $"esperado 'Sim' ou 'Não' em texto, veio {dt} '{Exibir(cel.GetString())}'",
                    _ => null,
                };
                if (problema != null)
                {
                    falhasTipo++;
                    if (falhasTipo <= 40)
                        res.Falhar("planilha-tipos", $"Aba '{ws.Name}', {cel.Address} ({colunas[c - 1].Cabecalho}): {problema}.");
                }
                else if (tipo is Tipo.Data or Tipo.DataHora)
                {
                    var formato = cel.Style.NumberFormat.Format.ToLowerInvariant();
                    var ok = tipo == Tipo.Data
                        ? formato is "dd/mm/yyyy" or "dd/mm/aaaa"
                        : formato is "dd/mm/yyyy hh:mm" or "dd/mm/aaaa hh:mm";
                    if (!ok && r == 2) res.Avisar($"Aba '{ws.Name}', coluna '{colunas[c - 1].Cabecalho}': formato '{cel.Style.NumberFormat.Format}' diferente do contrato.");
                }
                else if (tipo == Tipo.Valor)
                {
                    if (cel.Style.NumberFormat.Format != "#,##0.00" && r == 2)
                        res.Avisar($"Aba '{ws.Name}', coluna '{colunas[c - 1].Cabecalho}': formato '{cel.Style.NumberFormat.Format}' diferente de '#,##0.00'.");
                }
            }
            if (linhaVazia) continue;
            info.Linhas++;
            info.LinhasPlanilha.Add(r);
            info.Codigos.Add(ws.Cell(r, 1).GetString());
        }
        if (falhasTipo > 40) res.Falhar("planilha-tipos", $"Aba '{ws.Name}': mais {falhasTipo - 40} célula(s) com tipo errado.");
        return info;
    }

    // Resolve o destino de um hiperlink de célula para um caminho relativo do pacote; null se não for um link de arquivo.
    static string? DestinoRelativo(IXLCell cel, out string? motivo)
    {
        motivo = null;
        if (!cel.HasHyperlink) { motivo = "sem hiperlink"; return null; }
        var h = cel.GetHyperlink();
        string? alvo;
        if (h.IsExternal)
        {
            alvo = h.ExternalAddress?.OriginalString;
        }
        else
        {
            alvo = h.InternalAddress;
            motivo = $"hiperlink interno ('{alvo}') em vez de link para arquivo do pacote";
            return null;
        }
        if (string.IsNullOrEmpty(alvo)) { motivo = "hiperlink vazio"; return null; }
        alvo = alvo.Replace('\\', '/');
        try { alvo = Uri.UnescapeDataString(alvo); } catch (UriFormatException) { }
        if (alvo.StartsWith("file:", StringComparison.OrdinalIgnoreCase) || alvo.Contains("://") || Path.IsPathRooted(alvo))
        {
            motivo = $"hiperlink absoluto ('{alvo}'); deveria ser relativo";
            return null;
        }
        if (alvo.StartsWith("./", StringComparison.Ordinal)) alvo = alvo[2..];
        return alvo;
    }

    void VerificarLinksDePasta(IXLWorksheet ws, InfoAba info, string nomeAba)
    {
        var comAnexos = _titulos.Where(t => t.QuantidadeAnexos > 0).Select(t => t.Codigo).ToHashSet(StringComparer.Ordinal);
        for (int i = 0; i < info.Linhas; i++)
        {
            int r = info.LinhasPlanilha[i];
            var codigo = info.Codigos[i];
            var cel = ws.Cell(r, 1);
            var tem = cel.HasHyperlink;
            if (comAnexos.Contains(codigo) && !tem)
            {
                res.Falhar("planilha-hiperlinks", $"Aba '{nomeAba}', {cel.Address}: título {codigo} tem anexos mas o código não tem hiperlink para a pasta.");
                continue;
            }
            if (!tem) continue;
            var destino = DestinoRelativo(cel, out var motivo);
            if (destino == null) { res.Falhar("planilha-hiperlinks", $"Aba '{nomeAba}', {cel.Address}: {motivo}."); continue; }
            var pasta = destino.TrimEnd('/') + "/";
            if (!_pastas.Contains(pasta.TrimEnd('/')))
                res.Falhar("planilha-hiperlinks", $"Aba '{nomeAba}', {cel.Address}: hiperlink para '{Exibir(destino)}', que não é uma pasta do pacote.");
        }
    }

    void VerificarAbaAnexos(IXLWorksheet ws)
    {
        int ultima = ws.LastRowUsed()?.RowNumber() ?? 0;
        var porCaminho = _arquivos.ToDictionary(a => a.CaminhoRelativo, a => a, StringComparer.Ordinal);
        var vistos = new HashSet<string>(StringComparer.Ordinal);

        for (int r = 2; r <= ultima; r++)
        {
            var celArq = ws.Cell(r, 5);
            var caminhoTexto = celArq.GetString();
            if (string.IsNullOrEmpty(caminhoTexto) && ws.Cell(r, 1).IsEmpty()) continue;

            if (!celArq.HasHyperlink)
                res.Falhar("planilha-hiperlinks", $"Aba 'Anexos', {celArq.Address}: sem hiperlink para '{Exibir(caminhoTexto)}'.");
            else
            {
                var destino = DestinoRelativo(celArq, out var motivo);
                if (destino == null) res.Falhar("planilha-hiperlinks", $"Aba 'Anexos', {celArq.Address}: {motivo}.");
                else
                {
                    if (!_nomesEntradas.Contains(destino))
                        res.Falhar("planilha-hiperlinks", $"Aba 'Anexos', {celArq.Address}: hiperlink aponta para '{Exibir(destino)}', que não existe no ZIP.");
                    if (destino != caminhoTexto)
                        res.Falhar("planilha-hiperlinks", $"Aba 'Anexos', {celArq.Address}: o texto '{Exibir(caminhoTexto)}' difere do destino do hiperlink '{Exibir(destino)}'.");
                }
            }

            if (!vistos.Add(caminhoTexto))
                res.Falhar("planilha-totais", $"Aba 'Anexos': caminho repetido '{Exibir(caminhoTexto)}'.");
            if (!porCaminho.TryGetValue(caminhoTexto, out var item))
            {
                res.Falhar("planilha-totais", $"Aba 'Anexos', {celArq.Address}: '{Exibir(caminhoTexto)}' não está no índice.");
                continue;
            }

            // Colunas que repetem o índice.
            ComparaCelula(ws, r, 1, item.Titulo, "Código do título");
            ComparaCelula(ws, r, 2, item.TipoTitulo == "Solicitacao" ? "Solicitação" : "RDDV", "Tipo do título");
            ComparaCelula(ws, r, 3, item.TipoDocumento, "Tipo de documento");
            ComparaCelula(ws, r, 4, item.NomeOriginal, "Nome original");
            ComparaCelula(ws, r, 7, item.Sha256, "SHA-256");
            var tam = ws.Cell(r, 6);
            if (tam.DataType == XLDataType.Number && (long)tam.GetDouble() != item.TamanhoBytes)
                res.Falhar("planilha-totais", $"Aba 'Anexos', {tam.Address}: tamanho {tam.GetDouble():0} difere do índice ({item.TamanhoBytes}).");
        }
    }

    void ComparaCelula(IXLWorksheet ws, int linha, int coluna, string esperado, string nome)
    {
        var cel = ws.Cell(linha, coluna);
        var obtido = cel.GetString();
        if (!string.Equals(obtido, esperado, StringComparison.Ordinal))
            res.Falhar("planilha-totais", $"Aba 'Anexos', {cel.Address} ({nome}): '{Exibir(obtido)}' difere do índice ('{Exibir(esperado)}').");
    }

    // ---- Prévia HTML --------------------------------------------------------------------------------------

    string MontarPrevia()
    {
        var sb = new StringBuilder();
        string H(string s) => WebUtility.HtmlEncode(s);

        sb.AppendLine("<!DOCTYPE html><html lang=\"pt-BR\"><head><meta charset=\"utf-8\"><title>Prévia do pacote de exportação</title>");
        sb.AppendLine("<style>body{font-family:Segoe UI,Arial,sans-serif;margin:24px;color:#222}h1{font-size:20px}h2{font-size:16px;margin-top:28px}");
        sb.AppendLine("table{border-collapse:collapse;font-size:12px;margin-bottom:8px}th,td{border:1px solid #ccc;padding:3px 6px;white-space:nowrap;max-width:260px;overflow:hidden;text-overflow:ellipsis}");
        sb.AppendLine("th{background:#eee}.ok{color:#0a7a2f;font-weight:bold}.erro{color:#b00020;font-weight:bold}.pasta{font-weight:bold}.tree{font-family:Consolas,monospace;font-size:12px;line-height:1.5}");
        sb.AppendLine(".scroll{overflow-x:auto}</style></head><body>");
        sb.AppendLine($"<h1>Prévia do pacote <code>{H(Path.GetFileName(caminhoZip))}</code></h1>");
        sb.AppendLine($"<p>Resultado da verificação: <span class=\"{(res.Aprovado ? "ok" : "erro")}\">{(res.Aprovado ? "APROVADO" : "REPROVADO")}</span>" +
                      $" &middot; {_entradas.Count:N0} entradas &middot; {_arquivos.Count:N0} anexos &middot; {_arquivos.Sum(a => a.TamanhoBytes):N0} bytes de anexos &middot; {_titulos.Count:N0} títulos</p>");
        sb.AppendLine("<p>Dados fictícios gerados pela semente; a prévia mostra só as 10 primeiras linhas de cada aba.</p>");

        if (!res.Aprovado)
        {
            sb.AppendLine("<ul>");
            foreach (var f in res.Falhas.Take(20)) sb.AppendLine($"<li class=\"erro\">{H(f.Regra)}: {H(f.Detalhe)}</li>");
            sb.AppendLine("</ul>");
        }

        // Árvore de pastas.
        sb.AppendLine("<h2>Árvore do pacote</h2><div class=\"tree\">");
        var raiz = _entradas.Where(e => !e.FullName.Contains('/')).OrderBy(e => e.FullName, StringComparer.Ordinal).ToList();
        foreach (var e in raiz) sb.AppendLine($"{H(e.FullName)} <small>({e.Length:N0} bytes)</small><br>");
        var grupos = _entradas.Where(e => e.FullName.Contains('/')).GroupBy(e => e.FullName[..e.FullName.IndexOf('/')]).OrderBy(g => g.Key, StringComparer.Ordinal).ToList();
        foreach (var g in grupos.Take(40))
        {
            sb.AppendLine($"<span class=\"pasta\">{H(g.Key)}/</span><br>");
            foreach (var e in g.OrderBy(x => x.FullName, StringComparer.Ordinal).Take(6))
                sb.AppendLine($"&nbsp;&nbsp;&nbsp;&nbsp;{H(e.FullName[(e.FullName.IndexOf('/') + 1)..])} <small>({e.Length:N0} bytes)</small><br>");
            if (g.Count() > 6) sb.AppendLine($"&nbsp;&nbsp;&nbsp;&nbsp;... e mais {g.Count() - 6} arquivo(s)<br>");
        }
        if (grupos.Count > 40) sb.AppendLine($"<em>... e mais {grupos.Count - 40} pasta(s)</em>");
        sb.AppendLine("</div>");

        // Abas.
        if (_pasta != null)
        {
            foreach (var ws in _pasta.Worksheets)
            {
                int ultimaColuna = ws.LastColumnUsed()?.ColumnNumber() ?? 0;
                int ultimaLinha = ws.LastRowUsed()?.RowNumber() ?? 0;
                sb.AppendLine($"<h2>Aba {H(ws.Name)} <small>({Math.Max(0, ultimaLinha - 1):N0} linha(s) de dados)</small></h2><div class=\"scroll\"><table><tr>");
                for (int c = 1; c <= ultimaColuna; c++) sb.Append($"<th>{H(ws.Cell(1, c).GetString())}</th>");
                sb.AppendLine("</tr>");
                for (int r = 2; r <= Math.Min(ultimaLinha, 11); r++)
                {
                    sb.Append("<tr>");
                    for (int c = 1; c <= ultimaColuna; c++)
                    {
                        string texto;
                        try { texto = ws.Cell(r, c).GetFormattedString(); }
                        catch (Exception) { texto = ws.Cell(r, c).GetString(); }
                        sb.Append($"<td>{H(texto)}</td>");
                    }
                    sb.AppendLine("</tr>");
                }
                sb.AppendLine("</table></div>");
            }
        }

        sb.AppendLine("</body></html>");
        return sb.ToString();
    }
}
