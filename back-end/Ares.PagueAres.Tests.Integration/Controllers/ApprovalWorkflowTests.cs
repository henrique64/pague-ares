using Ares.PagueAres.Domain;
using Ares.PagueAres.Domain.Dtos;
using Ares.PagueAres.Infrastructure;
using Ares.PagueAres.Tests.Integration.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using System.Text;
using System.Text.Json;

namespace Ares.PagueAres.Tests.Integration.Controllers
{
    public class ApprovalWorkflowTests : IClassFixture<PagueAresWebAppFactory>
    {
        private readonly PagueAresWebAppFactory _factory;
        private static readonly JsonSerializerOptions JsonOpts = new() { PropertyNameCaseInsensitive = true };

        public ApprovalWorkflowTests(PagueAresWebAppFactory factory)
        {
            _factory = factory;
            _factory.SeedDatabase(ctx =>
            {
                if (!ctx.Solicitacaos.Any())
                {
                    PagueAresWebAppFactory.SeedBaseData(ctx);
                    PagueAresWebAppFactory.SeedSolicitacoes(ctx);
                    PagueAresWebAppFactory.SeedRddvs(ctx);
                }
            });
        }

        private HttpContent ToJson(object obj) =>
            new StringContent(JsonSerializer.Serialize(obj), Encoding.UTF8, "application/json");

        private SolicitacaoDto GetSolicitacao(int id)
        {
            using var scope = _factory.Services.CreateScope();
            var ctx = scope.ServiceProvider.GetRequiredService<PagueAresContext>();
            var s = ctx.Solicitacaos.AsNoTracking().First(x => x.IdSolicitacao == id);
            return new SolicitacaoDto
            {
                IdSolicitacao = s.IdSolicitacao,
                IdUsuario = s.IdUsuario,
                IdDepartamento = s.IdDepartamento,
                DataSolicitacao = s.DataSolicitacao,
                Descricao = s.Descricao,
                Valor = s.Valor,
                FormaPagamento = s.FormaPagamento,
                CentroCusto = s.CentroCusto,
                Projeto = s.Projeto,
                Pca = s.Pca,
                NumDocParceiro = s.NumDocParceiro,
                CodigoParceiro = s.CodigoParceiro,
                NomeParceiro = s.NomeParceiro,
                BancoParceiro = s.BancoParceiro,
                AgenciaParceiro = s.AgenciaParceiro,
                ContaParceiro = s.ContaParceiro,
                Observacao = s.Observacao,
                ObservacaoGestor = s.ObservacaoGestor,
                ObservacaoSetor = s.ObservacaoSetor,
                TipoAutorizacao = s.TipoAutorizacao,
                TipoSolicitacao = s.TipoSolicitacao,
                TipoPagamento = s.TipoPagamento,
                Rascunho = s.Rascunho,
                Cancelado = s.Cancelado,
                StatusGestor = s.StatusGestor,
                StatusFinanceiro = s.StatusFinanceiro,
                StatusContabilidade = s.StatusContabilidade,
                AprovadoGestor = s.AprovadoGestor,
                AprovadoSetor = s.AprovadoSetor,
                Documentos = []
            };
        }

        private Ares.PagueAres.Infrastructure.Models.Solicitacao GetSolicitacaoDb(int id)
        {
            using var scope = _factory.Services.CreateScope();
            var ctx = scope.ServiceProvider.GetRequiredService<PagueAresContext>();
            return ctx.Solicitacaos.AsNoTracking().First(x => x.IdSolicitacao == id);
        }

        [Fact]
        public async Task Post_SolicitacaoCancelada_RetornaErroDeNegocio()
        {
            var client = _factory.GetAuthenticatedClient(3, "Solicitante", "USR");
            var dto = GetSolicitacao(10); // cancelada
            dto.Descricao = "tentativa de edição";

            var response = await client.PostAsync("/api/request", ToJson(dto));
            var body = await response.Content.ReadAsStringAsync();
            var result = JsonSerializer.Deserialize<GenericResponse<object>>(body, JsonOpts);

            result!.Success.Should().BeFalse();
            result.Message.Should().Contain("cancelado");
        }

