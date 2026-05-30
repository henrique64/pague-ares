namespace Ares.PagueAres.Application.Exceptions
{
    public class LoginFailedException : Exception
    {

        public LoginFailedException() : base("O usuário e/ou senha informados são inválidos.")
        {

        }

    }
}
