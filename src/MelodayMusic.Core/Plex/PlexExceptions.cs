using System.Net;

namespace MelodayMusic.Core.Plex;

public class PlexException : Exception
{
    public PlexException(string message, Exception? inner = null) : base(message, inner) { }
}

public sealed class PlexUnauthorizedException : PlexException
{
    public PlexUnauthorizedException(string message) : base(message) { }
}

public sealed class PlexUnreachableException : PlexException
{
    public PlexUnreachableException(string message, Exception? inner = null) : base(message, inner) { }
}

public sealed class PlexApiException : PlexException
{
    public HttpStatusCode? StatusCode { get; }

    public PlexApiException(string message, HttpStatusCode? statusCode = null, Exception? inner = null)
        : base(message, inner)
    {
        StatusCode = statusCode;
    }
}
