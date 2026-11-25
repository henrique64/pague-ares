using Ares.PagueAres.Application.Exportacao;
using Ares.PagueAres.Domain.Dtos.Exportacao;
using Ares.PagueAres.Domain.Enums;
using Ares.PagueAres.Infrastructure;
using Ares.PagueAres.Infrastructure.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Logging;
using System.Data;
using System.Data.Common;
using System.Text.RegularExpressions;

namespace Ares.PagueAres.Tests.Integration.Exportacao
{
    /// <summary>
    /// Testes da fonte de dados da exportação com EF InMemory e dados fictícios. A view <c>vDespesasRddv</c> não existe no
    /// InMemory, então o contexto de teste lhe dá a mesma conta da view real. A tradução para SQL Server é conferida à
    /// parte, sem banco, por um interceptador que captura o SQL gerado e suprime a execução.
    /// </summary>
    public class FonteExportacaoEfTests
    {
        private static readonly CancellationToken Ct = CancellationToken.None;

        // -----------------------------------------------------------------------------------------------------
        // Infraestrutura de teste
        // -----------------------------------------------------------------------------------------------------

        /// <summary>Contexto que reproduz a view <c>vDespesasRddv</c> (o InMemory não tem views).</summary>
        private sealed class ContextoTeste(DbContextOptions<PagueAresContext> options) : PagueAresContext(options)
        {
            protected override void OnModelCreating(ModelBuilder modelBuilder)
            {
                base.OnModelCreating(modelBuilder);

                modelBuilder.Entity<vDespesasRddv>().ToInMemoryQuery(() =>
                    Rddv.Select(r => new vDespesasRddv
                    {
                        IdRelatorio = r.IdRelatorio,
                        Valor = ((r.DiariaViagem == true ? (r.Diarias ?? 0m) : 0m) * r.ValorDiaria)
                                + DespesaRddv.Where(d => d.IdRelatorio == r.IdRelatorio).Sum(d => d.Valor)
                                - (r.ValorAdiantamento + r.SaldoCartaoVtm)
                    }));
            }
        }

        private static (ContextoTeste Ctx, FonteExportacaoEf Fonte) Novo(Action<PagueAresContext>? semear = null, bool padrao = true)
        {
            var options = new DbContextOptionsBuilder<PagueAresContext>()
                .UseInMemoryDatabase("FonteExportacao_" + Guid.NewGuid())
                .Options;

            var ctx = new ContextoTeste(options);

            ctx.Usuarios.AddRange(
                NovoUsuario(1, "Ana Solicitante"), NovoUsuario(2, "Bruno Atribuido"),
                NovoUsuario(3, "Carla Gestora"), NovoUsuario(4, "Diego Outro"));
            ctx.Departamentos.AddRange(
                new Departamento { IdDepartamento = 1, Nome = "Tecnologia" },
                new Departamento { IdDepartamento = 2, Nome = "Operações" });
            ctx.TipoDocumentos.AddRange(
                new TipoDocumento { IdTipoDocumento = 1, Nome = "Nota Fiscal" },
                new TipoDocumento { IdTipoDocumento = 2, Nome = "Comprovante" },
                new TipoDocumento { IdTipoDocumento = 3, Nome = null });
            ctx.TipoViagems.AddRange(
                new TipoViagem { IdTipoViagem = 1, Nome = "Nacional", ValorDiaria = 100m },
                new TipoViagem { IdTipoViagem = 2, Nome = "Internacional", ValorDiaria = 300m });

            if (padrao) SemearPadrao(ctx);
            semear?.Invoke(ctx);

            ctx.SaveChanges();
            ctx.ChangeTracker.Clear();

            return (ctx, new FonteExportacaoEf(ctx));
        }

        private static Usuario NovoUsuario(int id, string nome) => new()
        {
            IdUsuario = id,
            Nome = nome,
            Email = $"usuario{id}@exemplo.invalid",
            Senha = "hash-ficticio",
            IdDepartamento = 1,
            Ativo = true,
            DataCadastro = new DateTime(2026, 1, 1)
        };

        private static Solicitacao Sol(int id, Action<Solicitacao>? ajustar = null)
        {
            var s = new Solicitacao
            {
                IdSolicitacao = id,
                IdUsuario = 1,
                IdDepartamento = 1,
                DataSolicitacao = new DateTime(2026, 3, 10, 9, 0, 0),
                TipoSolicitacao = 2,
                TipoPagamento = 1,
                FormaPagamento = 1,
                NumeroDocumento = "NF-" + id,
                Descricao = "Compra fictícia",
                Valor = 100m,
                CentroCusto = "CC-01",
                Projeto = "PRJ-01",
                Pca = "PCA-01",
                NumDocParceiro = "12345678000190",
                CodigoParceiro = "P001",
                NomeParceiro = "Fornecedor Alfa Ltda",
                BancoParceiro = "Banco Fictício",
                AgenciaParceiro = "0001",
                ContaParceiro = "12345-6",
                Observacao = "",
                TipoAutorizacao = 1,
                ObservacaoGestor = "",
                ObservacaoSetor = "",
                IdGestor = 3,
                AprovadoGestor = true,
                Rascunho = false,
                Cancelado = false,
                StatusGestor = 2,
                StatusFinanceiro = 1,
                StatusContabilidade = 1
            };

            ajustar?.Invoke(s);
            return s;
        }

        private static Rddv Rd(int id, Action<Rddv>? ajustar = null)
        {
            var r = new Rddv
            {
                IdRelatorio = id,
                IdUsuario = 1,
                IdDepartamento = 1,
                NomeFuncionario = "Maria Souza Teste",
                CPF = "111.222.333-96",
                Finalidade = "Visita fictícia",
                TipoRelatorio = 1,
                Moeda = "BRL",
                TipoViagem = 1,
                DataCadastro = new DateTime(2026, 3, 10, 9, 0, 0),
                LocalViagem = "Cidade Exemplo",
                CentroCusto = "CC-01",
                Projeto = "PRJ-01",
                PCA = "PCA-01",
                Observacao = "",
                TipoAutorizacao = 1,
                IdGestor = 3,
                AprovadoGestor = true,
                ObservacaoGestor = "",
                ObservacaoSetor = "",
                StatusGestor = 2,
                StatusFinanceiro = 1,
                StatusContabilidade = 1,
                Banco = "Banco Fictício",
                Agencia = "0001",
                Conta = "12345-6",
                Rascunho = false,
                Cancelado = false
            };

            ajustar?.Invoke(r);
            return r;
        }

