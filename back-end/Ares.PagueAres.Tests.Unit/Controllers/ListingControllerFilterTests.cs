using Ares.PagueAres.API.Controllers;
using Ares.PagueAres.Domain.Enums;
using Ares.PagueAres.Infrastructure.Models;
using FluentAssertions;

namespace Ares.PagueAres.Tests.Unit.Controllers
{
    public class ListingControllerFilterTests
    {
        // Cenários: gestor = 2, solicitantes 3 e 6, financeiro/atribuído = 4.
        private static IQueryable<ViewListagemSolicitacoes> Sample() => new List<ViewListagemSolicitacoes>
        {
            // 1: rascunho do solicitante 3 (gestor 2) — NÃO deve entrar no fluxo de aprovação
            new() { Codigo = 1, IdGestor = 2, IdSolicitante = 3, IdUsuarioAtribuido = null, Rascunho = true,  Cancelado = false, AprovadoGestor = false, IdStatusGestor = 1 },
            // 2: enviado/aprovado pelo gestor 2 (solicitante 3)
            new() { Codigo = 2, IdGestor = 2, IdSolicitante = 3, IdUsuarioAtribuido = null, Rascunho = false, Cancelado = false, AprovadoGestor = true,  IdStatusGestor = 2 },
            // 3: cancelado (gestor 2, solicitante 3) — NÃO deve entrar no fluxo de aprovação
            new() { Codigo = 3, IdGestor = 2, IdSolicitante = 3, IdUsuarioAtribuido = null, Rascunho = false, Cancelado = true,  AprovadoGestor = false, IdStatusGestor = 1 },
            // 4: outro gestor (9), aprovado, sem atribuição
            new() { Codigo = 4, IdGestor = 9, IdSolicitante = 6, IdUsuarioAtribuido = null, Rascunho = false, Cancelado = false, AprovadoGestor = true,  IdStatusGestor = 2 },
            // 5: aprovado, atribuído ao usuário 4
            new() { Codigo = 5, IdGestor = 9, IdSolicitante = 6, IdUsuarioAtribuido = 4,    Rascunho = false, Cancelado = false, AprovadoGestor = true,  IdStatusGestor = 2 },
            // 6: rascunho atribuído ao usuário 4 — NÃO deve entrar no fluxo de aprovação
            new() { Codigo = 6, IdGestor = 9, IdSolicitante = 6, IdUsuarioAtribuido = 4,    Rascunho = true,  Cancelado = false, AprovadoGestor = true,  IdStatusGestor = 2 },
        }.AsQueryable();

        [Fact]
        public void ManagerView_NaoRetornaRascunhoNemCancelado()
        {
            var result = ListingController
                .ApplyViewModeFilter(Sample(), PaymentListView.ManagerView, currentUserId: 2, isFinanceRole: false)
                .Select(r => r.Codigo)
                .ToList();

            // Do gestor 2 (1, 2, 3): rascunho (1) e cancelado (3) ficam de fora.
            result.Should().BeEquivalentTo(new[] { 2 });
        }

        [Fact]
        public void MyView_RetornaPropriosRegistrosInclusiveRascunho()
        {
            var result = ListingController
                .ApplyViewModeFilter(Sample(), PaymentListView.MyView, currentUserId: 3, isFinanceRole: false)
                .Select(r => r.Codigo)
                .ToList();

            // O solicitante vê os próprios, inclusive rascunho (1) e cancelado (3).
            result.Should().BeEquivalentTo(new[] { 1, 2, 3 });
        }

        [Fact]
        public void FinancialView_Financeiro_NaoRetornaRascunhoNemCancelado()
        {
            var result = ListingController
                .ApplyViewModeFilter(Sample(), PaymentListView.FinancialView, currentUserId: 4, isFinanceRole: true)
                .Select(r => r.Codigo)
                .ToList();

            // Aprovados pelo gestor (2, 4, 5, 6) menos o rascunho (6).
            result.Should().BeEquivalentTo(new[] { 2, 4, 5 });
        }

        [Fact]
        public void FinancialView_NaoFinanceiro_SomenteAtribuidosSemRascunho()
        {
            var result = ListingController
                .ApplyViewModeFilter(Sample(), PaymentListView.FinancialView, currentUserId: 4, isFinanceRole: false)
                .Select(r => r.Codigo)
                .ToList();

            // Atribuídos ao usuário 4 e aprovados (5, 6) menos o rascunho (6).
            result.Should().BeEquivalentTo(new[] { 5 });
        }

        [Fact]
        public void Dashboard_NaoRetornaRascunhoNemCancelado()
        {
            var result = ListingController
                .ApplyViewModeFilter(Sample(), PaymentListView.Dashboard, currentUserId: 1, isFinanceRole: false)
                .Select(r => r.Codigo)
                .ToList();

            // Não atribuídos, StatusGestor=2, não rejeitados, não rascunho, não cancelado → 2 e 4.
            result.Should().BeEquivalentTo(new[] { 2, 4 });
        }
    }
}
