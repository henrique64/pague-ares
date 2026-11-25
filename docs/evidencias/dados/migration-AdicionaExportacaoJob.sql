BEGIN TRANSACTION;
CREATE TABLE [ExportacaoJob] (
    [IdExportacaoJob] int NOT NULL IDENTITY,
    [IdUsuario] int NOT NULL,
    [Status] int NOT NULL,
    [FiltrosJson] nvarchar(max) NOT NULL,
    [DataCriacao] datetime2 NOT NULL,
    [DataInicio] datetime2 NULL,
    [DataConclusao] datetime2 NULL,
    [DataExpiracao] datetime2 NULL,
    [TotalTitulos] int NOT NULL DEFAULT 0,
    [TitulosProcessados] int NOT NULL DEFAULT 0,
    [TotalAnexos] int NOT NULL DEFAULT 0,
    [NomeArquivo] nvarchar(260) NULL,
    [TamanhoBytes] bigint NULL,
    [MensagemErro] nvarchar(500) NULL,
    [CancelamentoSolicitado] bit NOT NULL DEFAULT CAST(0 AS bit),
    [Versao] uniqueidentifier NOT NULL,
    CONSTRAINT [PK_ExportacaoJob] PRIMARY KEY ([IdExportacaoJob]),
    CONSTRAINT [FK_ExportacaoJob_Usuario] FOREIGN KEY ([IdUsuario]) REFERENCES [Usuario] ([IdUsuario]) ON DELETE NO ACTION
);

CREATE INDEX [IX_ExportacaoJob_IdUsuario_DataCriacao] ON [ExportacaoJob] ([IdUsuario], [DataCriacao]);

CREATE INDEX [IX_ExportacaoJob_Status] ON [ExportacaoJob] ([Status]);

INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
VALUES (N'20261125214019_AdicionaExportacaoJob', N'10.0.8');

COMMIT;
GO