        /// <summary>
        /// Três solicitações (101-103) e três RDDVs (201-203) que diferem em tudo o que os filtros olham.
        /// </summary>
        private static void SemearPadrao(PagueAresContext ctx)
        {
            ctx.Solicitacaos.AddRange(
                Sol(101, s =>
                {
                    s.DataSolicitacao = new DateTime(2026, 3, 10, 18, 30, 0);
                    s.DataVencimento = new DateTime(2026, 4, 10);
                    s.IdUsuario = 1; s.IdUsuarioAtribuido = 2; s.IdDepartamento = 1;
                    s.StatusGestor = 2; s.StatusFinanceiro = 2; s.StatusContabilidade = 1;
                    s.DataAprovacaoFinanceiro = new DateTime(2026, 3, 12, 15, 0, 0);
                    s.NomeParceiro = "Fornecedor Alfa Ltda"; s.NumDocParceiro = "12.345.678/0001-90";
                    s.TipoSolicitacao = 2; s.TipoPagamento = 1; s.FormaPagamento = 1;
                }),
                Sol(102, s =>
                {
                    s.DataSolicitacao = new DateTime(2026, 3, 11, 0, 0, 0);
                    s.DataVencimento = new DateTime(2026, 5, 20);
                    s.IdUsuario = 4; s.IdUsuarioAtribuido = null; s.IdDepartamento = 2;
                    s.StatusGestor = 3; s.StatusFinanceiro = 3; s.StatusContabilidade = 2;
                    s.DataAprovacaoFinanceiro = new DateTime(2026, 3, 20, 9, 0, 0);
                    s.NomeParceiro = "Beta Serviços SA"; s.NumDocParceiro = "98765432000100";
                    s.TipoSolicitacao = 1; s.TipoPagamento = 2; s.FormaPagamento = 2;
                }),
                Sol(103, s =>
                {
                    s.DataSolicitacao = new DateTime(2026, 4, 1, 10, 0, 0);
                    s.DataVencimento = null;
                    s.NumeroDocumento = null;
                    s.IdUsuario = 1; s.IdUsuarioAtribuido = 2; s.IdDepartamento = 2;
                    s.StatusGestor = 0; s.StatusFinanceiro = null; s.StatusContabilidade = 0;
                    s.DataAprovacaoFinanceiro = null;
                    s.NomeParceiro = "Gama Comércio"; s.NumDocParceiro = "11122233344";
                    s.TipoSolicitacao = 3; s.TipoPagamento = 1; s.FormaPagamento = 2;
                }));

            ctx.Rddv.AddRange(
                Rd(201, r =>
                {
                    r.DataCadastro = new DateTime(2026, 3, 10, 18, 0, 0);
                    r.DataVencimento = new DateTime(2026, 4, 10);
                    r.IdUsuario = 1; r.IdUsuarioAtribuido = 2; r.IdDepartamento = 1;
                    r.NomeFuncionario = "Maria Souza Teste"; r.CPF = "111.222.333-96";
                    r.TipoRelatorio = 1; r.TipoViagem = 1;
                    r.StatusGestor = 2; r.StatusFinanceiro = 2; r.StatusContabilidade = 1;
                    r.DataAprovacaoFinanceiro = new DateTime(2026, 3, 12, 15, 0, 0);
                    r.DiariaViagem = true; r.Diarias = 2m; r.ValorDiaria = 100m;
                    r.ValorAdiantamento = 50m; r.SaldoCartaoVtm = 10m;
                }),
                Rd(202, r =>
                {
                    r.DataCadastro = new DateTime(2026, 3, 11, 0, 0, 0);
                    r.DataVencimento = null;
                    r.IdUsuario = 4; r.IdUsuarioAtribuido = null; r.IdDepartamento = 2;
                    r.NomeFuncionario = "JOÃO PEREIRA TESTE"; r.CPF = "55566677788";
                    r.TipoRelatorio = 2; r.TipoViagem = 2;
                    r.StatusGestor = 3; r.StatusFinanceiro = 3; r.StatusContabilidade = 2;
                    r.DataAprovacaoFinanceiro = new DateTime(2026, 3, 20, 9, 0, 0);
                }),
                Rd(203, r =>
                {
                    r.DataCadastro = new DateTime(2026, 4, 1, 10, 0, 0);
                    r.DataVencimento = new DateTime(2026, 5, 20);
                    r.IdUsuario = 1; r.IdUsuarioAtribuido = 2; r.IdDepartamento = 2;
                    r.NomeFuncionario = "Carlos Lima Exemplo"; r.CPF = "99988877766";
                    r.TipoRelatorio = 3; r.TipoViagem = 1;
                    r.StatusGestor = 0; r.StatusFinanceiro = null; r.StatusContabilidade = 0;
                    r.DataAprovacaoFinanceiro = null;
                }));

            ctx.DespesaRddv.AddRange(
                new DespesaRddv { IdDespesaRddv = 1, IdRelatorio = 201, DataDespesa = new DateTime(2026, 3, 8), TipoDespesa = 1, Moeda = "BRL", Quantidade = 2m, Valor = 30m },
                new DespesaRddv { IdDespesaRddv = 2, IdRelatorio = 201, DataDespesa = new DateTime(2026, 3, 7), TipoDespesa = 4, Moeda = null, Quantidade = null, Valor = 20m });
        }

        private static async Task<(int[] Sol, int[] Rddv, int Total)> Consultar(FonteExportacaoEf fonte, FiltroExportacaoDto filtro)
        {
            var sol = (await fonte.ListarSolicitacoesAsync(filtro, Ct)).Select(s => s.Id).ToArray();
            var rddv = (await fonte.ListarRddvsAsync(filtro, Ct)).Select(r => r.Id).ToArray();
            var total = await fonte.ContarTitulosAsync(filtro, Ct);
            return (sol, rddv, total);
        }

        // -----------------------------------------------------------------------------------------------------
        // Escopo: rascunho, cancelado e aprovação do gestor
        // -----------------------------------------------------------------------------------------------------

        [Fact]
        public async Task Escopo_nunca_traz_solicitacao_em_rascunho_cancelada_ou_sem_aprovacao_do_gestor()
        {
            var (ctx, fonte) = Novo(c => c.Solicitacaos.AddRange(
                Sol(1),
                Sol(2, s => s.Rascunho = true),
                Sol(3, s => s.Cancelado = true),
                Sol(4, s => s.AprovadoGestor = false),
                Sol(5, s => s.AprovadoGestor = null)), padrao: false);
            using var _ = ctx;

            var resultado = await Consultar(fonte, new FiltroExportacaoDto());

            resultado.Sol.Should().Equal(1);
            resultado.Total.Should().Be(1);
        }

        [Fact]
        public async Task Escopo_nunca_traz_rddv_em_rascunho_cancelado_ou_sem_aprovacao_do_gestor()
        {
            var (ctx, fonte) = Novo(c => c.Rddv.AddRange(
                Rd(1),
                Rd(2, r => r.Rascunho = true),
                Rd(3, r => r.Cancelado = true),
                Rd(4, r => r.AprovadoGestor = false),
                Rd(5, r => r.AprovadoGestor = null)), padrao: false);
            using var _ = ctx;

            var resultado = await Consultar(fonte, new FiltroExportacaoDto());

            resultado.Rddv.Should().Equal(1);
            resultado.Total.Should().Be(1);
        }

        [Fact]
        public async Task Escopo_nao_depende_de_o_titulo_estar_atribuido_a_alguem()
        {
            var (ctx, fonte) = Novo();
            using var _ = ctx;

            var resultado = await Consultar(fonte, new FiltroExportacaoDto());

            resultado.Sol.Should().Equal(101, 102, 103);
            resultado.Rddv.Should().Equal(201, 202, 203);
            resultado.Total.Should().Be(6);
        }

