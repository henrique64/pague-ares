using System.Globalization;
using System.Text;

namespace Ares.PagueAres.Application.Exportacao
{
    /// <summary>
    /// Nomes das entradas do pacote de exportação (contrato C1). Os nomes originais dos anexos vêm do
    /// usuário; sem esta sanitização, uma entrada como <c>../../x</c> ou <c>C:\x</c> escreveria fora da
    /// pasta de destino na máquina de quem extrai o ZIP (zip-slip).
    /// </summary>
    public static class NomesPacote
    {
        /// <summary>Tamanho máximo do nome final do anexo (rótulo + nome), em caracteres.</summary>
        public const int TamanhoMaximoNome = 120;

        /// <summary>Rótulo usado quando o anexo não tem tipo de documento conhecido.</summary>
        public const string RotuloPadrao = "Documento";

        // O rótulo é limitado para sempre sobrar espaço para o nome original dentro dos 120 caracteres.
        private const int TamanhoMaximoRotulo = 60;

        // Trecho final que conta como extensão (com o ponto); acima disso o nome é tratado como sem extensão.
        private const int TamanhoMaximoExtensao = 16;

        private const string SeparadorRotulo = " - ";

        private static readonly char[] CaracteresProibidos = ['<', '>', ':', '"', '/', '\\', '|', '?', '*'];

        // Nomes de dispositivo do Windows, reservados com ou sem extensão (os sobrescritos ¹²³ também valem).
        private static readonly HashSet<string> NomesReservados = new(StringComparer.OrdinalIgnoreCase)
        {
            "CON", "PRN", "AUX", "NUL",
            "COM1", "COM2", "COM3", "COM4", "COM5", "COM6", "COM7", "COM8", "COM9", "COM¹", "COM²", "COM³",
            "LPT1", "LPT2", "LPT3", "LPT4", "LPT5", "LPT6", "LPT7", "LPT8", "LPT9", "LPT¹", "LPT²", "LPT³"
        };

        /// <summary>
        /// Sanitiza um nome de arquivo: remove caracteres de controle (inclusive os invisíveis de formatação,
        /// como os de inversão de direção do texto, que disfarçam a extensão), surrogates órfãos e
        /// <c>&lt; &gt; : " / \ | ? *</c>; troca qualquer sequência <c>..</c> por <c>.</c>; tira espaços do
        /// início e espaços e pontos do fim; e prefixa <c>_</c> em nome reservado do Windows. O resultado nunca
        /// contém separador de pasta. Não limita o tamanho: isso é feito em <see cref="MontarNomeAnexo"/>.
        /// </summary>
        public static string Sanitizar(string? nome)
        {
            if (string.IsNullOrEmpty(nome))
            {
                return "";
            }

            var sb = new StringBuilder(nome.Length);
            for (int i = 0; i < nome.Length; i++)
            {
                char c = nome[i];

                if (char.IsHighSurrogate(c))
                {
                    if (i + 1 < nome.Length && char.IsLowSurrogate(nome[i + 1]))
                    {
                        sb.Append(c).Append(nome[i + 1]);
                        i++;
                    }
                    continue;
                }

                if (char.IsLowSurrogate(c)
                    || char.IsControl(c)
                    || CharUnicodeInfo.GetUnicodeCategory(c) == UnicodeCategory.Format
                    || Array.IndexOf(CaracteresProibidos, c) >= 0)
                {
                    continue;
                }

                sb.Append(c);
            }

            var resultado = sb.ToString();
            while (resultado.Contains("..", StringComparison.Ordinal))
            {
                resultado = resultado.Replace("..", ".", StringComparison.Ordinal);
            }

            resultado = AparaFim(resultado.TrimStart());

            if (EhNomeReservado(resultado))
            {
                resultado = "_" + resultado;
            }

            return resultado;
        }

