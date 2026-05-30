using Ares.PagueAres.Application.External.ActiveDirectory.Dtos;
using System.DirectoryServices.AccountManagement;

namespace Ares.PagueAres.Application.External.ActiveDirectory
{
    public static class ActiveDirectoryService
    {

        //public bool ValidateUser()
        //{
        //    using (PrincipalContext pc = new PrincipalContext(ContextType.Domain, Domain))
        //    {
        //        // validate the credentials
        //        return pc.ValidateCredentials(Credentials.Username, Credentials.Password);
        //    }
        //}

        [System.Diagnostics.CodeAnalysis.SuppressMessage("Interoperability", "CA1416:Validar a compatibilidade da plataforma", Justification = "Necessidade específica")]
        public static AdUserInfo? GetUserInformation(string server, string domain, string username, string password, string loginName)
        {
            using PrincipalContext pc = new(ContextType.Domain, server, username, password);

            var user = UserPrincipal.FindByIdentity(pc, IdentityType.SamAccountName, $@"{domain}\" + loginName);

            if (user == null)
                return null;

            return new AdUserInfo()
            {
                Sid = user.Sid.Value,
                Email = user.EmailAddress ?? "",
                Login = user.SamAccountName,
                Name = user.DisplayName
            };
        }

        [System.Diagnostics.CodeAnalysis.SuppressMessage("Interoperability", "CA1416:Validar a compatibilidade da plataforma", Justification = "Necessidade específica")]
        public static bool TryAuthenticate(string server, string domain, string username, string password, string loginName, string loginPassword)
        {
            using PrincipalContext pc = new(ContextType.Domain, server, username, password);

            return pc.ValidateCredentials(loginName, loginPassword);
        }

    }
}
