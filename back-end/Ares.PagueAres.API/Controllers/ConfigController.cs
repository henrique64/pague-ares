using Ares.PagueAres.API.Extensions;
using Ares.PagueAres.Domain;
using Ares.PagueAres.Infrastructure;
using Ares.PagueAres.Infrastructure.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Ares.PagueAres.API.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    [Authorize]
    public class ConfigController : ControllerBase
    {

        private readonly PagueAresContext _dbContext;

        public ConfigController(PagueAresContext dbContext)
        {
            _dbContext = dbContext;
        }

        [HttpGet]
        public async Task<ActionResult<GenericResponse<IEnumerable<Configuracao>>>> GetConfig()
        {
            try
            {
                var config = await _dbContext.Configuracaos.ToListAsync();

                return Ok(new GenericResponse<IEnumerable<Configuracao>>()
                {
                    Success = true,
                    Records = config.Count,
                    Data = config,
                    Message = "",
                    Page = 1,
                    Pages = 1
                });
            }
            catch (Exception ex)
            {
                return BadRequest(new GenericResponse<IEnumerable<Configuracao>>()
                {
                    Success = false,
                    Records = 0,
                    Data = null,
                    Message = ex.Message,
                    Page = 1,
                    Pages = 1
                });
            }
        }

        [HttpPost]
        public async Task<ActionResult<GenericResponse<IEnumerable<Configuracao>>>> SetConfig([FromBody] IEnumerable<Configuracao> configuracaos)
        {
            try
            {
                if (!this.CurrentUserIsInRole("ADM"))
                    return Ok(new GenericResponse<IEnumerable<Configuracao>>() { Success = false, Message = "Apenas administradores podem alterar configurações.", Data = null, Page = 1, Pages = 1, Records = 0 });

                foreach (var config in configuracaos)
                {
                    var cfg = await _dbContext
                                        .Configuracaos
                                        .FirstOrDefaultAsync(c => c.Chave.ToLower().Trim() == config.Chave.ToLower().Trim());

                    if (cfg == null)
                    {
                        cfg = new Configuracao();

                        cfg.Chave = config.Chave;

                        _dbContext.Add(cfg);
                    }

                    cfg.Valor = config.Valor;
                    cfg.DataAlteracao = DateTime.Now;
                    cfg.UsuarioAlteracao = config.UsuarioAlteracao;

                    await _dbContext.SaveChangesAsync();

                    config.IdConfiguracao = cfg.IdConfiguracao;
                }

                return Ok(new GenericResponse<IEnumerable<Configuracao>>()
                {
                    Success = true,
                    Records = configuracaos.Count(),
                    Data = configuracaos,
                    Message = "",
                    Page = 1,
                    Pages = 1
                });
            }
            catch (Exception ex)
            {
                return BadRequest(new GenericResponse<IEnumerable<Configuracao>>()
                {
                    Success = false,
                    Records = 0,
                    Data = null,
                    Message = ex.Message,
                    Page = 1,
                    Pages = 1
                });
            }
        }

    }
}
