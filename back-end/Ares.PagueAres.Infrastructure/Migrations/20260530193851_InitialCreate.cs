using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Ares.PagueAres.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class InitialCreate : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "Configuracao",
                columns: table => new
                {
                    IdConfiguracao = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Chave = table.Column<string>(type: "varchar(512)", unicode: false, maxLength: 512, nullable: false),
                    Valor = table.Column<string>(type: "varchar(max)", unicode: false, nullable: false, defaultValueSql: "('')"),
                    DataAlteracao = table.Column<DateTime>(type: "datetime", nullable: true),
                    UsuarioAlteracao = table.Column<int>(type: "int", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Configuracao", x => x.IdConfiguracao);
                });

            migrationBuilder.CreateTable(
                name: "Departamento",
                columns: table => new
                {
                    IdDepartamento = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Nome = table.Column<string>(type: "varchar(256)", unicode: false, maxLength: 256, nullable: false),
                    CodigoLancamento = table.Column<string>(type: "varchar(256)", unicode: false, maxLength: 256, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Departamento", x => x.IdDepartamento);
                });

            migrationBuilder.CreateTable(
                name: "DespesaRddv",
                columns: table => new
                {
                    IdDespesaRddv = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    IdRelatorio = table.Column<int>(type: "int", nullable: false),
                    DataDespesa = table.Column<DateTime>(type: "datetime", nullable: false),
                    TipoDespesa = table.Column<int>(type: "int", nullable: false),
                    Moeda = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    Valor = table.Column<decimal>(type: "decimal(18,0)", nullable: false),
                    Quantidade = table.Column<decimal>(type: "decimal(18,0)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DespesaRddv", x => x.IdDespesaRddv);
                });

            migrationBuilder.CreateTable(
                name: "DocumentoRddv",
                columns: table => new
                {
                    IdDocumentoRddv = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    IdRelatorio = table.Column<int>(type: "int", nullable: false),
                    IdTipoDocumento = table.Column<int>(type: "int", nullable: false),
                    IdUsuario = table.Column<int>(type: "int", nullable: false),
                    DataCriacao = table.Column<DateTime>(type: "datetime", nullable: false, defaultValueSql: "(getdate())"),
                    Indice = table.Column<int>(type: "int", nullable: false),
                    NomeArquivo = table.Column<string>(type: "varchar(256)", unicode: false, maxLength: 256, nullable: false),
                    CaminhoArquivo = table.Column<string>(type: "varchar(1024)", unicode: false, maxLength: 1024, nullable: false),
                    Arquivo = table.Column<string>(type: "text", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DocumentoRddv", x => x.IdDocumentoRddv);
                });

            migrationBuilder.CreateTable(
                name: "DocumentoSolicitacao",
                columns: table => new
                {
                    IdDocumentoSolicitacao = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    IdSolicitacao = table.Column<int>(type: "int", nullable: false),
                    IdTipoDocumento = table.Column<int>(type: "int", nullable: false),
                    IdUsuario = table.Column<int>(type: "int", nullable: false),
                    DataCriacao = table.Column<DateTime>(type: "datetime", nullable: false, defaultValueSql: "(getdate())"),
                    Indice = table.Column<int>(type: "int", nullable: false),
                    NomeArquivo = table.Column<string>(type: "varchar(256)", unicode: false, maxLength: 256, nullable: false),
                    CaminhoArquivo = table.Column<string>(type: "varchar(1024)", unicode: false, maxLength: 1024, nullable: false),
                    Arquivo = table.Column<string>(type: "text", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DocumentoSolicitacao", x => x.IdDocumentoSolicitacao);
                });

            migrationBuilder.CreateTable(
                name: "Fornecedor",
                columns: table => new
                {
                    IdFornecedor = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    NumDocumento = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    Nome = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    Codigo = table.Column<string>(type: "nvarchar(max)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Fornecedor", x => x.IdFornecedor);
                });

            migrationBuilder.CreateTable(
                name: "Funcao",
                columns: table => new
                {
                    IdFuncao = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Nome = table.Column<string>(type: "varchar(256)", unicode: false, maxLength: 256, nullable: false),
                    Alias = table.Column<string>(type: "varchar(256)", unicode: false, maxLength: 256, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Funcao", x => x.IdFuncao);
                });

            migrationBuilder.CreateTable(
                name: "Rddv",
                columns: table => new
                {
                    IdRelatorio = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    IdUsuario = table.Column<int>(type: "int", nullable: false),
                    IdUsuarioAtribuido = table.Column<int>(type: "int", nullable: true),
                    DataAtribuicao = table.Column<DateTime>(type: "datetime2", nullable: true),
                    IdUsuarioAtribuidor = table.Column<int>(type: "int", nullable: true),
                    NomeFuncionario = table.Column<string>(type: "nvarchar(max)", nullable: false, defaultValueSql: "('')"),
                    CPF = table.Column<string>(type: "nvarchar(max)", nullable: false, defaultValueSql: "('')"),
                    IdDepartamento = table.Column<int>(type: "int", nullable: false),
                    Finalidade = table.Column<string>(type: "nvarchar(max)", nullable: false, defaultValueSql: "('')"),
                    TipoRelatorio = table.Column<int>(type: "int", nullable: false),
                    Moeda = table.Column<string>(type: "nvarchar(max)", nullable: false, defaultValueSql: "('')"),
                    TipoViagem = table.Column<int>(type: "int", nullable: false),
                    DataInicio = table.Column<DateTime>(type: "datetime", nullable: true),
                    DataFim = table.Column<DateTime>(type: "datetime", nullable: true),
                    DataCadastro = table.Column<DateTime>(type: "datetime", nullable: false),
                    DataVencimento = table.Column<DateTime>(type: "datetime", nullable: true),
                    LocalViagem = table.Column<string>(type: "nvarchar(max)", nullable: false, defaultValueSql: "('')"),
                    CentroCusto = table.Column<string>(type: "nvarchar(max)", nullable: false, defaultValueSql: "('')"),
                    Projeto = table.Column<string>(type: "nvarchar(max)", nullable: false, defaultValueSql: "('')"),
                    PCA = table.Column<string>(type: "nvarchar(max)", nullable: false, defaultValueSql: "('')"),
                    DiariaViagem = table.Column<bool>(type: "bit", nullable: true),
                    Diarias = table.Column<decimal>(type: "decimal(18,2)", nullable: true),
                    Adiantamento = table.Column<bool>(type: "bit", nullable: true),
                    ValorAdiantamento = table.Column<decimal>(type: "decimal(18,0)", nullable: false),
                    SaldoCartaoVtm = table.Column<decimal>(type: "decimal(18,0)", nullable: false),
                    Observacao = table.Column<string>(type: "nvarchar(max)", nullable: false, defaultValueSql: "('')"),
                    TipoAutorizacao = table.Column<int>(type: "int", nullable: false),
                    IdGestor = table.Column<int>(type: "int", nullable: true),
                    AprovadoGestor = table.Column<bool>(type: "bit", nullable: true),
                    ObservacaoGestor = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    AprovadoSetor = table.Column<bool>(type: "bit", nullable: true),
                    ObservacaoSetor = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    StatusGestor = table.Column<int>(type: "int", nullable: true),
                    StatusContabilidade = table.Column<int>(type: "int", nullable: true),
                    StatusFinanceiro = table.Column<int>(type: "int", nullable: true),
                    Banco = table.Column<string>(type: "nvarchar(max)", nullable: false, defaultValue: ""),
                    Agencia = table.Column<string>(type: "nvarchar(max)", nullable: false, defaultValue: ""),
                    Conta = table.Column<string>(type: "nvarchar(max)", nullable: false, defaultValue: ""),
                    ValorDiaria = table.Column<decimal>(type: "decimal(18,2)", nullable: false, defaultValue: 0m),
                    ValorKm = table.Column<decimal>(type: "decimal(18,2)", nullable: false, defaultValue: 0m),
                    DataAprovacaoGestor = table.Column<DateTime>(type: "datetime", nullable: true),
                    DataAprovacaoFinanceiro = table.Column<DateTime>(type: "datetime", nullable: true),
                    DataAprovacaoContabil = table.Column<DateTime>(type: "datetime", nullable: true),
                    Rascunho = table.Column<bool>(type: "bit", nullable: false, defaultValue: true),
                    Cancelado = table.Column<bool>(type: "bit", nullable: false, defaultValue: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Rddv", x => x.IdRelatorio);
                });

            migrationBuilder.CreateTable(
                name: "Solicitacao",
                columns: table => new
                {
                    IdSolicitacao = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    StatusGestor = table.Column<int>(type: "int", nullable: true, defaultValueSql: "((1))"),
                    StatusContabilidade = table.Column<int>(type: "int", nullable: true, defaultValueSql: "((1))"),
                    StatusFinanceiro = table.Column<int>(type: "int", nullable: true, defaultValueSql: "((1))"),
                    IdUsuario = table.Column<int>(type: "int", nullable: false),
                    IdDepartamento = table.Column<int>(type: "int", nullable: false),
                    IdUsuarioAtribuido = table.Column<int>(type: "int", nullable: true),
                    DataAtribuicao = table.Column<DateTime>(type: "datetime2", nullable: true),
                    IdUsuarioAtribuidor = table.Column<int>(type: "int", nullable: true),
                    DataSolicitacao = table.Column<DateTime>(type: "datetime", nullable: false, defaultValueSql: "(getdate())"),
                    TipoSolicitacao = table.Column<int>(type: "int", nullable: false, defaultValueSql: "((1))"),
                    TipoPagamento = table.Column<int>(type: "int", nullable: false),
                    DataDocumento = table.Column<DateTime>(type: "datetime", nullable: true),
                    DataVencimento = table.Column<DateTime>(type: "datetime", nullable: true),
                    NumeroDocumento = table.Column<string>(type: "varchar(64)", unicode: false, maxLength: 64, nullable: true, defaultValueSql: "('')"),
                    Descricao = table.Column<string>(type: "varchar(512)", unicode: false, maxLength: 512, nullable: false, defaultValueSql: "('')"),
                    Valor = table.Column<decimal>(type: "decimal(12,2)", nullable: false),
                    Parcelado = table.Column<bool>(type: "bit", nullable: true),
                    Parcelas = table.Column<int>(type: "int", nullable: true, defaultValueSql: "((1))"),
                    FormaPagamento = table.Column<int>(type: "int", nullable: false, defaultValueSql: "((1))"),
                    CentroCusto = table.Column<string>(type: "varchar(128)", unicode: false, maxLength: 128, nullable: false, defaultValueSql: "('')"),
                    Projeto = table.Column<string>(type: "varchar(128)", unicode: false, maxLength: 128, nullable: false, defaultValueSql: "('')"),
                    PCA = table.Column<string>(type: "varchar(128)", unicode: false, maxLength: 128, nullable: false, defaultValueSql: "('')"),
                    NumDocParceiro = table.Column<string>(type: "varchar(64)", unicode: false, maxLength: 64, nullable: false, defaultValueSql: "('')"),
                    CodigoParceiro = table.Column<string>(type: "varchar(64)", unicode: false, maxLength: 64, nullable: false, defaultValueSql: "('')"),
                    NomeParceiro = table.Column<string>(type: "varchar(128)", unicode: false, maxLength: 128, nullable: false, defaultValueSql: "('')"),
                    BancoParceiro = table.Column<string>(type: "varchar(64)", unicode: false, maxLength: 64, nullable: false, defaultValueSql: "('')"),
                    AgenciaParceiro = table.Column<string>(type: "varchar(32)", unicode: false, maxLength: 32, nullable: false, defaultValueSql: "('')"),
                    ContaParceiro = table.Column<string>(type: "varchar(32)", unicode: false, maxLength: 32, nullable: false, defaultValueSql: "('')"),
                    Observacao = table.Column<string>(type: "varchar(2048)", unicode: false, maxLength: 2048, nullable: false, defaultValueSql: "('')"),
                    TipoAutorizacao = table.Column<int>(type: "int", nullable: false, defaultValueSql: "((1))"),
                    IdGestor = table.Column<int>(type: "int", nullable: true),
                    AprovadoGestor = table.Column<bool>(type: "bit", nullable: true),
                    ObservacaoGestor = table.Column<string>(type: "varchar(512)", unicode: false, maxLength: 512, nullable: false, defaultValueSql: "('')"),
                    AprovadoSetor = table.Column<bool>(type: "bit", nullable: true),
                    ObservacaoSetor = table.Column<string>(type: "varchar(512)", unicode: false, maxLength: 512, nullable: false, defaultValueSql: "('')"),
                    DataAprovacaoGestor = table.Column<DateTime>(type: "datetime", nullable: true),
                    DataAprovacaoFinanceiro = table.Column<DateTime>(type: "datetime", nullable: true),
                    DataAprovacaoContabil = table.Column<DateTime>(type: "datetime", nullable: true),
                    Rascunho = table.Column<bool>(type: "bit", nullable: false, defaultValue: true),
                    Cancelado = table.Column<bool>(type: "bit", nullable: false, defaultValue: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Solicitacao", x => x.IdSolicitacao);
                });

            migrationBuilder.CreateTable(
                name: "TipoDocumento",
                columns: table => new
                {
                    IdTipoDocumento = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Nome = table.Column<string>(type: "varchar(64)", unicode: false, maxLength: 64, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TipoDocumento", x => x.IdTipoDocumento);
                });

            migrationBuilder.CreateTable(
                name: "TipoViagem",
                columns: table => new
                {
                    IdTipoViagem = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Nome = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    ValorDiaria = table.Column<decimal>(type: "money", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TipoViagem", x => x.IdTipoViagem);
                });

            migrationBuilder.CreateTable(
                name: "Usuario",
                columns: table => new
                {
                    IdUsuario = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Email = table.Column<string>(type: "varchar(512)", unicode: false, maxLength: 512, nullable: false),
                    Codigo = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    Nome = table.Column<string>(type: "varchar(512)", unicode: false, maxLength: 512, nullable: false),
                    Origem = table.Column<int>(type: "int", nullable: false),
                    IdExterno = table.Column<string>(type: "varchar(1024)", unicode: false, maxLength: 1024, nullable: true),
                    Senha = table.Column<string>(type: "varchar(512)", unicode: false, maxLength: 512, nullable: false),
                    IdDepartamento = table.Column<int>(type: "int", nullable: false),
                    Ativo = table.Column<bool>(type: "bit", nullable: false, defaultValueSql: "((1))"),
                    DataCadastro = table.Column<DateTime>(type: "datetime", nullable: false, defaultValueSql: "(getdate())"),
                    UsuarioCadastro = table.Column<int>(type: "int", nullable: true),
                    UltimaAlteracao = table.Column<DateTime>(type: "datetime", nullable: true),
                    UsuarioAlteracao = table.Column<int>(type: "int", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Usuario", x => x.IdUsuario);
                });

            migrationBuilder.CreateTable(
                name: "UsuarioFuncao",
                columns: table => new
                {
                    IdUsuario = table.Column<int>(type: "int", nullable: false),
                    IdFuncao = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_UsuarioFuncao", x => new { x.IdUsuario, x.IdFuncao });
                });

            // FK constraints omitidos do OnModelCreating (navigation properties comentadas)
            migrationBuilder.AddForeignKey("FK_Solicitacao_Usuario",        "Solicitacao",        "IdUsuario",      "Usuario",      principalColumn: "IdUsuario");
            migrationBuilder.AddForeignKey("FK_Rddv_Usuario",               "Rddv",               "IdUsuario",      "Usuario",      principalColumn: "IdUsuario");
            migrationBuilder.AddForeignKey("FK_DespesaRddv_Rddv",           "DespesaRddv",        "IdRelatorio",    "Rddv",         principalColumn: "IdRelatorio");
            migrationBuilder.AddForeignKey("FK_DocumentoSolicitacao_Solicitacao", "DocumentoSolicitacao", "IdSolicitacao",  "Solicitacao",  principalColumn: "IdSolicitacao");
            migrationBuilder.AddForeignKey("FK_DocumentoSolicitacao_TipoDocumento", "DocumentoSolicitacao", "IdTipoDocumento", "TipoDocumento", principalColumn: "IdTipoDocumento");
            migrationBuilder.AddForeignKey("FK_DocumentoSolicitacao_Usuario", "DocumentoSolicitacao", "IdUsuario",     "Usuario",      principalColumn: "IdUsuario");
            migrationBuilder.AddForeignKey("FK_DocumentoRddv_Rddv",         "DocumentoRddv",      "IdRelatorio",    "Rddv",         principalColumn: "IdRelatorio");
            migrationBuilder.AddForeignKey("FK_DocumentoRddv_TipoDocumento", "DocumentoRddv",      "IdTipoDocumento","TipoDocumento", principalColumn: "IdTipoDocumento");
            migrationBuilder.AddForeignKey("FK_DocumentoRddv_Usuario",      "DocumentoRddv",      "IdUsuario",      "Usuario",      principalColumn: "IdUsuario");
            migrationBuilder.AddForeignKey("FK_UsuarioFuncao_Usuario",       "UsuarioFuncao",      "IdUsuario",      "Usuario",      principalColumn: "IdUsuario");
            migrationBuilder.AddForeignKey("FK_UsuarioFuncao_Funcao",        "UsuarioFuncao",      "IdFuncao",       "Funcao",       principalColumn: "IdFuncao");

            // Unique constraint do Fornecedor
            migrationBuilder.CreateIndex("UK_Fornecedor", "Fornecedor", "NumDocumento", unique: true);

            // Seed: Departamentos
            migrationBuilder.Sql(@"
SET IDENTITY_INSERT Departamento ON;
IF NOT EXISTS (SELECT 1 FROM Departamento WHERE IdDepartamento = 1)  INSERT INTO Departamento (IdDepartamento, Nome, CodigoLancamento) VALUES (1,  'TI',            'TIN');
IF NOT EXISTS (SELECT 1 FROM Departamento WHERE IdDepartamento = 2)  INSERT INTO Departamento (IdDepartamento, Nome, CodigoLancamento) VALUES (2,  'Almoxarifado',  'ALM');
IF NOT EXISTS (SELECT 1 FROM Departamento WHERE IdDepartamento = 3)  INSERT INTO Departamento (IdDepartamento, Nome, CodigoLancamento) VALUES (3,  'Compras',       'CPS');
IF NOT EXISTS (SELECT 1 FROM Departamento WHERE IdDepartamento = 4)  INSERT INTO Departamento (IdDepartamento, Nome, CodigoLancamento) VALUES (4,  'Contabilidade', 'CTB');
IF NOT EXISTS (SELECT 1 FROM Departamento WHERE IdDepartamento = 5)  INSERT INTO Departamento (IdDepartamento, Nome, CodigoLancamento) VALUES (5,  'Diretoria',     'DIR');
IF NOT EXISTS (SELECT 1 FROM Departamento WHERE IdDepartamento = 6)  INSERT INTO Departamento (IdDepartamento, Nome, CodigoLancamento) VALUES (6,  'Engenharia',    'ENG');
IF NOT EXISTS (SELECT 1 FROM Departamento WHERE IdDepartamento = 7)  INSERT INTO Departamento (IdDepartamento, Nome, CodigoLancamento) VALUES (7,  'Financeiro',    'FIN');
IF NOT EXISTS (SELECT 1 FROM Departamento WHERE IdDepartamento = 8)  INSERT INTO Departamento (IdDepartamento, Nome, CodigoLancamento) VALUES (8,  'ILS',           'ILS');
IF NOT EXISTS (SELECT 1 FROM Departamento WHERE IdDepartamento = 9)  INSERT INTO Departamento (IdDepartamento, Nome, CodigoLancamento) VALUES (9,  'Infraestrutura','INF');
IF NOT EXISTS (SELECT 1 FROM Departamento WHERE IdDepartamento = 10) INSERT INTO Departamento (IdDepartamento, Nome, CodigoLancamento) VALUES (10, 'Marketing',     'MKT');
IF NOT EXISTS (SELECT 1 FROM Departamento WHERE IdDepartamento = 11) INSERT INTO Departamento (IdDepartamento, Nome, CodigoLancamento) VALUES (11, 'Montagem',      'MTG');
IF NOT EXISTS (SELECT 1 FROM Departamento WHERE IdDepartamento = 12) INSERT INTO Departamento (IdDepartamento, Nome, CodigoLancamento) VALUES (12, N'Produção',     'PRD');
IF NOT EXISTS (SELECT 1 FROM Departamento WHERE IdDepartamento = 13) INSERT INTO Departamento (IdDepartamento, Nome, CodigoLancamento) VALUES (13, 'Projetos',      'PJT');
IF NOT EXISTS (SELECT 1 FROM Departamento WHERE IdDepartamento = 14) INSERT INTO Departamento (IdDepartamento, Nome, CodigoLancamento) VALUES (14, 'Qualidade',     'QLD');
IF NOT EXISTS (SELECT 1 FROM Departamento WHERE IdDepartamento = 15) INSERT INTO Departamento (IdDepartamento, Nome, CodigoLancamento) VALUES (15, 'RH',            'RHU');
SET IDENTITY_INSERT Departamento OFF;
");

            // Seed: Funções
            migrationBuilder.Sql(@"
SET IDENTITY_INSERT Funcao ON;
IF NOT EXISTS (SELECT 1 FROM Funcao WHERE IdFuncao = 1) INSERT INTO Funcao (IdFuncao, Nome, Alias) VALUES (1, 'Administrador', 'ADM');
IF NOT EXISTS (SELECT 1 FROM Funcao WHERE IdFuncao = 2) INSERT INTO Funcao (IdFuncao, Nome, Alias) VALUES (2, 'Solicitante',   'SOL');
IF NOT EXISTS (SELECT 1 FROM Funcao WHERE IdFuncao = 3) INSERT INTO Funcao (IdFuncao, Nome, Alias) VALUES (3, 'Gestor',        'GES');
IF NOT EXISTS (SELECT 1 FROM Funcao WHERE IdFuncao = 4) INSERT INTO Funcao (IdFuncao, Nome, Alias) VALUES (4, 'Financeiro',    'FIN');
IF NOT EXISTS (SELECT 1 FROM Funcao WHERE IdFuncao = 5) INSERT INTO Funcao (IdFuncao, Nome, Alias) VALUES (5, 'Contabilidade', 'CON');
SET IDENTITY_INSERT Funcao OFF;
");

            // Seed: Tipos de Documento
            migrationBuilder.Sql(@"
IF NOT EXISTS (SELECT 1 FROM TipoDocumento WHERE Nome = 'Nota Fiscal')       INSERT INTO TipoDocumento (Nome) VALUES ('Nota Fiscal');
IF NOT EXISTS (SELECT 1 FROM TipoDocumento WHERE Nome = 'Fatura')             INSERT INTO TipoDocumento (Nome) VALUES ('Fatura');
IF NOT EXISTS (SELECT 1 FROM TipoDocumento WHERE Nome = N'Nota de Débito')   INSERT INTO TipoDocumento (Nome) VALUES (N'Nota de Débito');
IF NOT EXISTS (SELECT 1 FROM TipoDocumento WHERE Nome = 'Boleto')             INSERT INTO TipoDocumento (Nome) VALUES ('Boleto');
IF NOT EXISTS (SELECT 1 FROM TipoDocumento WHERE Nome = 'RDDV')               INSERT INTO TipoDocumento (Nome) VALUES ('RDDV');
IF NOT EXISTS (SELECT 1 FROM TipoDocumento WHERE Nome = 'Comprovantes')       INSERT INTO TipoDocumento (Nome) VALUES ('Comprovantes');
IF NOT EXISTS (SELECT 1 FROM TipoDocumento WHERE Nome = 'Arquivo Zipado')     INSERT INTO TipoDocumento (Nome) VALUES ('Arquivo Zipado');
IF NOT EXISTS (SELECT 1 FROM TipoDocumento WHERE Nome = 'Outros Documentos')  INSERT INTO TipoDocumento (Nome) VALUES ('Outros Documentos');
IF NOT EXISTS (SELECT 1 FROM TipoDocumento WHERE Nome = N'Autorização')       INSERT INTO TipoDocumento (Nome) VALUES (N'Autorização');
");

            // Seed: Tipos de Viagem
            migrationBuilder.Sql(@"
SET IDENTITY_INSERT TipoViagem ON;
IF NOT EXISTS (SELECT 1 FROM TipoViagem WHERE IdTipoViagem = 1) INSERT INTO TipoViagem (IdTipoViagem, Nome, ValorDiaria) VALUES (1, 'Nacional',             0);
IF NOT EXISTS (SELECT 1 FROM TipoViagem WHERE IdTipoViagem = 2) INSERT INTO TipoViagem (IdTipoViagem, Nome, ValorDiaria) VALUES (2, 'Internacional',         0);
IF NOT EXISTS (SELECT 1 FROM TipoViagem WHERE IdTipoViagem = 3) INSERT INTO TipoViagem (IdTipoViagem, Nome, ValorDiaria) VALUES (3, 'Internacional Israel',  0);
IF NOT EXISTS (SELECT 1 FROM TipoViagem WHERE IdTipoViagem = 4) INSERT INTO TipoViagem (IdTipoViagem, Nome, ValorDiaria) VALUES (4, 'Internacional Euro',    0);
SET IDENTITY_INSERT TipoViagem OFF;
");

            // Seed: Usuário admin (senha = 123456, hash SHA256)
            migrationBuilder.Sql(@"
SET IDENTITY_INSERT Usuario ON;
IF NOT EXISTS (SELECT 1 FROM Usuario WHERE IdUsuario = 1)
    INSERT INTO Usuario (IdUsuario, Email, Nome, Origem, IdExterno, Senha, IdDepartamento, Ativo, DataCadastro, UsuarioCadastro, UltimaAlteracao, UsuarioAlteracao)
    VALUES (1, 'admin@local', 'Administrador', 0, '', '8d969eef6ecad3c29a3a629280e686cf0c3f5d5a86aff3ca12020c923adc6c92', 1, 1, GETDATE(), NULL, NULL, NULL);
SET IDENTITY_INSERT Usuario OFF;
IF NOT EXISTS (SELECT 1 FROM UsuarioFuncao WHERE IdUsuario = 1 AND IdFuncao = 1)
    INSERT INTO UsuarioFuncao VALUES (1, 1);
");

            // Views — vDespesasRddv deve ser criada ANTES de vListagemSolicitacoes
            migrationBuilder.Sql(@"
CREATE VIEW [dbo].[vDespesasRddv] AS
SELECT
    R.IdRelatorio,
    (CASE WHEN R.DiariaViagem = 1
          THEN R.Diarias
          ELSE 0 END * ISNULL(R.ValorDiaria, 0)) +
    Desp.TotalDespesas - (ISNULL(R.ValorAdiantamento, 0.00) + ISNULL(R.SaldoCartaoVtm, 0.00)) AS Valor
FROM Rddv R
OUTER APPLY (
    SELECT ISNULL(SUM(Valor), 0) AS TotalDespesas
    FROM DespesaRddv D
    WHERE D.IdRelatorio = R.IdRelatorio
) Desp;
");

            migrationBuilder.Sql(@"
CREATE VIEW [dbo].[vListagemSolicitacoes] AS
SELECT
     S.IdSolicitacao AS Codigo
    ,0 AS IdTipo
    ,'Pagamento' AS Tipo
    ,S.DataSolicitacao
    ,S.DataVencimento
    ,S.NumeroDocumento
    ,US.Nome AS NomeSolicitante
    ,US.IdUsuario AS IdSolicitante
    ,UA.Nome AS UsuarioAtribuido
    ,UA.IdUsuario AS IdUsuarioAtribuido
    ,S.NomeParceiro
    ,S.Valor
    ,S.Descricao
    ,S.ObservacaoGestor
    ,S.StatusGestor        AS IdStatusGestor
    ,S.StatusFinanceiro    AS IdStatusFinanceiro
    ,S.StatusContabilidade AS IdStatusContabilidade
    ,CASE S.StatusGestor
     WHEN 1 THEN N'Aguardando Autorização' WHEN 2 THEN 'Autorizado' WHEN 3 THEN 'Negado'
     ELSE N'Aguardando Autorização' END AS StatusGestor
    ,CASE S.StatusFinanceiro
     WHEN 1 THEN N'Aguardando Autorização' WHEN 2 THEN 'Autorizado' WHEN 3 THEN 'Negado'
     ELSE N'Aguardando Autorização' END AS StatusFinanceiro
    ,CASE S.StatusContabilidade
     WHEN 1 THEN N'Aguardando Lançamento' WHEN 2 THEN N'Lançado' WHEN 3 THEN 'Negado'
     ELSE N'Aguardando Lançamento' END AS StatusContabilidade
    ,IdGestor, AprovadoGestor, AprovadoSetor, Rascunho, Cancelado
FROM Solicitacao S
INNER JOIN Usuario US ON US.IdUsuario = S.IdUsuario
LEFT  JOIN Usuario UA ON UA.IdUsuario = S.IdUsuarioAtribuido

UNION ALL

SELECT
     R.IdRelatorio AS Codigo
    ,R.TipoRelatorio AS IdTipo
    ,CONCAT('RDDV - ', CASE R.TipoRelatorio WHEN 1 THEN 'Adiantamento' WHEN 2 THEN N'Prestação de Contas' WHEN 3 THEN 'Reembolso' END) AS Tipo
    ,R.DataCadastro AS DataSolicitacao
    ,R.DataVencimento
    ,'#' AS NumeroDocumento
    ,US.Nome AS NomeSolicitante, US.IdUsuario AS IdSolicitante
    ,UA.Nome AS UsuarioAtribuido, UA.IdUsuario AS IdUsuarioAtribuido
    ,'#' AS NomeParceiro
    ,D.Valor
    ,'#' AS Descricao
    ,R.ObservacaoGestor
    ,R.StatusGestor        AS IdStatusGestor
    ,R.StatusFinanceiro    AS IdStatusFinanceiro
    ,R.StatusContabilidade AS IdStatusContabilidade
    ,CASE R.StatusGestor
     WHEN 1 THEN N'Aguardando Autorização' WHEN 2 THEN 'Autorizado' WHEN 3 THEN 'Negado'
     ELSE N'Aguardando Autorização' END AS StatusGestor
    ,CASE R.StatusFinanceiro
     WHEN 1 THEN N'Aguardando Autorização' WHEN 2 THEN 'Autorizado' WHEN 3 THEN 'Negado'
     ELSE N'Aguardando Autorização' END AS StatusFinanceiro
    ,CASE R.StatusContabilidade
     WHEN 1 THEN N'Aguardando Lançamento' WHEN 2 THEN N'Lançado' WHEN 3 THEN 'Negado'
     ELSE N'Aguardando Lançamento' END AS StatusContabilidade
    ,IdGestor, AprovadoGestor, AprovadoSetor, Rascunho, Cancelado
FROM Rddv R
INNER JOIN vDespesasRddv D  ON D.IdRelatorio = R.IdRelatorio
INNER JOIN Usuario US       ON US.IdUsuario  = R.IdUsuario
LEFT  JOIN Usuario UA       ON UA.IdUsuario  = R.IdUsuarioAtribuido;
");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Drop views primeiro (vListagemSolicitacoes depende de vDespesasRddv)
            migrationBuilder.Sql("DROP VIEW IF EXISTS [dbo].[vListagemSolicitacoes]");
            migrationBuilder.Sql("DROP VIEW IF EXISTS [dbo].[vDespesasRddv]");

            // Drop FKs antes das tabelas
            migrationBuilder.DropForeignKey("FK_DocumentoRddv_Rddv",               "DocumentoRddv");
            migrationBuilder.DropForeignKey("FK_DocumentoRddv_TipoDocumento",       "DocumentoRddv");
            migrationBuilder.DropForeignKey("FK_DocumentoRddv_Usuario",             "DocumentoRddv");
            migrationBuilder.DropForeignKey("FK_DocumentoSolicitacao_Solicitacao",  "DocumentoSolicitacao");
            migrationBuilder.DropForeignKey("FK_DocumentoSolicitacao_TipoDocumento","DocumentoSolicitacao");
            migrationBuilder.DropForeignKey("FK_DocumentoSolicitacao_Usuario",      "DocumentoSolicitacao");
            migrationBuilder.DropForeignKey("FK_DespesaRddv_Rddv",                 "DespesaRddv");
            migrationBuilder.DropForeignKey("FK_Rddv_Usuario",                     "Rddv");
            migrationBuilder.DropForeignKey("FK_Solicitacao_Usuario",               "Solicitacao");
            migrationBuilder.DropForeignKey("FK_UsuarioFuncao_Funcao",              "UsuarioFuncao");
            migrationBuilder.DropForeignKey("FK_UsuarioFuncao_Usuario",             "UsuarioFuncao");
            migrationBuilder.DropIndex("UK_Fornecedor", "Fornecedor");

            migrationBuilder.DropTable(name: "Configuracao");
            migrationBuilder.DropTable(name: "Departamento");
            migrationBuilder.DropTable(name: "DespesaRddv");
            migrationBuilder.DropTable(name: "DocumentoRddv");
            migrationBuilder.DropTable(name: "DocumentoSolicitacao");
            migrationBuilder.DropTable(name: "Fornecedor");
            migrationBuilder.DropTable(name: "Funcao");
            migrationBuilder.DropTable(name: "Rddv");
            migrationBuilder.DropTable(name: "Solicitacao");
            migrationBuilder.DropTable(name: "TipoDocumento");
            migrationBuilder.DropTable(name: "TipoViagem");
            migrationBuilder.DropTable(name: "Usuario");
            migrationBuilder.DropTable(name: "UsuarioFuncao");
        }
    }
}
