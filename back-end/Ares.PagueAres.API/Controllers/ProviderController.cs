using Ares.PagueAres.Domain;
using Ares.PagueAres.Domain.Dtos;
using Ares.PagueAres.Infrastructure;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Ares.PagueAres.API.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    [Authorize]
    public class ProviderController : ControllerBase
    {

        private readonly PagueAresContext _dbContext;

        public ProviderController(PagueAresContext dbContext)
        {
            _dbContext = dbContext;
        }

        [HttpGet]
        public async Task<ActionResult<GenericResponse<IEnumerable<FornecedorDto>>>> GetAllAsync([FromQuery] int pageSize = 50, [FromQuery] int page = 1)
        {
            try
            {
                var query = (from r in _dbContext.Fornecedors
                             orderby r.Nome
                             select new FornecedorDto()
                             {
                                 IdFornecedor = r.IdFornecedor,
                                 NumDocumento = r.NumDocumento,
                                 Nome = r.Nome,
                                 Codigo = r.Codigo
                             });

                var records = await query.CountAsync();

                var providers = await query
                                    .Skip(pageSize * (page - 1))
                                    .Take(pageSize)
                                    .ToListAsync();

                return Ok(new GenericResponse<IEnumerable<FornecedorDto>>()
                {
                    Success = true,
                    Message = "Registros obtidos com sucesso.",
                    Data = providers,
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
        public async Task<ActionResult<GenericResponse<FornecedorDto>>> GetProviderByIdAsync([FromRoute] int id)
        {
            try
            {
                var request = await _dbContext
                                    .Fornecedors
                                    .Where(s => s.IdFornecedor == id)
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

                var providerData = new FornecedorDto();

                providerData.IdFornecedor = request.IdFornecedor;
                providerData.NumDocumento = request.NumDocumento;
                providerData.Nome = request.Nome;
                providerData.Codigo = request.Codigo;

                return Ok(new GenericResponse<FornecedorDto>()
                {
                    Success = true,
                    Message = "Registros obtidos com sucesso.",
                    Data = providerData,
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

        [HttpGet("find/{numDoc}")]
        public async Task<ActionResult<GenericResponse<FornecedorDto>>> GetProviderByDocumentAsync([FromRoute] string numDoc)
        {
            try
            {
                var request = await _dbContext
                                    .Fornecedors
                                    .Where(s => s.NumDocumento == numDoc)
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

                var providerData = new FornecedorDto();

                providerData.IdFornecedor = request.IdFornecedor;
                providerData.NumDocumento = request.NumDocumento;
                providerData.Nome = request.Nome;
                providerData.Codigo = request.Codigo;

                return Ok(new GenericResponse<FornecedorDto>()
                {
                    Success = true,
                    Message = "Registros obtidos com sucesso.",
                    Data = providerData,
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

    }
}
