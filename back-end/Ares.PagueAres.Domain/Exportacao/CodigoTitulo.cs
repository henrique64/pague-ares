using Ares.PagueAres.Domain.Enums;

namespace Ares.PagueAres.Domain.Exportacao
{
    public static class CodigoTitulo
    {
        /// <summary>
        /// Código interno do título na exportação: <c>SOL-000123</c> ou <c>RDDV-000045</c>
        /// (mínimo de 6 dígitos; ids maiores saem com todos os dígitos).
        /// </summary>
        public static string Formatar(TipoTituloExportacao tipo, int id)
        {
            var prefixo = tipo switch
            {
                TipoTituloExportacao.Solicitacao => "SOL",
                TipoTituloExportacao.Rddv => "RDDV",
                _ => throw new ArgumentException("O tipo de título deve ser Solicitacao ou Rddv.", nameof(tipo))
            };

            return $"{prefixo}-{id:D6}";
        }
    }
}
