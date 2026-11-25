using Ares.PagueAres.Domain.Dtos.Exportacao;
using Ares.PagueAres.Domain.Enums;
using Ares.PagueAres.Domain.Exportacao;
using Ares.PagueAres.Infrastructure;
using Ares.PagueAres.Infrastructure.Models;
using Microsoft.EntityFrameworkCore;

namespace Ares.PagueAres.Application.Exportacao
{
    /// <summary>
    /// Fonte de dados da exportação sobre o <see cref="PagueAresContext"/>.
    /// <para>
    /// Escopo (fixo, vale para FIN e ADM): só títulos com <c>Rascunho == false</c>, <c>Cancelado == false</c> e
    /// <c>AprovadoGestor == true</c>; os filtros de <see cref="FiltroExportacaoDto"/> só restringem esse conjunto.
    /// </para>
    /// <para>
    /// Todas as consultas são <c>AsNoTracking</c> e traduzíveis para SQL Server: o filtro e os joins rodam no banco, e os
    /// rótulos de texto são resolvidos em memória depois, a partir de colunas já materializadas. Anexos: a listagem
    /// projeta só metadados (nunca a coluna <c>Arquivo</c>) e a leitura do conteúdo traz um anexo por chamada.
    /// </para>
    /// </summary>
    public class FonteExportacaoEf(PagueAresContext db) : IFonteExportacao
    {
        /// <summary>Máximo de ids por consulta com <c>IN</c>, para não estourar o limite de parâmetros do SQL Server.</summary>
        private const int TamanhoLote = 1000;

        public async Task<int> ContarTitulosAsync(FiltroExportacaoDto filtro, CancellationToken ct)
        {
            var total = 0;

            if (IncluiSolicitacoes(filtro))
                total += await ConsultarSolicitacoes(filtro).CountAsync(ct);

            if (IncluiRddvs(filtro))
                total += await ConsultarRddvs(filtro).CountAsync(ct);

            return total;
        }

        public async Task<IReadOnlyList<SolicitacaoExportacao>> ListarSolicitacoesAsync(FiltroExportacaoDto filtro, CancellationToken ct)
        {
            if (!IncluiSolicitacoes(filtro)) return [];

            // Joins à esquerda: um usuário ou departamento ausente nunca faz o título sumir da exportação.
            var linhas = await (
                from s in ConsultarSolicitacoes(filtro)
                from dep in db.Departamentos.AsNoTracking().Where(x => x.IdDepartamento == s.IdDepartamento).DefaultIfEmpty()
                from sol in db.Usuarios.AsNoTracking().Where(x => x.IdUsuario == s.IdUsuario).DefaultIfEmpty()
                from atr in db.Usuarios.AsNoTracking().Where(x => x.IdUsuario == s.IdUsuarioAtribuido).DefaultIfEmpty()
                from ges in db.Usuarios.AsNoTracking().Where(x => x.IdUsuario == s.IdGestor).DefaultIfEmpty()
                orderby s.IdSolicitacao
                select new
                {
                    Titulo = s,
                    Departamento = dep == null ? null : dep.Nome,
                    Solicitante = sol == null ? null : sol.Nome,
                    Atribuido = atr == null ? null : atr.Nome,
                    Gestor = ges == null ? null : ges.Nome
                }).ToListAsync(ct);

            return linhas.Select(l =>
            {
                var s = l.Titulo;
                return new SolicitacaoExportacao(
                    s.IdSolicitacao,
                    CodigoTitulo.Formatar(TipoTituloExportacao.Solicitacao, s.IdSolicitacao),
                    RotulosExportacao.TipoSolicitacao(s.TipoSolicitacao),
                    RotulosExportacao.TipoPagamento(s.TipoPagamento),
                    RotulosExportacao.FormaPagamento(s.FormaPagamento),
                    l.Departamento ?? RotulosExportacao.Desconhecido(s.IdDepartamento),
                    l.Solicitante ?? "",
                    l.Atribuido,
                    l.Gestor,
                    s.DataSolicitacao,
                    s.DataDocumento,
                    s.DataVencimento,
                    s.NumeroDocumento,
                    s.Descricao ?? "",
                    s.Valor,
                    s.Parcelado,
                    s.Parcelas,
                    s.CentroCusto ?? "",
                    s.Projeto ?? "",
                    s.Pca ?? "",
                    s.NomeParceiro ?? "",
                    s.NumDocParceiro ?? "",
                    s.CodigoParceiro ?? "",
                    s.BancoParceiro ?? "",
                    s.AgenciaParceiro ?? "",
                    s.ContaParceiro ?? "",
                    s.Observacao ?? "",
                    RotulosExportacao.StatusGestor(s.StatusGestor),
                    s.DataAprovacaoGestor,
                    s.ObservacaoGestor ?? "",
                    RotulosExportacao.StatusFinanceiro(s.StatusFinanceiro),
                    s.DataAprovacaoFinanceiro,
                    s.ObservacaoSetor ?? "",
                    RotulosExportacao.StatusContabil(s.StatusContabilidade),
                    s.DataAprovacaoContabil);
            }).ToList();
        }

