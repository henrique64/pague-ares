namespace Ares.PagueAres.Application.Exportacao
{
    /// <summary>
    /// Mede o espaço livre da unidade em que fica a pasta de exportação. É um serviço próprio para que a medição possa
    /// ser trocada nos testes (por exemplo, para simular um disco que enche durante a geração).
    /// </summary>
    public interface IMedidorEspacoLivre
    {
        /// <summary>
        /// Bytes livres da unidade da pasta, ou <c>null</c> quando não dá para medir (por exemplo, compartilhamento de
        /// rede).
        /// </summary>
        long? EspacoLivreBytes(string pasta);
    }

    /// <summary>
    /// Medição pela <see cref="DriveInfo"/>. No Windows a <see cref="DriveInfo"/> precisa da raiz da unidade; no Linux
    /// ela aceita qualquer caminho e mede o sistema de arquivos que o contém (o volume montado, no Docker).
    /// </summary>
    public sealed class MedidorEspacoLivreUnidade : IMedidorEspacoLivre
    {
        public long? EspacoLivreBytes(string pasta)
        {
            try
            {
                var caminho = Path.GetFullPath(pasta);
                var unidade = OperatingSystem.IsWindows() ? Path.GetPathRoot(caminho) : caminho;
                if (string.IsNullOrEmpty(unidade))
                    return null;

                return new DriveInfo(unidade).AvailableFreeSpace;
            }
            catch (Exception ex) when (ex is ArgumentException or IOException or UnauthorizedAccessException)
            {
                return null;
            }
        }
    }
}
