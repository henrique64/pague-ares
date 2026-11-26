using Ares.PagueAres.API.Controllers;
using Ares.PagueAres.Domain.Dtos.Authentication;
using Ares.PagueAres.Domain.Dtos.Listing;
using Ares.PagueAres.Domain.Enums;
using Ares.PagueAres.Infrastructure;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using System.Data;
using System.Data.Common;

namespace Ares.PagueAres.Tests.Integration.Controllers
{
    /// <summary>
    /// A regra de filtro compartilhada (<c>ListingController.ApplyFilters</c>) e a consulta do Excel
    /// <c>Pagamentos.xlsx</c> traduzem para SQL Server com todos os filtros preenchidos, sem avaliação no cliente. Sem
    /// banco: um interceptador captura o SQL gerado e suprime a conexão e a execução.
    /// </summary>
    public class PagamentosTraducaoSqlServerTests
    {
        private sealed class CapturaSql : DbCommandInterceptor, IDbConnectionInterceptor
        {
            public List<string> Comandos { get; } = [];

            ValueTask<InterceptionResult> IDbConnectionInterceptor.ConnectionOpeningAsync(
                DbConnection connection, ConnectionEventData eventData, InterceptionResult result, CancellationToken cancellationToken)
                => ValueTask.FromResult(InterceptionResult.Suppress());

            public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(
                DbCommand command, CommandEventData eventData, InterceptionResult<DbDataReader> result, CancellationToken cancellationToken = default)
            {
                Comandos.Add(command.CommandText);
                return ValueTask.FromResult(InterceptionResult<DbDataReader>.SuppressWithResult(new DataTable().CreateDataReader()));
            }
        }

        private static (PagueAresContext Ctx, CapturaSql Captura, List<string> Avisos) NovoContextoSqlServer()
        {
            var captura = new CapturaSql();
            var avisos = new List<string>();

            var options = new DbContextOptionsBuilder<PagueAresContext>()
                .UseSqlServer("Server=(local);Database=SemBanco;Integrated Security=true;TrustServerCertificate=True")
                .AddInterceptors(captura)
                .LogTo(avisos.Add, LogLevel.Warning)
                .Options;

            return (new PagueAresContext(options), captura, avisos);
        }

        [Fact]
        public void ApplyFilters_TodosOsFiltros_TraduzParaSqlServer()
        {
            var (ctx, _, avisos) = NovoContextoSqlServer();
            using var contexto = ctx;

            var filtro = new ListingFilterDto(
                DocumentNumber: "NF-1", RequestStart: new DateTime(2026, 1, 1), RequestEnd: new DateTime(2026, 12, 31),
                DueStart: new DateTime(2026, 1, 1), DueEnd: new DateTime(2026, 12, 31), Type: null, PartnerName: "Alfa",
                RequesterId: 3, AssigneeId: 4, IsAssigned: true, ManagerStatus: 2, FinanceStatus: 1, AccountingStatus: 1,
                DocumentId: 123, ViewMode: null, SortField: null);

            var sql = ListingController.ApplyFilters(ctx.ViewListagemSolicitacoes, filtro).ToQueryString();

            sql.Should().Contain("[vListagemSolicitacoes]").And.Contain("LOWER(");
            avisos.Should().BeEmpty("a tradução não pode gerar aviso do EF");
        }

        [Fact]
        public async Task ExportPaymentList_TodosOsFiltros_TraduzParaSqlServerEDevolveAPlanilha()
        {
            var (ctx, captura, avisos) = NovoContextoSqlServer();
            using var contexto = ctx;
            using var cache = new MemoryCache(new MemoryCacheOptions());

            var key = Guid.NewGuid();
            cache.Set("preview-key-" + key, new PreviewKeyDto(key, EnumRequestType.Exportacao, 0, 1));

            var controller = new ExportController(ctx, cache, NullLogger<ExportController>.Instance);

            const string sFilter = "{\"documentNumber\":\"NF-1\",\"requestStart\":\"2026-01-01\",\"requestEnd\":\"2026-12-31\"," +
                "\"dueStart\":\"2026-01-01\",\"dueEnd\":\"2026-12-31\",\"requesterId\":3,\"managerStatus\":2,\"financeStatus\":1," +
                "\"accountingStatus\":1,\"assigneeId\":4,\"isAssigned\":true,\"documentId\":123,\"partnerName\":\"Alfa\"," +
                "\"partnerDocument\":\"123.456\"}";

            var resultado = await controller.ExportPaymentList(sFilter, 0, key.ToString());

            resultado.Should().BeOfType<FileStreamResult>("a consulta traduz e a planilha sai, mesmo sem linhas");
            captura.Comandos.Should().ContainSingle();
            captura.Comandos[0].Should().Contain("[vListagemSolicitacoes]").And.Contain("EXISTS").And.Contain("ORDER BY");
            captura.Comandos[0].Should().Contain("LIKE N'RDDV%'",
                "solicitação e RDDV se distinguem pelo texto do tipo na view, e não pelo IdTipo, que no RDDV é o tipo de relatório");
            captura.Comandos[0].Should().NotContain("[IdTipo] = 0");
            avisos.Should().BeEmpty("a tradução não pode gerar aviso do EF");
        }
    }
}
