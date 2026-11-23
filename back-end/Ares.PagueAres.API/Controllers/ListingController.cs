using Ares.PagueAres.API.Extensions;
using Ares.PagueAres.Domain;
using Ares.PagueAres.Domain.Dtos;
using Ares.PagueAres.Domain.Dtos.Listing;
using Ares.PagueAres.Domain.Enums;
using Ares.PagueAres.Infrastructure;
using Ares.PagueAres.Infrastructure.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Ares.PagueAres.API.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    [Authorize]
    public class ListingController(PagueAresContext dbContext) : ControllerBase
    {

        [HttpGet]
        public async Task<ActionResult<GenericResponse<IEnumerable<ListagemSolicitacoesDto>>>> GetAllAsync([FromQuery] ListingFilterDto filter)
        {
            try
            {
                var query = BuildQuery(filter);

                var records = await query.CountAsync();

                var requests = await query
                                    .Skip(filter.PageSize * (filter.Page - 1))
                                    .Take(filter.PageSize)
                                    .Select(r => new ListagemSolicitacoesDto()
                                    {
                                        Codigo = r.Codigo,
                                        IdTipo = r.IdTipo,
                                        Tipo = r.Tipo,
                                        DataSolicitacao = r.DataSolicitacao,
                                        DataVencimento = r.DataVencimento,
                                        NumeroDocumento = r.NumeroDocumento,
                                        NomeSolicitante = r.NomeSolicitante,
                                        IdSolicitante = r.IdSolicitante,
                                        UsuarioAtribuido = r.UsuarioAtribuido,
                                        IdUsuarioAtribuido = r.IdUsuarioAtribuido,
                                        NomeParceiro = r.NomeParceiro,
                                        Valor = r.Valor,
                                        Descricao = r.Descricao,
                                        ObservacaoGestor = r.ObservacaoGestor,
                                        IdStatusGestor = r.IdStatusGestor,
                                        IdStatusFinanceiro = r.IdStatusFinanceiro,
                                        IdStatusContabilidade = r.IdStatusContabilidade,
                                        StatusGestor = r.StatusGestor,
                                        StatusFinanceiro = r.StatusFinanceiro,
                                        StatusContabilidade = r.StatusContabilidade,
                                        IdGestor = r.IdGestor,
                                        AprovadoSetor = r.AprovadoSetor,
                                        AprovadoGestor = r.AprovadoGestor,
                                        Rascunho = r.Rascunho,
                                        Cancelado = r.Cancelado
                                    })
                                    .ToListAsync();

                return Ok(new GenericResponse<IEnumerable<ListagemSolicitacoesDto>>()
                {
                    Success = true,
                    Message = "Registros obtidos com sucesso.",
                    Data = requests,
                    Page = filter.Page,
                    Pages = records / filter.PageSize,
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

        private IQueryable<ViewListagemSolicitacoes> BuildQuery(ListingFilterDto filter)
        {
            DateTime startDate = filter.RequestStart?.Date ?? new DateTime(1753, 1, 1);
            DateTime endDate = filter.RequestEnd?.Date ?? DateTime.Now.Date.AddDays(7);

            var query = (from r in dbContext.ViewListagemSolicitacoes
                         where r.DataSolicitacao.Date >= startDate &&
                                 r.DataSolicitacao.Date <= endDate
                         select r);

            if(!string.IsNullOrEmpty(filter.DocumentNumber))
            {
                query = query.Where(r => r.NumeroDocumento != null &&
                    r.NumeroDocumento == filter.DocumentNumber);
            }

            if ((filter.DocumentId ?? 0) > 0)
            {
                query = query.Where(r => r.Codigo == filter.DocumentId);
            }

            if (!string.IsNullOrEmpty(filter.PartnerName))
            {
                query = query.Where(r => r.NomeParceiro != null &&
                    r.NomeParceiro.IndexOf(filter.PartnerName) >= 0);
            }

            if ((filter.AssigneeId ?? 0) > 0)
            {
                query = query.Where(r => r.IdUsuarioAtribuido == filter.AssigneeId);
            }

            if (filter.DueStart is not null)
            {
                query = query.Where(r => r.DataVencimento >= filter.DueStart);
            }

            if (filter.DueEnd is not null)
            {
                query = query.Where(r => r.DataVencimento <= filter.DueEnd);
            }

            if ((filter.RequesterId ?? 0) > 0)
            {
                query = query.Where(r => r.IdSolicitante == filter.RequesterId);
            }

            if ((filter.ManagerStatus ?? 0) > 0)
            {
                query = query.Where(r => (r.IdStatusGestor ?? 1) == filter.ManagerStatus);
            }

            if ((filter.FinanceStatus ?? 0) > 0)
            {
                query = query.Where(r => (r.IdStatusFinanceiro ?? 1) == filter.FinanceStatus);
            }

            if ((filter.AccountingStatus ?? 0) > 0)
            {
                query = query.Where(r => (r.IdStatusContabilidade ?? 1) == filter.AccountingStatus);
            }

            if(filter.IsAssigned.HasValue)
            {
                query = query.Where(r => (r.IdUsuarioAtribuido == null && filter.IsAssigned == false) || 
                    (r.IdUsuarioAtribuido != null && filter.IsAssigned == true));
            }

            if (filter.ViewMode is not null && filter.ViewMode != PaymentListView.Undefined)
            {
                var currentUser = this.GetUserFromToken()
                    ?? throw new Exception("Usuário não autenticado.");

                query = ApplyViewModeFilter(
                    query,
                    filter.ViewMode.Value,
                    currentUser.IdUsuario,
                    this.CurrentUserIsInRole(Funcoes.Financeiro));
            }

            var sortField = string.IsNullOrEmpty(filter.SortField) ?
                "DataSolicitacao" : filter.SortField;

            sortField = char.ToUpper(sortField[0]) + sortField.Substring(1);

            if (string.Equals(filter.SortOrder, "asc", StringComparison.CurrentCultureIgnoreCase))
            {
                query = query.OrderBy(e => EF.Property<object>(e, sortField));
            }
            else
            {
                query = query.OrderByDescending(e => EF.Property<object>(e, sortField));
            }

            return query;
        }

        /// <summary>
        /// Aplica o filtro de visão (aba) à listagem. Rascunhos e cancelados NUNCA aparecem
        /// no fluxo de aprovação (Gestor / Financeiro / Dashboard); apenas a visão
        /// "Minhas Solicitações" exibe os próprios registros do usuário, inclusive rascunhos.
        /// </summary>
        public static IQueryable<ViewListagemSolicitacoes> ApplyViewModeFilter(
            IQueryable<ViewListagemSolicitacoes> query,
            PaymentListView viewMode,
            int currentUserId,
            bool isFinanceRole)
        {
            switch (viewMode)
            {
                case PaymentListView.MyView:
                    return query.Where(r => r.IdSolicitante == currentUserId);

                case PaymentListView.ManagerView:
                    return query.Where(r => r.IdGestor == currentUserId &&
                        r.Rascunho != true && r.Cancelado != true);

                case PaymentListView.FinancialView:
                    return isFinanceRole
                        ? query.Where(r => r.AprovadoGestor == true &&
                            r.Rascunho != true && r.Cancelado != true)
                        : query.Where(r => r.AprovadoGestor == true && r.IdUsuarioAtribuido == currentUserId &&
                            r.Rascunho != true && r.Cancelado != true);

                case PaymentListView.Dashboard:
                    return query.Where(r =>
                        r.IdUsuarioAtribuido == null &&
                        r.IdStatusGestor == 2 &&
                        r.IdStatusFinanceiro != 3 &&
                        r.IdStatusContabilidade != 3 &&
                        r.Rascunho != true && r.Cancelado != true);

                default:
                    return query;
            }
        }

    }
}