        /// <summary>
        /// Monta o nome do anexo dentro da pasta do título: <c>&lt;rótulo&gt; - &lt;nome original sanitizado&gt;</c>,
        /// com no máximo <see cref="TamanhoMaximoNome"/> caracteres e a extensão preservada. Sem rótulo, usa
        /// <see cref="RotuloPadrao"/>; nome original vazio (ou que fica vazio depois da sanitização) vira
        /// <c>anexo-&lt;idAnexo&gt;</c>, com a extensão do nome original quando houver uma.
        /// </summary>
        public static string MontarNomeAnexo(string? rotuloTipoDocumento, string? nomeOriginal, int idAnexo)
        {
            var rotulo = Sanitizar(rotuloTipoDocumento);
            if (rotulo.Length == 0)
            {
                rotulo = RotuloPadrao;
            }
            else if (rotulo.Length > TamanhoMaximoRotulo)
            {
                rotulo = AparaFim(CortarSemQuebrarSurrogate(rotulo, TamanhoMaximoRotulo));
            }

            var nome = Sanitizar(nomeOriginal);
            var extensao = Extensao(nome);
            var radical = nome[..^extensao.Length];
            if (radical.Trim('.', ' ').Length == 0)
            {
                radical = $"anexo-{idAnexo}";
            }

            return LimitarTamanho(rotulo + SeparadorRotulo + radical, extensao);
        }

        /// <summary>
        /// Deduplica os nomes de uma mesma pasta sem diferenciar caixa, preservando a ordem: o segundo
        /// <c>Nota Fiscal - boleto.pdf</c> vira <c>Nota Fiscal - boleto (2).pdf</c>, o terceiro <c>(3)</c>, e
        /// assim por diante. O sufixo entra antes da extensão e o nome continua dentro do tamanho máximo.
        /// </summary>
        public static IReadOnlyList<string> Deduplicar(IEnumerable<string> nomes)
        {
            ArgumentNullException.ThrowIfNull(nomes);

            var usados = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var resultado = new List<string>();

            foreach (var nome in nomes)
            {
                var candidato = nome;
                if (!usados.Add(candidato))
                {
                    var extensao = Extensao(nome);
                    var radical = nome[..^extensao.Length];
                    for (int n = 2; ; n++)
                    {
                        candidato = LimitarTamanho(radical, $" ({n}){extensao}");
                        if (usados.Add(candidato))
                        {
                            break;
                        }
                    }
                }

                resultado.Add(candidato);
            }

            return resultado;
        }

        /// <summary>Caminho da entrada no ZIP: sempre <c>&lt;código&gt;/&lt;nome&gt;</c>, com <c>/</c> como separador.</summary>
        public static string CaminhoEntrada(string codigoTitulo, string nomeArquivo) => $"{codigoTitulo}/{nomeArquivo}";

        /// <summary>
        /// Extensão do nome (com o ponto), ou vazio. Trechos longos demais ou com espaço depois do último ponto
        /// não são tratados como extensão (ex.: <c>relatório. versão final</c>).
        /// </summary>
        private static string Extensao(string nome)
        {
            int ponto = nome.LastIndexOf('.');
            if (ponto < 0)
            {
                return "";
            }

            var extensao = nome[ponto..];
            if (extensao.Length < 2 || extensao.Length > TamanhoMaximoExtensao || extensao.Any(char.IsWhiteSpace))
            {
                return "";
            }

            return extensao;
        }

        /// <summary>
        /// Junta <paramref name="inicio"/> e <paramref name="fim"/> sem passar de <see cref="TamanhoMaximoNome"/>,
        /// cortando só o início (o fim leva a extensão e o sufixo de deduplicação).
        /// </summary>
        private static string LimitarTamanho(string inicio, string fim)
        {
            if (inicio.Length + fim.Length <= TamanhoMaximoNome)
            {
                return inicio + fim;
            }

            int espaco = Math.Max(0, TamanhoMaximoNome - fim.Length);
            return AparaFim(CortarSemQuebrarSurrogate(inicio, espaco)) + fim;
        }

        private static string CortarSemQuebrarSurrogate(string texto, int tamanho)
        {
            if (texto.Length <= tamanho)
            {
                return texto;
            }

            if (tamanho > 0 && char.IsHighSurrogate(texto[tamanho - 1]))
            {
                tamanho--;
            }

            return texto[..tamanho];
        }

        /// <summary>Tira espaços (qualquer espaço em branco) e pontos do fim, que o Windows descarta.</summary>
        private static string AparaFim(string texto)
        {
            int fim = texto.Length;
            while (fim > 0 && (texto[fim - 1] == '.' || char.IsWhiteSpace(texto[fim - 1])))
            {
                fim--;
            }

            return texto[..fim];
        }

        private static bool EhNomeReservado(string nome)
        {
            int ponto = nome.IndexOf('.');
            var radical = (ponto < 0 ? nome : nome[..ponto]).TrimEnd();
            return NomesReservados.Contains(radical);
        }
    }
}
