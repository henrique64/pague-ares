using Ares.PagueAres.API.Extensions;
using Ares.PagueAres.Domain;
using Ares.PagueAres.Domain.Dtos.Authentication;
using Ares.PagueAres.Domain.Enums;
using Ares.PagueAres.Infrastructure;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Internal;
using Microsoft.Extensions.Caching.Memory;

namespace Ares.PagueAres.API.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    [Authorize]
    public class PreviewController(PagueAresContext dbContext, IMemoryCache memoryCache, ILogger<PreviewController> logger) : ControllerBase
    {
        private const string MensagemErroGenerica = "Não foi possível concluir a operação. Tente de novo.";

        [AllowAnonymous]
        [HttpGet("payment/{id}/{attachmentId}")]
        public async Task<ActionResult> PreviewPaymentAttachment([FromRoute] int id, [FromRoute] int attachmentId, [FromQuery] string key)
        {
            try
            {
                if (string.IsNullOrEmpty(key)) return Unauthorized("Chave de visualização não informada.");

                if (memoryCache.TryGetValue("preview-key-" + key, out PreviewKeyDto? previewKey))
                {
                    if (previewKey?.EntityId != id ||
                        previewKey?.RequestType != EnumRequestType.Solicitacao)
                    {
                        return Unauthorized("Chave de visualização não autorizada para esta solicitação.");
                    }
                }
                else
                {
                    return Unauthorized("Chave de visualização inválida ou expirada.");
                }

                var data = await dbContext.DocumentoSolicitacaos.Where(x => x.IdDocumentoSolicitacao == attachmentId).FirstOrDefaultAsync();

                if (data == null)
                {
                    return NotFound("O documento solicitado não foi localizado.");
                }

                string b64data = data.Arquivo;

                if (b64data.Contains(','))
                    b64data = b64data.Split(",")[1];

                var rawData = Convert.FromBase64String(b64data);

                if (string.IsNullOrEmpty(data.CaminhoArquivo))
                    data.CaminhoArquivo = "application/octet-stream";

                return File(rawData, data.CaminhoArquivo, data.NomeArquivo);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Falha ao abrir o anexo {AttachmentId} da solicitação {Id} para visualização.", attachmentId, id);
                return StatusCode(StatusCodes.Status500InternalServerError, MensagemErroGenerica);
            }
        }

        [AllowAnonymous]
        [HttpGet("rddv/{id}/{attachmentId}")]
        public async Task<ActionResult> PreviewRddvAttachment([FromRoute] int id, [FromRoute] int attachmentId, [FromQuery] string key)
        {
            try
            {
                if (string.IsNullOrEmpty(key)) return Unauthorized("Chave de visualização não informada.");

                if (memoryCache.TryGetValue("preview-key-" + key, out PreviewKeyDto? previewKey))
                {
                    if (previewKey?.EntityId != id ||
                        previewKey?.RequestType != EnumRequestType.Rddv)
                    {
                        return Unauthorized("Chave de visualização não autorizada para esta solicitação.");
                    }
                }
                else
                {
                    return Unauthorized("Chave de visualização inválida ou expirada.");
                }

                var data = await dbContext.DocumentoRddv.Where(x => x.IdDocumentoRddv == attachmentId).FirstOrDefaultAsync();

                if (data == null)
                {
                    return NotFound("O documento solicitado não foi localizado.");
                }

                string b64data = data.Arquivo;

                if (b64data.Contains(','))
                    b64data = b64data.Split(",")[1];

                var rawData = Convert.FromBase64String(b64data);

                if (string.IsNullOrEmpty(data.CaminhoArquivo))
                    data.CaminhoArquivo = "application/octet-stream";

                return File(rawData, data.CaminhoArquivo, data.NomeArquivo);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Falha ao abrir o anexo {AttachmentId} do RDDV {Id} para visualização.", attachmentId, id);
                return StatusCode(StatusCodes.Status500InternalServerError, MensagemErroGenerica);
            }
        }

        [HttpPost("generate-preview-key")]
        public async Task<ActionResult> GeneratePreviewKey(
            [FromQuery] int entityId,
            [FromQuery] EnumRequestType requestType)
        {
            try
            {
                var currentUser = this.GetUserFromToken();

                if (currentUser == null)
                {
                    return Unauthorized(new GenericResponse<object>()
                    {
                        Success = false,
                        Message = "Usuário não identificado.",
                        Data = null,
                        Page = 1,
                        Pages = 1,
                        Records = 0
                    });
                }

                if (requestType == EnumRequestType.Exportacao)
                {
                    // A exportação traz títulos de todos os usuários: só FIN e ADM, pelo papel do JWT.
                    if (!this.CurrentUserIsInRole(Funcoes.Financeiro, Funcoes.Administrador))
                    {
                        return StatusCode(StatusCodes.Status403Forbidden, new GenericResponse<object>()
                        {
                            Success = false,
                            Message = "Exportação permitida apenas para os perfis Financeiro e Administrador.",
                            Data = null,
                            Page = 1,
                            Pages = 1,
                            Records = 0
                        });
                    }
                }
                else
                {
                    int entityUserId = 0;
                    bool rascunho = false;

                    if (requestType == EnumRequestType.Solicitacao)
                    {
                        var request = await dbContext
                                        .Solicitacaos
                                        .Where(s => s.IdSolicitacao == entityId)
                                        .FirstOrDefaultAsync();

                        if (request == null)
                        {
                            return Ok(new GenericResponse<object>()
                            {
                                Success = false,
                                Message = "O registro solicitado não foi localizado.",
                                Data = null,
                                Page = 1,
                                Pages = 1,
                                Records = 0
                            });
                        }

                        entityUserId = request.IdUsuario;
                        rascunho = request.Rascunho;
                    }
                    else if (requestType == EnumRequestType.Rddv)
                    {
                        var rddv = await dbContext
                                        .Rddv
                                        .Where(s => s.IdRelatorio == entityId)
                                        .FirstOrDefaultAsync();

                        if (rddv == null)
                        {
                            return Ok(new GenericResponse<object>()
                            {
                                Success = false,
                                Message = "O registro solicitado não foi localizado.",
                                Data = null,
                                Page = 1,
                                Pages = 1,
                                Records = 0
                            });
                        }

                        entityUserId = rddv.IdUsuario;
                        rascunho = rddv.Rascunho;
                    }

                    bool isOwner = entityUserId == currentUser.IdUsuario;

                    // O dono sempre vê o próprio título. Os papéis do fluxo de aprovação veem os títulos
                    // de outros usuários, mas nunca um rascunho, que ainda não entrou no fluxo.
                    if (!isOwner &&
                        (rascunho || !this.CurrentUserIsInRole(["ADM", "GES", "FIN", "CON"])))
                    {
                        return Ok(new GenericResponse<object>()
                        {
                            Success = false,
                            Message = "A solicitação requisitada não pertence a este usuário.",
                            Data = null,
                            Page = 1,
                            Pages = 1,
                            Records = 0
                        });
                    }
                }

                var previewKey = new PreviewKeyDto(
                    Guid.NewGuid(),
                    requestType,
                    entityId,
                    currentUser.IdUsuario);

                memoryCache.Set(
                    "preview-key-" + previewKey.PreviewKey.ToString(), 
                    previewKey, 
                    TimeSpan.FromMinutes(1));

                return Ok(new GenericResponse<PreviewKeyDto>()
                {
                    Success = true,
                    Message = "Chave gerada com sucesso.",
                    Data = previewKey,
                    Page = 1,
                    Pages = 1,
                    Records = 0
                });
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Falha ao gerar a chave de visualização para {RequestType} {EntityId}.", requestType, entityId);
                return StatusCode(StatusCodes.Status500InternalServerError, new GenericResponse<object>()
                {
                    Success = false,
                    Message = MensagemErroGenerica,
                    Data = null,
                    Page = 1,
                    Pages = 1,
                    Records = 0
                });
            }
        }

    }
}
