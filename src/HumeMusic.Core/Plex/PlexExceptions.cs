using System.Net;

namespace HumeMusic.Core.Plex;

/// <summary>Base type for failures talking to plex.tv or a Plex Media Server.</summary>
public class PlexException : Exception
{
    public PlexException(string message, Exception? inner = null) : base(message, inner) { }
}

/// <summary>The token was rejected (HTTP 401). The user has to sign in again.</summary>
public sealed class PlexUnauthorizedException : PlexException
{
    public PlexUnauthorizedException(string message) : base(message) { }
}

/// <summary>The server or plex.tv could not be reached (DNS, TCP, TLS, timeout).</summary>
public sealed class PlexUnreachableException : PlexException
{
    public PlexUnreachableException(string message, Exception? inner = null) : base(message, inner) { }
}

/// <summary>The server answered with an error status or an unreadable body.</summary>
public sealed class PlexApiException : PlexException
{
    public HttpStatusCode? StatusCode { get; }

    public PlexApiException(string message, HttpStatusCode? statusCode = null, Exception? inner = null)
        : base(message, inner)
    {
        StatusCode = statusCode;
    }
}
