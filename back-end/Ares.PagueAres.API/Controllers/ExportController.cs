using Ares.PagueAres.API.Dtos;
using Ares.PagueAres.Domain.Dtos.Authentication;
using Ares.PagueAres.Infrastructure;
using ICSharpCode.SharpZipLib.Core;
using ICSharpCode.SharpZipLib.Zip;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Newtonsoft.Json;
using Spire.Xls;
using System.Collections.Concurrent;

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

        private Dictionary<int, string> StatusGestor = new Dictionary<int, string>() {
            {0, "Aguardando Autorização"},
            {1, "Aguardando Autorização"},
            {2, "Autorizado"},
            {3, "Negado"}
        };

        private Dictionary<int, string> StatusContabil = new Dictionary<int, string>() {
            {0, "Aguardando Lançamento"},
            {1, "Aguardando Lançamento"},
            {2, "Lançado"},
            {3, "Negado"}
        };

        private Dictionary<int, string> StatusFinanceiro = new Dictionary<int, string>() {
            {0, "Aguardando Autorização"},
            {1, "Aguardando Autorização"},
            {2, "Autorizado"},
            {3, "Negado" }
        };

        private Dictionary<int, string> DocumentTypes = new Dictionary<int, string>() {
            {1, "Adiantamento"},
            {2, "Pagamento"},
            {3, "Movimento Almoxarifado"}
        };

        private Dictionary<int, string> RddvReportType = new Dictionary<int, string>() {
            {1, "Adiantamento" },
            {2, "Prestação de Contas" },
            {3, "Reembolso"}
        };

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
                var requests = dbContext.Solicitacaos.AsQueryable();

                // Escopo de FIN e ADM, os únicos papéis que recebem a chave de exportação:
                // rascunhos e cancelados nunca entram, e só títulos aprovados pelo gestor.
                requests = requests.Where(d => !d.Rascunho && !d.Cancelado);
                requests = requests.Where(r => (r.AprovadoGestor ?? false));

                if (!string.IsNullOrEmpty(filter.DocumentNumber))
                {
                    requests = requests.Where(d => filter.DocumentNumber == d.NumeroDocumento);
                }

                if (filter.RequestStart.HasValue && filter.RequestEnd.HasValue)
                {
                    requests = requests.Where(d => d.DataSolicitacao >= filter.RequestStart && d.DataSolicitacao <= filter.RequestEnd);
                }

                if ((filter.AssigneeId ?? 0) > 0)
                {
                    requests = requests.Where(d => d.IdUsuarioAtribuido == filter.AssigneeId);
                }

                if (filter.DueStart.HasValue && filter.DueEnd.HasValue)
                {
                    requests = requests.Where(d => d.DataVencimento >= filter.DueStart && d.DataVencimento <= filter.DueEnd);
                }

                if ((filter.RequesterId ?? 0) > 0)
                {
                    requests = requests.Where(d => d.IdUsuario == filter.RequesterId);
                }

                if ((filter.ManagerStatus ?? 0) > 0)
                {
                    requests = requests.Where(d => (d.StatusGestor ?? 1) == filter.ManagerStatus);
                }

                if ((filter.FinanceStatus ?? 0) > 0)
                {
                    requests = requests.Where(d => (d.StatusFinanceiro ?? 1) == filter.FinanceStatus);
                }

                if ((filter.AccountingStatus ?? 0) > 0)
                {
                    requests = requests.Where(d => (d.StatusContabilidade ?? 1) == filter.AccountingStatus);
                }

                if (!string.IsNullOrEmpty(filter.PartnerName))
                {
                    requests = requests.Where(d => d.NomeParceiro.ToLower().Contains(filter.PartnerName));
                }

                if (!string.IsNullOrEmpty(filter.PartnerDocument))
                {
                    requests = requests.Where(d => d.NumDocParceiro.ToLower().Contains(filter.PartnerDocument));
                }

                if (filter.DocumentId.HasValue)
                {
                    requests = requests.Where(d => d.IdSolicitacao == filter.DocumentId.Value);
                }

                var requestsQuery = (from r in requests
                                     from u in dbContext.Usuarios.Where(x => x.IdUsuario == r.IdUsuario)
                                     from g in dbContext.Usuarios.Where(k => k.IdUsuario == r.IdGestor).DefaultIfEmpty()
                                     select new PaymentListItem()
                                     {
                                         Codigo = r.IdSolicitacao.ToString(),
                                         Tipo = "Pagamento",
                                         DataInclusao = r.DataSolicitacao.ToString("dd/MM/yyyy"),
                                         DataVencimento = r.DataVencimento.HasValue ? r.DataVencimento.Value.ToString("dd/MM/yyyy") : "",
                                         DocNumero = r.NumeroDocumento ?? "",
                                         Solicitante = u.Nome,
                                         Parceiro = r.NomeParceiro,
                                         Valor = r.Valor.ToString("0.00"),
                                         StatusGestor = StatusGestor[r.StatusGestor ?? 0],
                                         StatusContabil = StatusContabil[r.StatusContabilidade ?? 0],
                                         StatusFinanceiro = StatusFinanceiro[r.StatusFinanceiro ?? 0]
                                     });

                var rddv = dbContext.Rddv.AsQueryable();

                // Mesmo escopo de FIN e ADM das solicitações.
                rddv = rddv.Where(d => !d.Rascunho && !d.Cancelado);
                rddv = rddv.Where(r => (r.AprovadoGestor ?? false));

                if (filter.RequestStart.HasValue && filter.RequestEnd.HasValue)
                {
                    rddv = rddv.Where(d => d.DataCadastro >= filter.RequestStart && d.DataCadastro <= filter.RequestEnd);
                }

                if ((filter.AssigneeId ?? 0) > 0)
                {
                    rddv = rddv.Where(d => d.IdUsuarioAtribuido == filter.AssigneeId);
                }

                if (filter.DueStart.HasValue && filter.DueEnd.HasValue)
                {
                    rddv = rddv.Where(d => d.DataVencimento >= filter.DueStart && d.DataVencimento <= filter.DueEnd);
                }

                if ((filter.RequesterId ?? 0) > 0)
                {
                    rddv = rddv.Where(d => d.IdUsuario == filter.RequesterId);
                }

                if ((filter.ManagerStatus ?? 0) > 0)
                {
                    rddv = rddv.Where(d => (d.StatusGestor ?? 1) == filter.ManagerStatus);
                }

                if ((filter.FinanceStatus ?? 0) > 0)
                {
                    rddv = rddv.Where(d => (d.StatusFinanceiro ?? 1) == filter.FinanceStatus);
                }

                if ((filter.AccountingStatus ?? 0) > 0)
                {
                    rddv = rddv.Where(d => (d.StatusContabilidade ?? 1) == filter.AccountingStatus);
                }

                if (filter.DocumentId.HasValue)
                {
                    rddv = rddv.Where(d => d.IdRelatorio == filter.DocumentId.Value);
                }

                var rddvQuery = (from r in rddv
                                 from u in dbContext.Usuarios.Where(x => x.IdUsuario == r.IdUsuario)
                                 from g in dbContext.Usuarios.Where(k => k.IdUsuario == r.IdGestor).DefaultIfEmpty()
                                 from d in dbContext.vDespesasRddv.Where(k => k.IdRelatorio == r.IdRelatorio).DefaultIfEmpty()
                                 select new PaymentListItem()
                                 {
                                     Codigo = r.IdRelatorio.ToString(),
                                     Tipo = "RDDV - " + RddvReportType[r.TipoRelatorio],
                                     DataInclusao = r.DataCadastro.ToString("dd/MM/yyyy"),
                                     DataVencimento = r.DataVencimento.HasValue ? r.DataVencimento.Value.ToString("dd/MM/yyyy") : "",
                                     DocNumero = "#",
                                     Solicitante = u.Nome ?? "",
                                     Parceiro = u.Nome ?? "",
                                     Valor = d.Valor.ToString("0.00"),
                                     StatusGestor = StatusGestor[r.StatusGestor ?? 0],
                                     StatusContabil = StatusContabil[r.StatusContabilidade ?? 0],
                                     StatusFinanceiro = StatusFinanceiro[r.StatusFinanceiro ?? 0]
                                 });

                ConcurrentBag<IEnumerable<PaymentListItem>> items = new();
                List<Task> tasks = new();

                items.Add(await requestsQuery.ToListAsync());
                items.Add(await rddvQuery.ToListAsync());

                await Task.WhenAll(tasks);

                var dataset = items.SelectMany(x => x.ToList());

                var book = new Workbook();
                book.Worksheets.Clear();

                var sheet = book.Worksheets.Add("Pagamentos");

                var row = 1;

                sheet[row, 1].Value = "Codigo";
                sheet[row, 2].Value = "Tipo";
                sheet[row, 3].Value = "Data de Inclusao";
                sheet[row, 4].Value = "Data de Vencimento";
                sheet[row, 5].Value = "Doc. Número";
                sheet[row, 6].Value = "Solicitante";
                sheet[row, 7].Value = "Parceiro";
                sheet[row, 8].Value = "Valor";
                sheet[row, 9].Value = "Status Gestor";
                sheet[row, 10].Value = "Status Contábil";
                sheet[row, 11].Value = "Status Financeiro";

                row++;

                foreach (var item in dataset)
                {
                    sheet[row, 1].Value = item.Codigo;
                    sheet[row, 2].Value = item.Tipo;
                    sheet[row, 3].Value = item.DataInclusao;
                    sheet[row, 4].Value = item.DataVencimento;
                    sheet[row, 5].Value = item.DocNumero;
                    sheet[row, 6].Value = item.Solicitante;
                    sheet[row, 7].Value = item.Parceiro;
                    sheet[row, 8].Value = item.Valor;
                    sheet[row, 9].Value = item.StatusGestor;
                    sheet[row, 10].Value = item.StatusContabil;
                    sheet[row, 11].Value = item.StatusFinanceiro;

                    row++;
                }

                sheet.Range[1, 1, dataset.Count() + 1, 11].BorderAround();
                sheet.Range[1, 1, dataset.Count() + 1, 11].BorderInside();
                sheet.Range[1, 1, dataset.Count() + 1, 11].AutoFitColumns();

                sheet.Range[1, 1, 1, 11].Style.Font.IsBold = true;

                var ms = new MemoryStream();

                book.SaveToStream(ms, FileFormat.Version2016);

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

                foreach (var attachment in attachments)
                {
                    MemoryStream msAtt = new MemoryStream(Convert.FromBase64String(attachment.Arquivo.Contains(",") ? attachment.Arquivo.Split(",")[1] : attachment.Arquivo));

                    ZipEntry xmlEntry = new ZipEntry(attachment.NomeArquivo);
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

                foreach (var attachment in attachments)
                {
                    MemoryStream msAtt = new MemoryStream(Convert.FromBase64String(attachment.Arquivo.Contains(",") ? attachment.Arquivo.Split(",")[1] : attachment.Arquivo));

                    ZipEntry xmlEntry = new ZipEntry(attachment.NomeArquivo);
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
    }
}