        [Fact]
        public async Task Listagens_saem_ordenadas_pelo_id_e_sem_rastreamento()
        {
            var (ctx, fonte) = Novo(c =>
            {
                c.Solicitacaos.AddRange(Sol(30), Sol(10), Sol(20));
                c.Rddv.AddRange(Rd(9), Rd(7), Rd(8));
            }, padrao: false);
            using var _ = ctx;

            var resultado = await Consultar(fonte, new FiltroExportacaoDto());

            resultado.Sol.Should().Equal(10, 20, 30);
            resultado.Rddv.Should().Equal(7, 8, 9);
            ctx.ChangeTracker.Entries().Should().BeEmpty("a exportação lê tudo com AsNoTracking");
        }

        // -----------------------------------------------------------------------------------------------------
        // Filtros: um ou mais casos por campo de FiltroExportacaoDto
        // -----------------------------------------------------------------------------------------------------

        [Fact]
        public async Task Filtro_vazio_com_zeros_e_textos_vazios_nao_filtra()
        {
            var (ctx, fonte) = Novo();
            using var _ = ctx;

            var filtro = new FiltroExportacaoDto
            {
                Codigo = 0, NumeroDocumento = "", IdSolicitante = 0, IdAtribuido = 0, IdDepartamento = 0,
                StatusGestor = 0, StatusFinanceiro = 0, StatusContabil = 0, NomeParceiro = "  ", DocumentoParceiro = "",
                TipoSolicitacao = 0, TipoPagamento = 0, FormaPagamento = 0, TipoRelatorio = 0
            };

            var resultado = await Consultar(fonte, filtro);

            resultado.Sol.Should().Equal(101, 102, 103);
            resultado.Rddv.Should().Equal(201, 202, 203);
            resultado.Total.Should().Be(6);
        }

        [Fact]
        public async Task Filtro_codigo_vale_para_os_dois_tipos()
        {
            var (ctx, fonte) = Novo(c =>
            {
                c.Solicitacaos.Add(Sol(7));
                c.Rddv.Add(Rd(7));
            });
            using var _ = ctx;

            var soSolicitacao = await Consultar(fonte, new FiltroExportacaoDto { Codigo = 102 });
            soSolicitacao.Sol.Should().Equal(102);
            soSolicitacao.Rddv.Should().BeEmpty();
            soSolicitacao.Total.Should().Be(1);

            var soRddv = await Consultar(fonte, new FiltroExportacaoDto { Codigo = 202 });
            soRddv.Sol.Should().BeEmpty();
            soRddv.Rddv.Should().Equal(202);
            soRddv.Total.Should().Be(1);

            var ambos = await Consultar(fonte, new FiltroExportacaoDto { Codigo = 7 });
            ambos.Sol.Should().Equal(7);
            ambos.Rddv.Should().Equal(7);
            ambos.Total.Should().Be(2);
        }

        [Fact]
        public async Task Filtro_numero_do_documento_e_igualdade_exata_e_so_de_solicitacao()
        {
            var (ctx, fonte) = Novo();
            using var _ = ctx;

            var exato = await Consultar(fonte, new FiltroExportacaoDto { NumeroDocumento = "NF-102" });
            exato.Sol.Should().Equal(102);
            exato.Rddv.Should().BeEmpty("filtro só de Solicitação exclui os RDDVs");
            exato.Total.Should().Be(1);

            var parcial = await Consultar(fonte, new FiltroExportacaoDto { NumeroDocumento = "NF-10" });
            parcial.Sol.Should().BeEmpty("igualdade exata, não contém");
            parcial.Total.Should().Be(0);
        }

        [Fact]
        public async Task Filtro_data_da_solicitacao_so_com_inicio()
        {
            var (ctx, fonte) = Novo();
            using var _ = ctx;

            var resultado = await Consultar(fonte, new FiltroExportacaoDto { DataSolicitacaoInicio = new DateTime(2026, 3, 11) });

            resultado.Sol.Should().Equal(102, 103);
            resultado.Rddv.Should().Equal(202, 203);
            resultado.Total.Should().Be(4);
        }

        [Fact]
        public async Task Filtro_data_da_solicitacao_so_com_fim_e_fim_inclusivo_pelo_dia_inteiro()
        {
            var (ctx, fonte) = Novo();
            using var _ = ctx;

            // 101 e 201 estão às 18:30 e 18:00 do dia 10: entram. 102 e 202 são meia-noite do dia 11: ficam de fora.
            var resultado = await Consultar(fonte, new FiltroExportacaoDto { DataSolicitacaoFim = new DateTime(2026, 3, 10) });

            resultado.Sol.Should().Equal(101);
            resultado.Rddv.Should().Equal(201);
            resultado.Total.Should().Be(2);
        }

        [Fact]
        public async Task Filtro_data_da_solicitacao_com_inicio_e_fim_no_mesmo_dia()
        {
            var (ctx, fonte) = Novo();
            using var _ = ctx;

            var resultado = await Consultar(fonte, new FiltroExportacaoDto
            {
                DataSolicitacaoInicio = new DateTime(2026, 3, 11),
                DataSolicitacaoFim = new DateTime(2026, 3, 11)
            });

            resultado.Sol.Should().Equal(102);
            resultado.Rddv.Should().Equal(202);
        }

        [Fact]
        public async Task Filtro_data_do_fim_com_hora_ainda_vale_pelo_dia_inteiro()
        {
            var (ctx, fonte) = Novo();
            using var _ = ctx;

            var resultado = await Consultar(fonte, new FiltroExportacaoDto { DataSolicitacaoFim = new DateTime(2026, 3, 10, 0, 0, 1) });

            resultado.Sol.Should().Equal(101);
        }

        [Fact]
        public async Task Filtro_data_de_vencimento_so_com_inicio_so_com_fim_e_exclui_sem_vencimento()
        {
            var (ctx, fonte) = Novo();
            using var _ = ctx;

            var inicio = await Consultar(fonte, new FiltroExportacaoDto { DataVencimentoInicio = new DateTime(2026, 5, 1) });
            inicio.Sol.Should().Equal(102);
            inicio.Rddv.Should().Equal(203);

            var fim = await Consultar(fonte, new FiltroExportacaoDto { DataVencimentoFim = new DateTime(2026, 4, 10) });
            fim.Sol.Should().Equal(101);
            fim.Rddv.Should().Equal(201);

            var faixa = await Consultar(fonte, new FiltroExportacaoDto
            {
                DataVencimentoInicio = new DateTime(2026, 4, 1),
                DataVencimentoFim = new DateTime(2026, 5, 31)
            });
            faixa.Sol.Should().Equal(101, 102);
            faixa.Rddv.Should().Equal(201, 203);
        }

        [Fact]
        public async Task Filtro_data_de_aprovacao_do_financeiro_so_com_inicio_so_com_fim_e_exclui_sem_aprovacao()
        {
            var (ctx, fonte) = Novo();
            using var _ = ctx;

            var inicio = await Consultar(fonte, new FiltroExportacaoDto { DataAprovacaoFinanceiroInicio = new DateTime(2026, 3, 13) });
            inicio.Sol.Should().Equal(102);
            inicio.Rddv.Should().Equal(202);

            // 101 e 201 foram aprovadas às 15:00 do dia 12: o fim do dia 12 as inclui.
            var fim = await Consultar(fonte, new FiltroExportacaoDto { DataAprovacaoFinanceiroFim = new DateTime(2026, 3, 12) });
            fim.Sol.Should().Equal(101);
            fim.Rddv.Should().Equal(201);
        }

