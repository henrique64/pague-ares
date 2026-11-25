namespace Ares.PagueAres.Application.Exportacao
{
    /// <summary>
    /// Rótulos de texto usados na exportação de títulos (planilha, índices e nome dos anexos).
    /// Todos os textos ficam aqui, num lugar só. Código sem rótulo conhecido sai como <c>Código &lt;n&gt;</c>.
    /// <para>Origem de cada lista:</para>
    /// <list type="bullet">
    /// <item>Status de gestor e de financeiro (0/1, 2, 3) e status contábil (0/1, 2, 3): dicionários
    /// <c>StatusGestor</c>, <c>StatusFinanceiro</c> e <c>StatusContabil</c> de <c>ExportController.cs</c>.</item>
    /// <item>Tipo de relatório do RDDV (1, 2, 3): dicionário <c>RddvReportType</c> de <c>ExportController.cs</c>.</item>
    /// <item>Tipo de solicitação, tipo de pagamento e forma de pagamento: opções de
    /// <c>front-end/src/app/create-request/create-request.component.html</c> (selects "Tipo de Solicitação",
    /// "Tipo de Pagamento" e "Forma de Pagamento").</item>
    /// <item>Tipo de despesa do RDDV: lista <c>tiposDespesa</c> usada por <c>labelTipoDespesa</c> em
    /// <c>front-end/src/app/create-rddv/create-rddv.component.ts</c>.</item>
    /// <item>Departamento, tipo de documento e tipo de viagem não estão aqui: vêm das tabelas
    /// <c>Departamento</c>, <c>TipoDocumento</c> e <c>TipoViagem</c>.</item>
    /// </list>
    /// </summary>
    public static class RotulosExportacao
    {
        /// <summary>Rótulo do anexo sem tipo de documento (ou com tipo que não existe na tabela).</summary>
        public const string TipoDocumentoPadrao = "Documento";

        public static string Desconhecido(int codigo) => $"Código {codigo}";

        /// <summary>Status do gestor. Nulo vale 0, como no relatório legado.</summary>
        public static string StatusGestor(int? status) => StatusAutorizacao(status);

        /// <summary>Status do financeiro. Nulo vale 0, como no relatório legado.</summary>
        public static string StatusFinanceiro(int? status) => StatusAutorizacao(status);

        /// <summary>Status da contabilidade. Nulo vale 0, como no relatório legado.</summary>
        public static string StatusContabil(int? status) => (status ?? 0) switch
        {
            0 or 1 => "Aguardando Lançamento",
            2 => "Lançado",
            3 => "Negado",
            var outro => Desconhecido(outro)
        };

        public static string TipoRelatorio(int tipo) => tipo switch
        {
            1 => "Adiantamento",
            2 => "Prestação de Contas",
            3 => "Reembolso",
            _ => Desconhecido(tipo)
        };

        public static string TipoSolicitacao(int tipo) => tipo switch
        {
            1 => "Adiantamento",
            2 => "Pagamento",
            3 => "Movimento Almoxarifado",
            _ => Desconhecido(tipo)
        };

        public static string TipoPagamento(int tipo) => tipo switch
        {
            1 => "Nacional",
            2 => "Internacional",
            _ => Desconhecido(tipo)
        };

        public static string FormaPagamento(int forma) => forma switch
        {
            1 => "Boleto",
            2 => "Transferência Bancária",
            _ => Desconhecido(forma)
        };

        public static string TipoDespesa(int tipo) => tipo switch
        {
            1 => "Alimentação",
            2 => "Hospedagem",
            3 => "Passagens",
            4 => "Táxi",
            5 => "Kilometragem",
            6 => "Estacionamento",
            7 => "Pedágio",
            8 => "Combustível",
            9 => "Aluguel de Automóveis",
            10 => "Comunicações",
            11 => "Bagagens",
            12 => "Outros",
            _ => Desconhecido(tipo)
        };

        private static string StatusAutorizacao(int? status) => (status ?? 0) switch
        {
            0 or 1 => "Aguardando Autorização",
            2 => "Autorizado",
            3 => "Negado",
            var outro => Desconhecido(outro)
        };
    }
}
