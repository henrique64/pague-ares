using Ares.PagueAres.API.Dtos;
using Ares.PagueAres.Application.Exportacao;
using Ares.PagueAres.Domain.Dtos.Authentication;
using Ares.PagueAres.Domain.Dtos.Listing;
using Ares.PagueAres.Infrastructure;
using ICSharpCode.SharpZipLib.Core;
using ICSharpCode.SharpZipLib.Zip;
using ClosedXML.Excel;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Newtonsoft.Json;

namespace Ares.PagueAres.API.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    [Authorize]
    public class ExportController(PagueAresContext dbContext, IMemoryCache memoryCache, ILogger<ExportController> logger) : ControllerBase
    {
        private const string MensagemErroGenerica = "Não foi possível concluir a operação. Tente de novo.";
        private const string MensagemFiltroInvalido = "Filtro de exportação inválido.";

        // Garante que a leitura e a remoção da chave de exportação aconteçam juntas (uso único).
        private static readonly Lock TravaChaveExportacao = new();

        /// <summary>
        /// Valida a chave de exportação e a remove do cache na mesma operação, para que ela só
        /// possa ser usada uma vez. Devolve a resposta de recusa, ou null quando a chave é válida.
        /// </summary>
        private ActionResult? ConsumirChaveExportacao(string key)
        {
            lock (TravaChaveExportacao)
            {
                if (!memoryCache.TryGetValue("preview-key-" + key, out PreviewKeyDto? previewKey))
                {
                    return Unauthorized("Chave de visualização inválida ou expirada.");
                }

                if (previewKey?.RequestType != Domain.Enums.EnumRequestType.Exportacao)
                {
                    return Unauthorized("Chave de visualização inválida para esta solicitação.");
                }

                memoryCache.Remove("preview-key-" + key);

                return null;
            }
        }

        /// <param name="userId">
        /// Ignorado. Continua aceito porque o front-end ainda o envia, mas o escopo da exportação
        /// não depende de quem diz ser o usuário: a chave só é emitida para FIN e ADM.
        /// </param>
        [HttpGet]
        [Route("payments")]
        [AllowAnonymous]
        public async Task<ActionResult> ExportPaymentList([FromQuery] string sFilter, [FromQuery] int userId, [FromQuery] string key)
        {
            if (string.IsNullOrEmpty(key)) return Unauthorized("Chave de visualização não informada.");

            var recusaChave = ConsumirChaveExportacao(key);

            if (recusaChave != null)
            {
                return recusaChave;
            }

            ListFilterDto? nFilter;

            try
            {
                nFilter = JsonConvert.DeserializeObject<ListFilterDto>(sFilter);
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Filtro da exportação de pagamentos inválido.");
                return BadRequest(MensagemFiltroInvalido);
            }

            if (nFilter == null)
            {
                return BadRequest(MensagemFiltroInvalido);
            }

            ListFilterDto filter = nFilter;

            try
            {
                // A planilha parte da mesma view da lista (solicitações e RDDVs juntos) e usa a mesma regra de filtro
                // (ListingController.ApplyFilters), para trazer os mesmos títulos que a aba Financeiro/Contábil mostra
                // ao Financeiro com o mesmo filtro.
                var titulos = dbContext.ViewListagemSolicitacoes.AsQueryable();

                // Escopo de FIN e ADM, os únicos papéis que recebem a chave de exportação:
                // rascunhos e cancelados nunca entram, e só títulos aprovados pelo gestor.
                titulos = titulos.Where(r => r.AprovadoGestor == true && r.Rascunho != true && r.Cancelado != true);

                titulos = ListingController.ApplyFilters(titulos, new ListingFilterDto(
                    DocumentNumber: filter.DocumentNumber,
                    RequestStart: filter.RequestStart,
                    RequestEnd: filter.RequestEnd,
                    DueStart: filter.DueStart,
                    DueEnd: filter.DueEnd,
                    Type: null,
                    PartnerName: filter.PartnerName,
                    RequesterId: filter.RequesterId,
                    AssigneeId: filter.AssigneeId,
                    IsAssigned: filter.IsAssigned,
                    ManagerStatus: filter.ManagerStatus,
                    FinanceStatus: filter.FinanceStatus,
                    AccountingStatus: filter.AccountingStatus,
                    DocumentId: filter.DocumentId,
                    ViewMode: null,
                    SortField: null));

                // O documento do parceiro não existe na lista nem na view: vem da solicitação. O RDDV não tem documento
                // do parceiro e sai, como sai nos filtros de nome do parceiro e de número do documento.
                if (!string.IsNullOrEmpty(filter.PartnerDocument))
                {
                    var documentoParceiro = filter.PartnerDocument.ToLower();

                    titulos = titulos.Where(r => r.IdTipo == 0 && dbContext.Solicitacaos.Any(s =>
                        s.IdSolicitacao == r.Codigo && s.NumDocParceiro.ToLower().Contains(documentoParceiro)));
                }

                // Solicitações primeiro e RDDVs depois, cada grupo pelo código, como no relatório antigo.
                var linhas = await titulos
                    .OrderBy(r => r.IdTipo == 0 ? 0 : 1)
                    .ThenBy(r => r.Codigo)
                    .Select(r => new
                    {
                        r.Codigo,
                        r.IdTipo,
                        r.DataSolicitacao,
                        r.DataVencimento,
                        r.NumeroDocumento,
                        r.NomeSolicitante,
                        r.NomeParceiro,
                        r.Valor,
                        r.IdStatusGestor,
                        r.IdStatusContabilidade,
                        r.IdStatusFinanceiro
                    })
                    .ToListAsync();

                var dataset = linhas.Select(r =>
                {
                    var ehSolicitacao = r.IdTipo == 0;

                    return new PaymentListItem()
                    {
                        Codigo = r.Codigo.ToString(),
                        Tipo = ehSolicitacao ? "Pagamento" : "RDDV - " + RotulosExportacao.TipoRelatorio(r.IdTipo),
                        DataInclusao = r.DataSolicitacao,
                        DataVencimento = r.DataVencimento,
                        // O RDDV não tem número de documento: a view grava "#", e a planilha deixa a célula vazia.
                        DocNumero = ehSolicitacao ? r.NumeroDocumento ?? "" : "",
                        Solicitante = r.NomeSolicitante ?? "",
                        // No RDDV, o parceiro é o próprio solicitante (a view grava "#").
                        Parceiro = ehSolicitacao ? r.NomeParceiro ?? "" : r.NomeSolicitante ?? "",
                        Valor = r.Valor ?? 0m,
                        StatusGestor = RotulosExportacao.StatusGestor(r.IdStatusGestor),
                        StatusContabil = RotulosExportacao.StatusContabil(r.IdStatusContabilidade),
                        StatusFinanceiro = RotulosExportacao.StatusFinanceiro(r.IdStatusFinanceiro)
                    };
                });

                using var book = new XLWorkbook();
                var sheet = book.Worksheets.Add("Pagamentos");

                // Rótulos na caixa da planilha nova (PlanilhaExportacao) e da aba Financeiro/Contábil.
                string[] cabecalho =
                [
                    "Código", "Tipo", "Data de inclusão", "Data de vencimento", "Nº do documento", "Solicitante",
                    "Parceiro", "Valor", "Status gestor", "Status contábil", "Status financeiro"
                ];

                for (var coluna = 0; coluna < cabecalho.Length; coluna++)
                {
                    sheet.Cell(1, coluna + 1).Value = cabecalho[coluna];
                }

                var row = 2;

                foreach (var item in dataset)
                {
                    sheet.Cell(row, 1).Value = item.Codigo;
                    sheet.Cell(row, 2).Value = item.Tipo;
                    sheet.Cell(row, 3).Value = item.DataInclusao;
                    sheet.Cell(row, 4).Value = item.DataVencimento.HasValue ? item.DataVencimento.Value : Blank.Value;
                    sheet.Cell(row, 5).Value = item.DocNumero;
                    sheet.Cell(row, 6).Value = item.Solicitante;
                    sheet.Cell(row, 7).Value = item.Parceiro;
                    sheet.Cell(row, 8).Value = item.Valor;
                    sheet.Cell(row, 9).Value = item.StatusGestor;
                    sheet.Cell(row, 10).Value = item.StatusContabil;
                    sheet.Cell(row, 11).Value = item.StatusFinanceiro;

                    row++;
                }

                var ultimaLinha = row - 1;

                sheet.Range(2, 3, ultimaLinha, 4).Style.DateFormat.Format = "dd/mm/yyyy";
                sheet.Range(2, 8, ultimaLinha, 8).Style.NumberFormat.Format = "#,##0.00";

                var tabela = sheet.Range(1, 1, ultimaLinha, 11);
                tabela.Style.Border.OutsideBorder = XLBorderStyleValues.Thin;
                tabela.Style.Border.InsideBorder = XLBorderStyleValues.Thin;
                sheet.Range(1, 1, 1, 11).Style.Font.Bold = true;
                sheet.Columns(1, 11).AdjustToContents();

                var ms = new MemoryStream();

                book.SaveAs(ms);

                ms.Position = 0;

                return File(ms, "application/xlsx", "Pagamentos.xlsx");
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Falha ao gerar o Excel da exportação de pagamentos.");
                return StatusCode(StatusCodes.Status500InternalServerError, MensagemErroGenerica);
            }
        }

        [HttpGet]
        [Route("rddv/attachment/{id}")]
        [AllowAnonymous]
        public async Task<ActionResult> ExportRddvAttachments([FromRoute] int id, [FromQuery] string key)
        {
            try
            {
                if (string.IsNullOrEmpty(key)) return Unauthorized("Chave de visualização não informada.");

                if (memoryCache.TryGetValue("preview-key-" + key, out PreviewKeyDto? previewKey))
                {
                    // A chave precisa ser deste RDDV: outro id ou outro tipo de título é recusado.
                    if (previewKey?.EntityId != id ||
                        previewKey?.RequestType != Domain.Enums.EnumRequestType.Rddv)
                    {
                        return Unauthorized("Chave de visualização não autorizada para esta solicitação.");
                    }
                }
                else
                {
                    return Unauthorized("Chave de visualização inválida ou expirada.");
                }

                var attachments = await dbContext.DocumentoRddv.Where(r => r.IdRelatorio == id).ToListAsync();

                if (attachments.Count == 0)
                {
                    return BadRequest("O RDDV solicitado não existe ou não contém anexos.");
                }

                MemoryStream zipMs = new MemoryStream();
                ZipOutputStream zipStream = new ZipOutputStream(zipMs);

                zipStream.SetLevel(3); //0-9, 9 being the highest level of compression

                var nomesEntradas = NomesEntradasZip(attachments.Select(a => (a.IdDocumentoRddv, (string?)a.NomeArquivo)));

                foreach (var (attachment, nomeEntrada) in attachments.Zip(nomesEntradas))
                {
                    MemoryStream msAtt = new MemoryStream(Convert.FromBase64String(attachment.Arquivo.Contains(",") ? attachment.Arquivo.Split(",")[1] : attachment.Arquivo));

                    ZipEntry xmlEntry = new ZipEntry(nomeEntrada);
                    xmlEntry.DateTime = DateTime.Now;

                    zipStream.PutNextEntry(xmlEntry);

                    StreamUtils.Copy(msAtt, zipStream, new byte[4096]);
                    zipStream.CloseEntry();
                }

                zipStream.IsStreamOwner = false;
                zipStream.Close();

                zipMs.Position = 0;

                return File(zipMs, "application/zip", $"Anexos-RDDV-{id}.zip");
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Falha ao gerar o ZIP de anexos do RDDV {Id}.", id);
                return StatusCode(StatusCodes.Status500InternalServerError, MensagemErroGenerica);
            }
        }

        [HttpGet]
        [Route("request/attachment/{id}")]
        [AllowAnonymous]
        public async Task<ActionResult> ExportRequestAttachments([FromRoute] int id, [FromQuery] string key)
        {
            try
            {
                if (string.IsNullOrEmpty(key)) return Unauthorized("Chave de visualização não informada.");

                if (memoryCache.TryGetValue("preview-key-" + key, out PreviewKeyDto? previewKey))
                {
                    // A chave precisa ser desta solicitação: outro id ou outro tipo de título é recusado.
                    if (previewKey?.EntityId != id ||
                        previewKey?.RequestType != Domain.Enums.EnumRequestType.Solicitacao)
                    {
                        return Unauthorized("Chave de visualização não autorizada para esta solicitação.");
                    }
                }
                else
                {
                    return Unauthorized("Chave de visualização inválida ou expirada.");
                }

                var attachments = await dbContext.DocumentoSolicitacaos.Where(r => r.IdSolicitacao == id).ToListAsync();

                if (attachments.Count == 0)
                {
                    return BadRequest("O documento solicitado não existe ou não contém anexos.");
                }

                MemoryStream zipMs = new MemoryStream();
                ZipOutputStream zipStream = new ZipOutputStream(zipMs);

                zipStream.SetLevel(3); //0-9, 9 being the highest level of compression

                var nomesEntradas = NomesEntradasZip(attachments.Select(a => (a.IdDocumentoSolicitacao, (string?)a.NomeArquivo)));

                foreach (var (attachment, nomeEntrada) in attachments.Zip(nomesEntradas))
                {
                    MemoryStream msAtt = new MemoryStream(Convert.FromBase64String(attachment.Arquivo.Contains(",") ? attachment.Arquivo.Split(",")[1] : attachment.Arquivo));

                    ZipEntry xmlEntry = new ZipEntry(nomeEntrada);
                    xmlEntry.DateTime = DateTime.Now;

                    zipStream.PutNextEntry(xmlEntry);

                    StreamUtils.Copy(msAtt, zipStream, new byte[4096]);
                    zipStream.CloseEntry();
                }

                zipStream.IsStreamOwner = false;
                zipStream.Close();

                zipMs.Position = 0;

                return File(zipMs, "application/zip", $"Anexos-Solicitacao-{id}.zip");
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Falha ao gerar o ZIP de anexos da solicitação {Id}.", id);
                return StatusCode(StatusCodes.Status500InternalServerError, MensagemErroGenerica);
            }
        }

        /// <summary>
        /// Nomes das entradas dos ZIPs por título, na ordem dos anexos. O nome original vem do usuário: passa
        /// por <see cref="NomesPacote.Sanitizar"/> (sem <c>..</c>, separador de pasta nem unidade, para a extração
        /// não escrever fora da pasta de destino) e por <see cref="NomesPacote.Deduplicar"/> (dois anexos de
        /// mesmo nome, sem diferenciar caixa, não se sobrescrevem). Nome que já é seguro fica como está; nome que
        /// fica vazio depois da sanitização vira <c>anexo-&lt;id do anexo&gt;</c>.
        /// </summary>
        private static IReadOnlyList<string> NomesEntradasZip(IEnumerable<(int IdAnexo, string? NomeArquivo)> anexos)
        {
            return NomesPacote.Deduplicar(anexos.Select(anexo =>
            {
                var nome = NomesPacote.Sanitizar(anexo.NomeArquivo);
                return nome.Length > 0 ? nome : $"anexo-{anexo.IdAnexo}";
            }));
        }
    }
}