        [Fact]
        public async Task Filtro_solicitante_atribuido_e_departamento()
        {
            var (ctx, fonte) = Novo();
            using var _ = ctx;

            var solicitante = await Consultar(fonte, new FiltroExportacaoDto { IdSolicitante = 4 });
            solicitante.Sol.Should().Equal(102);
            solicitante.Rddv.Should().Equal(202);

            var atribuido = await Consultar(fonte, new FiltroExportacaoDto { IdAtribuido = 2 });
            atribuido.Sol.Should().Equal(101, 103);
            atribuido.Rddv.Should().Equal(201, 203);

            var departamento = await Consultar(fonte, new FiltroExportacaoDto { IdDepartamento = 2 });
            departamento.Sol.Should().Equal(102, 103);
            departamento.Rddv.Should().Equal(202, 203);
        }

        [Theory]
        [InlineData(2, new[] { 101 }, new[] { 201 })]
        [InlineData(3, new[] { 102 }, new[] { 202 })]
        [InlineData(1, new[] { 103 }, new[] { 203 })] // 0, 1 e nulo são "Aguardando"
        public async Task Filtro_status_do_gestor(int status, int[] solEsperadas, int[] rddvEsperados)
        {
            var (ctx, fonte) = Novo();
            using var _ = ctx;

            var resultado = await Consultar(fonte, new FiltroExportacaoDto { StatusGestor = status });

            resultado.Sol.Should().Equal(solEsperadas);
            resultado.Rddv.Should().Equal(rddvEsperados);
        }

        [Theory]
        [InlineData(2, new[] { 101 }, new[] { 201 })]
        [InlineData(3, new[] { 102 }, new[] { 202 })]
        [InlineData(1, new[] { 103 }, new[] { 203 })] // 103 e 203 têm status nulo
        public async Task Filtro_status_do_financeiro(int status, int[] solEsperadas, int[] rddvEsperados)
        {
            var (ctx, fonte) = Novo();
            using var _ = ctx;

            var resultado = await Consultar(fonte, new FiltroExportacaoDto { StatusFinanceiro = status });

            resultado.Sol.Should().Equal(solEsperadas);
            resultado.Rddv.Should().Equal(rddvEsperados);
        }

        [Theory]
        [InlineData(1, new[] { 101, 103 }, new[] { 201, 203 })] // 1 e 0 são "Aguardando Lançamento"
        [InlineData(2, new[] { 102 }, new[] { 202 })]
        [InlineData(3, new int[0], new int[0])]
        public async Task Filtro_status_da_contabilidade(int status, int[] solEsperadas, int[] rddvEsperados)
        {
            var (ctx, fonte) = Novo();
            using var _ = ctx;

            var resultado = await Consultar(fonte, new FiltroExportacaoDto { StatusContabil = status });

            resultado.Sol.Should().Equal(solEsperadas);
            resultado.Rddv.Should().Equal(rddvEsperados);
        }

        [Fact]
        public async Task Filtro_nome_do_parceiro_ignora_caixa_e_contem()
        {
            var (ctx, fonte) = Novo();
            using var _ = ctx;

            // Solicitação filtra por NomeParceiro. O termo vem em caixa diferente da gravada.
            var parceiro = await Consultar(fonte, new FiltroExportacaoDto { NomeParceiro = "FORNECEDOR alfa" });
            parceiro.Sol.Should().Equal(101);
            parceiro.Rddv.Should().BeEmpty();

            // RDDV filtra por NomeFuncionario. A gravada está em maiúsculas e o termo em minúsculas.
            var funcionario = await Consultar(fonte, new FiltroExportacaoDto { NomeParceiro = "joão pereira" });
            funcionario.Rddv.Should().Equal(202);
            funcionario.Sol.Should().BeEmpty();

            // "contém": o termo aparece no meio do nome.
            var meio = await Consultar(fonte, new FiltroExportacaoDto { NomeParceiro = "teste" });
            meio.Rddv.Should().Equal(201, 202);
            meio.Sol.Should().BeEmpty();
        }

        [Fact]
        public async Task Filtro_documento_do_parceiro_ignora_pontuacao_dos_dois_lados()
        {
            var (ctx, fonte) = Novo();
            using var _ = ctx;

            // Gravado com pontuação (101), termo sem pontuação.
            (await Consultar(fonte, new FiltroExportacaoDto { DocumentoParceiro = "12345678000190" })).Sol.Should().Equal(101);
            // Gravado com pontuação, termo parcial com pontuação diferente.
            (await Consultar(fonte, new FiltroExportacaoDto { DocumentoParceiro = "12.345.678/0001" })).Sol.Should().Equal(101);
            // Gravado sem pontuação (102), termo com pontuação.
            (await Consultar(fonte, new FiltroExportacaoDto { DocumentoParceiro = "98.765.432" })).Sol.Should().Equal(102);

            // CPF do RDDV: gravado com pontuação, termo sem; e o contrário.
            (await Consultar(fonte, new FiltroExportacaoDto { DocumentoParceiro = "11122233396" })).Rddv.Should().Equal(201);
            (await Consultar(fonte, new FiltroExportacaoDto { DocumentoParceiro = "555.666.777-88" })).Rddv.Should().Equal(202);

            // Termo comum aos dois tipos traz solicitação e RDDV.
            var ambos = await Consultar(fonte, new FiltroExportacaoDto { DocumentoParceiro = "111 222 333" });
            ambos.Sol.Should().Equal(103);
            ambos.Rddv.Should().Equal(201);
        }

        [Fact]
        public async Task Filtro_documento_so_com_pontuacao_nao_filtra()
        {
            var (ctx, fonte) = Novo();
            using var _ = ctx;

            var resultado = await Consultar(fonte, new FiltroExportacaoDto { DocumentoParceiro = " .-/ " });

            resultado.Total.Should().Be(6);
        }

        [Fact]
        public async Task Filtros_so_de_solicitacao_tipo_pagamento_e_forma()
        {
            var (ctx, fonte) = Novo();
            using var _ = ctx;

            var tipoSolicitacao = await Consultar(fonte, new FiltroExportacaoDto { TipoSolicitacao = 1 });
            tipoSolicitacao.Sol.Should().Equal(102);
            tipoSolicitacao.Rddv.Should().BeEmpty();

            var tipoPagamento = await Consultar(fonte, new FiltroExportacaoDto { TipoPagamento = 2 });
            tipoPagamento.Sol.Should().Equal(102);
            tipoPagamento.Rddv.Should().BeEmpty();

            var forma = await Consultar(fonte, new FiltroExportacaoDto { FormaPagamento = 2 });
            forma.Sol.Should().Equal(102, 103);
            forma.Rddv.Should().BeEmpty();
            forma.Total.Should().Be(2);
        }

        [Fact]
        public async Task Filtro_so_de_rddv_tipo_de_relatorio_exclui_as_solicitacoes()
        {
            var (ctx, fonte) = Novo();
            using var _ = ctx;

            var resultado = await Consultar(fonte, new FiltroExportacaoDto { TipoRelatorio = 2 });

            resultado.Rddv.Should().Equal(202);
            resultado.Sol.Should().BeEmpty();
            resultado.Total.Should().Be(1);
        }

