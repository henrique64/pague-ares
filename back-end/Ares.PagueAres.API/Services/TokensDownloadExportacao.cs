using System.Buffers.Text;
using System.Collections.Concurrent;
using System.Security.Cryptography;
using Ares.PagueAres.Application.Configuration;
using Ares.PagueAres.Domain.Dtos.Exportacao;
using Microsoft.Extensions.Options;

namespace Ares.PagueAres.API.Services
{
    /// <summary>
    /// Tokens de download das exportações (contrato C8). O download é uma navegação direta do navegador, sem o cabeçalho
    /// <c>Authorization</c>, por isso cada link carrega um token curto: 32 bytes aleatórios em base64url, vinculado a um
    /// job e ao usuário que o pediu, válido por <c>ValidadeLinkDownloadSegundos</c> e de uso único.
    /// <para>
    /// Os tokens vivem só na memória do processo (singleton): uma reciclagem do processo invalida os links ainda não
    /// usados, e o usuário só precisa pedir outro.
    /// </para>
    /// </summary>
    public sealed class TokensDownloadExportacao
    {
        private const int BytesPorToken = 32;

        private readonly ConcurrentDictionary<string, TokenDownloadExportacao> _tokens = new(StringComparer.Ordinal);
        private readonly TimeSpan _validade;

        public TokensDownloadExportacao(IOptions<ExportacaoSettings> settings)
        {
            _validade = TimeSpan.FromSeconds(Math.Max(1, settings.Value.ValidadeLinkDownloadSegundos));
        }

        /// <summary>
        /// Emite um token novo para o download do job. Quem chama garante que o usuário é o dono e que o job está
        /// Concluido; aqui só se registra o vínculo e a validade.
        /// </summary>
        public LinkDownloadExportacaoDto Emitir(int idExportacaoJob, int idUsuario)
        {
            var agora = DateTime.UtcNow;
            DescartarVencidos(agora);

            var expiraEm = agora.Add(_validade);
            string token;
            do
            {
                token = Base64Url.EncodeToString(RandomNumberGenerator.GetBytes(BytesPorToken));
            }
            while (!_tokens.TryAdd(token, new TokenDownloadExportacao(idExportacaoJob, idUsuario, expiraEm)));

            return new LinkDownloadExportacaoDto { Token = token, ExpiraEm = expiraEm };
        }

        /// <summary>
        /// Consome o token: a remoção é atômica (<see cref="ConcurrentDictionary{TKey,TValue}.TryRemove(TKey, out TValue)"/>),
        /// então, entre requisições simultâneas com o mesmo token, só uma o recebe. Devolve <c>null</c> quando o token está
        /// ausente, é desconhecido, já foi usado ou venceu.
        /// </summary>
        public TokenDownloadExportacao? Consumir(string? token)
        {
            if (string.IsNullOrWhiteSpace(token) || !_tokens.TryRemove(token, out var registro))
                return null;

            return registro.ExpiraEm > DateTime.UtcNow ? registro : null;
        }

        /// <summary>Tira da memória os tokens vencidos que nunca foram usados.</summary>
        private void DescartarVencidos(DateTime agora)
        {
            foreach (var par in _tokens)
            {
                if (par.Value.ExpiraEm <= agora)
                    _tokens.TryRemove(par);
            }
        }
    }

    /// <summary>Vínculo de um token de download: o job, o usuário que pediu o link e o fim da validade (UTC).</summary>
    public sealed record TokenDownloadExportacao(int IdExportacaoJob, int IdUsuario, DateTime ExpiraEm);
}
