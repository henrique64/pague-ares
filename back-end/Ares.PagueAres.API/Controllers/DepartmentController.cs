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
    public class DepartmentController(PagueAresContext dbContext) : ControllerBase
    {
        [HttpGet]
        public async Task<ActionResult<GenericResponse<IEnumerable<DepartamentoDto>>>> GetAllAsync()
        {
            try
            {
                var departments = await dbContext.Departamentos
                    .AsNoTracking()
                    .OrderBy(d => d.Nome)
                    .Select(d => new DepartamentoDto
                    {
                        IdDepartamento = d.IdDepartamento,
                        Nome = d.Nome,
                        CodigoLancamento = d.CodigoLancamento
                    })
                    .ToListAsync();

                return Ok(new GenericResponse<IEnumerable<DepartamentoDto>>()
                {
                    Success = true,
                    Message = "Registros obtidos com sucesso.",
                    Data = departments,
                    Page = 1,
                    Pages = 1,
                    Records = departments.Count
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