        [Fact]
        public async Task Filtros_de_tipos_diferentes_juntos_nao_trazem_nada()
        {
            var (ctx, fonte) = Novo();
            using var _ = ctx;

            var resultado = await Consultar(fonte, new FiltroExportacaoDto { TipoPagamento = 1, TipoRelatorio = 1 });

            resultado.Sol.Should().BeEmpty();
            resultado.Rddv.Should().BeEmpty();
            resultado.Total.Should().Be(0);
        }

        [Fact]
        public async Task Tipo_do_titulo_restringe_a_um_dos_tipos()
        {
            var (ctx, fonte) = Novo();
            using var _ = ctx;

            var soSolicitacao = await Consultar(fonte, new FiltroExportacaoDto { TipoTitulo = TipoTituloExportacao.Solicitacao });
            soSolicitacao.Sol.Should().Equal(101, 102, 103);
            soSolicitacao.Rddv.Should().BeEmpty();
            soSolicitacao.Total.Should().Be(3);

            var soRddv = await Consultar(fonte, new FiltroExportacaoDto { TipoTitulo = TipoTituloExportacao.Rddv });
            soRddv.Sol.Should().BeEmpty();
            soRddv.Rddv.Should().Equal(201, 202, 203);
            soRddv.Total.Should().Be(3);

            // Filtro só de Solicitação com TipoTitulo = Rddv: contradição, nada a exportar.
            var contradicao = await Consultar(fonte, new FiltroExportacaoDto { TipoTitulo = TipoTituloExportacao.Rddv, TipoPagamento = 1 });
            contradicao.Total.Should().Be(0);

            // Valor fora do enum não exporta nada (a validação de 400 é da API).
            var invalido = await Consultar(fonte, new FiltroExportacaoDto { TipoTitulo = (TipoTituloExportacao)99 });
            invalido.Total.Should().Be(0);
        }

        [Fact]
        public async Task Filtros_combinados_valem_em_conjunto()
        {
            var (ctx, fonte) = Novo();
            using var _ = ctx;

            var resultado = await Consultar(fonte, new FiltroExportacaoDto
            {
                IdDepartamento = 2,
                DataSolicitacaoInicio = new DateTime(2026, 3, 11),
                StatusContabil = 1
            });

            resultado.Sol.Should().Equal(103);
            resultado.Rddv.Should().Equal(203);
            resultado.Total.Should().Be(2);
        }

        // -----------------------------------------------------------------------------------------------------
        // Rótulos e campos
        // -----------------------------------------------------------------------------------------------------

        [Fact]
        public async Task Solicitacao_sai_com_codigo_rotulos_nomes_e_valores()
        {
            var (ctx, fonte) = Novo();
            using var _ = ctx;

            var s = (await fonte.ListarSolicitacoesAsync(new FiltroExportacaoDto { Codigo = 101 }, Ct)).Single();

            s.Codigo.Should().Be("SOL-000101");
            s.TipoSolicitacao.Should().Be("Pagamento");
            s.TipoPagamento.Should().Be("Nacional");
            s.FormaPagamento.Should().Be("Boleto");
            s.Departamento.Should().Be("Tecnologia");
            s.Solicitante.Should().Be("Ana Solicitante");
            s.AtribuidoA.Should().Be("Bruno Atribuido");
            s.Gestor.Should().Be("Carla Gestora");
            s.StatusGestor.Should().Be("Autorizado");
            s.StatusFinanceiro.Should().Be("Autorizado");
            s.StatusContabil.Should().Be("Aguardando Lançamento");
            s.NumeroDocumento.Should().Be("NF-101");
            s.NomeParceiro.Should().Be("Fornecedor Alfa Ltda");
            s.DocumentoParceiro.Should().Be("12.345.678/0001-90", "dados do parceiro saem completos, como gravados");
            s.Banco.Should().Be("Banco Fictício");
            s.DataAprovacaoFinanceiro.Should().Be(new DateTime(2026, 3, 12, 15, 0, 0));
            s.Valor.Should().Be(100m);
        }

        [Fact]
        public async Task Solicitacao_sem_atribuido_nem_gestor_nem_numero_do_documento_sai_com_nulos()
        {
            var (ctx, fonte) = Novo(c => c.Solicitacaos.Add(Sol(5, s =>
            {
                s.IdUsuarioAtribuido = null;
                s.IdGestor = null;
                s.NumeroDocumento = null;
            })));
            using var _ = ctx;

            var s = (await fonte.ListarSolicitacoesAsync(new FiltroExportacaoDto { Codigo = 5 }, Ct)).Single();

            s.AtribuidoA.Should().BeNull();
            s.Gestor.Should().BeNull();
            s.NumeroDocumento.Should().BeNull();
        }

        [Fact]
        public async Task Rotulo_desconhecido_vira_codigo_n_na_solicitacao()
        {
            var (ctx, fonte) = Novo(c => c.Solicitacaos.Add(Sol(9, s =>
            {
                s.TipoSolicitacao = 99;
                s.TipoPagamento = 7;
                s.FormaPagamento = 5;
                s.StatusGestor = 9;
                s.StatusFinanceiro = 8;
                s.StatusContabilidade = 6;
                s.IdDepartamento = 42;
            })), padrao: false);
            using var _ = ctx;

            var s = (await fonte.ListarSolicitacoesAsync(new FiltroExportacaoDto(), Ct)).Single();

            s.TipoSolicitacao.Should().Be("Código 99");
            s.TipoPagamento.Should().Be("Código 7");
            s.FormaPagamento.Should().Be("Código 5");
            s.StatusGestor.Should().Be("Código 9");
            s.StatusFinanceiro.Should().Be("Código 8");
            s.StatusContabil.Should().Be("Código 6");
            s.Departamento.Should().Be("Código 42");
            s.Solicitante.Should().Be("Ana Solicitante");
        }

        [Fact]
        public async Task Rddv_sai_com_codigo_rotulos_tipo_de_viagem_e_total_da_view()
        {
            var (ctx, fonte) = Novo();
            using var _ = ctx;

            var r = (await fonte.ListarRddvsAsync(new FiltroExportacaoDto { Codigo = 201 }, Ct)).Single();

            r.Codigo.Should().Be("RDDV-000201");
            r.TipoRelatorio.Should().Be("Adiantamento");
            r.NomeFuncionario.Should().Be("Maria Souza Teste");
            r.Cpf.Should().Be("111.222.333-96");
            r.Departamento.Should().Be("Tecnologia");
            r.Solicitante.Should().Be("Ana Solicitante");
            r.AtribuidoA.Should().Be("Bruno Atribuido");
            r.Gestor.Should().Be("Carla Gestora");
            r.TipoViagem.Should().Be("Nacional");
            r.StatusFinanceiro.Should().Be("Autorizado");
            // View: diárias (2 x 100) + despesas (30 + 20) - (adiantamento 50 + saldo do cartão 10) = 190.
            r.TotalDespesas.Should().Be(190m);

            var internacional = (await fonte.ListarRddvsAsync(new FiltroExportacaoDto { Codigo = 202 }, Ct)).Single();
            internacional.TipoViagem.Should().Be("Internacional");
            internacional.TipoRelatorio.Should().Be("Prestação de Contas");
            internacional.Cpf.Should().Be("55566677788");
        }

