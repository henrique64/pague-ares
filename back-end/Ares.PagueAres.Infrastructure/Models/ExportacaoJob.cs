namespace Ares.PagueAres.Infrastructure.Models
{
    /// <summary>
    /// Estado persistido de uma exportação de títulos com anexos, para que o job sobreviva
    /// à reciclagem do processo (app pool do IIS) entre a criação e o download.
    /// </summary>
    public class ExportacaoJob
    {
        public int IdExportacaoJob { get; set; }

        /// <summary>Dono do job (FK para <c>Usuario.IdUsuario</c>).</summary>
        public int IdUsuario { get; set; }

        /// <summary>Valor de <c>StatusExportacao</c> (Domain), guardado como inteiro.</summary>
        public int Status { get; set; }

        /// <summary><c>FiltroExportacaoDto</c> serializado em JSON.</summary>
        public string FiltrosJson { get; set; } = null!;

        /// <summary>Data de criação, em UTC.</summary>
        public DateTime DataCriacao { get; set; }

        /// <summary>Início do processamento, em UTC.</summary>
        public DateTime? DataInicio { get; set; }

        /// <summary>Conclusão do processamento, em UTC.</summary>
        public DateTime? DataConclusao { get; set; }

        /// <summary>Momento em que o arquivo deixa de estar disponível (conclusão + retenção), em UTC.</summary>
        public DateTime? DataExpiracao { get; set; }

        public int TotalTitulos { get; set; }
        public int TitulosProcessados { get; set; }
        public int TotalAnexos { get; set; }

        /// <summary>
        /// Só o nome do arquivo gerado (<c>exportacao-&lt;id&gt;.zip</c>), nunca um caminho:
        /// o caminho é sempre a pasta configurada mais este nome.
        /// </summary>
        public string? NomeArquivo { get; set; }

        public long? TamanhoBytes { get; set; }

        /// <summary>Mensagem amigável para o usuário; nunca o texto de uma exceção.</summary>
        public string? MensagemErro { get; set; }

        public bool CancelamentoSolicitado { get; set; }

        /// <summary>
        /// Token de concorrência otimista. O <see cref="PagueAresContext"/> troca o valor por um
        /// <see cref="Guid.NewGuid"/> a cada gravação do job, de modo que uma gravação feita a partir
        /// de uma leitura antiga falhe com <c>DbUpdateConcurrencyException</c>.
        /// </summary>
        public Guid Versao { get; set; }
    }
}
