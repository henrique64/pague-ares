using Ares.PagueAres.API.Controllers;
using Ares.PagueAres.Domain.Dtos.Authentication;
using Ares.PagueAres.Domain.Enums;
using Ares.PagueAres.Infrastructure;
using Ares.PagueAres.Infrastructure.Models;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;

namespace Ares.PagueAres.Tests.Unit.Preview
{
    public class PreviewTokenValidationTests
    {
        private static PagueAresContext CreateContext(string dbName)
        {
            var opts = new DbContextOptionsBuilder<PagueAresContext>()
                .UseInMemoryDatabase(dbName)
                .Options;
            return new PagueAresContext(opts);
        }

        private static PreviewController CreateController(PagueAresContext ctx, IMemoryCache cache)
        {
            var controller = new PreviewController(ctx, cache);
            controller.ControllerContext = new ControllerContext
            {
                HttpContext = new DefaultHttpContext()
            };
            return controller;
        }

        private static string StoreKey(IMemoryCache cache, int entityId, EnumRequestType requestType)
        {
            var key = new PreviewKeyDto(Guid.NewGuid(), requestType, entityId);
            cache.Set("preview-key-" + key.PreviewKey.ToString(), key, TimeSpan.FromMinutes(1));
            return key.PreviewKey.ToString();
        }

        private static void SeedPaymentAttachment(PagueAresContext ctx, int attachmentId, int solicitacaoId)
        {
            // Seed attachment — arquivo is a small valid base64 PNG
            var smallPng = "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR42mNk+M9QDwADhgGAWjR9awAAAABJRU5ErkJggg==";
            ctx.DocumentoSolicitacaos.Add(new DocumentoSolicitacao
            {
                IdDocumentoSolicitacao = attachmentId,
                IdSolicitacao = solicitacaoId,
                Arquivo = smallPng,
                NomeArquivo = "test.png",
                CaminhoArquivo = "image/png"
            });
            ctx.SaveChanges();
        }

        [Fact]
        public async Task PreviewPayment_TokenValido_EntityIdCorreto_ServeArquivo()
        {
            using var ctx = CreateContext(nameof(PreviewPayment_TokenValido_EntityIdCorreto_ServeArquivo));
            var cache = new MemoryCache(new MemoryCacheOptions());
            SeedPaymentAttachment(ctx, attachmentId: 1, solicitacaoId: 10);
            var previewKey = StoreKey(cache, entityId: 10, EnumRequestType.Solicitacao);

            var ctrl = CreateController(ctx, cache);
            var result = await ctrl.PreviewPaymentAttachment(id: 10, attachmentId: 1, key: previewKey);

            result.Should().BeOfType<FileContentResult>();
        }

        [Fact]
        public async Task PreviewPayment_TokenValido_EntityIdErrado_RetornaUnauthorized()
        {
            using var ctx = CreateContext(nameof(PreviewPayment_TokenValido_EntityIdErrado_RetornaUnauthorized));
            var cache = new MemoryCache(new MemoryCacheOptions());
            // Token para entityId=10, mas chamamos com id=99
            var previewKey = StoreKey(cache, entityId: 10, EnumRequestType.Solicitacao);

            var ctrl = CreateController(ctx, cache);
            var result = await ctrl.PreviewPaymentAttachment(id: 99, attachmentId: 1, key: previewKey);

            result.Should().BeOfType<UnauthorizedObjectResult>();
        }

        [Fact]
        public async Task PreviewRddv_TipoErrado_RetornaUnauthorized()
        {
            using var ctx = CreateContext(nameof(PreviewRddv_TipoErrado_RetornaUnauthorized));
            var cache = new MemoryCache(new MemoryCacheOptions());
            // Token de tipo Solicitacao, mas endpoint é RDDV
            var previewKey = StoreKey(cache, entityId: 10, EnumRequestType.Solicitacao);

            var ctrl = CreateController(ctx, cache);
            var result = await ctrl.PreviewRddvAttachment(id: 10, attachmentId: 1, key: previewKey);

            result.Should().BeOfType<UnauthorizedObjectResult>();
        }

        [Fact]
        public async Task PreviewPayment_TokenInexistente_RetornaUnauthorized()
        {
            using var ctx = CreateContext(nameof(PreviewPayment_TokenInexistente_RetornaUnauthorized));
            var cache = new MemoryCache(new MemoryCacheOptions());

            var ctrl = CreateController(ctx, cache);
            var result = await ctrl.PreviewPaymentAttachment(id: 10, attachmentId: 1, key: "chave-inexistente");

            result.Should().BeOfType<UnauthorizedObjectResult>();
        }
    }
}
