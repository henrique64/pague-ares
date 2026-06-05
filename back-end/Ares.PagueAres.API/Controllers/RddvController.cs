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
    public class RddvController : ControllerBase
    {

        private readonly PagueAresContext _dbContext;
        private readonly EmailService _email;

        public RddvController(PagueAresContext dbContext, EmailService email)
        {
            _dbContext = dbContext;
            _email = email;
        }

        [HttpGet]
        public async Task<ActionResult<GenericResponse<IEnumerable<RddvDto>>>> GetAllAsync([FromQuery] DateTime? startDate, [FromQuery] DateTime? endDate, [FromQuery] string? number, [FromQuery] int? type, [FromQuery] int? status, [FromQuery] int pageSize = int.MaxValue, [FromQuery] int page = 1)
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

                var query = (from r in _dbContext.Rddv
                             from u in _dbContext.Usuarios.Where(x => x.IdUsuario == r.IdUsuario)
                             from g in _dbContext.Usuarios.Where(k => k.IdUsuario == r.IdGestor).DefaultIfEmpty()
                             from d in _dbContext.vDespesasRddv.Where(k => k.IdRelatorio == r.IdRelatorio).DefaultIfEmpty()
                             where r.DataCadastro.Date >= startDate &&
                                     r.DataCadastro.Date <= endDate
                             select new RddvDto()
                             {
                                 IdRelatorio = r.IdRelatorio,
                                 IdUsuario = r.IdUsuario,
                                 DataCadastro = r.DataCadastro,
                                 NomeFuncionario = r.NomeFuncionario,
                                 IdUsuarioAtribuido = r.IdUsuarioAtribuido,
                                 DataAtribuicao = r.DataAtribuicao,
                                 IdUsuarioAtribuidor = r.IdUsuarioAtribuidor,
                                 CPF = r.CPF,
                                 IdDepartamento = r.IdDepartamento,
                                 Finalidade = r.Finalidade,
                                 TipoRelatorio = r.TipoRelatorio,
                                 Moeda = r.Moeda,
                                 TipoViagem = r.TipoViagem,
                                 DataInicio = r.DataInicio,
                                 DataFim = r.DataFim,
                                 LocalViagem = r.LocalViagem,
                                 CentroCusto = r.CentroCusto,
                                 Projeto = r.Projeto,
                                 PCA = r.PCA,
                                 DiariaViagem = r.DiariaViagem,
                                 Adiantamento = r.Adiantamento,
                                 ValorAdiantamento = r.ValorAdiantamento,
                                 SaldoCartaoVtm = r.SaldoCartaoVtm,
                                 Observacao = r.Observacao,
                                 TipoAutorizacao = r.TipoAutorizacao,
                                 IdGestor = r.IdGestor,
                                 NomeGestor = g == null ? "" : g.Nome,
                                 AprovadoGestor = r.AprovadoGestor,
                                 ObservacaoGestor = r.ObservacaoGestor,
                                 AprovadoSetor = r.AprovadoSetor,
                                 ObservacaoSetor = r.ObservacaoSetor,
                                 StatusGestor = r.StatusGestor ?? 1,
                                 StatusContabilidade = r.StatusContabilidade ?? 1,
                                 StatusFinanceiro = r.StatusFinanceiro ?? 1,
                                 ValorTotal = d.Valor,
                                 Banco = r.Banco,
                                 Agencia = r.Agencia,
                                 Conta = r.Conta,
                                 ValorDiaria = r.ValorDiaria,
                                 ValorKm = r.ValorKm,
                                 DataAprovacaoGestor = r.DataAprovacaoGestor,
                                 DataAprovacaoFinanceiro = r.DataAprovacaoFinanceiro,
                                 DataAprovacaoContabil = r.DataAprovacaoContabil,
                                 Cancelado = r.Cancelado,
                                 Rascunho = r.Rascunho,
                                 DataVencimento = r.DataVencimento
                             });

                var records = await query.CountAsync();

                var requests = await query
                                    .Skip(pageSize * (page - 1))
                                    .Take(pageSize)
                                    .ToListAsync();

                return Ok(new GenericResponse<IEnumerable<RddvDto>>()
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
        public async Task<ActionResult<GenericResponse<RddvDto>>> GetRequest([FromRoute] int id)
        {
            try
            {
                var request = await _dbContext
                                    .Rddv
                                    .Where(s => s.IdRelatorio == id)
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

                if (request.IdUsuario != currentUser.IdUsuario &&
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

                var attachments = await _dbContext
                                            .DocumentoRddv
                                            .Where(d => d.IdRelatorio == id)
                                            .Select(d => new DocumentoRddvDto()
                                            {
                                                IdDocumentoRddv = d.IdDocumentoRddv,
                                                IdRelatorio = d.IdRelatorio,
                                                IdTipoDocumento = d.IdTipoDocumento,
                                                IdUsuario = d.IdUsuario,
                                                DataCriacao = d.DataCriacao,
                                                Indice = d.Indice,
                                                NomeArquivo = d.NomeArquivo,
                                                CaminhoArquivo = d.CaminhoArquivo
                                            })
                                            .ToListAsync();

                var expenses = await _dbContext
                                            .DespesaRddv
                                            .Where(d => d.IdRelatorio == id)
                                            .Select(d => new DespesaRddvDto()
                                            {
                                                IdDespesaRddv = d.IdDespesaRddv,
                                                IdRelatorio = d.IdRelatorio,
                                                DataDespesa = d.DataDespesa,
                                                TipoDespesa = d.TipoDespesa,
                                                Moeda = d.Moeda,
                                                Valor = d.Valor,
                                                Quantidade = d.Quantidade
                                            })
                                            .ToListAsync();

                var tot = await _dbContext.vDespesasRddv.FirstOrDefaultAsync(x => x.IdRelatorio == id);

                var requestData = new RddvDto();

                requestData.IdRelatorio = request.IdRelatorio;
                requestData.IdUsuario = request.IdUsuario;
                requestData.DataCadastro = request.DataCadastro;
                requestData.NomeFuncionario = request.NomeFuncionario;
                requestData.IdUsuarioAtribuido = request.IdUsuarioAtribuido;
                requestData.DataAtribuicao = request.DataAtribuicao;
                requestData.IdUsuarioAtribuidor = request.IdUsuarioAtribuidor;
                requestData.CPF = request.CPF;
                requestData.IdDepartamento = request.IdDepartamento;
                requestData.Finalidade = request.Finalidade;
                requestData.TipoRelatorio = request.TipoRelatorio;
                requestData.Moeda = request.Moeda;
                requestData.TipoViagem = request.TipoViagem;
                requestData.DataInicio = request.DataInicio;
                requestData.DataFim = request.DataFim;
                requestData.LocalViagem = request.LocalViagem;
                requestData.CentroCusto = request.CentroCusto;
                requestData.Projeto = request.Projeto;
                requestData.PCA = request.PCA;
                requestData.DiariaViagem = request.DiariaViagem;
                requestData.Diarias = request.Diarias;
                requestData.Adiantamento = request.Adiantamento;
                requestData.ValorAdiantamento = request.ValorAdiantamento;
                requestData.SaldoCartaoVtm = request.SaldoCartaoVtm;
                requestData.Observacao = request.Observacao;
                requestData.TipoAutorizacao = request.TipoAutorizacao;
                requestData.IdGestor = request.IdGestor;
                requestData.NomeGestor = gestor?.Nome ?? "";
                requestData.AprovadoGestor = request.AprovadoGestor;
                requestData.ObservacaoGestor = request.ObservacaoGestor;
                requestData.AprovadoSetor = request.AprovadoSetor;
                requestData.ObservacaoSetor = request.ObservacaoSetor;
                requestData.StatusGestor = request.StatusGestor;
                requestData.StatusContabilidade = request.StatusContabilidade;
                requestData.StatusFinanceiro = request.StatusFinanceiro;
                requestData.ValorTotal = tot?.Valor ?? 0m;
                requestData.Banco = request.Banco;
                requestData.Agencia = request.Agencia;
                requestData.Conta = request.Conta;
                requestData.ValorDiaria = request.ValorDiaria;
                requestData.ValorKm = request.ValorKm;
                requestData.DataAprovacaoGestor = request.DataAprovacaoGestor;
                requestData.DataAprovacaoFinanceiro = request.DataAprovacaoFinanceiro;
                requestData.DataAprovacaoContabil = request.DataAprovacaoContabil;
                requestData.Cancelado = request.Cancelado;
                requestData.Rascunho = request.Rascunho;
                requestData.DataVencimento = request.DataVencimento;

                requestData.Documentos = attachments;
                requestData.Despesas = expenses;

                return Ok(new GenericResponse<RddvDto>()
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
        public async Task<ActionResult<GenericResponse<RddvDto>>> GravarSolicitacao([FromBody] RddvDto rddv)
        {
            IDbContextTransaction? tran = null;

            try
            {
                Rddv? requestData = null;

                bool sendNewRequestEmail = false;
                bool sendApprovedEmail = false;
                bool sendDeniedEmail = false;
                bool sendPaidEmail = false;
                bool sendPaymentDeniedEmail = false;
                bool sendAccountingDeniedEmail = false;
                bool sendAccountingApprovedEmail = false;

                if (rddv.IdRelatorio == 0)
                {
                    requestData = new Rddv();

                    requestData.DataCadastro = DateTime.Now;
                    requestData.ValorDiaria = rddv.ValorDiaria;

                    _dbContext.Add(requestData);
                }
                else
                {
                    requestData = await _dbContext.Rddv.Where(r => r.IdRelatorio == rddv.IdRelatorio).FirstOrDefaultAsync();

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
                }

                if (!rddv.Rascunho && requestData.Rascunho)
                {
                    sendNewRequestEmail = true;

                    if (rddv.TipoAutorizacao == 1)
                    {
                        requestData.AprovadoGestor = rddv.AprovadoGestor = true;
                        requestData.DataAprovacaoGestor = DateTime.Now;
                        requestData.StatusGestor = rddv.StatusGestor = 2;
                        requestData.ObservacaoGestor = rddv.ObservacaoGestor = "Autorização por anexo.";

                        sendApprovedEmail = true;
                    }
                    else
                    {
                        requestData.StatusGestor = rddv.StatusGestor = 1;
                    }
                }

                if (rddv.IdRelatorio > 0)
                {
                    if (rddv.AprovadoGestor != requestData.AprovadoGestor)
                    {
                        requestData.StatusGestor = (rddv.AprovadoGestor ?? false) ? 2 : 3;

                        if ((rddv.AprovadoGestor ?? false))
                        {
                            requestData.DataAprovacaoGestor = DateTime.Now;
                        }

                        sendApprovedEmail = requestData.StatusGestor == 2;
                        sendDeniedEmail = requestData.StatusGestor == 3;
                    }

                    if (rddv.AprovadoSetor != requestData.AprovadoSetor)
                    {
                        requestData.StatusFinanceiro = (rddv.AprovadoSetor ?? false) ? 2 : 3;

                        if ((rddv.AprovadoSetor ?? false))
                        {
                            requestData.DataAprovacaoFinanceiro = DateTime.Now;
                        }
                        else
                        {
                            requestData.StatusContabilidade = 3;
                            rddv.StatusContabilidade = 3;
                        }

                        sendPaidEmail = requestData.StatusFinanceiro == 2;
                        sendPaymentDeniedEmail = requestData.StatusFinanceiro == 3;
                    }

                    if (rddv.StatusContabilidade != requestData.StatusContabilidade)
                    {
                        if ((rddv.StatusContabilidade ?? 0) == 2)
                        {
                            requestData.DataAprovacaoContabil = DateTime.Now;
                        }

                        sendAccountingApprovedEmail = rddv.StatusContabilidade == 2;
                        sendAccountingDeniedEmail = rddv.StatusContabilidade == 3;
                    }
                }

                tran = await _dbContext.Database.BeginTransactionAsync();

                requestData.IdUsuario = rddv.IdUsuario;
                requestData.DataCadastro = rddv.DataCadastro;
                requestData.NomeFuncionario = rddv.NomeFuncionario;
                requestData.CPF = rddv.CPF;
                requestData.IdDepartamento = rddv.IdDepartamento;
                requestData.Finalidade = rddv.Finalidade;
                requestData.TipoRelatorio = rddv.TipoRelatorio;
                requestData.Moeda = rddv.Moeda ?? "";
                requestData.TipoViagem = rddv.TipoViagem;
                requestData.DataInicio = rddv.DataInicio;
                requestData.DataFim = rddv.DataFim;
                requestData.LocalViagem = rddv.LocalViagem;
                requestData.CentroCusto = rddv.CentroCusto;
                requestData.Projeto = rddv.Projeto;
                requestData.PCA = rddv.PCA;
                requestData.DiariaViagem = rddv.DiariaViagem;
                requestData.Diarias = rddv.Diarias;
                requestData.Adiantamento = rddv.Adiantamento;
                requestData.ValorAdiantamento = rddv.ValorAdiantamento;
                requestData.SaldoCartaoVtm = rddv.SaldoCartaoVtm;
                requestData.Observacao = rddv.Observacao ?? "";
                requestData.TipoAutorizacao = rddv.TipoAutorizacao;
                requestData.IdGestor = rddv.IdGestor;
                requestData.AprovadoGestor = rddv.AprovadoGestor;
                requestData.ObservacaoGestor = rddv.ObservacaoGestor ?? "";
                requestData.AprovadoSetor = rddv.AprovadoSetor;
                requestData.ObservacaoSetor = rddv.ObservacaoSetor ?? "";
                requestData.StatusContabilidade = rddv.StatusContabilidade;
                requestData.Banco = rddv.Banco;
                requestData.Agencia = rddv.Agencia;
                requestData.Conta = rddv.Conta;
                requestData.ValorKm = rddv.ValorKm;
                requestData.Cancelado = rddv.Cancelado;
                requestData.Rascunho = rddv.Rascunho;
                requestData.DataVencimento = rddv.DataVencimento;

                await _dbContext.SaveChangesAsync();

                rddv.IdRelatorio = requestData.IdRelatorio;

                // Remover documentos marcados para exclusão

                foreach (var doc in rddv.Documentos.Where(d => d.Excluido && d.IdDocumentoRddv > 0))
                {
                    var docData = await _dbContext.DocumentoRddv.Where(d => d.IdDocumentoRddv == doc.IdDocumentoRddv).FirstOrDefaultAsync();

                    if (docData == null)
                        continue;

                    _dbContext.Remove(docData);

                    await _dbContext.SaveChangesAsync();
                }

                // Incluir novos documentos

                foreach (var doc in rddv.Documentos.Where(d => d.IdDocumentoRddv == 0))
                {
                    var docData = new DocumentoRddv();

                    docData.IdRelatorio = requestData.IdRelatorio;
                    docData.IdTipoDocumento = doc.IdTipoDocumento;
                    docData.IdUsuario = doc.IdUsuario;
                    docData.DataCriacao = doc.DataCriacao;
                    docData.Indice = doc.Indice;
                    docData.NomeArquivo = doc.NomeArquivo;
                    docData.CaminhoArquivo = doc.CaminhoArquivo;
                    docData.Arquivo = doc.Arquivo ?? "";

                    _dbContext.Add(docData);

                    await _dbContext.SaveChangesAsync();

                    doc.IdDocumentoRddv = docData.IdDocumentoRddv;
                    doc.IdRelatorio = docData.IdRelatorio;
                }

                // Remover despesas marcadas para exclusão

                foreach (var doc in rddv.Despesas.Where(d => d.Excluido && d.IdDespesaRddv > 0))
                {
                    var expData = await _dbContext.DespesaRddv.Where(d => d.IdDespesaRddv == doc.IdDespesaRddv).FirstOrDefaultAsync();

                    if (expData == null)
                        continue;

                    _dbContext.Remove(expData);

                    await _dbContext.SaveChangesAsync();
                }

                // Incluir novas despesas

                foreach (var doc in rddv.Despesas.Where(d => d.IdDespesaRddv == 0))
                {
                    var expData = new DespesaRddv();

                    expData.IdRelatorio = requestData.IdRelatorio;
                    expData.DataDespesa = doc.DataDespesa;
                    expData.TipoDespesa = doc.TipoDespesa;
                    expData.Moeda = doc.Moeda;
                    expData.Valor = doc.Valor;
                    expData.Quantidade = doc.Quantidade;

                    _dbContext.Add(expData);

                    await _dbContext.SaveChangesAsync();

                    doc.IdDespesaRddv = expData.IdDespesaRddv;
                    doc.IdRelatorio = expData.IdRelatorio;
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
                    await _email.SendNewRequestEmail(true, requestData.IdRelatorio);
                }

                if (sendApprovedEmail)
                {
                    await _email.SendApprovalEmail(true, requestData.IdRelatorio);
                }

                if (sendDeniedEmail)
                {
                    await _email.SendDenialEmail(true, requestData.IdRelatorio);
                }

                if (sendPaidEmail)
                {
                    await _email.SendPaidEmail(true, requestData.IdRelatorio);
                }

                if (sendPaymentDeniedEmail)
                {
                    await _email.SendPaymentDeniedEmail(true, requestData.IdRelatorio);
                }

                if (sendAccountingApprovedEmail)
                {
                    await _email.SendAccountingPaidEmail(true, requestData.IdRelatorio);
                }

                if (sendAccountingDeniedEmail)
                {
                    await _email.SendAccountingDenialEmail(true, requestData.IdRelatorio);
                }

                return Ok(new GenericResponse<RddvDto>()
                {
                    Success = true,
                    Message = "Registro armazenado com sucesso.",
                    Data = rddv,
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
                var pendentes = await _dbContext.Rddv.Where(s => (s.StatusContabilidade ?? 1) == 1 &&
                                                                  (s.StatusFinanceiro ?? 1) == 2 &&
                                                                  (s.AprovadoGestor ?? false) && !s.Rascunho && !s.Cancelado).CountAsync();
                var aprovados = await _dbContext.Rddv.Where(s => s.StatusFinanceiro == 2 && !s.Rascunho && !s.Cancelado).CountAsync();
                var reprovados = await _dbContext.Rddv.Where(s => (s.StatusFinanceiro == 3 || s.StatusContabilidade == 3) && !s.Rascunho && !s.Cancelado).CountAsync();

                return Ok(new GenericResponse<DashboardDto>()
                {
                    Success = true,
                    Message = "",
                    Data = new DashboardDto()
                    {
                        Pendentes = pendentes,
                        Aprovados = aprovados,
                        Reprovados = reprovados
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
                var report = await _dbContext.Rddv.FirstOrDefaultAsync(s => s.IdRelatorio == id);

                if (report == null)
                    return BadRequest(new GenericResponse<object>()
                    {
                        Success = false,
                        Message = "Relatório não encontrado.",
                        Data = null,
                        Page = 1,
                        Pages = 1,
                        Records = 0
                    });

                report.IdUsuarioAtribuido = userId;
                report.DataAtribuicao = DateTime.Now;

                await _dbContext.SaveChangesAsync();

                await _email.SendRequestAssignedEmail(true, report.IdRelatorio);

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

    }
}