        [Fact]
        public async Task Rotulo_desconhecido_vira_codigo_n_no_rddv()
        {
            var (ctx, fonte) = Novo(c => c.Rddv.Add(Rd(9, r =>
            {
                r.TipoRelatorio = 9;
                r.TipoViagem = 77;
                r.StatusGestor = 9;
                r.IdDepartamento = 42;
            })), padrao: false);
            using var _ = ctx;

            var r = (await fonte.ListarRddvsAsync(new FiltroExportacaoDto(), Ct)).Single();

            r.TipoRelatorio.Should().Be("Código 9");
            r.TipoViagem.Should().Be("Código 77");
            r.StatusGestor.Should().Be("Código 9");
            r.Departamento.Should().Be("Código 42");
        }

        [Theory]
        [InlineData(null, "Aguardando Autorização")]
        [InlineData(0, "Aguardando Autorização")]
        [InlineData(1, "Aguardando Autorização")]
        [InlineData(2, "Autorizado")]
        [InlineData(3, "Negado")]
        [InlineData(4, "Código 4")]
        public void Rotulos_de_status_do_gestor_e_do_financeiro(int? status, string esperado)
        {
            RotulosExportacao.StatusGestor(status).Should().Be(esperado);
            RotulosExportacao.StatusFinanceiro(status).Should().Be(esperado);
        }

        [Theory]
        [InlineData(null, "Aguardando Lançamento")]
        [InlineData(0, "Aguardando Lançamento")]
        [InlineData(1, "Aguardando Lançamento")]
        [InlineData(2, "Lançado")]
        [InlineData(3, "Negado")]
        [InlineData(4, "Código 4")]
        public void Rotulos_de_status_contabil(int? status, string esperado)
        {
            RotulosExportacao.StatusContabil(status).Should().Be(esperado);
        }

        [Theory]
        [InlineData(1, "Adiantamento", "Adiantamento", "Nacional", "Boleto", "Alimentação")]
        [InlineData(2, "Prestação de Contas", "Pagamento", "Internacional", "Transferência Bancária", "Hospedagem")]
        [InlineData(3, "Reembolso", "Movimento Almoxarifado", "Código 3", "Código 3", "Passagens")]
        public void Rotulos_dos_tipos(int codigo, string relatorio, string solicitacao, string pagamento, string forma, string despesa)
        {
            RotulosExportacao.TipoRelatorio(codigo).Should().Be(relatorio);
            RotulosExportacao.TipoSolicitacao(codigo).Should().Be(solicitacao);
            RotulosExportacao.TipoPagamento(codigo).Should().Be(pagamento);
            RotulosExportacao.FormaPagamento(codigo).Should().Be(forma);
            RotulosExportacao.TipoDespesa(codigo).Should().Be(despesa);
        }

        [Fact]
        public void Rotulos_de_tipo_de_despesa_seguem_a_lista_do_front()
        {
            var esperados = new[]
            {
                "Alimentação", "Hospedagem", "Passagens", "Táxi", "Kilometragem", "Estacionamento",
                "Pedágio", "Combustível", "Aluguel de Automóveis", "Comunicações", "Bagagens", "Outros"
            };

            for (var i = 0; i < esperados.Length; i++)
                RotulosExportacao.TipoDespesa(i + 1).Should().Be(esperados[i]);

            RotulosExportacao.TipoDespesa(0).Should().Be("Código 0");
            RotulosExportacao.TipoDespesa(13).Should().Be("Código 13");
        }

        // -----------------------------------------------------------------------------------------------------
        // Despesas
        // -----------------------------------------------------------------------------------------------------

        [Fact]
        public async Task Despesas_saem_com_rotulo_codigo_do_rddv_e_ordenadas()
        {
            var (ctx, fonte) = Novo();
            using var _ = ctx;

            var despesas = await fonte.ListarDespesasAsync([201, 202, 203], Ct);

            despesas.Select(d => d.IdDespesa).Should().Equal(2, 1); // 07/03 antes de 08/03
            despesas[0].CodigoRddv.Should().Be("RDDV-000201");
            despesas[0].IdRddv.Should().Be(201);
            despesas[0].TipoDespesa.Should().Be("Táxi");
            despesas[0].Moeda.Should().BeNull();
            despesas[0].Quantidade.Should().BeNull();
            despesas[0].Valor.Should().Be(20m);
            despesas[1].TipoDespesa.Should().Be("Alimentação");
            despesas[1].Moeda.Should().Be("BRL");
            despesas[1].Quantidade.Should().Be(2m);
        }

        [Fact]
        public async Task Despesas_so_dos_rddvs_pedidos_e_lista_vazia_nao_consulta_nada()
        {
            var (ctx, fonte) = Novo();
            using var _ = ctx;

            (await fonte.ListarDespesasAsync([202, 203], Ct)).Should().BeEmpty();
            (await fonte.ListarDespesasAsync([], Ct)).Should().BeEmpty();
        }

        [Fact]
        public async Task Despesas_com_mais_de_mil_ids_sao_lidas_em_lotes()
        {
            var (ctx, fonte) = Novo(c =>
            {
                for (var i = 1; i <= 2500; i++)
                    c.DespesaRddv.Add(new DespesaRddv { IdDespesaRddv = 1000 + i, IdRelatorio = 5000 + i, DataDespesa = new DateTime(2026, 1, 1), TipoDespesa = 12, Valor = 1m });
            });
            using var _ = ctx;

            var ids = Enumerable.Range(5001, 2500).Append(201).ToList();
            var despesas = await fonte.ListarDespesasAsync(ids, Ct);

            despesas.Should().HaveCount(2502);
            despesas.Select(d => d.IdRddv).Distinct().Should().HaveCount(2501);
            despesas.Select(d => d.IdRddv).Should().BeInAscendingOrder();
        }

        // -----------------------------------------------------------------------------------------------------
        // Anexos
        // -----------------------------------------------------------------------------------------------------

        private static DocumentoSolicitacao DocSol(int id, int idSolicitacao, int tipo, string nome, int indice, string arquivo) => new()
        {
            IdDocumentoSolicitacao = id,
            IdSolicitacao = idSolicitacao,
            IdTipoDocumento = tipo,
            IdUsuario = 1,
            DataCriacao = new DateTime(2026, 3, 9, 8, 0, 0),
            Indice = indice,
            NomeArquivo = nome,
            CaminhoArquivo = "",
            Arquivo = arquivo
        };

        private static DocumentoRddv DocRd(int id, int idRelatorio, int tipo, string nome, int indice, string arquivo) => new()
        {
            IdDocumentoRddv = id,
            IdRelatorio = idRelatorio,
            IdTipoDocumento = tipo,
            IdUsuario = 1,
            DataCriacao = new DateTime(2026, 3, 9, 8, 0, 0),
            Indice = indice,
            NomeArquivo = nome,
            CaminhoArquivo = "",
            Arquivo = arquivo
        };