        public async Task<IReadOnlyList<RddvExportacao>> ListarRddvsAsync(FiltroExportacaoDto filtro, CancellationToken ct)
        {
            if (!IncluiRddvs(filtro)) return [];

            var linhas = await (
                from r in ConsultarRddvs(filtro)
                from dep in db.Departamentos.AsNoTracking().Where(x => x.IdDepartamento == r.IdDepartamento).DefaultIfEmpty()
                from sol in db.Usuarios.AsNoTracking().Where(x => x.IdUsuario == r.IdUsuario).DefaultIfEmpty()
                from atr in db.Usuarios.AsNoTracking().Where(x => x.IdUsuario == r.IdUsuarioAtribuido).DefaultIfEmpty()
                from ges in db.Usuarios.AsNoTracking().Where(x => x.IdUsuario == r.IdGestor).DefaultIfEmpty()
                from tv in db.TipoViagems.AsNoTracking().Where(x => x.IdTipoViagem == r.TipoViagem).DefaultIfEmpty()
                from tot in db.vDespesasRddv.AsNoTracking().Where(x => x.IdRelatorio == r.IdRelatorio).DefaultIfEmpty()
                orderby r.IdRelatorio
                select new
                {
                    Titulo = r,
                    Departamento = dep == null ? null : dep.Nome,
                    Solicitante = sol == null ? null : sol.Nome,
                    Atribuido = atr == null ? null : atr.Nome,
                    Gestor = ges == null ? null : ges.Nome,
                    TipoViagem = tv == null ? null : tv.Nome,
                    TotalDespesas = tot == null ? (decimal?)null : tot.Valor
                }).ToListAsync(ct);

            return linhas.Select(l =>
            {
                var r = l.Titulo;
                return new RddvExportacao(
                    r.IdRelatorio,
                    CodigoTitulo.Formatar(TipoTituloExportacao.Rddv, r.IdRelatorio),
                    RotulosExportacao.TipoRelatorio(r.TipoRelatorio),
                    r.NomeFuncionario ?? "",
                    r.CPF ?? "",
                    l.Departamento ?? RotulosExportacao.Desconhecido(r.IdDepartamento),
                    l.Solicitante ?? "",
                    l.Atribuido,
                    l.Gestor,
                    r.Finalidade ?? "",
                    l.TipoViagem ?? RotulosExportacao.Desconhecido(r.TipoViagem),
                    r.LocalViagem ?? "",
                    r.DataInicio,
                    r.DataFim,
                    r.DataCadastro,
                    r.DataVencimento,
                    r.Moeda ?? "",
                    r.DiariaViagem,
                    r.Diarias,
                    r.ValorDiaria,
                    r.ValorKm,
                    r.Adiantamento,
                    r.ValorAdiantamento,
                    r.SaldoCartaoVtm,
                    l.TotalDespesas,
                    r.CentroCusto ?? "",
                    r.Projeto ?? "",
                    r.PCA ?? "",
                    r.Banco ?? "",
                    r.Agencia ?? "",
                    r.Conta ?? "",
                    r.Observacao ?? "",
                    RotulosExportacao.StatusGestor(r.StatusGestor),
                    r.DataAprovacaoGestor,
                    r.ObservacaoGestor ?? "",
                    RotulosExportacao.StatusFinanceiro(r.StatusFinanceiro),
                    r.DataAprovacaoFinanceiro,
                    r.ObservacaoSetor ?? "",
                    RotulosExportacao.StatusContabil(r.StatusContabilidade),
                    r.DataAprovacaoContabil);
            }).ToList();
        }