        [Fact]
        public async Task Post_NovaSolicitacao_CriaCorretamente()
        {
            var client = _factory.GetAuthenticatedClient(3, "Solicitante", "USR");
            var dto = new SolicitacaoDto
            {
                IdSolicitacao = 0,
                IdUsuario = 3,
                IdDepartamento = 1,
                DataSolicitacao = DateTime.Now,
                Descricao = "Nova solicitação teste",
                Valor = 500m,
                FormaPagamento = 1,
                CentroCusto = "", Projeto = "", Pca = "",
                NumDocParceiro = "", CodigoParceiro = "", NomeParceiro = "Fornecedor",
                BancoParceiro = "", AgenciaParceiro = "", ContaParceiro = "",
                Observacao = "", ObservacaoGestor = "", ObservacaoSetor = "",
                TipoAutorizacao = 0, TipoSolicitacao = 1, TipoPagamento = 1,
                Rascunho = true,
                Documentos = []
            };

            var response = await client.PostAsync("/api/request", ToJson(dto));
            var body = await response.Content.ReadAsStringAsync();
            var result = JsonSerializer.Deserialize<GenericResponse<SolicitacaoDto>>(body, JsonOpts);

            result!.Success.Should().BeTrue($"API retornou: {result.Message}");
            result.Data!.IdSolicitacao.Should().BeGreaterThan(0);
        }

        [Fact]
        public async Task Post_GestorAprova_StatusGestorVira2()
        {
            var client = _factory.GetAuthenticatedClient(2, "Gestor", "GES");
            var dto = GetSolicitacao(101); // pendente gestor — ID exclusivo deste teste
            dto.AprovadoGestor = true;
            dto.ObservacaoGestor = "Aprovado pelo gestor";

            var response = await client.PostAsync("/api/request", ToJson(dto));
            var body = await response.Content.ReadAsStringAsync();
            var result = JsonSerializer.Deserialize<GenericResponse<SolicitacaoDto>>(body, JsonOpts);

            result!.Success.Should().BeTrue($"API retornou: {result.Message}");
            GetSolicitacaoDb(101).StatusGestor.Should().Be(2);
        }

        [Fact]
        public async Task Post_GestorRejeita_StatusGestorVira3()
        {
            var client = _factory.GetAuthenticatedClient(2, "Gestor", "GES");
            var dto = GetSolicitacao(102); // ID exclusivo deste teste
            dto.AprovadoGestor = false;
            dto.ObservacaoGestor = "Reprovado";

            var response = await client.PostAsync("/api/request", ToJson(dto));
            var body = await response.Content.ReadAsStringAsync();
            var result = JsonSerializer.Deserialize<GenericResponse<SolicitacaoDto>>(body, JsonOpts);

            result!.Success.Should().BeTrue($"API retornou: {result.Message}");
            GetSolicitacaoDb(102).StatusGestor.Should().Be(3);
        }

        [Fact]
        public async Task Post_FinanceiroAprova_StatusFinanceiroVira2()
        {
            var client = _factory.GetAuthenticatedClient(4, "Financeiro", "FIN");
            var dto = GetSolicitacao(103); // aprovada pelo gestor, pendente financeiro — ID exclusivo
            dto.AprovadoSetor = true;

            var response = await client.PostAsync("/api/request", ToJson(dto));
            var body = await response.Content.ReadAsStringAsync();
            var result = JsonSerializer.Deserialize<GenericResponse<SolicitacaoDto>>(body, JsonOpts);

            result!.Success.Should().BeTrue($"API retornou: {result.Message}");
            GetSolicitacaoDb(103).StatusFinanceiro.Should().Be(2);
        }

        [Fact]
        public async Task Post_FinanceiroRejeita_StatusFinanceiroVira3_EContabilidadeVira3()
        {
            var client = _factory.GetAuthenticatedClient(4, "Financeiro", "FIN");
            var dto = GetSolicitacao(104); // ID exclusivo deste teste
            dto.AprovadoSetor = false;
            dto.ObservacaoSetor = "Negado pelo financeiro";

            var response = await client.PostAsync("/api/request", ToJson(dto));
            var body = await response.Content.ReadAsStringAsync();
            var result = JsonSerializer.Deserialize<GenericResponse<SolicitacaoDto>>(body, JsonOpts);

            result!.Success.Should().BeTrue($"API retornou: {result.Message}");
            var dbState = GetSolicitacaoDb(104);
            dbState.StatusFinanceiro.Should().Be(3);
            dbState.StatusContabilidade.Should().Be(3);
        }

