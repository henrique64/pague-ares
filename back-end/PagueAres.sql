-- Configuração do Sistema

CREATE TABLE Configuracao (
	IdConfiguracao		INT NOT NULL IDENTITY,
	Chave				VARCHAR(512) NOT NULL,
	Valor				VARCHAR(MAX) NOT NULL DEFAULT (''),
	DataAlteracao		DATETIME NULL,
	UsuarioAlteracao	INT NULL,
	CONSTRAINT PK_Configuracao PRIMARY KEY (IdConfiguracao)
)

-- Cadastro de Usuários

CREATE TABLE Departamento (
	IdDepartamento		INT NOT NULL IDENTITY,
	Nome				VARCHAR(256) NOT NULL,
	CodigoLancamento	VARCHAR(256),
	CONSTRAINT PK_Departamento PRIMARY KEY (IdDepartamento)
)
GO

SET IDENTITY_INSERT Departamento ON

INSERT INTO Departamento (IdDepartamento, Nome, CodigoLancamento) VALUES (1, 'TI', 'TIN')
INSERT INTO Departamento (IdDepartamento, Nome, CodigoLancamento) VALUES (2, 'Almoxarifado', 'ALM')
INSERT INTO Departamento (IdDepartamento, Nome, CodigoLancamento) VALUES (3, 'Compras', 'CPS')
INSERT INTO Departamento (IdDepartamento, Nome, CodigoLancamento) VALUES (4, 'Contabilidade', 'CTB')
INSERT INTO Departamento (IdDepartamento, Nome, CodigoLancamento) VALUES (5, 'Diretoria', 'DIR')
INSERT INTO Departamento (IdDepartamento, Nome, CodigoLancamento) VALUES (6, 'Engenharia', 'ENG')
INSERT INTO Departamento (IdDepartamento, Nome, CodigoLancamento) VALUES (7, 'Financeiro', 'FIN')
INSERT INTO Departamento (IdDepartamento, Nome, CodigoLancamento) VALUES (8, 'ILS', 'ILS')
INSERT INTO Departamento (IdDepartamento, Nome, CodigoLancamento) VALUES (9, 'Infraestrutura', 'INF')
INSERT INTO Departamento (IdDepartamento, Nome, CodigoLancamento) VALUES (10, 'Marketing', 'MKT')
INSERT INTO Departamento (IdDepartamento, Nome, CodigoLancamento) VALUES (11, 'Montagem', 'MTG')
INSERT INTO Departamento (IdDepartamento, Nome, CodigoLancamento) VALUES (12, 'Produção', 'PRD')
INSERT INTO Departamento (IdDepartamento, Nome, CodigoLancamento) VALUES (13, 'Projetos', 'PJT')
INSERT INTO Departamento (IdDepartamento, Nome, CodigoLancamento) VALUES (14, 'Qualidade', 'QLD')
INSERT INTO Departamento (IdDepartamento, Nome, CodigoLancamento) VALUES (15, 'RH', 'RHU')


SET IDENTITY_INSERT Departamento OFF

GO

CREATE TABLE Funcao (
	IdFuncao		INT NOT NULL IDENTITY,
	Nome			VARCHAR(256) NOT NULL,
	Alias			VARCHAR(256),
	CONSTRAINT PK_Funcao PRIMARY KEY (IdFuncao)
)
GO

SET IDENTITY_INSERT Funcao ON

INSERT INTO Funcao (IdFuncao, Nome, Alias) VALUES (1, 'Administrador', 'ADM')
INSERT INTO Funcao (IdFuncao, Nome, Alias) VALUES (2, 'Solicitante', 'SOL')
INSERT INTO Funcao (IdFuncao, Nome, Alias) VALUES (3, 'Gestor', 'GES')
INSERT INTO Funcao (IdFuncao, Nome, Alias) VALUES (4, 'Financeiro', 'FIN')
INSERT INTO Funcao (IdFuncao, Nome, Alias) VALUES (5, 'Contabilidade', 'CON')

SET IDENTITY_INSERT Funcao OFF

GO

CREATE TABLE Usuario (
	IdUsuario			INT NOT NULL IDENTITY,
	Email				VARCHAR(512) NOT NULL,
	Nome				VARCHAR(512) NOT NULL,
	Origem				INT NOT NULL,
	IdExterno			VARCHAR(1024) NULL,
	Senha				VARCHAR(512) NOT NULL,
	IdDepartamento		INT NOT NULL,
	Ativo				BIT NOT NULL DEFAULT (1),
	DataCadastro		DATETIME NOT NULL DEFAULT (GETDATE()),
	UsuarioCadastro		INT NULL,
	UltimaAlteracao		DATETIME NULL,
	UsuarioAlteracao	INT NULL,
	Codigo				VARCHAR(128) NULL DEFAULT (''),
	CONSTRAINT PK_Usuario PRIMARY KEY (IdUsuario)
)
GO

