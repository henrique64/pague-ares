using Ares.PagueAres.Application.Configuration;
using Ares.PagueAres.Application.External.ActiveDirectory;
using Ares.PagueAres.Application.External.ActiveDirectory.Dtos;
using Ares.PagueAres.Domain;
using Ares.PagueAres.Domain.Dtos;
using Ares.PagueAres.Domain.Dtos.Authentication;
using Ares.PagueAres.Infrastructure;
using Ares.PagueAres.Infrastructure.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.Options;
using System.Security.Cryptography;
using System.Text;

namespace Ares.PagueAres.API.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    [Authorize]
    public class UsersController : ControllerBase
    {

        private readonly PagueAresContext _dbContext;
        private readonly IOptions<ActiveDirectorySettings> _adOptions;

        public UsersController(PagueAresContext dbContext,
                               IOptions<ActiveDirectorySettings> adOptions)
        {
            _dbContext = dbContext;
            _adOptions = adOptions;
        }

        [HttpGet]
        public async Task<ActionResult<GenericResponse<IEnumerable<UsuarioDto>>>> GetAllUsers()
        {
            try
            {
                var users = await _dbContext
                                    .Usuarios
                                    .Select(dbUser =>
                                         new UsuarioDto()
                                         {
                                             IdUsuario = dbUser.IdUsuario,
                                             Email = dbUser.Email,
                                             Nome = dbUser.Nome,
                                             Origem = dbUser.Origem,
                                             IdDepartamento = dbUser.IdDepartamento,
                                         }).ToListAsync();

                return Ok(new GenericResponse<IEnumerable<UsuarioDto>>()
                {
                    Success = true,
                    Data = users,
                    Message = "",
                    Page = 1,
                    Pages = 1,
                    Records = users.Count
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
        public async Task<ActionResult<GenericResponse<UsuarioDto>>> GetUserById([FromRoute] int id)
        {
            try
            {
                var dbUser = await _dbContext.Usuarios.FirstOrDefaultAsync(u => u.IdUsuario == id);

                if (dbUser == null)
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

                var idFuncao = await _dbContext
                                    .UsuarioFuncaos
                                    .Where(f => f.IdUsuario == dbUser.IdUsuario)
                                    .Select(f => f.IdFuncao)
                                    .ToListAsync();

                var funcoes = await _dbContext
                                    .Funcaos
                                    .Where(f => idFuncao.Contains(f.IdFuncao))
                                    .ToListAsync();

                UsuarioDto user = new UsuarioDto()
                {
                    IdUsuario = dbUser.IdUsuario,
                    Email = dbUser.Email,
                    Nome = dbUser.Nome,
                    Origem = dbUser.Origem,
                    IdExterno = dbUser.IdExterno,
                    Codigo = dbUser.Codigo ?? "",
                    IdDepartamento = dbUser.IdDepartamento,
                    Ativo = dbUser.Ativo,
                    DataCadastro = dbUser.DataCadastro,
                    UsuarioCadastro = dbUser.UsuarioCadastro,
                    UltimaAlteracao = dbUser.UltimaAlteracao,
                    UsuarioAlteracao = dbUser.UsuarioAlteracao,
                    Funcoes = funcoes.Select(f =>
                    {
                        return new FuncaoDto()
                        {
                            IdFuncao = f.IdFuncao,
                            Alias = f.Alias,
                            Nome = f.Nome
                        };
                    }).ToList()
                };

                return Ok(new GenericResponse<UsuarioDto>()
                {
                    Success = true,
                    Message = "",
                    Data = user,
                    Page = 1,
                    Pages = 1,
                    Records = 0
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
        public async Task<ActionResult<GenericResponse<UsuarioDto>>> SaveUser([FromBody] UsuarioDto user)
        {
            IDbContextTransaction? tran = null;

            try
            {
                Usuario? userData = null;

                if (user.IdUsuario == 0)
                {
                    userData = new Usuario()
                    {
                        Email = user.Email,
                        Nome = user.Nome,
                        Origem = user.Origem,
                        IdExterno = user.IdExterno,
                        Codigo = user.Codigo,
                        DataCadastro = user.DataCadastro
                    };

                    _dbContext.Add(userData);
                }
                else
                {
                    userData = await _dbContext.Usuarios.FirstOrDefaultAsync(u => u.IdUsuario == user.IdUsuario);

                    if (userData == null)
                    {
                        return Ok(new GenericResponse<object>()
                        {
                            Success = false,
                            Message = "O usuário informado não foi encontrado.",
                            Data = null,
                            Page = 1,
                            Pages = 1,
                            Records = 0
                        });
                    }
                }

                if (user.Senha.Trim() != "")
                {
                    userData.Senha = ComputeSha256Hash(user.Senha);
                }

                if (user.Origem == 0)
                {
                    userData.Email = user.Email;
                    userData.Nome = user.Nome;
                }

                if (user.Origem == 1)
                {
                    userData.Senha = "";
                }

                userData.IdDepartamento = user.IdDepartamento;
                userData.Ativo = user.Ativo;
                userData.UsuarioCadastro = user.UsuarioCadastro;
                userData.UltimaAlteracao = user.UltimaAlteracao;
                userData.UsuarioAlteracao = user.UsuarioAlteracao;

                tran = await _dbContext.Database.BeginTransactionAsync();

                await _dbContext.SaveChangesAsync();

                user.IdUsuario = userData.IdUsuario;

                var roles = _dbContext.UsuarioFuncaos.Where(f => f.IdUsuario == userData.IdUsuario);

                _dbContext.RemoveRange(roles);

                await _dbContext.SaveChangesAsync();

                foreach (var f in user.Funcoes)
                {
                    var userRole = new UsuarioFuncao()
                    {
                        IdUsuario = userData.IdUsuario,
                        IdFuncao = f.IdFuncao
                    };

                    _dbContext.Add(userRole);

                    await _dbContext.SaveChangesAsync();
                }

                await tran.CommitAsync();

                return Ok(new GenericResponse<UsuarioDto>()
                {
                    Success = true,
                    Message = "Registro armazenado com sucesso.",
                    Data = user,
                    Page = 1,
                    Pages = 1,
                    Records = 0
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

        [HttpGet("directory")]
        public async Task<ActionResult<GenericResponse<UsuarioDto>>> SearchDirectory([FromQuery] string userName)
        {
            return await Task.Run(() =>
            {
                try
                {
                    var adOptions = _adOptions.Value;

                    if (!adOptions.EnableDomainAuthentication)
                        return Ok(new GenericResponse<object>()
                        {
                            Success = false,
                            Message = "Autenticação via Active Directory não disponível.",
                            Data = null,
                            Page = 1,
                            Pages = 1,
                            Records = 0
                        });

                    foreach (var server in adOptions.Servers)
                    {
                        try
                        {
                            AdUserInfo? adUser = ActiveDirectoryService.GetUserInformation(server.Host, server.Domain, server.Username, server.Password, userName);

                            if (adUser == null)
                                continue;


                            var userData = new UsuarioDto();

                            userData.Origem = 1;
                            userData.DataCadastro = DateTime.Now;
                            userData.Ativo = true;
                            userData.Senha = "";
                            userData.UsuarioCadastro = null;
                            userData.IdExterno = adUser.Sid;
                            userData.Codigo = adUser.Login;

                            userData.UltimaAlteracao = DateTime.Now;
                            userData.Nome = adUser.Name;
                            userData.Email = adUser.Email;

                            return Ok(new GenericResponse<UsuarioDto>()
                            {
                                Success = true,
                                Message = "",
                                Data = userData,
                                Page = 1,
                                Pages = 1,
                                Records = 0
                            });
                        }
                        catch
                        {
                            continue;
                        }
                    }

                    return Ok(new GenericResponse<object>()
                    {
                        Success = false,
                        Message = "Usuário não encontrado.",
                        Data = null,
                        Page = 1,
                        Pages = 1,
                        Records = 0
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
            });
        }

        private string ComputeSha256Hash(string rawData)
        {
            // Create a SHA256   
            using (SHA256 sha256Hash = SHA256.Create())
            {
                // ComputeHash - returns byte array  
                byte[] bytes = sha256Hash.ComputeHash(Encoding.UTF8.GetBytes(rawData));

                // Convert byte array to a string   
                StringBuilder builder = new StringBuilder();
                for (int i = 0; i < bytes.Length; i++)
                {
                    builder.Append(bytes[i].ToString("x2"));
                }
                return builder.ToString();
            }
        }

        [HttpGet]
        [Route("list")]
        public async Task<ActionResult<GenericResponse<IEnumerable<UserListDto>>>> GetList([FromQuery] string roles = "")
        {
            try
            {
                List<int>? roleIds = null;

                if(!string.IsNullOrEmpty(roles))
                {
                    var dbRoles = await _dbContext.Funcaos
                        .ToListAsync();

                    var roleAliases = roles.Split(",")
                        .Where(x => !string.IsNullOrEmpty(x))
                        .ToList();

                    roleIds = [.. dbRoles.Where(r => roleAliases.Contains(r.Alias)).Select(r => r.IdFuncao)];
                }

                var userList = await (from user in _dbContext.Usuarios
                                        from userRole in _dbContext.UsuarioFuncaos.Where(r => r.IdUsuario == user.IdUsuario)
                                        where user.Ativo == true && (
                                            roleIds == null || roleIds.Contains(userRole.IdFuncao))
                                        orderby user.Nome
                                        select new UserListDto(user.IdUsuario, user.Nome))
                                        .ToListAsync();

                return Ok(new GenericResponse<IEnumerable<UserListDto>>()
                {
                    Success = true,
                    Data = userList,
                    Message = "",
                    Page = 1,
                    Pages = 1,
                    Records = userList.Count
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
