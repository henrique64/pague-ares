using Ares.PagueAres.Domain.Enums;
using Ares.PagueAres.Domain.Exportacao;
using FluentAssertions;

namespace Ares.PagueAres.Tests.Unit.Exportacao
{
    public class CodigoTituloTests
    {
        [Fact]
        public void Formatar_Solicitacao_PreencheSeisDigitos()
        {
            CodigoTitulo.Formatar(TipoTituloExportacao.Solicitacao, 123).Should().Be("SOL-000123");
        }

        [Fact]
        public void Formatar_Rddv_PreencheSeisDigitos()
        {
            CodigoTitulo.Formatar(TipoTituloExportacao.Rddv, 45).Should().Be("RDDV-000045");
        }

        [Fact]
        public void Formatar_IdAcimaDeSeisDigitos_MantemTodosOsDigitos()
        {
            CodigoTitulo.Formatar(TipoTituloExportacao.Solicitacao, 1234567).Should().Be("SOL-1234567");
        }

        [Fact]
        public void Formatar_Ambos_LancaArgumentException()
        {
            var acao = () => CodigoTitulo.Formatar(TipoTituloExportacao.Ambos, 1);

            acao.Should().Throw<ArgumentException>();
        }
    }
}