SET IDENTITY_INSERT Usuario ON

INSERT INTO Usuario (
	 IdUsuario		
	,Email			
	,Nome			
	,Origem			
	,IdExterno		
	,Senha			
	,IdDepartamento	
	,Ativo			
	,DataCadastro	
	,UsuarioCadastro	
	,UltimaAlteracao	
	,UsuarioAlteracao
)
VALUES (
	 1
	,'admin@local'
	,'Administrador'
	,0
	,''
	,'8d969eef6ecad3c29a3a629280e686cf0c3f5d5a86aff3ca12020c923adc6c92' -- 123456
	,1
	,1
	,GETDATE()
	,NULL
	,NULL
	,NULL
)

SET IDENTITY_INSERT Usuario OFF

GO

CREATE TABLE UsuarioFuncao (
	IdUsuario	INT NOT NULL,
	IdFuncao	INT NOT NULL,
	CONSTRAINT PK_UsuarioFuncao PRIMARY KEY (IdUsuario, IdFuncao),
	CONSTRAINT FK_UsuarioFuncao_Usuario FOREIGN KEY (IdUsuario) REFERENCES Usuario (IdUsuario),
	CONSTRAINT FK_UsuarioFuncao_Funcao FOREIGN KEY (IdFuncao) REFERENCES Funcao (IdFuncao)
)
GO

INSERT INTO UsuarioFuncao VALUES (1, 1)

GO

CREATE TABLE Solicitacao (
	IdSolicitacao			INT NOT NULL IDENTITY,
	StatusGestor			INT NULL DEFAULT (1),
	StatusContabilidade		INT NULL DEFAULT (1),
	StatusFinanceiro		INT NULL DEFAULT (1),
	IdUsuario				INT NOT NULL,
	IdDepartamento			INT NOT NULL,
	IdUsuarioAtribuido		INT NULL,
	DataAtribuicao			DATETIME NULL,
	IdUsuarioAtribuidor		INT NULL,
	DataSolicitacao			DATETIME NOT NULL DEFAULT (GETDATE()),
	TipoSolicitacao			INT NOT NULL DEFAULT (1),
	TipoPagamento			INT NOT NULL DEFAULT (1),
	DataDocumento			DATETIME,
	DataVencimento			DATETIME,
	NumeroDocumento			VARCHAR(64) NULL DEFAULT (''),
	Descricao				VARCHAR(512) NOT NULL DEFAULT (''),
	Valor					DECIMAL (12, 2) NOT NULL DEFAULT (0),
	Parcelado				BIT NULL DEFAULT (0),
	Parcelas				INT NULL DEFAULT (1),
	FormaPagamento			INT NOT NULL DEFAULT (1),
	CentroCusto				VARCHAR(128) NOT NULL DEFAULT (''),
	Projeto					VARCHAR(128) NOT NULL DEFAULT (''),
	PCA						VARCHAR(128) NOT NULL DEFAULT (''),
	NumDocParceiro			VARCHAR(64) NOT NULL DEFAULT (''),
	CodigoParceiro			VARCHAR(64) NOT NULL DEFAULT (''),
	NomeParceiro			VARCHAR(128) NOT NULL DEFAULT (''),
	BancoParceiro			VARCHAR(64) NOT NULL DEFAULT (''),
	AgenciaParceiro			VARCHAR(32) NOT NULL DEFAULT (''),
	ContaParceiro			VARCHAR(32) NOT NULL DEFAULT (''),
	Observacao				VARCHAR(2048) NOT NULL DEFAULT (''),
	TipoAutorizacao			INT NOT NULL DEFAULT (1),
	IdGestor				INT NULL,
	AprovadoGestor			BIT NULL,
	ObservacaoGestor		VARCHAR(512) NOT NULL DEFAULT (''),
	AprovadoSetor			BIT NULL,
	ObservacaoSetor			VARCHAR(512) NOT NULL DEFAULT (''),
	DataAprovacaoGestor		DATETIME NULL,
	DataAprovacaoFinanceiro DATETIME NULL,
	DataAprovacaoContabil	DATETIME NULL,
	Rascunho				BIT NOT NULL DEFAULT 1,
	Cancelado				BIT NOT NULL DEFAULT 0,
	CONSTRAINT PK_Solicitacao PRIMARY KEY (IdSolicitacao),
	CONSTRAINT FK_Solicitacao_Usuario FOREIGN KEY (IdUsuario) REFERENCES Usuario (IdUsuario)
)
GO

