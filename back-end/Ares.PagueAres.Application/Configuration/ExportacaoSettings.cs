namespace Ares.PagueAres.Application.Configuration
{
    public class ExportacaoSettings
    {
        public string Pasta { get; set; } = "";                 // vazio = Path.Combine(Path.GetTempPath(), "PagueAres", "Exportacao")
        public double RetencaoHoras { get; set; } = 48;
        public int IntervaloLimpezaMinutos { get; set; } = 30;
        public int MaxTitulosPorExportacao { get; set; } = 5000;
        public int MaxJobsAtivosPorUsuario { get; set; } = 2;   // Pendente + Processando
        public long MaxBytesPorExportacao { get; set; } = 21474836480; // 20 GiB
        public long EspacoLivreMinimoMb { get; set; } = 2048;
        public int ValidadeLinkDownloadSegundos { get; set; } = 120;
        public bool WorkerHabilitado { get; set; } = true;
    }
}