        public async Task<IReadOnlyList<DespesaRddvExportacao>> ListarDespesasAsync(IReadOnlyCollection<int> idsRddv, CancellationToken ct)
        {
            var linhas = new List<DespesaRddv>();

            foreach (var lote in idsRddv.Distinct().Chunk(TamanhoLote))
            {
                var ids = lote.ToList();

                linhas.AddRange(await db.DespesaRddv
                    .AsNoTracking()
                    .Where(d => ids.Contains(d.IdRelatorio))
                    .ToListAsync(ct));
            }

            return linhas
                .OrderBy(d => d.IdRelatorio)
                .ThenBy(d => d.DataDespesa)
                .ThenBy(d => d.IdDespesaRddv)
                .Select(d => new DespesaRddvExportacao(
                    d.IdDespesaRddv,
                    d.IdRelatorio,
                    CodigoTitulo.Formatar(TipoTituloExportacao.Rddv, d.IdRelatorio),
                    d.DataDespesa,
                    RotulosExportacao.TipoDespesa(d.TipoDespesa),
                    d.Moeda,
                    d.Quantidade,
                    d.Valor))
                .ToList();
        }

        public async Task<IReadOnlyList<AnexoExportacao>> ListarAnexosAsync(
            TipoTituloExportacao tipo, IReadOnlyCollection<int> idsTitulo, CancellationToken ct)
        {
            ExigirTipoUnico(tipo);

            var linhas = new List<LinhaAnexo>();

            foreach (var lote in idsTitulo.Distinct().Chunk(TamanhoLote))
            {
                var ids = lote.ToList();

                // Só metadados: a coluna Arquivo (base64, pode ter vários MB) nunca entra na projeção.
                if (tipo == TipoTituloExportacao.Solicitacao)
                {
                    linhas.AddRange(await (
                        from d in db.DocumentoSolicitacaos.AsNoTracking().Where(x => ids.Contains(x.IdSolicitacao))
                        from t in db.TipoDocumentos.AsNoTracking().Where(x => x.IdTipoDocumento == d.IdTipoDocumento).DefaultIfEmpty()
                        select new LinhaAnexo(
                            d.IdDocumentoSolicitacao, d.IdSolicitacao, d.IdTipoDocumento,
                            t == null ? null : t.Nome, d.NomeArquivo, d.Indice, d.DataCriacao)
                        ).ToListAsync(ct));
                }
                else
                {
                    linhas.AddRange(await (
                        from d in db.DocumentoRddv.AsNoTracking().Where(x => ids.Contains(x.IdRelatorio))
                        from t in db.TipoDocumentos.AsNoTracking().Where(x => x.IdTipoDocumento == d.IdTipoDocumento).DefaultIfEmpty()
                        select new LinhaAnexo(
                            d.IdDocumentoRddv, d.IdRelatorio, d.IdTipoDocumento,
                            t == null ? null : t.Nome, d.NomeArquivo, d.Indice, d.DataCriacao)
                        ).ToListAsync(ct));
                }
            }

            return linhas
                .OrderBy(l => l.IdTitulo)
                .ThenBy(l => l.Indice)
                .ThenBy(l => l.IdAnexo)
                .Select(l => new AnexoExportacao(
                    l.IdAnexo,
                    tipo,
                    l.IdTitulo,
                    CodigoTitulo.Formatar(tipo, l.IdTitulo),
                    l.IdTipoDocumento,
                    string.IsNullOrWhiteSpace(l.TipoDocumentoNome) ? RotulosExportacao.TipoDocumentoPadrao : l.TipoDocumentoNome.Trim(),
                    l.NomeArquivo ?? "",
                    l.DataCriacao))
                .ToList();
        }

