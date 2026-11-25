using System.Collections.Concurrent;
using System.Threading.Channels;

namespace Ares.PagueAres.Application.Exportacao
{
    /// <summary>
    /// Fila em memória das exportações a gerar (contrato C9), compartilhada pela API e pelo
    /// <see cref="ExportacaoWorker"/>. Guarda só ids: o estado de cada job está na tabela <c>ExportacaoJob</c>, e o
    /// worker relê o job e o reivindica pelo token <c>Versao</c> antes de gerar, então um id repetido ou de um job
    /// que já não está Pendente é simplesmente pulado. Também registra o job em execução neste processo, para que
    /// <see cref="SolicitarCancelamento"/> interrompa a geração na hora.
    /// </summary>
    public sealed class ExportacaoFila
    {
        private readonly Channel<int> _canal = Channel.CreateUnbounded<int>(new UnboundedChannelOptions
        {
            SingleReader = true,
            SingleWriter = false,
        });

        private readonly ConcurrentDictionary<int, ExecucaoExportacao> _emExecucao = new();

        // Garante um job por vez neste processo mesmo que, por engano, mais de um worker use a mesma fila.
        private readonly SemaphoreSlim _umJobPorVez = new(1, 1);

        /// <summary>Põe o job no fim da fila. Nunca bloqueia (a fila não tem limite).</summary>
        public void Enfileirar(int idExportacao) => _canal.Writer.TryWrite(idExportacao);

        /// <summary>
        /// Interrompe a geração do job, se ele estiver em execução neste processo. Devolve <c>false</c> quando o job
        /// não está rodando aqui (Pendente, já terminado ou em execução em outro processo); nesses casos o pedido
        /// gravado no banco (<c>CancelamentoSolicitado</c>) é o que vale, e o worker o confere ao reivindicar o job e a
        /// cada gravação de progresso.
        /// </summary>
        public bool SolicitarCancelamento(int idExportacao)
        {
            if (!_emExecucao.TryGetValue(idExportacao, out var execucao))
                return false;

            execucao.Cancelar();
            return true;
        }

        /// <summary>Lê os ids na ordem de chegada até o token ser cancelado. Só o worker lê.</summary>
        internal IAsyncEnumerable<int> LerAsync(CancellationToken ct) => _canal.Reader.ReadAllAsync(ct);

        /// <summary>
        /// Registra o job como em execução neste processo, esperando o anterior terminar se houver um. O registro deve
        /// ser descartado ao fim da geração.
        /// </summary>
        internal async Task<ExecucaoExportacao> IniciarExecucaoAsync(int idExportacao, CancellationTokenSource cts, CancellationToken ct)
        {
            await _umJobPorVez.WaitAsync(ct);
            var execucao = new ExecucaoExportacao(idExportacao, cts, Encerrar);
            _emExecucao[idExportacao] = execucao;
            return execucao;
        }

        private void Encerrar(ExecucaoExportacao execucao)
        {
            _emExecucao.TryRemove(new KeyValuePair<int, ExecucaoExportacao>(execucao.IdExportacao, execucao));
            _umJobPorVez.Release();
        }
    }

    /// <summary>
    /// Job em execução neste processo: o <see cref="CancellationTokenSource"/> da geração e o motivo de uma eventual
    /// interrupção (pedido do usuário ou perda da posse do job para outro executor).
    /// </summary>
    internal sealed class ExecucaoExportacao : IDisposable
    {
        private readonly CancellationTokenSource _cts;
        private readonly Action<ExecucaoExportacao> _aoEncerrar;
        private int _encerrada;
        private volatile bool _cancelamentoSolicitado;
        private volatile bool _possePerdida;

        public ExecucaoExportacao(int idExportacao, CancellationTokenSource cts, Action<ExecucaoExportacao> aoEncerrar)
        {
            IdExportacao = idExportacao;
            _cts = cts;
            _aoEncerrar = aoEncerrar;
        }

        public int IdExportacao { get; }

        /// <summary>O usuário pediu o cancelamento (pela fila ou pela coluna do banco).</summary>
        public bool CancelamentoSolicitado => _cancelamentoSolicitado;

        /// <summary>O job deixou de estar Processando no banco: outro executor mudou o status dele.</summary>
        public bool PossePerdida => _possePerdida;

        public void Cancelar()
        {
            _cancelamentoSolicitado = true;
            Interromper();
        }

        public void PerderPosse()
        {
            _possePerdida = true;
            Interromper();
        }

        public void Dispose()
        {
            if (Interlocked.Exchange(ref _encerrada, 1) == 0)
                _aoEncerrar(this);
        }

        private void Interromper()
        {
            try
            {
                _cts.Cancel();
            }
            catch (ObjectDisposedException)
            {
                // A geração já terminou e o token foi descartado: não há o que interromper.
            }
        }
    }
}
