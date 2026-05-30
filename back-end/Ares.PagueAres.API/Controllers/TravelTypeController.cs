using Ares.PagueAres.Domain;
using Ares.PagueAres.Domain.Dtos;
using Ares.PagueAres.Infrastructure;
using Ares.PagueAres.Infrastructure.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace Ares.PagueAres.API.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    [Authorize]
    public class TravelTypeController : ControllerBase
    {

        private readonly PagueAresContext _dbContext;

        public TravelTypeController(PagueAresContext dbContext)
        {
            _dbContext = dbContext;
        }

        [HttpGet]
        public async Task<ActionResult<GenericResponse<IEnumerable<TipoViagemDto>>>> GetAllAsync([FromQuery] int pageSize = 50, [FromQuery] int page = 1)
        {
            try
            {
                var query = (from r in _dbContext.TipoViagems
                             select new TipoViagemDto()
                             {
                                 IdTipoViagem = r.IdTipoViagem,
                                 Nome = r.Nome,
                                 ValorDiaria = r.ValorDiaria
                             });

                var records = await query.CountAsync();

                var requests = await query
                                    .Skip(pageSize * (page - 1))
                                    .Take(pageSize)
                                    .ToListAsync();

                return Ok(new GenericResponse<IEnumerable<TipoViagemDto>>()
                {
                    Success = true,
                    Message = "Registros obtidos com sucesso.",
                    Data = requests,
                    Page = page,
                    Pages = records / pageSize,
                    Records = records
                });
            }
            catch (Exception ex)
            {
                return Ok(new GenericResponse<object>()
                {
                    Success = false,
                    Message = ex.Message,
                    Data = null,
                    Page = 1,
                    Pages = 1,
                    Records = 0
                });
            }
        }

        [HttpGet("{id}")]
        public async Task<ActionResult<GenericResponse<TipoViagemDto>>> GetByIdAsync([FromRoute] int id)
        {
            try
            {
                var request = await _dbContext
                                    .TipoViagems
                                    .Where(s => s.IdTipoViagem == id)
                                    .FirstOrDefaultAsync();

                if (request == null)
                {
                    return Ok(new GenericResponse<object>()
                    {
                        Success = false,
                        Message = "O registro solicitado não foi localizado.",
                        Data = null,
                        Page = 1,
                        Pages = 1,
                        Records = 0
                    });
                }

                var typeData = new TipoViagemDto();

                typeData.IdTipoViagem = request.IdTipoViagem;
                typeData.Nome = request.Nome;
                typeData.ValorDiaria = request.ValorDiaria;

                return Ok(new GenericResponse<TipoViagemDto>()
                {
                    Success = true,
                    Message = "Registros obtidos com sucesso.",
                    Data = typeData,
                    Page = 1,
                    Pages = 1,
                    Records = 1
                });
            }
            catch (Exception ex)
            {
                return Ok(new GenericResponse<object>()
                {
                    Success = false,
                    Message = ex.Message,
                    Data = null,
                    Page = 1,
                    Pages = 1,
                    Records = 0
                });
            }
        }

        [HttpPost]
        public async Task<ActionResult<GenericResponse<TipoViagemDto>>> UpsertAsync([FromBody] TipoViagemDto data)
        {
            IDbContextTransaction? tran = null;

            try
            {
                TipoViagem? dbData = null;

                if (data.IdTipoViagem == 0)
                {
                    dbData = new TipoViagem();

                    dbData.ValorDiaria = 0;

                    _dbContext.Add(dbData);
                }
                else
                {
                    dbData = await _dbContext.TipoViagems.Where(r => r.IdTipoViagem == data.IdTipoViagem).FirstOrDefaultAsync();

                    if (dbData == null)
                    {
                        return Ok(new GenericResponse<object>()
                        {
                            Success = false,
                            Message = "O registro solicitado não foi localizado.",
                            Data = null,
                            Page = 1,
                            Pages = 1,
                            Records = 0
                        });
                    }
                }

                tran = await _dbContext.Database.BeginTransactionAsync();

                dbData.IdTipoViagem = data.IdTipoViagem;
                dbData.Nome = data.Nome;
                dbData.ValorDiaria = data.ValorDiaria;

                await _dbContext.SaveChangesAsync();

                data.IdTipoViagem = dbData.IdTipoViagem;

                await tran.CommitAsync();

                return Ok(new GenericResponse<TipoViagemDto>()
                {
                    Success = true,
                    Message = "Registro armazenado com sucesso.",
                    Data = data,
                    Page = 1,
                    Pages = 1,
                    Records = 1
                });
            }
            catch (Exception ex)
            {
                if (tran != null)
                    await tran.RollbackAsync();

                return Ok(new GenericResponse<object>()
                {
                    Success = false,
                    Message = ex.Message,
                    Data = null,
                    Page = 1,
                    Pages = 1,
                    Records = 0
                });
            }
        }

    }
}
