using Ares.PagueAres.Infrastructure;
using Ares.PagueAres.Infrastructure.Models;
using Microsoft.EntityFrameworkCore;

namespace Ares.PagueAres.Tests.Integration.Exportacao
{
    /// <summary>
    /// Mapeamento da tabela ExportacaoJob no provedor InMemory e o token de concorrência Versao,
    /// que a fila de exportação usa para que dois processos nunca reivindiquem o mesmo job.
    /// </summary>
    public class ExportacaoJobModeloTests
    {
        private const int IdUsuarioFicticio = 1;

        private readonly DbContextOptions<PagueAresContext> _opcoes =
            new DbContextOptionsBuilder<PagueAresContext>()
                .UseInMemoryDatabase("ExportacaoJobModelo_" + Guid.NewGuid())
                .Options;

        private PagueAresContext NovoContexto() => new(_opcoes);

        private int CriarJobPendente()
        {
            using var ctx = NovoContexto();
            ctx.Usuarios.Add(new Usuario
            {
                IdUsuario = IdUsuarioFicticio,
                Email = "financeiro.ficticio@exemplo.invalid",
                Nome = "Financeiro Fictício",
                Senha = "hash-ficticio",
                IdDepartamento = 1,
                Ativo = true,
            });

            var job = new ExportacaoJob
            {
                IdUsuario = IdUsuarioFicticio,
                Status = 0,
                FiltrosJson = "{\"tipoTitulo\":0,\"incluirAnexos\":true}",
                DataCriacao = DateTime.UtcNow,
            };
            ctx.ExportacaoJobs.Add(job);
            ctx.SaveChanges();
            return job.IdExportacaoJob;
        }

        [Fact]
        public void GravaELeJob_PeloInMemory()
        {
            var criacao = new DateTime(2026, 11, 25, 17, 0, 0, DateTimeKind.Utc);
            int id;
            Guid versaoGravada;

            using (var ctx = NovoContexto())
            {
                var job = new ExportacaoJob
                {
                    IdUsuario = IdUsuarioFicticio,
                    Status = 2,
                    FiltrosJson = "{\"tipoTitulo\":1}",
                    DataCriacao = criacao,
                    DataInicio = criacao.AddMinutes(1),
                    DataConclusao = criacao.AddMinutes(5),
                    DataExpiracao = criacao.AddMinutes(5).AddHours(48),
                    TotalTitulos = 25,
                    TitulosProcessados = 25,
                    TotalAnexos = 40,
                    NomeArquivo = "exportacao-1.zip",
                    TamanhoBytes = 131_072_000L,
                    MensagemErro = null,
                    CancelamentoSolicitado = false,
                };
                ctx.ExportacaoJobs.Add(job);
                ctx.SaveChanges();

                id = job.IdExportacaoJob;
                versaoGravada = job.Versao;
            }

            id.Should().BeGreaterThan(0);
            versaoGravada.Should().NotBe(Guid.Empty, "a inclusão já grava uma Versao nova");

            using (var ctx = NovoContexto())
            {
                var lido = ctx.ExportacaoJobs.AsNoTracking().Single(j => j.IdExportacaoJob == id);

                lido.IdUsuario.Should().Be(IdUsuarioFicticio);
                lido.Status.Should().Be(2);
                lido.FiltrosJson.Should().Be("{\"tipoTitulo\":1}");
                lido.DataCriacao.Should().Be(criacao);
                lido.DataInicio.Should().Be(criacao.AddMinutes(1));
                lido.DataConclusao.Should().Be(criacao.AddMinutes(5));
                lido.DataExpiracao.Should().Be(criacao.AddMinutes(5).AddHours(48));
                lido.TotalTitulos.Should().Be(25);
                lido.TitulosProcessados.Should().Be(25);
                lido.TotalAnexos.Should().Be(40);
                lido.NomeArquivo.Should().Be("exportacao-1.zip");
                lido.TamanhoBytes.Should().Be(131_072_000L);
                lido.MensagemErro.Should().BeNull();
                lido.CancelamentoSolicitado.Should().BeFalse();
                lido.Versao.Should().Be(versaoGravada);
            }
        }

        [Fact]
        public void CadaGravacao_TrocaAVersao()
        {
            var id = CriarJobPendente();

            using var ctx = NovoContexto();
            var job = ctx.ExportacaoJobs.Single(j => j.IdExportacaoJob == id);
            var versaoInicial = job.Versao;

            job.Status = 1;
            ctx.SaveChanges();
            var versaoAposPrimeira = job.Versao;

            job.TitulosProcessados = 10;
            ctx.SaveChanges();
            var versaoAposSegunda = job.Versao;

            versaoAposPrimeira.Should().NotBe(versaoInicial);
            versaoAposSegunda.Should().NotBe(versaoAposPrimeira);
            versaoAposSegunda.Should().NotBe(Guid.Empty);
        }

        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public async Task SegundoContexto_ComVersaoAntiga_LancaDbUpdateConcurrencyException(bool assincrono)
        {
            var id = CriarJobPendente();

            using var primeiro = NovoContexto();
            using var segundo = NovoContexto();

            // Os dois processos leem o mesmo job Pendente, com a mesma Versao.
            var jobPrimeiro = primeiro.ExportacaoJobs.Single(j => j.IdExportacaoJob == id);
            var jobSegundo = segundo.ExportacaoJobs.Single(j => j.IdExportacaoJob == id);
            var versaoLida = jobPrimeiro.Versao;
            jobSegundo.Versao.Should().Be(versaoLida);

            // O primeiro reivindica o job (Pendente -> Processando); a gravação troca a Versao.
            jobPrimeiro.Status = 1;
            jobPrimeiro.DataInicio = DateTime.UtcNow;
            if (assincrono)
                await primeiro.SaveChangesAsync();
            else
                primeiro.SaveChanges();

            jobPrimeiro.Versao.Should().NotBe(versaoLida);

            // O segundo tenta reivindicar a partir da leitura antiga e precisa perder.
            jobSegundo.Status = 1;
            jobSegundo.DataInicio = DateTime.UtcNow;
            Func<Task> gravarSegundo = assincrono
                ? () => segundo.SaveChangesAsync()
                : () => Task.FromResult(segundo.SaveChanges());

            await gravarSegundo.Should().ThrowAsync<DbUpdateConcurrencyException>();

            using var conferencia = NovoContexto();
            var gravado = conferencia.ExportacaoJobs.AsNoTracking().Single(j => j.IdExportacaoJob == id);
            gravado.Versao.Should().Be(jobPrimeiro.Versao, "só a gravação do primeiro contexto pode valer");
        }
    }
}
