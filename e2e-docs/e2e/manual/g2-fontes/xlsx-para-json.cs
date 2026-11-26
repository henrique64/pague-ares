#:property PublishAot=false
#:package ClosedXML@0.105.0

// Lê uma planilha .xlsx (titulos.xlsx do pacote ou Pagamentos.xlsx) e grava o conteúdo em JSON, do jeito que o Excel
// mostraria: texto já formatado (datas, valores), negrito, hiperlink, largura de coluna e linhas congeladas.
// Quem monta as páginas das fotos do manual (montar-paginas.ts) lê esse JSON; nada é inventado aqui.
//
// Uso:  dotnet run e2e-docs/e2e/manual/g2-fontes/xlsx-para-json.cs -- <planilha.xlsx> <saida.json>

using System.Text.Json;
using ClosedXML.Excel;

if (args.Length != 2)
{
    Console.Error.WriteLine("Uso: dotnet run xlsx-para-json.cs -- <planilha.xlsx> <saida.json>");
    return 2;
}

using var livro = new XLWorkbook(args[0]);
var abas = new List<object>();
foreach (var aba in livro.Worksheets)
{
    var ultimaLinha = aba.LastRowUsed()?.RowNumber() ?? 0;
    var ultimaColuna = aba.LastColumnUsed()?.ColumnNumber() ?? 0;
    var larguras = Enumerable.Range(1, ultimaColuna).Select(c => Math.Round(aba.Column(c).Width, 1)).ToList();
    var linhas = new List<object>();
    for (var l = 1; l <= ultimaLinha; l++)
    {
        var celulas = new List<object>();
        for (var c = 1; c <= ultimaColuna; c++)
        {
            var cel = aba.Cell(l, c);
            string? ligacao = null;
            if (cel.HasHyperlink)
            {
                var h = cel.GetHyperlink();
                ligacao = h.IsExternal ? h.ExternalAddress?.ToString() : h.InternalAddress;
            }
            celulas.Add(new
            {
                t = cel.IsEmpty() ? "" : cel.GetFormattedString(),
                num = cel.DataType is XLDataType.Number or XLDataType.DateTime,
                neg = cel.Style.Font.Bold,
                fundo = cel.Style.Fill.PatternType == XLFillPatternValues.Solid ? cel.Style.Fill.BackgroundColor.Color.ToArgb().ToString("X8")[2..] : null,
                lig = ligacao,
            });
        }
        linhas.Add(celulas);
    }
    abas.Add(new
    {
        nome = aba.Name,
        linhasCongeladas = aba.SheetView.SplitRow,
        colunasCongeladas = aba.SheetView.SplitColumn,
        filtro = aba.AutoFilter.IsEnabled,
        bordas = aba.Cell(1, 1).Style.Border.TopBorder != XLBorderStyleValues.None,
        larguras,
        linhas,
    });
}

File.WriteAllText(args[1], JsonSerializer.Serialize(new { abas }, new JsonSerializerOptions { WriteIndented = false, Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping }));
Console.WriteLine($"{abas.Count} aba(s) gravada(s) em {args[1]}");
return 0;
