using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Ares.PagueAres.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class DecimalPrecision_Indexes : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterColumn<decimal>(
                name: "ValorAdiantamento",
                table: "Rddv",
                type: "decimal(18,2)",
                precision: 18,
                scale: 2,
                nullable: false,
                oldClrType: typeof(decimal),
                oldType: "decimal(18,0)");

            migrationBuilder.AlterColumn<decimal>(
                name: "SaldoCartaoVtm",
                table: "Rddv",
                type: "decimal(18,2)",
                precision: 18,
                scale: 2,
                nullable: false,
                oldClrType: typeof(decimal),
                oldType: "decimal(18,0)");

            migrationBuilder.AlterColumn<decimal>(
                name: "Diarias",
                table: "Rddv",
                type: "decimal(5,2)",
                precision: 5,
                scale: 2,
                nullable: true,
                oldClrType: typeof(decimal),
                oldType: "decimal(18,2)",
                oldNullable: true);

            migrationBuilder.AlterColumn<string>(
                name: "NumDocumento",
                table: "Fornecedor",
                type: "varchar(64)",
                unicode: false,
                maxLength: 64,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "nvarchar(max)");

            migrationBuilder.AlterColumn<string>(
                name: "Nome",
                table: "Fornecedor",
                type: "varchar(256)",
                unicode: false,
                maxLength: 256,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "nvarchar(max)");

            migrationBuilder.AlterColumn<string>(
                name: "Codigo",
                table: "Fornecedor",
                type: "varchar(128)",
                unicode: false,
                maxLength: 128,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "nvarchar(max)");

            migrationBuilder.AlterColumn<decimal>(
                name: "Valor",
                table: "DespesaRddv",
                type: "decimal(18,2)",
                precision: 18,
                scale: 2,
                nullable: false,
                oldClrType: typeof(decimal),
                oldType: "decimal(18,0)");

            migrationBuilder.AlterColumn<decimal>(
                name: "Quantidade",
                table: "DespesaRddv",
                type: "decimal(18,4)",
                precision: 18,
                scale: 4,
                nullable: true,
                oldClrType: typeof(decimal),
                oldType: "decimal(18,0)",
                oldNullable: true);

            // Indexes para filtros de listagem
            migrationBuilder.Sql("IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_Solicitacao_Status' AND object_id = OBJECT_ID('Solicitacao')) CREATE INDEX IX_Solicitacao_Status ON Solicitacao (StatusGestor, StatusFinanceiro, StatusContabilidade)");
            migrationBuilder.Sql("IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_Solicitacao_Usuario' AND object_id = OBJECT_ID('Solicitacao')) CREATE INDEX IX_Solicitacao_Usuario ON Solicitacao (IdUsuario, DataSolicitacao DESC)");
            migrationBuilder.Sql("IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_Rddv_Status' AND object_id = OBJECT_ID('Rddv')) CREATE INDEX IX_Rddv_Status ON Rddv (StatusGestor, StatusFinanceiro, StatusContabilidade)");
            migrationBuilder.Sql("IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_Rddv_Usuario' AND object_id = OBJECT_ID('Rddv')) CREATE INDEX IX_Rddv_Usuario ON Rddv (IdUsuario, DataCadastro DESC)");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterColumn<decimal>(
                name: "ValorAdiantamento",
                table: "Rddv",
                type: "decimal(18,0)",
                nullable: false,
                oldClrType: typeof(decimal),
                oldType: "decimal(18,2)",
                oldPrecision: 18,
                oldScale: 2);

            migrationBuilder.AlterColumn<decimal>(
                name: "SaldoCartaoVtm",
                table: "Rddv",
                type: "decimal(18,0)",
                nullable: false,
                oldClrType: typeof(decimal),
                oldType: "decimal(18,2)",
                oldPrecision: 18,
                oldScale: 2);

            migrationBuilder.AlterColumn<decimal>(
                name: "Diarias",
                table: "Rddv",
                type: "decimal(18,2)",
                nullable: true,
                oldClrType: typeof(decimal),
                oldType: "decimal(5,2)",
                oldPrecision: 5,
                oldScale: 2,
                oldNullable: true);

            migrationBuilder.AlterColumn<string>(
                name: "NumDocumento",
                table: "Fornecedor",
                type: "nvarchar(max)",
                nullable: false,
                oldClrType: typeof(string),
                oldType: "varchar(64)",
                oldUnicode: false,
                oldMaxLength: 64);

            migrationBuilder.AlterColumn<string>(
                name: "Nome",
                table: "Fornecedor",
                type: "nvarchar(max)",
                nullable: false,
                oldClrType: typeof(string),
                oldType: "varchar(256)",
                oldUnicode: false,
                oldMaxLength: 256);

            migrationBuilder.AlterColumn<string>(
                name: "Codigo",
                table: "Fornecedor",
                type: "nvarchar(max)",
                nullable: false,
                oldClrType: typeof(string),
                oldType: "varchar(128)",
                oldUnicode: false,
                oldMaxLength: 128);

            migrationBuilder.AlterColumn<decimal>(
                name: "Valor",
                table: "DespesaRddv",
                type: "decimal(18,0)",
                nullable: false,
                oldClrType: typeof(decimal),
                oldType: "decimal(18,2)",
                oldPrecision: 18,
                oldScale: 2);

            migrationBuilder.AlterColumn<decimal>(
                name: "Quantidade",
                table: "DespesaRddv",
                type: "decimal(18,0)",
                nullable: true,
                oldClrType: typeof(decimal),
                oldType: "decimal(18,4)",
                oldPrecision: 18,
                oldScale: 4,
                oldNullable: true);

            migrationBuilder.Sql("DROP INDEX IF EXISTS IX_Solicitacao_Status ON Solicitacao");
            migrationBuilder.Sql("DROP INDEX IF EXISTS IX_Solicitacao_Usuario ON Solicitacao");
            migrationBuilder.Sql("DROP INDEX IF EXISTS IX_Rddv_Status ON Rddv");
            migrationBuilder.Sql("DROP INDEX IF EXISTS IX_Rddv_Usuario ON Rddv");
        }
    }
}
