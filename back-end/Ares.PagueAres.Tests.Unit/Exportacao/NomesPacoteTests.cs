using Ares.PagueAres.Application.Exportacao;
using FluentAssertions;

namespace Ares.PagueAres.Tests.Unit.Exportacao
{
    public class NomesPacoteTests
    {
        public static TheoryData<string> NomesMaliciosos => new()
        {
            "../../etc/passwd",
            "..\\..\\Windows\\System32\\x.dll",
            "C:\\Windows\\x.exe",
            "a/b\\c.pdf",
            "/etc/shadow",
            "\\\\servidor\\compartilhamento\\x.pdf",
            "....//....//x.pdf",
            ". . /x.pdf",
            "nome:fluxo.pdf",
            "..",
            "."
        };

        [Theory]
        [MemberData(nameof(NomesMaliciosos))]
        public void Sanitizar_NomeMalicioso_NaoTemSeparadorPontoPontoNemDoisPontos(string nome)
        {
            var resultado = NomesPacote.Sanitizar(nome);

            resultado.Should().NotContain("/").And.NotContain("\\").And.NotContain("..").And.NotContain(":");
        }

        [Theory]
        [MemberData(nameof(NomesMaliciosos))]
        public void MontarNomeAnexo_NomeMalicioso_FicaDentroDaPasta(string nome)
        {
            var resultado = NomesPacote.MontarNomeAnexo("Nota Fiscal", nome, 7);

            resultado.Should().NotContain("/").And.NotContain("\\").And.NotContain("..").And.NotContain(":");
            resultado.Should().StartWith("Nota Fiscal - ");
            resultado.Length.Should().BeLessThanOrEqualTo(NomesPacote.TamanhoMaximoNome);
        }

        [Theory]
        [InlineData("../../etc/passwd", ".etcpasswd")]
        [InlineData("C:\\Windows\\x.exe", "CWindowsx.exe")]
        [InlineData("a/b\\c.pdf", "abc.pdf")]
        [InlineData("<a>|b?*\"c\".pdf", "abc.pdf")]
        [InlineData("nome com espaço final .pdf ", "nome com espaço final .pdf")]
        [InlineData("arquivo. . .", "arquivo")]
        [InlineData("arquivo...pdf", "arquivo.pdf")]
        [InlineData("  começa com espaço.pdf", "começa com espaço.pdf")]
        [InlineData("boleto março.pdf", "boleto março.pdf")]
        public void Sanitizar_AplicaAsRegrasDoContrato(string nome, string esperado)
        {
            NomesPacote.Sanitizar(nome).Should().Be(esperado);
        }

        [Theory]
        [InlineData("CON.pdf", "_CON.pdf")]
        [InlineData("con", "_con")]
        [InlineData("Prn.txt", "_Prn.txt")]
        [InlineData("aux", "_aux")]
        [InlineData("NUL.tar.gz", "_NUL.tar.gz")]
        [InlineData("com1.pdf", "_com1.pdf")]
        [InlineData("COM9", "_COM9")]
        [InlineData("lpt1.doc", "_lpt1.doc")]
        [InlineData("LPT9.xlsx", "_LPT9.xlsx")]
        public void Sanitizar_NomeReservadoDoWindows_GanhaPrefixo(string nome, string esperado)
        {
            NomesPacote.Sanitizar(nome).Should().Be(esperado);
        }

        [Theory]
        [InlineData("CONSOLE.pdf")]
        [InlineData("COM10.pdf")]
        [InlineData("contrato.pdf")]
        [InlineData("LPT.pdf")]
        public void Sanitizar_NomeParecidoComReservado_NaoMuda(string nome)
        {
            NomesPacote.Sanitizar(nome).Should().Be(nome);
        }

        [Fact]
        public void Sanitizar_RemoveCaracteresDeControleEDeInversaoDeDirecao()
        {
            // U+202E inverte a direção do texto e faria "fatura‮fdp.exe" aparecer como "faturaexe.pdf".
            NomesPacote.Sanitizar("fatura\u202Efdp.exe").Should().Be("faturafdp.exe");
            NomesPacote.Sanitizar("a\u0001b\tc\r\nd\u007F.pdf").Should().Be("abcd.pdf");
            NomesPacote.Sanitizar("\uFEFFrecibo.pdf").Should().Be("recibo.pdf");
        }

        [Fact]
        public void Sanitizar_SurrogateOrfao_EhRemovidoEParValidoFica()
        {
            NomesPacote.Sanitizar("a\uD800b.pdf").Should().Be("ab.pdf");
            NomesPacote.Sanitizar("foto 📷.jpg").Should().Be("foto 📷.jpg");
        }

        [Theory]
        [InlineData(null)]
        [InlineData("")]
        [InlineData("   ")]
        public void Sanitizar_NomeVazio_DevolveVazio(string? nome)
        {
            NomesPacote.Sanitizar(nome).Should().BeEmpty();
        }

        [Fact]
        public void MontarNomeAnexo_JuntaRotuloENomeSanitizado()
        {
            NomesPacote.MontarNomeAnexo("Nota Fiscal", "boleto março.pdf", 1).Should().Be("Nota Fiscal - boleto março.pdf");
        }

