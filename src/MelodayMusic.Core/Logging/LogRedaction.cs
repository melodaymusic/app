using System.Text.RegularExpressions;

namespace MelodayMusic.Core.Logging;

public static partial class LogRedaction
{
    private static readonly HashSet<string> PersonalFields = new(StringComparer.OrdinalIgnoreCase)
        { "Track", "Artist", "Next", "Title", "User", "Username", "Email", "Server", "Library", "Device", "Playlist" };

    public static string RemovePersonalFields(string message, IEnumerable<KeyValuePair<string, object?>> fields)
    {
        foreach (var field in fields)
            if (PersonalFields.Contains(field.Key) && field.Value is string { Length: > 0 } text)
                message = message.Replace(text, "[private]", StringComparison.Ordinal);
        return message;
    }

    public static string Redact(string value)
    {
        value = QuotedSecrets().Replace(value, m => m.Groups[1].Value + m.Groups[2].Value + "***" + m.Groups[2].Value);
        value = Secrets().Replace(value, m => m.Groups[1].Value + "***");
        value = EncodedSecrets().Replace(value, m => m.Groups[1].Value + "***");
        value = AuthorizationHeader().Replace(value, m => m.Groups[1].Value + "***");
        return Authorization().Replace(value, m => m.Groups[1].Value + "***");
    }

    [GeneratedRegex(@"((?:X-Plex-Token|authToken|authentication[_-]?token|accessToken|access_token|refresh_token|token|api[_-]?key|api[_-]?secret|shared[_-]?secret|api_sig|client_secret|session[_-]?key|password|\bcode)[""']?\s*[=:]\s*)([""'])(.*?)\2", RegexOptions.IgnoreCase | RegexOptions.Singleline)]
    private static partial Regex QuotedSecrets();

    [GeneratedRegex(@"(\bAuthorization[""']?\s*[:=]\s*[""']?)[^\r\n""',}]+", RegexOptions.IgnoreCase)]
    private static partial Regex AuthorizationHeader();

    [GeneratedRegex(@"((?:X-Plex-Token|authToken|authentication[_-]?token|accessToken|access_token|refresh_token|token|api[_-]?key|api[_-]?secret|shared[_-]?secret|api_sig|client_secret|session[_-]?key|password|\bcode)[""']?\s*[=:]\s*[""']?)[^&""'\s,}<>]+", RegexOptions.IgnoreCase)]
    private static partial Regex Secrets();

    [GeneratedRegex(@"((?:X-Plex-Token|authToken|accessToken|token|api[_-]?key|api[_-]?secret|shared[_-]?secret|api_sig|session[_-]?key|code)%3D)(?:(?!%26)[^&\s""'<>])+", RegexOptions.IgnoreCase)]
    private static partial Regex EncodedSecrets();

    [GeneratedRegex(@"(\b(?:Bearer|Basic|Token)\s+)[A-Za-z0-9._~+/=-]+", RegexOptions.IgnoreCase)]
    private static partial Regex Authorization();
}
