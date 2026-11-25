using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Ares.PagueAres.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AdicionaExportacaoJob : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "ExportacaoJob",
                columns: table => new
                {
                    IdExportacaoJob = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    IdUsuario = table.Column<int>(type: "int", nullable: false),
                    Status = table.Column<int>(type: "int", nullable: false),
                    FiltrosJson = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    DataCriacao = table.Column<DateTime>(type: "datetime2", nullable: false),
                    DataInicio = table.Column<DateTime>(type: "datetime2", nullable: true),
                    DataConclusao = table.Column<DateTime>(type: "datetime2", nullable: true),
                    DataExpiracao = table.Column<DateTime>(type: "datetime2", nullable: true),
                    TotalTitulos = table.Column<int>(type: "int", nullable: false, defaultValue: 0),
                    TitulosProcessados = table.Column<int>(type: "int", nullable: false, defaultValue: 0),
                    TotalAnexos = table.Column<int>(type: "int", nullable: false, defaultValue: 0),
                    NomeArquivo = table.Column<string>(type: "nvarchar(260)", maxLength: 260, nullable: true),
                    TamanhoBytes = table.Column<long>(type: "bigint", nullable: true),
                    MensagemErro = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    CancelamentoSolicitado = table.Column<bool>(type: "bit", nullable: false, defaultValue: false),
                    Versao = table.Column<Guid>(type: "uniqueidentifier", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ExportacaoJob", x => x.IdExportacaoJob);
                    table.ForeignKey(
                        name: "FK_ExportacaoJob_Usuario",
                        column: x => x.IdUsuario,
                        principalTable: "Usuario",
                        principalColumn: "IdUsuario",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ExportacaoJob_IdUsuario_DataCriacao",
                table: "ExportacaoJob",
                columns: new[] { "IdUsuario", "DataCriacao" });

            migrationBuilder.CreateIndex(
                name: "IX_ExportacaoJob_Status",
                table: "ExportacaoJob",
                column: "Status");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ExportacaoJob");
        }
    }
}
