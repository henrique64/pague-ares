using Ares.PagueAres.API.Extensions;
using Ares.PagueAres.Application.Services;
using Ares.PagueAres.Domain;
using Ares.PagueAres.Domain.Dtos;
using Ares.PagueAres.Infrastructure;
using Ares.PagueAres.Infrastructure.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace Ares.PagueAres.API.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    [Authorize]
    public class RequestController : ControllerBase
    {

        private readonly PagueAresContext _dbContext;
        private readonly EmailService _email;

        public RequestController(PagueAresContext dbContext, EmailService email)
        {
            _dbContext = dbContext;
            _email = email;
        }

        [HttpGet]
        public async Task<ActionResult<GenericResponse<IEnumerable<SolicitacaoDto>>>> GetAllAsync([FromQuery] DateTime? startDate, [FromQuery] DateTime? endDate, [FromQuery] string? number, [FromQuery] int? type, [FromQuery] int? status, [FromQuery] int pageSize = int.MaxValue, [FromQuery] int page = 1)
        {
            try
            {
                if (startDate == null)
                {
                    startDate = new DateTime(1753, 1, 1);
                    endDate = DateTime.Now.Date.AddDays(7);
                }

                if (endDate == null)
                {
                    startDate = new DateTime(1753, 1, 1);
                    endDate = DateTime.Now.Date.AddDays(7);
                }

                var query = (from r in _dbContext.Solicitacaos
                             from u in _dbContext.Usuarios.Where(x => x.IdUsuario == r.IdUsuario)
                             from g in _dbContext.Usuarios.Where(k => k.IdUsuario == r.IdGestor).DefaultIfEmpty()
                             where r.DataSolicitacao.Date >= startDate &&
                                     r.DataSolicitacao.Date <= endDate
                             select new SolicitacaoDto()
                             {
                                 IdSolicitacao = r.IdSolicitacao,
                                 StatusGestor = r.StatusGestor,
                                 StatusContabilidade = r.StatusContabilidade,
                                 StatusFinanceiro = r.StatusFinanceiro,
                                 IdUsuario = r.IdUsuario,
                                 NomeUsuario = u.Nome,
                                 IdDepartamento = r.IdDepartamento,
                                 IdUsuarioAtribuido = r.IdUsuarioAtribuido,
                                 DataAtribuicao = r.DataAtribuicao,
                                 IdUsuarioAtribuidor = r.IdUsuarioAtribuidor,
                                 DataSolicitacao = r.DataSolicitacao,
                                 TipoSolicitacao = r.TipoSolicitacao,
                                 TipoPagamento = r.TipoPagamento,
                                 DataDocumento = r.DataDocumento,
                                 DataVencimento = r.DataVencimento,
                                 NumeroDocumento = r.NumeroDocumento,
                                 Descricao = r.Descricao,
                                 Valor = r.Valor,
                                 Parcelado = r.Parcelado,
                                 Parcelas = r.Parcelas,
                                 FormaPagamento = r.FormaPagamento,
                                 CentroCusto = r.CentroCusto,
                                 Projeto = r.Projeto,
                                 Pca = r.Pca,
                                 NumDocParceiro = r.NumDocParceiro,
                                 CodigoParceiro = r.CodigoParceiro,
                                 NomeParceiro = r.NomeParceiro,
                                 BancoParceiro = r.BancoParceiro,
                                 AgenciaParceiro = r.AgenciaParceiro,
                                 ContaParceiro = r.ContaParceiro,
                                 Observacao = r.Observacao,
                                 TipoAutorizacao = r.TipoAutorizacao,
                                 IdGestor = r.IdGestor,
                                 NomeGestor = g == null ? "" : g.Nome,
                                 AprovadoGestor = r.AprovadoGestor,
                                 ObservacaoGestor = r.ObservacaoGestor,
                                 AprovadoSetor = r.AprovadoSetor,
                                 ObservacaoSetor = r.ObservacaoSetor,
                                 DataAprovacaoGestor = r.DataAprovacaoGestor,
                                 DataAprovacaoFinanceiro = r.DataAprovacaoFinanceiro,
                                 DataAprovacaoContabil = r.DataAprovacaoContabil,
                                 Cancelado = r.Cancelado,
                                 Rascunho = r.Rascunho
                             });

                var records = await query.CountAsync();

                var requests = await query
                                    .Skip(pageSize * (page - 1))
                                    .Take(pageSize)
                                    .ToListAsync();

                return Ok(new GenericResponse<IEnumerable<SolicitacaoDto>>()
                {
                    Success = true,
                    Message = "Registros obtidos com sucesso.",
                    Data = requests,
                    Page = page,
                    Pages = records / pageSize,
                    Records = records
                });
            }
            catch (Exception ex)
            {
                return Ok(new GenericResponse<object>()
                {
                    Success = false,
                    Message = ex.Message,
                    Data = null,
                    Page = 1,
                    Pages = 1,
                    Records = 0
                });
            }
        }

        [HttpGet("{id}")]
        public async Task<ActionResult<GenericResponse<SolicitacaoDto>>> GetRequest([FromRoute] int id)
        {
            try
            {
                var request = await _dbContext
                                    .Solicitacaos
                                    .Where(s => s.IdSolicitacao == id)
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

                var currentUser = this.GetUserFromToken();
                
                if(request.IdUsuario != currentUser.IdUsuario &&
                    !this.CurrentUserIsInRole(["ADM", "GES", "FIN", "CON"]))
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

                var gestor = await _dbContext.Usuarios.FirstOrDefaultAsync(u => u.IdUsuario == request.IdGestor);
                var user = await _dbContext.Usuarios.FirstOrDefaultAsync(u => u.IdUsuario == request.IdUsuario);

                var attachments = await _dbContext
                                            .DocumentoSolicitacaos
                                            .Where(d => d.IdSolicitacao == id)
                                            .Select(d => new DocumentoSolicitacaoDto()
                                            {
                                                IdDocumentoSolicitacao = d.IdDocumentoSolicitacao,
                                                IdSolicitacao = d.IdSolicitacao,
                                                IdTipoDocumento = d.IdTipoDocumento,
                                                IdUsuario = d.IdUsuario,
                                                DataCriacao = d.DataCriacao,
                                                Indice = d.Indice,
                                                NomeArquivo = d.NomeArquivo,
                                                CaminhoArquivo = d.CaminhoArquivo
                                            })
                                            .ToListAsync();

                var requestData = new SolicitacaoDto();

                requestData.IdSolicitacao = request.IdSolicitacao;
                requestData.StatusGestor = request.StatusGestor;
                requestData.StatusContabilidade = request.StatusContabilidade;
                requestData.StatusFinanceiro = request.StatusFinanceiro;
                requestData.IdUsuario = request.IdUsuario;
                requestData.NomeUsuario = user?.Nome ?? "";
                requestData.IdDepartamento = request.IdDepartamento;
                requestData.IdUsuarioAtribuido = request.IdUsuarioAtribuido;
                requestData.DataAtribuicao = request.DataAtribuicao;
                requestData.IdUsuarioAtribuidor = request.IdUsuarioAtribuidor;
                requestData.DataSolicitacao = request.DataSolicitacao;
                requestData.TipoSolicitacao = request.TipoSolicitacao;
                requestData.TipoPagamento = request.TipoPagamento;
                requestData.DataDocumento = request.DataDocumento;
                requestData.DataVencimento = request.DataVencimento;
                requestData.NumeroDocumento = request.NumeroDocumento;
                requestData.Descricao = request.Descricao;
                requestData.Valor = request.Valor;
                requestData.Parcelado = request.Parcelado;
                requestData.Parcelas = request.Parcelas;
                requestData.FormaPagamento = request.FormaPagamento;
                requestData.CentroCusto = request.CentroCusto;
                requestData.Projeto = request.Projeto;
                requestData.Pca = request.Pca;
                requestData.NumDocParceiro = request.NumDocParceiro;
                requestData.CodigoParceiro = request.CodigoParceiro;
                requestData.NomeParceiro = request.NomeParceiro;
                requestData.BancoParceiro = request.BancoParceiro;
                requestData.AgenciaParceiro = request.AgenciaParceiro;
                requestData.ContaParceiro = request.ContaParceiro;
                requestData.Observacao = request.Observacao;
                requestData.TipoAutorizacao = request.TipoAutorizacao;
                requestData.IdGestor = request.IdGestor;
                requestData.NomeGestor = gestor?.Nome ?? "";
                requestData.AprovadoGestor = request.AprovadoGestor;
                requestData.ObservacaoGestor = request.ObservacaoGestor;
                requestData.AprovadoSetor = request.AprovadoSetor;
                requestData.ObservacaoSetor = request.ObservacaoSetor;
                requestData.DataAprovacaoGestor = request.DataAprovacaoGestor;
                requestData.DataAprovacaoFinanceiro = request.DataAprovacaoFinanceiro;
                requestData.DataAprovacaoContabil = request.DataAprovacaoContabil;
                requestData.Cancelado = request.Cancelado;
                requestData.Rascunho = request.Rascunho;

                requestData.Documentos = attachments;

                return Ok(new GenericResponse<SolicitacaoDto>()
                {
                    Success = true,
                    Message = "Registros obtidos com sucesso.",
                    Data = requestData,
                    Page = 1,
                    Pages = 1,
                    Records = 1
                });
            }
            catch (Exception ex)
            {
                return Ok(new GenericResponse<object>()
                {
                    Success = false,
                    Message = ex.Message,
                    Data = null,
                    Page = 1,
                    Pages = 1,
                    Records = 0
                });
            }
        }

        [HttpPost]
        public async Task<ActionResult<GenericResponse<SolicitacaoDto>>> GravarSolicitacao([FromBody] SolicitacaoDto solicitacao)
        {
            IDbContextTransaction? tran = null;

            try
            {
                var currentUser = this.GetUserFromToken();
                Solicitacao? requestData = null;

                bool sendNewRequestEmail = false;
                bool sendApprovedEmail = false;
                bool sendDeniedEmail = false;
                bool sendPaidEmail = false;
                bool sendPaymentDeniedEmail = false;
                bool sendAccountingDeniedEmail = false;
                bool sendAccountingApprovedEmail = false;

                if (solicitacao.IdSolicitacao == 0)
                {
                    requestData = new Solicitacao();

                    requestData.StatusGestor = 1;
                    requestData.StatusContabilidade = 1;
                    requestData.StatusFinanceiro = 1;

                    _dbContext.Add(requestData);
                }
                else
                {
                    requestData = await _dbContext.Solicitacaos.Where(r => r.IdSolicitacao == solicitacao.IdSolicitacao).FirstOrDefaultAsync();

                    if (requestData == null)
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

                    if (requestData.Cancelado)
                    {
                        return Ok(new GenericResponse<object>()
                        {
                            Success = false,
                            Message = "Registro cancelado não pode ser alterado.",
                            Data = null,
                            Page = 1,
                            Pages = 1,
                            Records = 0
                        });
                    }

                    if (requestData.IdUsuario != currentUser?.IdUsuario &&
                        !this.CurrentUserIsInRole("ADM", "GES", "FIN", "CON"))
                    {
                        return Ok(new GenericResponse<object>()
                        {
                            Success = false,
                            Message = "Você não tem permissão para editar esta solicitação.",
                            Data = null,
                            Page = 1,
                            Pages = 1,
                            Records = 0
                        });
                    }
                }

                if (!solicitacao.Rascunho && requestData.Rascunho)
                {
                    sendNewRequestEmail = true;

                    if (solicitacao.TipoAutorizacao == 1)
                    {
                        requestData.AprovadoGestor = solicitacao.AprovadoGestor = true;
                        requestData.StatusGestor = solicitacao.StatusGestor = 2;
                        requestData.ObservacaoGestor = solicitacao.ObservacaoGestor = "Autorização por anexo.";
                        requestData.DataAprovacaoGestor = DateTime.Now;

                        sendApprovedEmail = true;
                    }
                }

                if (solicitacao.IdSolicitacao > 0)
                {
                    if (solicitacao.AprovadoGestor != requestData.AprovadoGestor)
                    {
                        if (!this.CurrentUserIsInRole("ADM", "GES"))
                            return Ok(new GenericResponse<object>() { Success = false, Message = "Apenas o gestor ou administrador pode aprovar/reprovar.", Data = null, Page = 1, Pages = 1, Records = 0 });

                        requestData.StatusGestor = (solicitacao.AprovadoGestor ?? false) ? 2 : 3;

                        if ((solicitacao.AprovadoGestor ?? false))
                        {
                            requestData.DataAprovacaoGestor = DateTime.Now;
                        }

                        sendApprovedEmail = requestData.StatusGestor == 2;
                        sendDeniedEmail = requestData.StatusGestor == 3;
                    }

                    if (solicitacao.AprovadoSetor != requestData.AprovadoSetor)
                    {
                        if (!this.CurrentUserIsInRole("ADM", "FIN"))
                            return Ok(new GenericResponse<object>() { Success = false, Message = "Apenas o financeiro ou administrador pode autorizar pagamentos.", Data = null, Page = 1, Pages = 1, Records = 0 });

                        requestData.StatusFinanceiro = (solicitacao.AprovadoSetor ?? false) ? 2 : 3;

                        if ((solicitacao.AprovadoSetor ?? false))
                        {
                            requestData.DataAprovacaoFinanceiro = DateTime.Now;
                        }
                        else
                        {
                            requestData.StatusContabilidade = 3;
                            solicitacao.StatusContabilidade = 3;
                        }

                        sendPaidEmail = requestData.StatusFinanceiro == 2;
                        sendPaymentDeniedEmail = requestData.StatusFinanceiro == 3;
                    }

                    if (solicitacao.StatusContabilidade != requestData.StatusContabilidade)
                    {
                        if (!this.CurrentUserIsInRole("ADM", "FIN", "CON"))
                            return Ok(new GenericResponse<object>() { Success = false, Message = "Apenas financeiro, contabilidade ou administrador pode lançar/recusar.", Data = null, Page = 1, Pages = 1, Records = 0 });

                        if ((solicitacao.StatusContabilidade ?? 0) == 2)
                        {
                            requestData.DataAprovacaoContabil = DateTime.Now;
                        }

                        sendAccountingApprovedEmail = solicitacao.StatusContabilidade == 2;
                        sendAccountingDeniedEmail = solicitacao.StatusContabilidade == 3;
                    }
                }

                tran = await _dbContext.Database.BeginTransactionAsync();

                if (solicitacao.GravarDadosParceiro ?? false)
                {
                    await AtualizarDadosPaceiroAsync(solicitacao);
                }

                requestData.IdUsuario = solicitacao.IdUsuario;
                requestData.IdDepartamento = solicitacao.IdDepartamento;
                requestData.DataSolicitacao = solicitacao.DataSolicitacao;
                requestData.TipoSolicitacao = solicitacao.TipoSolicitacao;
                requestData.TipoPagamento = solicitacao.TipoPagamento;
                requestData.DataDocumento = solicitacao.DataDocumento;
                requestData.DataVencimento = solicitacao.DataVencimento;
                requestData.NumeroDocumento = solicitacao.NumeroDocumento;
                requestData.Descricao = solicitacao.Descricao;
                requestData.Valor = solicitacao.Valor;
                requestData.Parcelado = solicitacao.Parcelado;
                requestData.Parcelas = solicitacao.Parcelas;
                requestData.FormaPagamento = solicitacao.FormaPagamento;
                requestData.CentroCusto = solicitacao.CentroCusto;
                requestData.Projeto = solicitacao.Projeto;
                requestData.Pca = solicitacao.Pca;
                requestData.NumDocParceiro = solicitacao.NumDocParceiro ?? "";
                requestData.CodigoParceiro = solicitacao.CodigoParceiro ?? "";
                requestData.NomeParceiro = solicitacao.NomeParceiro ?? "";
                requestData.BancoParceiro = solicitacao.BancoParceiro ?? "";
                requestData.AgenciaParceiro = solicitacao.AgenciaParceiro ?? "";
                requestData.ContaParceiro = solicitacao.ContaParceiro ?? "";
                requestData.Observacao = solicitacao.Observacao ?? "";
                requestData.TipoAutorizacao = solicitacao.TipoAutorizacao;
                requestData.IdGestor = solicitacao.IdGestor;
                requestData.AprovadoGestor = solicitacao.AprovadoGestor;
                requestData.ObservacaoGestor = solicitacao.ObservacaoGestor;
                requestData.AprovadoSetor = solicitacao.AprovadoSetor;
                requestData.ObservacaoSetor = solicitacao.ObservacaoSetor;
                requestData.StatusContabilidade = solicitacao.StatusContabilidade;
                requestData.Cancelado = solicitacao.Cancelado;
                requestData.Rascunho = solicitacao.Rascunho;

                await _dbContext.SaveChangesAsync();

                solicitacao.IdSolicitacao = requestData.IdSolicitacao;

                // Remover documentos marcados para exclusão

                foreach (var doc in solicitacao.Documentos.Where(d => d.Excluido && d.IdDocumentoSolicitacao > 0))
                {
                    var docData = await _dbContext.DocumentoSolicitacaos.Where(d => d.IdDocumentoSolicitacao == doc.IdDocumentoSolicitacao).FirstOrDefaultAsync();

                    if (docData == null)
                        continue;

                    _dbContext.Remove(docData);

                    await _dbContext.SaveChangesAsync();
                }

                // Incluir novos documentos

                foreach (var doc in solicitacao.Documentos.Where(d => d.IdDocumentoSolicitacao == 0))
                {
                    var docData = new DocumentoSolicitacao();

                    docData.IdSolicitacao = requestData.IdSolicitacao;
                    docData.IdTipoDocumento = doc.IdTipoDocumento;
                    docData.IdUsuario = doc.IdUsuario;
                    docData.DataCriacao = doc.DataCriacao;
                    docData.Indice = doc.Indice;
                    docData.NomeArquivo = doc.NomeArquivo;
                    docData.CaminhoArquivo = doc.CaminhoArquivo;
                    docData.Arquivo = doc.Arquivo ?? "";

                    _dbContext.Add(docData);

                    await _dbContext.SaveChangesAsync();

                    doc.IdDocumentoSolicitacao = docData.IdDocumentoSolicitacao;
                    doc.IdSolicitacao = docData.IdSolicitacao;
                }

                await tran.CommitAsync();

                sendNewRequestEmail = sendNewRequestEmail == true &&
                                      sendApprovedEmail == false &&
                                      sendDeniedEmail == false &&
                                      sendPaidEmail == false &&
                                      sendPaymentDeniedEmail == false &&
                                      sendAccountingApprovedEmail == false &&
                                      sendAccountingDeniedEmail == false;

                if (sendNewRequestEmail)
                {
                    await _email.SendNewRequestEmail(false, requestData.IdSolicitacao);
                }

                if (sendApprovedEmail)
                {
                    await _email.SendApprovalEmail(false, requestData.IdSolicitacao);
                }

                if (sendDeniedEmail)
                {
                    await _email.SendDenialEmail(false, requestData.IdSolicitacao);
                }

                if (sendPaidEmail)
                {
                    await _email.SendPaidEmail(false, requestData.IdSolicitacao);
                }

                if (sendPaymentDeniedEmail)
                {
                    await _email.SendPaymentDeniedEmail(false, requestData.IdSolicitacao);
                }

                if (sendAccountingApprovedEmail)
                {
                    await _email.SendAccountingPaidEmail(false, requestData.IdSolicitacao);
                }

                if (sendAccountingDeniedEmail)
                {
                    await _email.SendAccountingDenialEmail(false, requestData.IdSolicitacao);
                }

                return Ok(new GenericResponse<SolicitacaoDto>()
                {
                    Success = true,
                    Message = "Registro armazenado com sucesso.",
                    Data = solicitacao,
                    Page = 1,
                    Pages = 1,
                    Records = 1
                });
            }
            catch (Exception ex)
            {
                if (tran != null)
                    await tran.RollbackAsync();

                return Ok(new GenericResponse<object>()
                {
                    Success = false,
                    Message = ex.Message,
                    Data = null,
                    Page = 1,
                    Pages = 1,
                    Records = 0
                });
            }
        }

        [HttpGet("dashboard")]
        public async Task<ActionResult<GenericResponse<DashboardDto>>> GetDashboard()
        {
            try
            {
                var pendentes = await _dbContext.Solicitacaos.Where(s => (s.StatusContabilidade ?? 1) == 1 && /* Pagamento Aprovado */
                                                                         (s.StatusFinanceiro ?? 1) == 2 && /* Aguardando Lançamento */
                                                                         (s.AprovadoGestor ?? false) && !s.Rascunho && !s.Cancelado).CountAsync();

                var aprovados = await _dbContext.Solicitacaos.Where(s => s.StatusFinanceiro == 2 && !s.Rascunho && !s.Cancelado).CountAsync();
                var reprovados = await _dbContext.Solicitacaos.Where(s => (s.StatusFinanceiro == 3 || s.StatusContabilidade == 3) && !s.Rascunho && !s.Cancelado).CountAsync();

                var pendentesRddv = await _dbContext.Rddv.Where(s => (s.StatusContabilidade ?? 1) == 1 && /* Pagamento Aprovado */
                                                                     (s.StatusFinanceiro ?? 1) == 2 && /* Aguardando Lançamento */
                                                                     (s.AprovadoGestor ?? false) && !s.Rascunho && !s.Cancelado).CountAsync();

                var aprovadosRddv = await _dbContext.Rddv.Where(s => s.StatusFinanceiro == 2 && !s.Rascunho && !s.Cancelado).CountAsync();
                var reprovadosRddv = await _dbContext.Rddv.Where(s => (s.StatusFinanceiro == 3 || s.StatusContabilidade == 3) && !s.Rascunho && !s.Cancelado).CountAsync();

                return Ok(new GenericResponse<DashboardDto>()
                {
                    Success = true,
                    Message = "",
                    Data = new DashboardDto()
                    {
                        Pendentes = pendentes + pendentesRddv,
                        Aprovados = aprovados + aprovadosRddv,
                        Reprovados = reprovados + reprovadosRddv
                    },
                    Page = 1,
                    Pages = 1,
                    Records = 0
                });
            }
            catch (Exception ex)
            {
                return Ok(new GenericResponse<object>()
                {
                    Success = false,
                    Message = ex.Message,
                    Data = null,
                    Page = 1,
                    Pages = 1,
                    Records = 0
                });
            }
        }

        [HttpGet("{id}/user")]
        public async Task<ActionResult<GenericResponse<object>>> SetUser([FromRoute] int id, [FromQuery] int userId)
        {
            try
            {
                var request = await _dbContext.Solicitacaos.FirstOrDefaultAsync(s => s.IdSolicitacao == id);

                if (request == null)
                    return BadRequest(new GenericResponse<object>()
                    {
                        Success = false,
                        Message = "Solicitação não encontrada.",
                        Data = null,
                        Page = 1,
                        Pages = 1,
                        Records = 0
                    });

                request.IdUsuarioAtribuido = userId;
                request.DataAtribuicao = DateTime.Now;

                await _dbContext.SaveChangesAsync();

                await _email.SendRequestAssignedEmail(false, request.IdSolicitacao);

                return Ok(new GenericResponse<object>()
                {
                    Success = true,
                    Message = "Usuário atribuído com sucesso",
                    Data = null,
                    Page = 1,
                    Pages = 1,
                    Records = 0
                });
            }
            catch (Exception ex)
            {
                return BadRequest(new GenericResponse<object>()
                {
                    Success = false,
                    Message = ex.Message,
                    Data = null,
                    Page = 1,
                    Pages = 1,
                    Records = 0
                });
            }
        }

        private async Task AtualizarDadosPaceiroAsync(SolicitacaoDto solicitacao)
        {
            if (string.IsNullOrEmpty(solicitacao.NumDocParceiro) ||
                string.IsNullOrEmpty(solicitacao.NomeParceiro))
                return;

            var dbForn = await _dbContext
                            .Fornecedors
                            .FirstOrDefaultAsync(f => f.NumDocumento == solicitacao.NumDocParceiro);

            if (dbForn == null)
            {
                dbForn = new Infrastructure.Models.Fornecedor();
                dbForn.NumDocumento = solicitacao.NumDocParceiro;

                _dbContext.Fornecedors.Add(dbForn);
            }

            dbForn.Nome = solicitacao.NomeParceiro ?? "";
            dbForn.Codigo = solicitacao.CodigoParceiro ?? "";
        }

    }
}
