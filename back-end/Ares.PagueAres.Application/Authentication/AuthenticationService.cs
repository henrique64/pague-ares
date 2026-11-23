using Ares.PagueAres.Application.Configuration;
using Ares.PagueAres.Application.Exceptions;
using Ares.PagueAres.Application.External.ActiveDirectory;
using Ares.PagueAres.Application.External.ActiveDirectory.Dtos;
using Ares.PagueAres.Domain;
using Ares.PagueAres.Domain.Dtos;
using Ares.PagueAres.Domain.Dtos.Authentication;
using Ares.PagueAres.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;

namespace Ares.PagueAres.Application.Authentication
{
    public class AuthenticationService(
        PagueAresContext dbContext,
        IOptions<ActiveDirectorySettings> adSettings,
        IOptions<JwtSettings> jwtSettings) : IAuthenticationService
    {

        private readonly JwtSettings _jwtSettings = jwtSettings.Value;

        public GenericResponse<TokenDto> GenerateAuthenticationToken(UsuarioDto userData)
        {
            var token = GenerateJwtToken(userData);

            return new GenericResponse<TokenDto>()
            {
                Success = true,
                Message = "",
                Data = token,
                Page = 1,
                Pages = 1,
                Records = 1
            };
        }

        public async Task<UsuarioDto?> GetUserById(int id)
        {
            var dbUser = await dbContext.Usuarios.FirstOrDefaultAsync(u => u.IdUsuario == id);

            if (dbUser == null)
            {
                // Buscar no AD

                return null;
            }

            var idFuncao = await dbContext
                                .UsuarioFuncaos
                                .Where(f => f.IdUsuario == dbUser.IdUsuario)
                                .Select(f => f.IdFuncao)
                                .ToListAsync();

            var funcoes = await dbContext
                                .Funcaos
                                .Where(f => idFuncao.Contains(f.IdFuncao))
                                .ToListAsync();

            UsuarioDto user = new ()
            {
                IdUsuario = dbUser.IdUsuario,
                Codigo = dbUser.Codigo,
                Email = dbUser.Email,
                Nome = dbUser.Nome,
                Origem = dbUser.Origem,
                IdExterno = dbUser.IdExterno,
                //Senha = dbUser.Senha,
                IdDepartamento = dbUser.IdDepartamento,
                Ativo = dbUser.Ativo,
                DataCadastro = dbUser.DataCadastro,
                UsuarioCadastro = dbUser.UsuarioCadastro,
                UltimaAlteracao = dbUser.UltimaAlteracao,
                UsuarioAlteracao = dbUser.UsuarioAlteracao,
                Funcoes = [.. funcoes.Select(f =>
                {
                    return new FuncaoDto()
                    {
                        IdFuncao = f.IdFuncao,
                        Alias = f.Alias,
                        Nome = f.Nome
                    };
                })]
            };

            return user;
        }

        public async Task<UsuarioDto> TryAuthenticate(string username, string password)
        {
            var dbUser = await dbContext
                                    .Usuarios
                                    .FirstOrDefaultAsync(u => u.Email.ToLower().Trim() == username.ToLower().Trim() ||
                                                              (u.Codigo ?? "").ToLower().Trim() == username.ToLower().Trim());

            var isAdUser = false;

            if (dbUser == null || dbUser?.Origem == 1)
            {
                var adResult = await TryDomainAuthentication(username, password);

                if (!adResult)
                    throw new LoginFailedException();

                // Obter dados do usuario previamente registrados

                dbUser = await dbContext
                                    .Usuarios
                                    .FirstOrDefaultAsync(u => u.Email.ToLower().Trim() == username.ToLower().Trim() ||
                                                              ((u.Codigo ?? "").ToLower().Trim() == username.ToLower().Trim() && (u.Codigo ?? "") != ""));

                if (dbUser == null)
                    throw new LoginFailedException();

                isAdUser = true;
            }

            // Validar senha
            if (!isAdUser)
            {
                if (!VerifyPassword(password, dbUser.Senha))
                    throw new LoginFailedException();

                // Migração transparente: se ainda é SHA256 (64 chars hex), re-hash com BCrypt
                if (dbUser.Senha.Length == 64 && !dbUser.Senha.StartsWith("$2"))
                {
                    dbUser.Senha = BCrypt.Net.BCrypt.HashPassword(password);
                    await dbContext.SaveChangesAsync();
                }
            }

            var idFuncao = await dbContext
                                .UsuarioFuncaos
                                .Where(f => f.IdUsuario == dbUser.IdUsuario)
                                .Select(f => f.IdFuncao)
                                .ToListAsync();

            var funcoes = await dbContext
                                .Funcaos
                                .Where(f => idFuncao.Contains(f.IdFuncao))
                                .ToListAsync();

            UsuarioDto user = new UsuarioDto()
            {
                IdUsuario = dbUser.IdUsuario,
                Codigo = dbUser.Codigo,
                Email = dbUser.Email,
                Nome = dbUser.Nome,
                Origem = dbUser.Origem,
                IdExterno = dbUser.IdExterno,
                //Senha = dbUser.Senha,
                IdDepartamento = dbUser.IdDepartamento,
                Ativo = dbUser.Ativo,
                DataCadastro = dbUser.DataCadastro,
                UsuarioCadastro = dbUser.UsuarioCadastro,
                UltimaAlteracao = dbUser.UltimaAlteracao,
                UsuarioAlteracao = dbUser.UsuarioAlteracao,
                Funcoes = [.. funcoes.Select(f =>
                {
                    return new FuncaoDto()
                    {
                        IdFuncao = f.IdFuncao,
                        Alias = f.Alias,
                        Nome = f.Nome
                    };
                })]
            };

            return user;
        }

        private async Task<bool> TryDomainAuthentication(string username, string password)
        {
            var adOptions = adSettings.Value;

            if (!adOptions.EnableDomainAuthentication)
                return false;

            foreach (var server in adOptions.Servers)
            {
                try
                {
                    AdUserInfo? adUser = ActiveDirectoryService.GetUserInformation(server.Host, server.Domain, server.Username, server.Password, username);

                    if (adUser == null)
                        continue;

                    // Validar senha
                    if (!ActiveDirectoryService.TryAuthenticate(server.Host, server.Domain, server.Username, server.Password, username, password))
                        return false;

                    // Verificar se o usuário está registrado na base interna
                    var dbUser = await dbContext
                                    .Usuarios
                                    .FirstOrDefaultAsync(u => u.Email.ToLower().Trim() == adUser.Email.ToLower().Trim() ||
                                                              ((u.Codigo ?? "").ToLower().Trim() == username.ToLower().Trim() && (u.Codigo ?? "") != ""));

                    if (dbUser == null)
                    {
                        dbUser = new Infrastructure.Models.Usuario();

                        dbUser.Origem = 1;
                        dbUser.DataCadastro = DateTime.Now;
                        dbUser.Ativo = true;
                        dbUser.Senha = "";
                        dbUser.UsuarioCadastro = null;
                        dbUser.IdExterno = adUser.Sid;
                        dbUser.Codigo = adUser.Login;

                        dbContext.Add(dbUser);
                    }

                    dbUser.UltimaAlteracao = DateTime.Now;
                    dbUser.Nome = adUser.Name;
                    dbUser.Email = adUser.Email;
                    dbUser.Codigo = username;
                    dbUser.IdExterno = adUser.Sid;

                    await dbContext.SaveChangesAsync();

                    return true;
                }
                catch
                {
                    continue;
                }
            }

            return false;
        }

        private static bool VerifyPassword(string password, string storedHash)
        {
            // BCrypt hash
            if (storedHash.StartsWith("$2"))
                return BCrypt.Net.BCrypt.Verify(password, storedHash);

            // SHA256 legado
            return ComputeSha256Hash(password).Equals(storedHash, StringComparison.OrdinalIgnoreCase);
        }

        private static string ComputeSha256Hash(string rawData)
        {
            byte[] bytes = SHA256.HashData(Encoding.UTF8.GetBytes(rawData));

            StringBuilder builder = new();
            for (int i = 0; i < bytes.Length; i++)
            {
                builder.Append(bytes[i].ToString("x2"));
            }
            return builder.ToString();
        }

        private TokenDto GenerateJwtToken(UsuarioDto userData)
        {
            var claims = new[]
            {
                new Claim(JwtRegisteredClaimNames.Sub, userData.Nome),
                new Claim(JwtRegisteredClaimNames.NameId, userData.IdUsuario.ToString()),
                new Claim(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString()),
                new Claim("Roles", string.Join(",", (userData.Funcoes ?? []).Select(x => x.Alias)))
            };

            var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(_jwtSettings.Secret));
            var creds = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);
            var expires = DateTime.UtcNow.AddMinutes(_jwtSettings.ExpiryMinutes);

            var token = new JwtSecurityToken(
                issuer: _jwtSettings.Issuer,
                audience: _jwtSettings.Audience,
                claims: claims,
                expires: expires,
                signingCredentials: creds);

            return new TokenDto()
            {
                Token = new JwtSecurityTokenHandler().WriteToken(token),
                Expires = expires
            };
        }
    }
}