CREATE TABLE TipoDocumento (
	IdTipoDocumento	INT NOT NULL IDENTITY,
	Nome VARCHAR(64),
	CONSTRAINT PK_TipoDocumento PRIMARY KEY (IdTipoDocumento)
)
GO

INSERT INTO TipoDocumento (Nome) VALUES ('Nota Fiscal')
INSERT INTO TipoDocumento (Nome) VALUES ('Fatura')
INSERT INTO TipoDocumento (Nome) VALUES ('Nota de Débito')
INSERT INTO TipoDocumento (Nome) VALUES ('Boleto')
INSERT INTO TipoDocumento (Nome) VALUES ('RDDV')
INSERT INTO TipoDocumento (Nome) VALUES ('Comprovantes')
INSERT INTO TipoDocumento (Nome) VALUES ('Arquivo Zipado')
INSERT INTO TipoDocumento (Nome) VALUES ('Outros Documentos')
INSERT INTO TipoDocumento (Nome) VALUES ('Autorização')

GO

CREATE TABLE DocumentoSolicitacao (
	IdDocumentoSolicitacao	INT NOT NULL IDENTITY,
	IdSolicitacao			INT NOT NULL,
	IdTipoDocumento			INT NOT NULL,
	IdUsuario				INT NOT NULL,
	DataCriacao				DATETIME NOT NULL DEFAULT (GETDATE()),
	Indice					INT NOT NULL,
	NomeArquivo				VARCHAR(256) NOT NULL,
	CaminhoArquivo			VARCHAR(1024) NOT NULL,
	Arquivo					TEXT NOT NULL,
	CONSTRAINT PK_DocumentoSolicitacao PRIMARY KEY (IdDocumentoSolicitacao),
	CONSTRAINT FK_DocumentoSolicitacao_Solicitacao FOREIGN KEY (IdSolicitacao) REFERENCES Solicitacao (IdSolicitacao),
	CONSTRAINT FK_DocumentoSolicitacao_TipoDocumento FOREIGN KEY (IdTipoDocumento) REFERENCES TipoDocumento (IdTipoDocumento),
	CONSTRAINT FK_DocumentoSolicitacao_Usuario FOREIGN KEY (IdUsuario) REFERENCES Usuario (IdUsuario)
)
GO

CREATE TABLE Rddv (
	IdRelatorio				INT NOT NULL IDENTITY,
	IdUsuario				INT NOT NULL,
	IdUsuarioAtribuido		INT NULL,
	DataAtribuicao			DATETIME NULL,
	IdUsuarioAtribuidor		INT NULL,
	DataCadastro			DATETIME NOT NULL DEFAULT (GETDATE()),
	NomeFuncionario			VARCHAR(512) NOT NULL DEFAULT (''),
	CPF						VARCHAR(512) NOT NULL DEFAULT (''),
	IdDepartamento			INT NOT NULL,
	Finalidade				VARCHAR(512) NOT NULL DEFAULT (''),
	TipoRelatorio			INT NOT NULL,
	Moeda					VARCHAR(16) NOT NULL DEFAULT (''),
	TipoViagem				INT NOT NULL,
	DataInicio				DATETIME,
	DataFim					DATETIME,
	LocalViagem				VARCHAR(512) NOT NULL DEFAULT (''),
	CentroCusto				VARCHAR(512) NOT NULL DEFAULT (''),
	Projeto					VARCHAR(512) NOT NULL DEFAULT (''),
	PCA						VARCHAR(512) NOT NULL DEFAULT (''),
	DiariaViagem			BIT NULL,
	Diarias					MONEY NULL,
	Adiantamento			BIT NULL,
	ValorAdiantamento		MONEY NOT NULL,
	SaldoCartaoVtm			MONEY NOT NULL,
	Observacao				VARCHAR(512) NOT NULL DEFAULT (''),
	TipoAutorizacao			INT NOT NULL DEFAULT (1),
	IdGestor				INT NULL,
	AprovadoGestor			BIT NULL,
	ObservacaoGestor		VARCHAR(512) NOT NULL DEFAULT (''),
	AprovadoSetor			BIT NULL,
	ObservacaoSetor			VARCHAR(512) NOT NULL DEFAULT (''),
	StatusGestor			INT NULL DEFAULT (1),
	StatusContabilidade		INT NULL DEFAULT (1),
	StatusFinanceiro		INT NULL DEFAULT (1),
	Banco					VARCHAR(256) NOT NULL DEFAULT (''),
	Agencia					VARCHAR(256) NOT NULL DEFAULT (''),
	Conta					VARCHAR(256) NOT NULL DEFAULT (''),
	ValorDiaria				MONEY NOT NULL DEFAULT (0),
	ValorKm					MONEY NOT NULL DEFAULT (0),
	DataAprovacaoGestor		DATETIME NULL,
	DataAprovacaoFinanceiro DATETIME NULL,
	DataAprovacaoContabil	DATETIME NULL,
	Rascunho				BIT NOT NULL DEFAULT 1,
	Cancelado				BIT NOT NULL DEFAULT 0,
	DataVencimento			DATETIME,
	CONSTRAINT PK_Rddv PRIMARY KEY (IdRelatorio),
	CONSTRAINT FK_Rddv_Usuario FOREIGN KEY (IdUsuario) REFERENCES Usuario (IdUsuario)
)
GO