        public async Task<string?> LerConteudoBase64Async(TipoTituloExportacao tipo, int idAnexo, CancellationToken ct)
        {
            ExigirTipoUnico(tipo);

            // Um anexo por chamada e só a coluna Arquivo.
            if (tipo == TipoTituloExportacao.Solicitacao)
            {
                return await db.DocumentoSolicitacaos
                    .AsNoTracking()
                    .Where(d => d.IdDocumentoSolicitacao == idAnexo)
                    .Select(d => d.Arquivo)
                    .FirstOrDefaultAsync(ct);
            }

            return await db.DocumentoRddv
                .AsNoTracking()
                .Where(d => d.IdDocumentoRddv == idAnexo)
                .Select(d => d.Arquivo)
                .FirstOrDefaultAsync(ct);
        }

        // ---------------------------------------------------------------------------------------------------------
        // Escopo e filtros
        // ---------------------------------------------------------------------------------------------------------

        /// <summary>
        /// Um filtro que só vale para Solicitação (documento, tipo de solicitação, tipo e forma de pagamento) exclui os
        /// RDDVs quando informado; um filtro que só vale para RDDV (tipo de relatório) exclui as solicitações.
        /// </summary>
        private static bool IncluiSolicitacoes(FiltroExportacaoDto f) =>
            f.TipoTitulo is TipoTituloExportacao.Ambos or TipoTituloExportacao.Solicitacao
            && !Informado(f.TipoRelatorio);

        private static bool IncluiRddvs(FiltroExportacaoDto f) =>
            f.TipoTitulo is TipoTituloExportacao.Ambos or TipoTituloExportacao.Rddv
            && TextoExato(f.NumeroDocumento) is null
            && !Informado(f.TipoSolicitacao)
            && !Informado(f.TipoPagamento)
            && !Informado(f.FormaPagamento);

