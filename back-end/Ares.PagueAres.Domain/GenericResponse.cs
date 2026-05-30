namespace Ares.PagueAres.Domain
{
    public class GenericResponse<T> where T : class
    {

        public bool Success { get; set; }

        public string Message { get; set; } = "";

        public T? Data { get; set; }

        public int Records { get; set; }

        public int Pages { get; set; }

        public int Page { get; set; }

    }
}