CREATE TABLE DespesaRddv (
	IdDespesaRddv		INT NOT NULL IDENTITY,
	IdRelatorio			INT NOT NULL,
	DataDespesa			DATETIME,
	TipoDespesa			INT NOT NULL,
	Moeda				VARCHAR(16),
	Valor				MONEY,
	Quantidade			MONEY NULL,
	CONSTRAINT PK_DespesaRddv PRIMARY KEY (IdDespesaRddv),
	CONSTRAINT FK_DespesaRddv_Rddv FOREIGN KEY (IdRelatorio) REFERENCES Rddv (IdRelatorio)
)

CREATE TABLE DocumentoRddv (
	IdDocumentoRddv			INT NOT NULL IDENTITY,
	IdRelatorio				INT NOT NULL,
	IdTipoDocumento			INT NOT NULL,
	IdUsuario				INT NOT NULL,
	DataCriacao				DATETIME NOT NULL DEFAULT (GETDATE()),
	Indice					INT NOT NULL,
	NomeArquivo				VARCHAR(256) NOT NULL,
	CaminhoArquivo			VARCHAR(1024) NOT NULL,
	Arquivo					TEXT NOT NULL,
	CONSTRAINT PK_DocumentoRddv PRIMARY KEY (IdDocumentoRddv),
	CONSTRAINT FK_DocumentoRddv_Rddv FOREIGN KEY (IdRelatorio) REFERENCES Rddv (IdRelatorio),
	CONSTRAINT FK_DocumentoRddv_TipoDocumento FOREIGN KEY (IdTipoDocumento) REFERENCES TipoDocumento (IdTipoDocumento),
	CONSTRAINT FK_DocumentoRddv_Usuario FOREIGN KEY (IdUsuario) REFERENCES Usuario (IdUsuario)
)
GO

CREATE VIEW [dbo].[vDespesasRddv] AS  
SELECT
	R.IdRelatorio,
	(CASE WHEN R.DiariaViagem = 1 
		  THEN R.Diarias 
		  ELSE 0 END * ISNULL(R.ValorDiaria, 0)) +
	Desp.TotalDespesas - (ISNULL(R.ValorAdiantamento, 0.00) + ISNULL(R.SaldoCartaoVtm, 0.00)) AS Valor
FROM
	Rddv R
OUTER APPLY (
	SELECT
		ISNULL(SUM(Valor), 0) AS TotalDespesas
	FROM
		DespesaRddv D
	WHERE
		D.IdRelatorio = R.IdRelatorio
) Desp
GO

CREATE TABLE Fornecedor (
	IdFornecedor	INT NOT NULL IDENTITY,
	NumDocumento	VARCHAR(64) NOT NULL,
	Nome			VARCHAR(256) NOT NULL,
	Codigo			VARCHAR(128) NOT NULL,
	CONSTRAINT PK_Fornecedor PRIMARY KEY (IdFornecedor),
	CONSTRAINT UK_Fornecedor UNIQUE (NumDocumento)
)
GO

CREATE TABLE TipoViagem (
	IdTipoViagem	INT NOT NULL IDENTITY,
	Nome			VARCHAR(128) NOT NULL,
	ValorDiaria		MONEY NOT NULL DEFAULT (0),
	CONSTRAINT PK_TipoViagem PRIMARY KEY (IdTipoViagem)
)