        private IQueryable<Solicitacao> ConsultarSolicitacoes(FiltroExportacaoDto f)
        {
            var q = db.Solicitacaos.AsNoTracking()
                .Where(s => !s.Rascunho && !s.Cancelado && s.AprovadoGestor == true);

            if (Informado(f.Codigo))
            {
                var codigo = f.Codigo!.Value;
                q = q.Where(s => s.IdSolicitacao == codigo);
            }

            if (TextoExato(f.NumeroDocumento) is { } numeroDocumento)
                q = q.Where(s => s.NumeroDocumento == numeroDocumento);

            if (f.DataSolicitacaoInicio is { } solicitacaoInicio)
            {
                var inicio = solicitacaoInicio.Date;
                q = q.Where(s => s.DataSolicitacao >= inicio);
            }

            if (f.DataSolicitacaoFim is { } solicitacaoFim)
            {
                var limite = LimiteExclusivo(solicitacaoFim);
                q = q.Where(s => s.DataSolicitacao < limite);
            }

            if (f.DataVencimentoInicio is { } vencimentoInicio)
            {
                var inicio = vencimentoInicio.Date;
                q = q.Where(s => s.DataVencimento >= inicio);
            }

            if (f.DataVencimentoFim is { } vencimentoFim)
            {
                var limite = LimiteExclusivo(vencimentoFim);
                q = q.Where(s => s.DataVencimento < limite);
            }

            if (f.DataAprovacaoFinanceiroInicio is { } aprovacaoInicio)
            {
                var inicio = aprovacaoInicio.Date;
                q = q.Where(s => s.DataAprovacaoFinanceiro >= inicio);
            }

            if (f.DataAprovacaoFinanceiroFim is { } aprovacaoFim)
            {
                var limite = LimiteExclusivo(aprovacaoFim);
                q = q.Where(s => s.DataAprovacaoFinanceiro < limite);
            }

            if (Informado(f.IdSolicitante))
            {
                var id = f.IdSolicitante!.Value;
                q = q.Where(s => s.IdUsuario == id);
            }

            if (Informado(f.IdAtribuido))
            {
                var id = f.IdAtribuido!.Value;
                q = q.Where(s => s.IdUsuarioAtribuido == id);
            }

            if (Informado(f.IdDepartamento))
            {
                var id = f.IdDepartamento!.Value;
                q = q.Where(s => s.IdDepartamento == id);
            }

            // Status 0, 1 e nulo são o mesmo rótulo ("Aguardando"): o filtro 1 pega os três.
            if (Informado(f.StatusGestor))
            {
                var status = f.StatusGestor!.Value;
                q = status == 1
                    ? q.Where(s => (s.StatusGestor ?? 1) <= 1)
                    : q.Where(s => s.StatusGestor == status);
            }

            if (Informado(f.StatusFinanceiro))
            {
                var status = f.StatusFinanceiro!.Value;
                q = status == 1
                    ? q.Where(s => (s.StatusFinanceiro ?? 1) <= 1)
                    : q.Where(s => s.StatusFinanceiro == status);
            }

            if (Informado(f.StatusContabil))
            {
                var status = f.StatusContabil!.Value;
                q = status == 1
                    ? q.Where(s => (s.StatusContabilidade ?? 1) <= 1)
                    : q.Where(s => s.StatusContabilidade == status);
            }

            if (TextoBusca(f.NomeParceiro) is { } nome)
                q = q.Where(s => s.NomeParceiro.ToLower().Contains(nome));

            if (DocumentoBusca(f.DocumentoParceiro) is { } documento)
            {
                q = q.Where(s => s.NumDocParceiro
                    .Replace(".", "").Replace("-", "").Replace("/", "").Replace(" ", "")
                    .ToLower().Contains(documento));
            }

            if (Informado(f.TipoSolicitacao))
            {
                var tipo = f.TipoSolicitacao!.Value;
                q = q.Where(s => s.TipoSolicitacao == tipo);
            }

            if (Informado(f.TipoPagamento))
            {
                var tipo = f.TipoPagamento!.Value;
                q = q.Where(s => s.TipoPagamento == tipo);
            }

            if (Informado(f.FormaPagamento))
            {
                var forma = f.FormaPagamento!.Value;
                q = q.Where(s => s.FormaPagamento == forma);
            }

            return q;
        }

        private IQueryable<Rddv> ConsultarRddvs(FiltroExportacaoDto f)
        {
            var q = db.Rddv.AsNoTracking()
                .Where(r => !r.Rascunho && !r.Cancelado && r.AprovadoGestor == true);

            if (Informado(f.Codigo))
            {
                var codigo = f.Codigo!.Value;
                q = q.Where(r => r.IdRelatorio == codigo);
            }

            // Para o RDDV, a "data da solicitação" é a data de cadastro.
            if (f.DataSolicitacaoInicio is { } solicitacaoInicio)
            {
                var inicio = solicitacaoInicio.Date;
                q = q.Where(r => r.DataCadastro >= inicio);
            }

            if (f.DataSolicitacaoFim is { } solicitacaoFim)
            {
                var limite = LimiteExclusivo(solicitacaoFim);
                q = q.Where(r => r.DataCadastro < limite);
            }

            if (f.DataVencimentoInicio is { } vencimentoInicio)
            {
                var inicio = vencimentoInicio.Date;
                q = q.Where(r => r.DataVencimento >= inicio);
            }

            if (f.DataVencimentoFim is { } vencimentoFim)
            {
                var limite = LimiteExclusivo(vencimentoFim);
                q = q.Where(r => r.DataVencimento < limite);
            }

            if (f.DataAprovacaoFinanceiroInicio is { } aprovacaoInicio)
            {
                var inicio = aprovacaoInicio.Date;
                q = q.Where(r => r.DataAprovacaoFinanceiro >= inicio);
            }

            if (f.DataAprovacaoFinanceiroFim is { } aprovacaoFim)
            {
                var limite = LimiteExclusivo(aprovacaoFim);
                q = q.Where(r => r.DataAprovacaoFinanceiro < limite);
            }

            if (Informado(f.IdSolicitante))
            {
                var id = f.IdSolicitante!.Value;
                q = q.Where(r => r.IdUsuario == id);
            }

            if (Informado(f.IdAtribuido))
            {
                var id = f.IdAtribuido!.Value;
                q = q.Where(r => r.IdUsuarioAtribuido == id);
            }

            if (Informado(f.IdDepartamento))
            {
                var id = f.IdDepartamento!.Value;
                q = q.Where(r => r.IdDepartamento == id);
            }

            if (Informado(f.StatusGestor))
            {
                var status = f.StatusGestor!.Value;
                q = status == 1
                    ? q.Where(r => (r.StatusGestor ?? 1) <= 1)
                    : q.Where(r => r.StatusGestor == status);
            }

            if (Informado(f.StatusFinanceiro))
            {
                var status = f.StatusFinanceiro!.Value;
                q = status == 1
                    ? q.Where(r => (r.StatusFinanceiro ?? 1) <= 1)
                    : q.Where(r => r.StatusFinanceiro == status);
            }

            if (Informado(f.StatusContabil))
            {
                var status = f.StatusContabil!.Value;
                q = status == 1
                    ? q.Where(r => (r.StatusContabilidade ?? 1) <= 1)
                    : q.Where(r => r.StatusContabilidade == status);
            }

            // Para o RDDV, o "parceiro" é o funcionário e o documento é o CPF.
            if (TextoBusca(f.NomeParceiro) is { } nome)
                q = q.Where(r => r.NomeFuncionario.ToLower().Contains(nome));

            if (DocumentoBusca(f.DocumentoParceiro) is { } documento)
            {
                q = q.Where(r => r.CPF
                    .Replace(".", "").Replace("-", "").Replace("/", "").Replace(" ", "")
                    .ToLower().Contains(documento));
            }

            if (Informado(f.TipoRelatorio))
            {
                var tipo = f.TipoRelatorio!.Value;
                q = q.Where(r => r.TipoRelatorio == tipo);
            }

            return q;
        }

