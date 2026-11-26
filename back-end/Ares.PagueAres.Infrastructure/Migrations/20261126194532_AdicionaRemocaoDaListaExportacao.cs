using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Ares.PagueAres.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AdicionaRemocaoDaListaExportacao : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "DataRemocaoDaLista",
                table: "ExportacaoJob",
                type: "datetime2",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "DataRemocaoDaLista",
                table: "ExportacaoJob");
        }
    }
}
