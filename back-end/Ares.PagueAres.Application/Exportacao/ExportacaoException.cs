namespace Ares.PagueAres.Application.Exportacao
{
    /// <summary>Falha prevista da exportação; a mensagem é amigável e pode ser mostrada ao usuário.</summary>
    public sealed class ExportacaoException(string mensagemAmigavel) : Exception(mensagemAmigavel);
}