        [Fact]
        public async Task Anexos_de_solicitacao_saem_so_com_metadados_ordenados_e_com_rotulo_de_tipo()
        {
            var grande = new string('A', 6_000_000); // base64 fictício de um anexo grande
            var (ctx, fonte) = Novo(c => c.DocumentoSolicitacaos.AddRange(
                DocSol(12, 101, 2, "recibo.jpg", 2, "QUJD"),
                DocSol(11, 101, 1, "boleto março.pdf", 1, grande),
                DocSol(13, 101, 99, "sem-tipo.bin", 3, "QUJD"),   // tipo que não existe na tabela
                DocSol(14, 101, 3, "tipo-sem-nome.bin", 4, "QUJD"), // tipo existe, mas sem nome
                DocSol(15, 102, 1, "outra.pdf", 1, "QUJD")));
            using var _ = ctx;

            var anexos = await fonte.ListarAnexosAsync(TipoTituloExportacao.Solicitacao, [101], Ct);

            anexos.Select(a => a.IdAnexo).Should().Equal(11, 12, 13, 14);
            anexos.Should().OnlyContain(a => a.TipoTitulo == TipoTituloExportacao.Solicitacao && a.IdTitulo == 101 && a.CodigoTitulo == "SOL-000101");
            anexos[0].TipoDocumento.Should().Be("Nota Fiscal");
            anexos[0].NomeOriginal.Should().Be("boleto março.pdf");
            anexos[0].IdTipoDocumento.Should().Be(1);
            anexos[0].DataCriacao.Should().Be(new DateTime(2026, 3, 9, 8, 0, 0));
            anexos[1].TipoDocumento.Should().Be("Comprovante");
            anexos[2].TipoDocumento.Should().Be("Documento", "tipo desconhecido usa o rótulo Documento (C1)");
            anexos[3].TipoDocumento.Should().Be("Documento", "tipo sem nome usa o rótulo Documento (C1)");

            // O record de metadados não tem onde guardar o conteúdo; o SQL que não lê a coluna é conferido em outro teste.
            typeof(AnexoExportacao).GetProperties().Select(p => p.Name)
                .Should().NotContain(n => n.Contains("Arquivo") || n.Contains("Conteudo") || n.Contains("Base64"));
        }

        [Fact]
        public async Task Anexos_de_rddv_vem_da_tabela_de_rddv()
        {
            var (ctx, fonte) = Novo(c =>
            {
                c.DocumentoRddv.AddRange(
                    DocRd(21, 201, 2, "recibo.jpg", 1, "QUJD"),
                    DocRd(22, 202, 1, "nf.pdf", 1, "QUJD"));
                c.DocumentoSolicitacaos.Add(DocSol(31, 201, 1, "de-solicitacao.pdf", 1, "QUJD")); // mesmo id 201, outra tabela
            });
            using var _ = ctx;

            var anexos = await fonte.ListarAnexosAsync(TipoTituloExportacao.Rddv, [201, 202], Ct);

            anexos.Select(a => (a.IdAnexo, a.CodigoTitulo, a.TipoDocumento)).Should().Equal(
                (21, "RDDV-000201", "Comprovante"),
                (22, "RDDV-000202", "Nota Fiscal"));
            anexos.Should().OnlyContain(a => a.TipoTitulo == TipoTituloExportacao.Rddv);
        }

        [Fact]
        public async Task Anexos_com_mais_de_mil_ids_sao_lidos_em_lotes()
        {
            var (ctx, fonte) = Novo(c =>
            {
                c.DocumentoSolicitacaos.Add(DocSol(1, 1, 1, "primeiro.pdf", 1, "QUJD"));
                c.DocumentoSolicitacaos.Add(DocSol(2, 2400, 1, "ultimo.pdf", 1, "QUJD"));
            });
            using var _ = ctx;

            var anexos = await fonte.ListarAnexosAsync(TipoTituloExportacao.Solicitacao, Enumerable.Range(1, 2500).ToList(), Ct);

            anexos.Select(a => a.IdTitulo).Should().Equal(1, 2400);
        }

        [Fact]
        public async Task Anexos_de_tipo_ambos_nao_e_aceito()
        {
            var (ctx, fonte) = Novo();
            using var _ = ctx;

            var listar = () => fonte.ListarAnexosAsync(TipoTituloExportacao.Ambos, [1], Ct);
            var ler = () => fonte.LerConteudoBase64Async(TipoTituloExportacao.Ambos, 1, Ct);

            await listar.Should().ThrowAsync<ArgumentException>();
            await ler.Should().ThrowAsync<ArgumentException>();
        }

        [Fact]
        public async Task Conteudo_do_anexo_vem_de_um_anexo_por_chamada_e_da_tabela_certa()
        {
            var (ctx, fonte) = Novo(c =>
            {
                c.DocumentoSolicitacaos.AddRange(
                    DocSol(7, 101, 1, "a.pdf", 1, "data:application/pdf;base64,QUJD"),
                    DocSol(8, 101, 1, "b.pdf", 2, "REVG"));
                c.DocumentoRddv.Add(DocRd(7, 201, 1, "c.pdf", 1, "R0hJ")); // mesmo id 7, outra tabela
            });
            using var _ = ctx;

            (await fonte.LerConteudoBase64Async(TipoTituloExportacao.Solicitacao, 7, Ct)).Should().Be("data:application/pdf;base64,QUJD");
            (await fonte.LerConteudoBase64Async(TipoTituloExportacao.Solicitacao, 8, Ct)).Should().Be("REVG");
            (await fonte.LerConteudoBase64Async(TipoTituloExportacao.Rddv, 7, Ct)).Should().Be("R0hJ");
            (await fonte.LerConteudoBase64Async(TipoTituloExportacao.Solicitacao, 999, Ct)).Should().BeNull();
            ctx.ChangeTracker.Entries().Should().BeEmpty();
        }

        // -----------------------------------------------------------------------------------------------------
        // Tradução para SQL Server (sem banco): SQL capturado e nenhum aviso do EF
        // -----------------------------------------------------------------------------------------------------

        /// <summary>
        /// Registra o SQL de cada comando e suprime a abertura da conexão e a execução, devolvendo um leitor vazio. Assim
        /// o provedor SQL Server compila e traduz as consultas de verdade sem tocar em nenhum banco.
        /// </summary>
        private sealed class CapturaSql : DbCommandInterceptor, IDbConnectionInterceptor
        {
            public List<string> Comandos { get; } = [];

            ValueTask<InterceptionResult> IDbConnectionInterceptor.ConnectionOpeningAsync(
                DbConnection connection, ConnectionEventData eventData, InterceptionResult result, CancellationToken cancellationToken)
                => ValueTask.FromResult(InterceptionResult.Suppress());

            public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(
                DbCommand command, CommandEventData eventData, InterceptionResult<DbDataReader> result, CancellationToken cancellationToken = default)
            {
                Comandos.Add(command.CommandText);

                // Consulta de contagem precisa de uma linha com o total; as demais, de um leitor sem linhas.
                var tabela = new DataTable();
                if (command.CommandText.TrimStart().StartsWith("SELECT COUNT(*)", StringComparison.OrdinalIgnoreCase))
                {
                    tabela.Columns.Add("Total", typeof(int));
                    tabela.Rows.Add(0);
                }

                return ValueTask.FromResult(InterceptionResult<DbDataReader>.SuppressWithResult(tabela.CreateDataReader()));
            }

            public override ValueTask<InterceptionResult<object>> ScalarExecutingAsync(
                DbCommand command, CommandEventData eventData, InterceptionResult<object> result, CancellationToken cancellationToken = default)
            {
                Comandos.Add(command.CommandText);
                return ValueTask.FromResult(InterceptionResult<object>.SuppressWithResult(0));
            }
        }

