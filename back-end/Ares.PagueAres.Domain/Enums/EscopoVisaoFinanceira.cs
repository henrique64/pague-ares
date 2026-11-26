namespace Ares.PagueAres.Domain.Enums
{
    /// <summary>
    /// O que a visão Financeiro/Contábil da listagem (<c>PaymentListView.FinancialView</c>) mostra ao usuário, conforme o
    /// perfil dele.
    /// </summary>
    public enum EscopoVisaoFinanceira
    {
        /// <summary>Nada: perfis de fora da área financeira (Solicitante, Gestor), mesmo com título atribuído a eles.</summary>
        Nenhum = 0,

        /// <summary>Só os títulos atribuídos ao usuário: Contabilidade.</summary>
        Atribuidos = 1,

        /// <summary>Todos os títulos aprovados pelo gestor: Financeiro e Administrador.</summary>
        Todos = 2
    }
}