        [Fact]
        public async Task Post_ContabilidadeLanca_StatusContabilidadeVira2()
        {
            var client = _factory.GetAuthenticatedClient(5, "Contabilidade", "CON");
            var dto = GetSolicitacao(105); // aprovada pelo financeiro — ID exclusivo
            dto.StatusContabilidade = 2;

            var response = await client.PostAsync("/api/request", ToJson(dto));
            var body = await response.Content.ReadAsStringAsync();
            var result = JsonSerializer.Deserialize<GenericResponse<SolicitacaoDto>>(body, JsonOpts);

            result!.Success.Should().BeTrue();
            result.Data!.StatusContabilidade.Should().Be(2);
        }

        [Fact]
        public async Task Post_ContabilidadeRecusa_StatusContabilidadeVira3()
        {
            var client = _factory.GetAuthenticatedClient(5, "Contabilidade", "CON");
            var dto = GetSolicitacao(106); // ID exclusivo deste teste
            dto.StatusContabilidade = 3;

            var response = await client.PostAsync("/api/request", ToJson(dto));
            var body = await response.Content.ReadAsStringAsync();
            var result = JsonSerializer.Deserialize<GenericResponse<SolicitacaoDto>>(body, JsonOpts);

            result!.Success.Should().BeTrue();
            result.Data!.StatusContabilidade.Should().Be(3);
        }

        [Fact]
        public async Task PostRddv_CanceladoRetornaErro()
        {
            var client = _factory.GetAuthenticatedClient(3, "Solicitante", "USR");

            using var scope = _factory.Services.CreateScope();
            var ctx = scope.ServiceProvider.GetRequiredService<PagueAresContext>();
            var r = ctx.Rddv.First(x => x.IdRelatorio == 50); // cancelado
            var dto = new RddvDto
            {
                IdRelatorio = r.IdRelatorio,
                IdUsuario = r.IdUsuario,
                IdDepartamento = r.IdDepartamento,
                NomeFuncionario = r.NomeFuncionario,
                CPF = r.CPF,
                Finalidade = r.Finalidade,
                TipoRelatorio = r.TipoRelatorio,
                Moeda = r.Moeda,
                TipoViagem = r.TipoViagem,
                LocalViagem = r.LocalViagem,
                CentroCusto = r.CentroCusto,
                Projeto = r.Projeto,
                PCA = r.PCA,
                Observacao = r.Observacao,
                ObservacaoGestor = r.ObservacaoGestor,
                ObservacaoSetor = r.ObservacaoSetor,
                TipoAutorizacao = r.TipoAutorizacao,
                Banco = r.Banco, Agencia = r.Agencia, Conta = r.Conta,
                DataCadastro = r.DataCadastro,
                Rascunho = r.Rascunho,
                Cancelado = r.Cancelado
            };

            var response = await client.PostAsync("/api/rddv", ToJson(dto));
            var body = await response.Content.ReadAsStringAsync();
            var result = JsonSerializer.Deserialize<GenericResponse<object>>(body, JsonOpts);

            result!.Success.Should().BeFalse();
            result.Message.Should().Contain("cancelado");
        }

        [Fact]
        public async Task GetDashboard_ComRddvsPendentes_RetornaContagemCorreta()
        {
            // Seedamos 2 RDDVs com statusFinanceiro=2 (aprovados), que entram em "Aprovados"
            var client = _factory.GetAuthenticatedClient(1, "Admin", "ADM");
            var response = await client.GetAsync("/api/request/dashboard");

            var body = await response.Content.ReadAsStringAsync();
            var result = JsonSerializer.Deserialize<GenericResponse<DashboardDto>>(body, JsonOpts);

            result!.Success.Should().BeTrue();
            result.Data!.Aprovados.Should().BeGreaterThanOrEqualTo(2, "há 2 RDDVs com statusFinanceiro=2 seedados");
        }
    }
}