        private static (PagueAresContext Ctx, CapturaSql Captura, List<string> Avisos) NovoContextoSqlServer()
        {
            var captura = new CapturaSql();
            var avisos = new List<string>();

            var options = new DbContextOptionsBuilder<PagueAresContext>()
                .UseSqlServer("Server=(local);Database=SemBanco;Integrated Security=true;TrustServerCertificate=True")
                .AddInterceptors(captura)
                .LogTo(avisos.Add, LogLevel.Warning)
                .Options;

            return (new PagueAresContext(options), captura, avisos);
        }

        private static FiltroExportacaoDto FiltroCompleto(TipoTituloExportacao tipo)
        {
            var filtro = new FiltroExportacaoDto
            {
                TipoTitulo = tipo,
                Codigo = 123,
                DataSolicitacaoInicio = new DateTime(2026, 1, 1),
                DataSolicitacaoFim = new DateTime(2026, 12, 31),
                DataVencimentoInicio = new DateTime(2026, 1, 1),
                DataVencimentoFim = new DateTime(2026, 12, 31),
                DataAprovacaoFinanceiroInicio = new DateTime(2026, 1, 1),
                DataAprovacaoFinanceiroFim = new DateTime(2026, 12, 31),
                IdSolicitante = 1, IdAtribuido = 2, IdDepartamento = 3,
                StatusGestor = 2, StatusFinanceiro = 1, StatusContabil = 3,
                NomeParceiro = "Alfa", DocumentoParceiro = "123.456"
            };

            if (tipo == TipoTituloExportacao.Rddv)
            {
                filtro.TipoRelatorio = 2;
            }
            else
            {
                filtro.NumeroDocumento = "NF-1";
                filtro.TipoSolicitacao = 2;
                filtro.TipoPagamento = 1;
                filtro.FormaPagamento = 2;
            }

            return filtro;
        }

        [Fact]
        public async Task Todas_as_consultas_traduzem_para_sql_server_sem_avaliacao_no_cliente_nem_avisos()
        {
            var (ctx, captura, avisos) = NovoContextoSqlServer();
            using var _ = ctx;
            var fonte = new FonteExportacaoEf(ctx);

            foreach (var filtro in new[]
            {
                new FiltroExportacaoDto(),
                FiltroCompleto(TipoTituloExportacao.Solicitacao),
                FiltroCompleto(TipoTituloExportacao.Rddv)
            })
            {
                await fonte.ContarTitulosAsync(filtro, Ct);
                await fonte.ListarSolicitacoesAsync(filtro, Ct);
                await fonte.ListarRddvsAsync(filtro, Ct);
            }

            await fonte.ListarDespesasAsync([1, 2, 3], Ct);
            await fonte.ListarAnexosAsync(TipoTituloExportacao.Solicitacao, [1, 2, 3], Ct);
            await fonte.ListarAnexosAsync(TipoTituloExportacao.Rddv, [1, 2, 3], Ct);
            await fonte.LerConteudoBase64Async(TipoTituloExportacao.Solicitacao, 1, Ct);
            await fonte.LerConteudoBase64Async(TipoTituloExportacao.Rddv, 1, Ct);

            // Qualquer trecho sem tradução teria lançado InvalidOperationException acima; os avisos do EF (inclusive
            // sobre consulta sem ORDER BY ou avaliação no cliente) também não podem aparecer.
            captura.Comandos.Should().NotBeEmpty();
            avisos.Should().BeEmpty();
        }

        [Fact]
        public async Task Sql_das_solicitacoes_aplica_escopo_filtros_e_nunca_le_senha()
        {
            var (ctx, captura, _) = NovoContextoSqlServer();
            using var _ = ctx;
            var fonte = new FonteExportacaoEf(ctx);

            await fonte.ListarSolicitacoesAsync(FiltroCompleto(TipoTituloExportacao.Solicitacao), Ct);

            var sql = captura.Comandos.Should().ContainSingle().Subject;
            sql.Should().Contain("[Rascunho]").And.Contain("[Cancelado]").And.Contain("[AprovadoGestor]");
            sql.Should().Contain("REPLACE(").And.Contain("LOWER(");
            sql.Should().Contain("LEFT JOIN");
            sql.Should().Contain("ORDER BY");
            sql.Should().NotContain("Senha", "o hash da senha do usuário nunca entra na consulta");
            sql.Should().NotMatchRegex(@"\[Arquivo\]");
        }

        [Fact]
        public async Task Sql_dos_rddvs_junta_a_view_de_despesas_e_o_tipo_de_viagem()
        {
            var (ctx, captura, _) = NovoContextoSqlServer();
            using var _ = ctx;
            var fonte = new FonteExportacaoEf(ctx);

            await fonte.ListarRddvsAsync(FiltroCompleto(TipoTituloExportacao.Rddv), Ct);

            var sql = captura.Comandos.Should().ContainSingle().Subject;
            sql.Should().Contain("[vDespesasRddv]").And.Contain("[TipoViagem]");
            sql.Should().Contain("REPLACE(").And.Contain("LOWER(");
            sql.Should().NotContain("Senha");
        }

        [Fact]
        public async Task Sql_de_anexos_lista_so_metadados_e_leitura_traz_so_a_coluna_arquivo()
        {
            var (ctx, captura, _) = NovoContextoSqlServer();
            using var _ = ctx;
            var fonte = new FonteExportacaoEf(ctx);

            await fonte.ListarAnexosAsync(TipoTituloExportacao.Solicitacao, [1, 2], Ct);
            await fonte.ListarAnexosAsync(TipoTituloExportacao.Rddv, [1, 2], Ct);

            captura.Comandos.Should().HaveCount(2);
            captura.Comandos.Should().OnlyContain(sql => !Regex.IsMatch(sql, @"\[Arquivo\]"),
                "a listagem nunca projeta a coluna Arquivo (base64 de vários MB)");
            captura.Comandos.Should().OnlyContain(sql => sql.Contains("[NomeArquivo]") && sql.Contains("[DataCriacao]"));

            captura.Comandos.Clear();
            await fonte.LerConteudoBase64Async(TipoTituloExportacao.Solicitacao, 5, Ct);
            await fonte.LerConteudoBase64Async(TipoTituloExportacao.Rddv, 5, Ct);

            captura.Comandos.Should().HaveCount(2);
            foreach (var sql in captura.Comandos)
            {
                sql.Should().Contain("TOP(1)");
                sql.Should().MatchRegex(@"SELECT TOP\(1\) \[\w+\]\.\[Arquivo\]\s+FROM");
                sql.Should().NotContain("[NomeArquivo]").And.NotContain("[CaminhoArquivo]");
            }
        }

        [Fact]
        public async Task Lotes_de_ids_nao_passam_de_mil_por_consulta()
        {
            var (ctx, captura, _) = NovoContextoSqlServer();
            using var _ = ctx;
            var fonte = new FonteExportacaoEf(ctx);

            await fonte.ListarAnexosAsync(TipoTituloExportacao.Solicitacao, Enumerable.Range(1, 2500).ToList(), Ct);
            captura.Comandos.Should().HaveCount(3, "2500 ids = lotes de 1000, 1000 e 500");

            captura.Comandos.Clear();
            await fonte.ListarDespesasAsync(Enumerable.Range(1, 1001).ToList(), Ct);
            captura.Comandos.Should().HaveCount(2);
        }
    }
}