SET IDENTITY_INSERT TipoViagem ON

INSERT INTO TipoViagem (IdTipoViagem, Nome, ValorDiaria) VALUES (1, 'Nacional', 0)
INSERT INTO TipoViagem (IdTipoViagem, Nome, ValorDiaria) VALUES (2, 'Internacional', 0)
INSERT INTO TipoViagem (IdTipoViagem, Nome, ValorDiaria) VALUES (3, 'Internacional Israel', 0)
INSERT INTO TipoViagem (IdTipoViagem, Nome, ValorDiaria) VALUES (4, 'Internacional Euro', 0)

SET IDENTITY_INSERT TipoViagem OFF
GO

CREATE VIEW vListagemSolicitacoes
AS

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
	,S.StatusGestor		   AS IdStatusGestor
	,S.StatusFinanceiro	   AS IdStatusFinanceiro
	,S.StatusContabilidade AS IdStatusContabilidade
	,CASE S.StatusGestor
	 WHEN 1 THEN 'Aguardando Autorização'
	 WHEN 2 THEN 'Autorizado'
	 WHEN 3 THEN 'Negado'
	 ELSE 'Aguardando Autorização'
	 END AS StatusGestor
	 ,CASE S.StatusFinanceiro
	 WHEN 1 THEN 'Aguardando Autorização'
	 WHEN 2 THEN 'Autorizado'
	 WHEN 3 THEN 'Negado'
	 ELSE 'Aguardando Autorização'
	 END AS StatusFinanceiro
	 ,CASE S.StatusContabilidade
	 WHEN 1 THEN 'Aguardando Lançamento'
	 WHEN 2 THEN 'Lançado'
	 WHEN 3 THEN 'Negado'
	 ELSE 'Aguardando Lançamento'
	 END AS StatusContabilidade,
	 IdGestor,
	 AprovadoGestor,
	 AprovadoSetor,
	 Rascunho,
	 Cancelado
FROM
	Solicitacao S
INNER JOIN Usuario US
	ON US.IdUsuario = S.IdUsuario
LEFT JOIN Usuario UA
	ON UA.IdUsuario = S.IdUsuarioAtribuido

UNION ALL

SELECT
	 R.IdRelatorio AS Codigo
	,R.TipoRelatorio AS IdTipo
	,CONCAT('RDDV - ', CASE R.TipoRelatorio WHEN 1 THEN 'Adiantamento' WHEN 2 THEN 'Prestação de Contas' WHEN 3 THEN 'Reembolso' END) AS Tipo
	,R.DataCadastro AS DataSolicitacao
	,R.DataVencimento
	,'#' AS NumeroDocumento
	,US.Nome AS NomeSolicitante
	,US.IdUsuario AS IdSolicitante
	,UA.Nome AS UsuarioAtribuido
	,UA.IdUsuario AS IdUsuarioAtribuido
	,'#' AS Parceiro
	,D.Valor
	,'#' AS Descricao
	,R.ObservacaoGestor
	,R.StatusGestor		   AS IdStatusGestor
	,R.StatusFinanceiro	   AS IdStatusFinanceiro
	,R.StatusContabilidade AS IdStatusContabilidade
	,CASE R.StatusGestor
	 WHEN 1 THEN 'Aguardando Autorização'
	 WHEN 2 THEN 'Autorizado'
	 WHEN 3 THEN 'Negado'
	 ELSE 'Aguardando Autorização'
	 END AS StatusGestor
	 ,CASE R.StatusFinanceiro
	 WHEN 1 THEN 'Aguardando Autorização'
	 WHEN 2 THEN 'Autorizado'
	 WHEN 3 THEN 'Negado'
	 ELSE 'Aguardando Autorização'
	 END AS StatusFinanceiro
	 ,CASE R.StatusContabilidade
	 WHEN 1 THEN 'Aguardando Lançamento'
	 WHEN 2 THEN 'Lançado'
	 WHEN 3 THEN 'Negado'
	 ELSE 'Aguardando Lançamento'
	 END AS StatusContabilidade,
	 IdGestor,
	 AprovadoGestor,
	 AprovadoSetor,
	 Rascunho,
	 Cancelado
FROM
	Rddv R
INNER JOIN vDespesasRddv D
	ON D.IdRelatorio = R.IdRelatorio
INNER JOIN Usuario US
	ON US.IdUsuario = R.IdUsuario
LEFT JOIN Usuario UA
	ON UA.IdUsuario = R.IdUsuarioAtribuido
