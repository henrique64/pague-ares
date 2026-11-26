using System.Buffers;
using System.Security.Cryptography;

namespace Ares.PagueAres.Application.Exportacao
{
    /// <summary>
    /// Decodifica o base64 de um anexo em blocos, escrevendo cada bloco no destino e no hash ao mesmo tempo,
    /// sem materializar o arquivo decodificado inteiro em memória.
    /// </summary>
    public static class DecodificadorBase64
    {
        /// <summary>Caracteres base64 por bloco: 64 KiB, múltiplo de 4.</summary>
        public const int TamanhoBloco = 64 * 1024;

        // Espaços que o base64 gravado pode trazer (quebras de linha MIME, por exemplo); são ignorados.
        private static readonly SearchValues<char> Espacos = SearchValues.Create(" \t\r\n");

        /// <summary>
        /// Decodifica <paramref name="conteudo"/> (com ou sem o prefixo <c>data:...;base64,</c>) para
        /// <paramref name="destino"/>, acumulando os bytes em <paramref name="hash"/>. Devolve o total de bytes
        /// escritos. Lança <see cref="FormatException"/> se o conteúdo não for base64 válido; nesse caso parte do
        /// conteúdo pode já ter sido escrita no destino.
        /// </summary>
        public static async Task<long> DecodificarAsync(string conteudo, Stream destino, IncrementalHash hash, CancellationToken ct)
        {
            ArgumentNullException.ThrowIfNull(conteudo);
            ArgumentNullException.ThrowIfNull(destino);
            ArgumentNullException.ThrowIfNull(hash);

            var dados = RemoverPrefixoDataUri(conteudo);
            var caracteres = ArrayPool<char>.Shared.Rent(TamanhoBloco);
            var bytes = ArrayPool<byte>.Shared.Rent(TamanhoBloco / 4 * 3);
            try
            {
                long total = 0;
                int posicao = 0;
                bool encontrouPreenchimento = false;

                while (true)
                {
                    ct.ThrowIfCancellationRequested();

                    int lidos = CopiarSignificativos(dados.Span, ref posicao, caracteres.AsSpan(0, TamanhoBloco));
                    if (lidos == 0)
                    {
                        break;
                    }

                    // O preenchimento "=" só pode aparecer no fim do conteúdo.
                    if (encontrouPreenchimento)
                    {
                        throw new FormatException("Conteúdo base64 inválido: dados depois do preenchimento.");
                    }

                    // Só o último bloco pode ter tamanho que não é múltiplo de 4 (base64 sem preenchimento);
                    // completa com "=" quando faltam 1 ou 2 caracteres. Sobrar 1 caractere é sempre inválido.
                    int resto = lidos % 4;
                    if (resto == 1)
                    {
                        throw new FormatException("Conteúdo base64 inválido: tamanho incompatível.");
                    }
                    if (resto != 0)
                    {
                        for (int i = resto; i < 4; i++)
                        {
                            caracteres[lidos++] = '=';
                        }
                    }

                    encontrouPreenchimento = caracteres[lidos - 1] == '=';

                    if (!Convert.TryFromBase64Chars(caracteres.AsSpan(0, lidos), bytes, out int escritos))
                    {
                        throw new FormatException("Conteúdo base64 inválido.");
                    }

                    hash.AppendData(bytes, 0, escritos);
                    await destino.WriteAsync(bytes.AsMemory(0, escritos), ct);
                    total += escritos;
                }

                return total;
            }
            finally
            {
                ArrayPool<char>.Shared.Return(caracteres);
                ArrayPool<byte>.Shared.Return(bytes);
            }
        }

        /// <summary>
        /// Separa o prefixo <c>data:...;base64,</c>: se o conteúdo começa com <c>data:</c>, descarta tudo até a
        /// primeira vírgula, inclusive.
        /// </summary>
        public static ReadOnlyMemory<char> RemoverPrefixoDataUri(string conteudo)
        {
            ArgumentNullException.ThrowIfNull(conteudo);

            var dados = conteudo.AsMemory();
            int inicio = 0;
            while (inicio < dados.Length && char.IsWhiteSpace(dados.Span[inicio]))
            {
                inicio++;
            }
            dados = dados[inicio..];

            if (!dados.Span.StartsWith("data:", StringComparison.OrdinalIgnoreCase))
            {
                return dados;
            }

            int virgula = dados.Span.IndexOf(',');
            if (virgula < 0)
            {
                throw new FormatException("Conteúdo base64 inválido: prefixo data: sem vírgula.");
            }

            return dados[(virgula + 1)..];
        }

        /// <summary>
        /// Copia para <paramref name="destino"/> os próximos caracteres de <paramref name="origem"/> a partir de
        /// <paramref name="posicao"/>, pulando espaços, até encher o destino ou acabar a origem.
        /// </summary>
        private static int CopiarSignificativos(ReadOnlySpan<char> origem, ref int posicao, Span<char> destino)
        {
            int copiados = 0;
            while (posicao < origem.Length && copiados < destino.Length)
            {
                var trecho = origem[posicao..];
                if (trecho.Length > destino.Length - copiados)
                {
                    trecho = trecho[..(destino.Length - copiados)];
                }

                int espaco = trecho.IndexOfAny(Espacos);
                if (espaco < 0)
                {
                    trecho.CopyTo(destino[copiados..]);
                    copiados += trecho.Length;
                    posicao += trecho.Length;
                }
                else
                {
                    trecho[..espaco].CopyTo(destino[copiados..]);
                    copiados += espaco;
                    posicao += espaco + 1;
                }
            }

            return copiados;
        }
    }
}
