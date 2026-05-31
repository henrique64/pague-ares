using Ares.PagueAres.Infrastructure.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Ares.PagueAres.Infrastructure
{
    public partial class PagueAresContext : DbContext
    {
        public PagueAresContext()
        {
        }

        public PagueAresContext(DbContextOptions<PagueAresContext> options)
            : base(options)
        {
        }

        public virtual DbSet<Configuracao> Configuracaos { get; set; } = null!;
        public virtual DbSet<Departamento> Departamentos { get; set; } = null!;
        public virtual DbSet<DocumentoSolicitacao> DocumentoSolicitacaos { get; set; } = null!;
        public virtual DbSet<Funcao> Funcaos { get; set; } = null!;
        public virtual DbSet<Solicitacao> Solicitacaos { get; set; } = null!;
        public virtual DbSet<TipoDocumento> TipoDocumentos { get; set; } = null!;
        public virtual DbSet<Usuario> Usuarios { get; set; } = null!;
        public virtual DbSet<UsuarioFuncao> UsuarioFuncaos { get; set; } = null!;
        public virtual DbSet<Rddv> Rddv { get; set; } = null!;
        public virtual DbSet<DespesaRddv> DespesaRddv { get; set; } = null!;
        public virtual DbSet<DocumentoRddv> DocumentoRddv { get; set; } = null!;
        public virtual DbSet<vDespesasRddv> vDespesasRddv { get; set; } = null!;
        public virtual DbSet<Fornecedor> Fornecedors { get; set; } = null!;
        public virtual DbSet<TipoViagem> TipoViagems { get; set; } = null!;
        public virtual DbSet<ViewListagemSolicitacoes> ViewListagemSolicitacoes { get; set; } = null!;

        protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder)
        {
            if (!optionsBuilder.IsConfigured)
            {
                // Fallback apenas para design-time (dotnet ef migrations add).
                // Em runtime, a connection string é sempre injetada via DI em Program.cs.
                // Defina PAGUEARES_CONNECTION_STRING localmente ou no .env para Docker.
                var connectionString = Environment.GetEnvironmentVariable("PAGUEARES_CONNECTION_STRING")
                    ?? "Server=localhost;Database=PagueAres;UID=sa;PWD=@12344321@;TrustServerCertificate=True";

                optionsBuilder.UseSqlServer(connectionString);
                optionsBuilder.EnableDetailedErrors(true);
                optionsBuilder.EnableSensitiveDataLogging(true);

                var loggerFactory = LoggerFactory.Create(b => b.AddConsole());
                optionsBuilder.UseLoggerFactory(loggerFactory);
            }
        }

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            modelBuilder.Entity<Configuracao>(entity =>
            {
                entity.HasKey(e => e.IdConfiguracao);

                entity.ToTable("Configuracao", tb => tb.HasTrigger("TRG"));

                entity.Property(e => e.Chave)
                    .HasMaxLength(512)
                    .IsUnicode(false);

                entity.Property(e => e.DataAlteracao).HasColumnType("datetime");

                entity.Property(e => e.Valor)
                    .IsUnicode(false)
                    .HasDefaultValueSql("('')");
            });

            modelBuilder.Entity<Departamento>(entity =>
            {
                entity.HasKey(e => e.IdDepartamento);

                entity.ToTable("Departamento", tb => tb.HasTrigger("TRG"));

                entity.Property(e => e.CodigoLancamento)
                    .HasMaxLength(256)
                    .IsUnicode(false);

                entity.Property(e => e.Nome)
                    .HasMaxLength(256)
                    .IsUnicode(false);
            });

            modelBuilder.Entity<DocumentoSolicitacao>(entity =>
            {
                entity.HasKey(e => e.IdDocumentoSolicitacao);

                entity.ToTable("DocumentoSolicitacao", tb => tb.HasTrigger("TRG"));

                entity.Property(e => e.Arquivo).HasColumnType("text");

                entity.Property(e => e.CaminhoArquivo)
                    .HasMaxLength(1024)
                    .IsUnicode(false);

                entity.Property(e => e.DataCriacao)
                    .HasColumnType("datetime")
                    .HasDefaultValueSql("(getdate())");

                entity.Property(e => e.NomeArquivo)
                    .HasMaxLength(256)
                    .IsUnicode(false);
            });

            modelBuilder.Entity<Funcao>(entity =>
            {
                entity.HasKey(e => e.IdFuncao);

                entity.ToTable("Funcao", tb => tb.HasTrigger("TRG"));

                entity.Property(e => e.Alias)
                    .HasMaxLength(256)
                    .IsUnicode(false);

                entity.Property(e => e.Nome)
                    .HasMaxLength(256)
                    .IsUnicode(false);
            });

            modelBuilder.Entity<UsuarioFuncao>(entity =>
            {
                entity.HasKey(e => new { e.IdUsuario, e.IdFuncao });

                entity.ToTable("UsuarioFuncao", tb => tb.HasTrigger("TRG"));

                entity.Property(e => e.IdUsuario)
                    .HasColumnName("IdUsuario")
                    .HasColumnType("int");

                entity.Property(e => e.IdFuncao)
                    .HasColumnName("IdFuncao")
                    .HasColumnType("int");
            });

            modelBuilder.Entity<Solicitacao>(entity =>
            {
                entity.HasKey(e => e.IdSolicitacao);

                entity.ToTable("Solicitacao", tb => tb.HasTrigger("TRG"));

                entity.Property(e => e.AgenciaParceiro)
                    .HasMaxLength(32)
                    .IsUnicode(false)
                    .HasDefaultValueSql("('')");

                entity.Property(e => e.BancoParceiro)
                    .HasMaxLength(64)
                    .IsUnicode(false)
                    .HasDefaultValueSql("('')");

                entity.Property(e => e.CentroCusto)
                    .HasMaxLength(128)
                    .IsUnicode(false)
                    .HasDefaultValueSql("('')");

                entity.Property(e => e.CodigoParceiro)
                    .HasMaxLength(64)
                    .IsUnicode(false)
                    .HasDefaultValueSql("('')");

                entity.Property(e => e.ContaParceiro)
                    .HasMaxLength(32)
                    .IsUnicode(false)
                    .HasDefaultValueSql("('')");

                entity.Property(e => e.DataDocumento).HasColumnType("datetime");

                entity.Property(e => e.DataSolicitacao)
                    .HasColumnType("datetime")
                    .HasDefaultValueSql("(getdate())");

                entity.Property(e => e.DataVencimento).HasColumnType("datetime");

                entity.Property(e => e.Descricao)
                    .HasMaxLength(512)
                    .IsUnicode(false)
                    .HasDefaultValueSql("('')");

                entity.Property(e => e.FormaPagamento).HasDefaultValueSql("((1))");

                entity.Property(e => e.NomeParceiro)
                    .HasMaxLength(128)
                    .IsUnicode(false)
                    .HasDefaultValueSql("('')");

                entity.Property(e => e.NumDocParceiro)
                    .HasMaxLength(64)
                    .IsUnicode(false)
                    .HasDefaultValueSql("('')");

                entity.Property(e => e.NumeroDocumento)
                    .HasMaxLength(64)
                    .IsUnicode(false)
                    .HasDefaultValueSql("('')");

                entity.Property(e => e.Observacao)
                    .HasMaxLength(2048)
                    .IsUnicode(false)
                    .HasDefaultValueSql("('')");

                entity.Property(e => e.ObservacaoGestor)
                    .HasMaxLength(512)
                    .IsUnicode(false)
                    .HasDefaultValueSql("('')");

                entity.Property(e => e.ObservacaoSetor)
                    .HasMaxLength(512)
                    .IsUnicode(false)
                    .HasDefaultValueSql("('')");

                entity.Property(e => e.Parcelas).HasDefaultValueSql("((1))");

                entity.Property(e => e.Pca)
                    .HasMaxLength(128)
                    .IsUnicode(false)
                    .HasColumnName("PCA")
                    .HasDefaultValueSql("('')");

                entity.Property(e => e.Projeto)
                    .HasMaxLength(128)
                    .IsUnicode(false)
                    .HasDefaultValueSql("('')");

                entity.Property(e => e.StatusContabilidade).HasDefaultValueSql("((1))");

                entity.Property(e => e.StatusFinanceiro).HasDefaultValueSql("((1))");

                entity.Property(e => e.StatusGestor).HasDefaultValueSql("((1))");

                entity.Property(e => e.TipoAutorizacao).HasDefaultValueSql("((1))");

                entity.Property(e => e.TipoSolicitacao).HasDefaultValueSql("((1))");

                entity.Property(e => e.Valor).HasColumnType("decimal(12, 2)");

                entity.Property(e => e.DataAprovacaoGestor).HasColumnType("datetime");
                entity.Property(e => e.DataAprovacaoFinanceiro).HasColumnType("datetime");
                entity.Property(e => e.DataAprovacaoContabil).HasColumnType("datetime");

                entity.Property(e => e.Rascunho).IsRequired().HasColumnType("bit").HasDefaultValue(true);
                entity.Property(e => e.Cancelado).IsRequired().HasColumnType("bit").HasDefaultValue(false);

                //entity.HasOne(d => d.IdUsuarioNavigation)
                //    .WithMany(p => p.Solicitacaos)
                //    .HasForeignKey(d => d.IdUsuario)
                //    .OnDelete(DeleteBehavior.ClientSetNull)
                //    .HasConstraintName("FK_Solicitacao_Usuario");
            });

            modelBuilder.Entity<TipoDocumento>(entity =>
            {
                entity.HasKey(e => e.IdTipoDocumento);

                entity.ToTable("TipoDocumento", tb => tb.HasTrigger("TRG"));

                entity.Property(e => e.Nome)
                    .HasMaxLength(64)
                    .IsUnicode(false);
            });

            modelBuilder.Entity<Usuario>(entity =>
            {
                entity.HasKey(e => e.IdUsuario);

                entity.ToTable("Usuario", tb => tb.HasTrigger("TRG"));

                entity.Property(e => e.Ativo)
                    .IsRequired()
                    .HasDefaultValueSql("((1))");

                entity.Property(e => e.DataCadastro)
                    .HasColumnType("datetime")
                    .HasDefaultValueSql("(getdate())");

                entity.Property(e => e.Email)
                    .HasMaxLength(512)
                    .IsUnicode(false);

                entity.Property(e => e.IdExterno)
                    .HasMaxLength(1024)
                    .IsUnicode(false);

                entity.Property(e => e.Nome)
                    .HasMaxLength(512)
                    .IsUnicode(false);

                entity.Property(e => e.Senha)
                    .HasMaxLength(512)
                    .IsUnicode(false);

                entity.Property(e => e.UltimaAlteracao).HasColumnType("datetime");

                //entity.HasMany(d => d.IdFuncaos)
                //    .WithMany(p => p.IdUsuarios)
                //    .UsingEntity<Dictionary<string, object>>(
                //        "UsuarioFuncao",
                //        l => l.HasOne<Funcao>().WithMany().HasForeignKey("IdFuncao").OnDelete(DeleteBehavior.ClientSetNull).HasConstraintName("FK_UsuarioFuncao_Funcao"),
                //        r => r.HasOne<Usuario>().WithMany().HasForeignKey("IdUsuario").OnDelete(DeleteBehavior.ClientSetNull).HasConstraintName("FK_UsuarioFuncao_Usuario"),
                //        j =>
                //        {
                //            j.HasKey("IdUsuario", "IdFuncao");

                //            j.ToTable("UsuarioFuncao");
                //        });
            });

            modelBuilder.Entity<Rddv>(entity =>
            {
                entity.HasKey(e => e.IdRelatorio);

                entity.ToTable("Rddv", tb => tb.HasTrigger("TRG"));

                entity.Property(e => e.IdRelatorio).IsRequired();
                entity.Property(e => e.IdUsuario).IsRequired();
                entity.Property(e => e.NomeFuncionario).IsRequired().HasDefaultValueSql("('')");
                entity.Property(e => e.CPF).IsRequired().HasDefaultValueSql("('')");
                entity.Property(e => e.IdDepartamento).IsRequired();
                entity.Property(e => e.Finalidade).IsRequired().HasDefaultValueSql("('')");
                entity.Property(e => e.TipoRelatorio).IsRequired();
                entity.Property(e => e.Moeda).IsRequired().HasDefaultValueSql("('')");
                entity.Property(e => e.TipoViagem).IsRequired();
                entity.Property(e => e.DataInicio).HasColumnType("datetime");
                entity.Property(e => e.DataFim).HasColumnType("datetime");
                entity.Property(e => e.DataCadastro).IsRequired().HasColumnType("datetime");
                entity.Property(e => e.LocalViagem).IsRequired().HasDefaultValueSql("('')");
                entity.Property(e => e.CentroCusto).IsRequired().HasDefaultValueSql("('')");
                entity.Property(e => e.Projeto).IsRequired().HasDefaultValueSql("('')");
                entity.Property(e => e.PCA).IsRequired().HasDefaultValueSql("('')");
                entity.Property(e => e.DiariaViagem);
                entity.Property(e => e.Diarias);
                entity.Property(e => e.Adiantamento);
                entity.Property(e => e.ValorAdiantamento).IsRequired().HasColumnType("decimal");
                entity.Property(e => e.SaldoCartaoVtm).IsRequired().HasColumnType("decimal");
                entity.Property(e => e.Observacao).IsRequired().HasDefaultValueSql("('')");
                entity.Property(e => e.Banco).IsRequired().HasDefaultValue(string.Empty);
                entity.Property(e => e.Agencia).IsRequired().HasDefaultValue(string.Empty);
                entity.Property(e => e.Conta).IsRequired().HasDefaultValue(string.Empty);
                entity.Property(e => e.ValorDiaria).IsRequired().HasDefaultValue(0m);
                entity.Property(e => e.ValorKm).IsRequired().HasDefaultValue(0m);
                entity.Property(e => e.DataAprovacaoGestor).HasColumnType("datetime");
                entity.Property(e => e.DataAprovacaoFinanceiro).HasColumnType("datetime");
                entity.Property(e => e.DataAprovacaoContabil).HasColumnType("datetime");
                entity.Property(e => e.Rascunho).IsRequired().HasColumnType("bit").HasDefaultValue(true);
                entity.Property(e => e.Cancelado).IsRequired().HasColumnType("bit").HasDefaultValue(false);
                entity.Property(e => e.DataVencimento).HasColumnType("datetime");
            });

            modelBuilder.Entity<DespesaRddv>(entity =>
            {
                entity.HasKey(e => e.IdDespesaRddv);

                entity.ToTable("DespesaRddv", tb => tb.HasTrigger("TRG"));

                entity.Property(e => e.IdDespesaRddv).IsRequired();
                entity.Property(e => e.IdRelatorio).IsRequired();
                entity.Property(e => e.DataDespesa).HasColumnType("datetime");
                entity.Property(e => e.TipoDespesa).IsRequired();
                entity.Property(e => e.Moeda);
                entity.Property(e => e.Valor).HasColumnType("decimal");
                entity.Property(e => e.Quantidade).HasColumnType("decimal");
            });

            modelBuilder.Entity<DocumentoRddv>(entity =>
            {
                entity.HasKey(e => e.IdDocumentoRddv);

                entity.ToTable("DocumentoRddv", tb => tb.HasTrigger("TRG"));

                entity.Property(e => e.Arquivo).HasColumnType("text");

                entity.Property(e => e.CaminhoArquivo)
                    .HasMaxLength(1024)
                    .IsUnicode(false);

                entity.Property(e => e.DataCriacao)
                    .HasColumnType("datetime")
                    .HasDefaultValueSql("(getdate())");

                entity.Property(e => e.NomeArquivo)
                    .HasMaxLength(256)
                    .IsUnicode(false);
            });

            modelBuilder.Entity<vDespesasRddv>(entity =>
            {
                entity.HasNoKey().ToView("vDespesasRddv");

                entity.Property(e => e.IdRelatorio);
                entity.Property(e => e.Valor);
            });

            modelBuilder.Entity<Fornecedor>(entity =>
            {
                entity.HasKey(e => e.IdFornecedor);

                entity.ToTable("Fornecedor", tb => tb.HasTrigger("TRG"));

                entity.Property(e => e.NumDocumento).IsRequired().HasMaxLength(64).IsUnicode(false);
                entity.Property(e => e.Nome).IsRequired().HasMaxLength(256).IsUnicode(false);
                entity.Property(e => e.Codigo).IsRequired().HasMaxLength(128).IsUnicode(false);
            });

            modelBuilder.Entity<TipoViagem>(entity =>
            {
                entity.HasKey(e => e.IdTipoViagem);

                entity.ToTable("TipoViagem", tb => tb.HasTrigger("TRG"));

                entity.Property(e => e.IdTipoViagem).IsRequired();
                entity.Property(e => e.Nome).IsRequired();
                entity.Property(e => e.ValorDiaria).HasColumnType("money").IsRequired();
            });

            modelBuilder.Entity<ViewListagemSolicitacoes>(entity =>
            {
                entity.HasNoKey().ToView("vListagemSolicitacoes");
            });

            OnModelCreatingPartial(modelBuilder);
        }

        partial void OnModelCreatingPartial(ModelBuilder modelBuilder);
    }
}
