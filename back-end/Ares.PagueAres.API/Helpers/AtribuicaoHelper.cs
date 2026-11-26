using Ares.PagueAres.Domain;
using Ares.PagueAres.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace Ares.PagueAres.API.Helpers
{
    /// <summary>
    /// Regras da atribuição de um título (solicitação ou RDDV) a um usuário, usadas por
    /// <c>RequestController.SetUser</c> e <c>RddvController.SetUser</c> (<c>GET /api/{request|rddv}/{id}/user?userId=</c>),
    /// a lista "Atribuir" da tela inicial.
    /// <list type="bullet">
    /// <item>Quem atribui: perfil Financeiro ou Administrador, pelo papel do JWT. A tela libera a lista só para o
    /// Financeiro; o Administrador entra porque tem acesso total.</item>
    /// <item>Quem recebe: usuário ativo com perfil Financeiro ou Contabilidade, o mesmo conjunto que a tela oferece
    /// (<c>GET /api/users/list?roles=FIN,CON</c>). O título atribuído aparece na visão financeira de quem o recebe e sai
    /// da fila do Dashboard, por isso o destino não pode ser um usuário qualquer.</item>
    /// </list>
    /// </summary>
    public static class AtribuicaoHelper
    {
        public const string MensagemSemPermissao = "Atribuição permitida apenas para os perfis Financeiro e Administrador.";

        public const string MensagemDestinoInvalido =
            "O usuário informado não pode receber a atribuição: escolha um usuário ativo com perfil Financeiro ou Contabilidade.";

        /// <summary>Verdadeiro quando o usuário do JWT pode atribuir títulos (Financeiro ou Administrador).</summary>
        public static bool PodeAtribuir(HttpContext context) =>
            AuthenticationHelper.CurrentUserIsInRole(context, Funcoes.Financeiro, Funcoes.Administrador);

        /// <summary>
        /// Verdadeiro quando <paramref name="idUsuario"/> é de um usuário ativo com perfil Financeiro ou Contabilidade no
        /// banco. Id inexistente, zero, usuário inativo e usuário sem um desses perfis dão falso.
        /// </summary>
        public static Task<bool> PodeReceberAtribuicaoAsync(PagueAresContext dbContext, int idUsuario) =>
            (from usuario in dbContext.Usuarios
             join usuarioFuncao in dbContext.UsuarioFuncaos on usuario.IdUsuario equals usuarioFuncao.IdUsuario
             join funcao in dbContext.Funcaos on usuarioFuncao.IdFuncao equals funcao.IdFuncao
             where usuario.IdUsuario == idUsuario && usuario.Ativo == true &&
                 (funcao.Alias == Funcoes.Financeiro || funcao.Alias == Funcoes.Contabilidade)
             select usuario.IdUsuario)
            .AnyAsync();
    }
}