        [Theory]
        [InlineData(null)]
        [InlineData("")]
        [InlineData("  ")]
        public void MontarNomeAnexo_SemTipoDeDocumento_UsaDocumento(string? rotulo)
        {
            NomesPacote.MontarNomeAnexo(rotulo, "recibo.jpg", 1).Should().Be("Documento - recibo.jpg");
        }

        [Fact]
        public void MontarNomeAnexo_RotuloComCaracterProibido_EhSanitizado()
        {
            NomesPacote.MontarNomeAnexo("NF/Fatura", "x.pdf", 1).Should().Be("NFFatura - x.pdf");
        }

        [Theory]
        [InlineData(null, "Nota Fiscal - anexo-7")]
        [InlineData("", "Nota Fiscal - anexo-7")]
        [InlineData("   ", "Nota Fiscal - anexo-7")]
        [InlineData("???", "Nota Fiscal - anexo-7")]
        [InlineData(".pdf", "Nota Fiscal - anexo-7.pdf")]
        [InlineData("???.pdf", "Nota Fiscal - anexo-7.pdf")]
        public void MontarNomeAnexo_NomeVazio_UsaAnexoComId(string? nome, string esperado)
        {
            NomesPacote.MontarNomeAnexo("Nota Fiscal", nome, 7).Should().Be(esperado);
        }

        [Fact]
        public void MontarNomeAnexo_NomeDe300Caracteres_LimitaA120PreservandoExtensao()
        {
            var nome = new string('a', 296) + ".pdf";

            var resultado = NomesPacote.MontarNomeAnexo("Nota Fiscal", nome, 1);

            resultado.Length.Should().Be(NomesPacote.TamanhoMaximoNome);
            resultado.Should().StartWith("Nota Fiscal - aaa").And.EndWith("a.pdf");
        }

        [Fact]
        public void MontarNomeAnexo_NomeLongoSemExtensao_LimitaA120()
        {
            var resultado = NomesPacote.MontarNomeAnexo("Comprovante", new string('b', 300), 1);

            resultado.Length.Should().Be(NomesPacote.TamanhoMaximoNome);
            resultado.Should().StartWith("Comprovante - bbb");
        }

        [Fact]
        public void MontarNomeAnexo_CorteNaoQuebraSurrogate()
        {
            var nome = string.Concat(Enumerable.Repeat("📷", 150)) + ".jpg";

            var resultado = NomesPacote.MontarNomeAnexo("Foto", nome, 1);

            resultado.Length.Should().BeLessThanOrEqualTo(NomesPacote.TamanhoMaximoNome);
            resultado.Should().EndWith(".jpg");
            resultado.Where(char.IsSurrogate).Count().Should().Be(resultado.Count(char.IsHighSurrogate) * 2);
        }

        [Fact]
        public void Deduplicar_DuplicadosEmCaixasDiferentes_RecebemNumero()
        {
            var resultado = NomesPacote.Deduplicar(
            [
                "Nota Fiscal - boleto.pdf",
                "NOTA FISCAL - BOLETO.PDF",
                "nota fiscal - boleto.pdf",
                "Nota Fiscal - outro.pdf"
            ]);

            resultado.Should().Equal(
                "Nota Fiscal - boleto.pdf",
                "NOTA FISCAL - BOLETO (2).PDF",
                "nota fiscal - boleto (3).pdf",
                "Nota Fiscal - outro.pdf");
        }

        [Fact]
        public void Deduplicar_NomeJaExistenteComSufixo_NaoColide()
        {
            var resultado = NomesPacote.Deduplicar(["a.pdf", "a (2).pdf", "a.pdf"]);

            resultado.Should().Equal("a.pdf", "a (2).pdf", "a (3).pdf");
            resultado.Should().OnlyHaveUniqueItems();
        }

        [Fact]
        public void Deduplicar_SemExtensao_SufixoNoFim()
        {
            NomesPacote.Deduplicar(["Documento - anexo-1", "documento - ANEXO-1"])
                .Should().Equal("Documento - anexo-1", "documento - ANEXO-1 (2)");
        }

        [Fact]
        public void Deduplicar_NomeNoLimite_ContinuaComNoMaximo120()
        {
            var nome = NomesPacote.MontarNomeAnexo("Nota Fiscal", new string('c', 300) + ".pdf", 1);

            var resultado = NomesPacote.Deduplicar([nome, nome, nome]);

            resultado.Should().OnlyHaveUniqueItems();
            resultado.Should().AllSatisfy(n =>
            {
                n.Length.Should().BeLessThanOrEqualTo(NomesPacote.TamanhoMaximoNome);
                n.Should().EndWith(".pdf");
            });
            resultado[1].Should().EndWith(" (2).pdf");
            resultado[2].Should().EndWith(" (3).pdf");
        }

        [Fact]
        public void CaminhoEntrada_UsaBarraNormal()
        {
            NomesPacote.CaminhoEntrada("SOL-000123", "Nota Fiscal - boleto.pdf").Should().Be("SOL-000123/Nota Fiscal - boleto.pdf");
        }
    }
}
