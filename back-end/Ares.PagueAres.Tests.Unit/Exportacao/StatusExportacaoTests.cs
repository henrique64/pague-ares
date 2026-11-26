using Ares.PagueAres.Domain.Enums;
using FluentAssertions;

namespace Ares.PagueAres.Tests.Unit.Exportacao
{
    /// <summary>
    /// A situação do job é guardada como inteiro na coluna <c>ExportacaoJob.Status</c> e chega assim ao front-end
    /// (contrato C1). Renumerar um valor muda o significado das linhas já gravadas; acrescentar um exige revisar a API,
    /// o worker, a limpeza e a tela.
    /// </summary>
    public class StatusExportacaoTests
    {
        [Fact]
        public void ValoresGuardadosNaColunaStatus_SaoOsDoContrato()
        {
            var valores = Enum.GetValues<StatusExportacao>().ToDictionary(s => s.ToString(), s => (int)s);

            valores.Should().Equal(new Dictionary<string, int>
            {
                ["Pendente"] = 0,
                ["Processando"] = 1,
                ["Concluido"] = 2,
                ["Falhou"] = 3,
                ["Expirado"] = 4,
                ["Cancelado"] = 5,
                ["Excluido"] = 6,
            });
        }
    }
}
