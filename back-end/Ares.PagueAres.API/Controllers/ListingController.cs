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
            var query = ApplyFilters(dbContext.ViewListagemSolicitacoes, filter);

            var currentUser = this.GetUserFromToken()
                ?? throw new Exception("Usuário não autenticado.");

            // A visão é sempre aplicada: sem viewMode, ou com Undefined (0), a lista vale como Minhas Solicitações, para
            // que nenhum pedido receba os títulos de todos os usuários. Um valor fora do enum nem chega aqui: o model
            // binding o recusa com 400.
            var viewMode = filter.ViewMode is null or PaymentListView.Undefined
                ? PaymentListView.MyView
                : filter.ViewMode.Value;

            query = ApplyViewModeFilter(
                query,
                viewMode,
                currentUser.IdUsuario,
                veTodosNaVisaoFinanceira: this.CurrentUserIsInRole(Funcoes.Financeiro, Funcoes.Administrador),
                veFilaDoDashboard: this.CurrentUserIsInRole(Funcoes.Financeiro, Funcoes.Contabilidade, Funcoes.Administrador));

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
        /// Aplica os filtros da tela de pagamentos (sem a visão e sem a ordenação). É a regra única de filtro da lista
        /// (<see cref="GetAllAsync"/>) e do Excel <c>Pagamentos.xlsx</c> (<c>ExportController.ExportPaymentList</c>), para
        /// que os dois tragam os mesmos títulos com o mesmo filtro.
        /// <list type="bullet">
        /// <item>Período da solicitação com um lado só: sem início vale desde 01/01/1753; sem fim vale até hoje + 7 dias.
        /// A comparação é por data, sem a hora.</item>
        /// <item>Vencimento com um lado só: cada lado informado limita o vencimento.</item>
        /// <item>Número do documento (igual) e nome do parceiro (contém, sem diferenciar maiúsculas de minúsculas) valem
        /// para os dois tipos de título. Na view, o RDDV tem <c>#</c> nos dois campos, então sai com esses filtros.</item>
        /// <item>Código, solicitante, atribuído, os três status e "atribuído ou não" valem para os dois tipos. Código zero
        /// e id zero valem como filtro vazio.</item>
        /// </list>
        /// </summary>
        public static IQueryable<ViewListagemSolicitacoes> ApplyFilters(
            IQueryable<ViewListagemSolicitacoes> query,
            ListingFilterDto filter)
        {
            DateTime startDate = filter.RequestStart?.Date ?? new DateTime(1753, 1, 1);
            DateTime endDate = filter.RequestEnd?.Date ?? DateTime.Now.Date.AddDays(7);

            query = query.Where(r => r.DataSolicitacao.Date >= startDate &&
                r.DataSolicitacao.Date <= endDate);

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
                var nomeParceiro = filter.PartnerName.ToLower();

                query = query.Where(r => r.NomeParceiro != null &&
                    r.NomeParceiro.ToLower().Contains(nomeParceiro));
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

            return query;
        }

        /// <summary>
        /// Aplica o filtro de visão (aba) à listagem. Rascunhos e cancelados NUNCA aparecem
        /// no fluxo de aprovação (Gestor / Financeiro / Dashboard); apenas a visão
        /// "Minhas Solicitações" exibe os próprios registros do usuário, inclusive rascunhos.
        /// Toda visão tem escopo: Undefined (0) e qualquer valor fora do enum valem como Minhas Solicitações, e nunca
        /// devolvem a lista inteira.
        /// </summary>
        /// <param name="veTodosNaVisaoFinanceira">
        /// Verdadeiro quando o usuário tem o perfil Financeiro ou Administrador: na visão financeira (aba
        /// Financeiro/Contábil) ele vê todos os títulos aprovados pelo gestor. Falso nos demais perfis (Contabilidade,
        /// por exemplo), que veem só os títulos atribuídos a eles. As outras visões não usam este parâmetro.
        /// </param>
        /// <param name="veFilaDoDashboard">
        /// Verdadeiro quando o usuário tem o perfil Financeiro, Contabilidade ou Administrador, os perfis que veem a tela
        /// inicial. A fila do Dashboard traz títulos de todos os usuários; sem um desses perfis, ela vem vazia. As
        /// outras visões não usam este parâmetro.
        /// </param>
        public static IQueryable<ViewListagemSolicitacoes> ApplyViewModeFilter(
            IQueryable<ViewListagemSolicitacoes> query,
            PaymentListView viewMode,
            int currentUserId,
            bool veTodosNaVisaoFinanceira,
            bool veFilaDoDashboard)
        {
            switch (viewMode)
            {
                case PaymentListView.ManagerView:
                    return query.Where(r => r.IdGestor == currentUserId &&
                        r.Rascunho != true && r.Cancelado != true);

                case PaymentListView.FinancialView:
                    return veTodosNaVisaoFinanceira
                        ? query.Where(r => r.AprovadoGestor == true &&
                            r.Rascunho != true && r.Cancelado != true)
                        : query.Where(r => r.AprovadoGestor == true && r.IdUsuarioAtribuido == currentUserId &&
                            r.Rascunho != true && r.Cancelado != true);

                case PaymentListView.Dashboard:
                    if (!veFilaDoDashboard)
                    {
                        return query.Where(r => false);
                    }

                    return query.Where(r =>
                        r.IdUsuarioAtribuido == null &&
                        r.IdStatusGestor == 2 &&
                        r.IdStatusFinanceiro != 3 &&
                        r.IdStatusContabilidade != 3 &&
                        r.Rascunho != true && r.Cancelado != true);

                // Minhas Solicitações, e também Undefined e qualquer valor fora do enum.
                case PaymentListView.MyView:
                default:
                    return query.Where(r => r.IdSolicitante == currentUserId);
            }
        }

    }
}
