using Ares.PagueAres.Infrastructure;
using Ares.PagueAres.Infrastructure.Models;
using Microsoft.EntityFrameworkCore;

namespace Ares.PagueAres.Tests.Integration.Infrastructure
{
    /// <summary>
    /// Contexto dos testes de integração. O EF InMemory não tem views, então este contexto dá à
    /// <c>vListagemSolicitacoes</c> (entidade <see cref="ViewListagemSolicitacoes"/>) a mesma montagem da view real
    /// (migration <c>InitialCreate</c>): solicitações e RDDVs juntos, com <c>IdTipo</c> 0 na solicitação e o tipo de
    /// relatório no RDDV, e <c>#</c> no número do documento, no parceiro e na descrição do RDDV.
    /// <para>Duas diferenças, sem efeito nos testes: o valor do RDDV vem de um LEFT JOIN com a
    /// <c>vDespesasRddv</c>, que continua vazia no InMemory (no SQL Server ela tem uma linha por RDDV, e o INNER JOIN da
    /// view real dá o mesmo conjunto), e o id do usuário atribuído vem da própria tabela do título.</para>
    /// </summary>
    public sealed class PagueAresContextTeste(DbContextOptions<PagueAresContext> options) : PagueAresContext(options)
    {
        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            base.OnModelCreating(modelBuilder);

            modelBuilder.Entity<ViewListagemSolicitacoes>().ToInMemoryQuery(() =>
                (from s in Solicitacaos
                 join us in Usuarios on s.IdUsuario equals us.IdUsuario
                 from ua in Usuarios.Where(u => u.IdUsuario == s.IdUsuarioAtribuido).DefaultIfEmpty()
                 select new ViewListagemSolicitacoes
                 {
                     Codigo = s.IdSolicitacao,
                     IdTipo = 0,
                     Tipo = "Pagamento",
                     DataSolicitacao = s.DataSolicitacao,
                     DataVencimento = s.DataVencimento,
                     NumeroDocumento = s.NumeroDocumento,
                     NomeSolicitante = us.Nome,
                     IdSolicitante = us.IdUsuario,
                     UsuarioAtribuido = ua.Nome,
                     IdUsuarioAtribuido = s.IdUsuarioAtribuido,
                     NomeParceiro = s.NomeParceiro,
                     Valor = s.Valor,
                     Descricao = s.Descricao,
                     ObservacaoGestor = s.ObservacaoGestor,
                     IdStatusGestor = s.StatusGestor,
                     IdStatusFinanceiro = s.StatusFinanceiro,
                     IdStatusContabilidade = s.StatusContabilidade,
                     StatusGestor = s.StatusGestor == 2 ? "Autorizado" : s.StatusGestor == 3 ? "Negado" : "Aguardando Autorização",
                     StatusFinanceiro = s.StatusFinanceiro == 2 ? "Autorizado" : s.StatusFinanceiro == 3 ? "Negado" : "Aguardando Autorização",
                     StatusContabilidade = s.StatusContabilidade == 2 ? "Lançado" : s.StatusContabilidade == 3 ? "Negado" : "Aguardando Lançamento",
                     IdGestor = s.IdGestor,
                     AprovadoGestor = s.AprovadoGestor,
                     AprovadoSetor = s.AprovadoSetor,
                     Rascunho = s.Rascunho,
                     Cancelado = s.Cancelado
                 })
                .Concat(
                    from r in Rddv
                    join us in Usuarios on r.IdUsuario equals us.IdUsuario
                    from ua in Usuarios.Where(u => u.IdUsuario == r.IdUsuarioAtribuido).DefaultIfEmpty()
                    from d in vDespesasRddv.Where(k => k.IdRelatorio == r.IdRelatorio).DefaultIfEmpty()
                    select new ViewListagemSolicitacoes
                    {
                        Codigo = r.IdRelatorio,
                        IdTipo = r.TipoRelatorio,
                        Tipo = r.TipoRelatorio == 1 ? "RDDV - Adiantamento"
                            : r.TipoRelatorio == 2 ? "RDDV - Prestação de Contas"
                            : r.TipoRelatorio == 3 ? "RDDV - Reembolso"
                            : "RDDV - ",
                        DataSolicitacao = r.DataCadastro,
                        DataVencimento = r.DataVencimento,
                        NumeroDocumento = "#",
                        NomeSolicitante = us.Nome,
                        IdSolicitante = us.IdUsuario,
                        UsuarioAtribuido = ua.Nome,
                        IdUsuarioAtribuido = r.IdUsuarioAtribuido,
                        NomeParceiro = "#",
                        Valor = (decimal?)d.Valor,
                        Descricao = "#",
                        ObservacaoGestor = r.ObservacaoGestor,
                        IdStatusGestor = r.StatusGestor,
                        IdStatusFinanceiro = r.StatusFinanceiro,
                        IdStatusContabilidade = r.StatusContabilidade,
                        StatusGestor = r.StatusGestor == 2 ? "Autorizado" : r.StatusGestor == 3 ? "Negado" : "Aguardando Autorização",
                        StatusFinanceiro = r.StatusFinanceiro == 2 ? "Autorizado" : r.StatusFinanceiro == 3 ? "Negado" : "Aguardando Autorização",
                        StatusContabilidade = r.StatusContabilidade == 2 ? "Lançado" : r.StatusContabilidade == 3 ? "Negado" : "Aguardando Lançamento",
                        IdGestor = r.IdGestor,
                        AprovadoGestor = r.AprovadoGestor,
                        AprovadoSetor = r.AprovadoSetor,
                        Rascunho = r.Rascunho,
                        Cancelado = r.Cancelado
                    }));
        }
    }
}