        /// <summary>Limite exclusivo de um "fim" inclusivo pelo dia inteiro: o dia seguinte à meia-noite do dia informado.</summary>
        private static DateTime LimiteExclusivo(DateTime fim) =>
            fim.Date == DateTime.MaxValue.Date ? DateTime.MaxValue : fim.Date.AddDays(1);

        /// <summary>Inteiro nulo ou 0 não filtra.</summary>
        private static bool Informado(int? valor) => valor is not null and not 0;

        /// <summary>Texto para igualdade exata; nulo, vazio ou só espaços não filtra.</summary>
        private static string? TextoExato(string? valor)
        {
            var texto = valor?.Trim();
            return string.IsNullOrEmpty(texto) ? null : texto;
        }

        /// <summary>Termo para busca "contém" sem diferenciar caixa: minúsculas, igual ao lado do banco (<c>ToLower</c>).</summary>
        private static string? TextoBusca(string? valor)
        {
            var texto = valor?.Trim().ToLower();
            return string.IsNullOrEmpty(texto) ? null : texto;
        }

        /// <summary>
        /// Termo de documento sem <c>.</c>, <c>-</c>, <c>/</c> e espaços, em minúsculas. No banco a coluna passa pelo mesmo
        /// <c>Replace</c> encadeado, de modo que "123.456.789-00" encontra "12345678900" e vice-versa.
        /// </summary>
        private static string? DocumentoBusca(string? valor)
        {
            if (valor is null) return null;

            var texto = valor.Replace(".", "").Replace("-", "").Replace("/", "").Replace(" ", "").ToLower();
            return texto.Length == 0 ? null : texto;
        }

        private static void ExigirTipoUnico(TipoTituloExportacao tipo)
        {
            if (tipo is not (TipoTituloExportacao.Solicitacao or TipoTituloExportacao.Rddv))
                throw new ArgumentException("O tipo de título deve ser Solicitacao ou Rddv.", nameof(tipo));
        }

        private sealed record LinhaAnexo(
            int IdAnexo, int IdTitulo, int IdTipoDocumento, string? TipoDocumentoNome,
            string? NomeArquivo, int Indice, DateTime DataCriacao);
    }
}
