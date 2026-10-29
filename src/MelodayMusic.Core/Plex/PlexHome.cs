using System.Text.Json;
using System.Xml.Linq;

namespace MelodayMusic.Core.Plex;

/// <summary>A member of the Plex Home the signed-in account belongs to.</summary>
/// <param name="IsManaged">A managed (restricted) user, set up by the Home's admin without a Plex account of their own.</param>
/// <param name="HasPin">Switching to this member asks for their Plex Home PIN.</param>
public sealed record PlexHomeUser(string Id, string Uuid, string Name, string? Thumb, bool IsAdmin, bool IsManaged, bool IsGuest, bool HasPin);

public sealed class PlexHomePinException() : PlexException("That PIN isn't right.");

/// <summary>Reads plex.tv's answers about a Plex Home, which come as JSON from the current API and XML from the older one.</summary>
public static class PlexHome
{
    public static IReadOnlyList<PlexHomeUser> ParseUsers(string body)
    {
        body = body.Trim();
        if (body.Length == 0) return [];
        return body[0] == '<' ? FromXml(XDocument.Parse(body)) : FromJson(body);
    }

    /// <summary>The token a switch hands back for the member switched to.</summary>
    public static string? ParseToken(string body)
    {
        body = body.Trim();
        if (body.Length == 0) return null;
        if (body[0] == '<')
        {
            foreach (var element in XDocument.Parse(body).Descendants().Prepend(XDocument.Parse(body).Root!))
                if (Attribute(element, "authenticationToken", "authToken") is { Length: > 0 } token) return token;
            return null;
        }
        using var json = JsonDocument.Parse(body);
        return Token(json.RootElement);

        static string? Token(JsonElement element)
        {
            if (element.ValueKind != JsonValueKind.Object) return null;
            foreach (var name in new[] { "authToken", "authenticationToken" })
                if (element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String && value.GetString() is { Length: > 0 } token)
                    return token;
            return element.TryGetProperty("user", out var user) ? Token(user) : null;
        }
    }

    private static List<PlexHomeUser> FromJson(string body)
    {
        using var json = JsonDocument.Parse(body);
        var root = json.RootElement;
        JsonElement list = default;
        if (root.ValueKind == JsonValueKind.Array) list = root;
        else if (root.ValueKind == JsonValueKind.Object)
        {
            if (root.TryGetProperty("users", out var users)) list = users;
            else if (root.TryGetProperty("MediaContainer", out var container) && container.TryGetProperty("User", out var inner)) list = inner;
        }
        if (list.ValueKind != JsonValueKind.Array) return [];

        var found = new List<PlexHomeUser>();
        foreach (var user in list.EnumerateArray())
        {
            string? Text(string name) => user.TryGetProperty(name, out var v) ? v.ValueKind switch
            {
                JsonValueKind.String => v.GetString(),
                JsonValueKind.Number => v.GetRawText(),
                _ => null,
            } : null;
            bool Flag(string name) => user.TryGetProperty(name, out var v) && (v.ValueKind == JsonValueKind.True
                || v.ValueKind == JsonValueKind.Number && v.GetRawText() != "0"
                || v.ValueKind == JsonValueKind.String && v.GetString() is "1" or "true");
            if (Make(Text("id"), Text("uuid"), Text("friendlyName"), Text("title"), Text("username"), Text("thumb"),
                Flag("admin"), Flag("restricted"), Flag("guest"), Flag("protected")) is { } made) found.Add(made);
        }
        return found;
    }

    private static List<PlexHomeUser> FromXml(XDocument xml)
    {
        var found = new List<PlexHomeUser>();
        foreach (var user in xml.Descendants().Where(e => e.Name.LocalName == "User"))
        {
            bool Flag(string name) => Attribute(user, name) is "1" or "true";
            if (Make(Attribute(user, "id"), Attribute(user, "uuid"), Attribute(user, "friendlyName"), Attribute(user, "title"), Attribute(user, "username"),
                Attribute(user, "thumb"), Flag("admin"), Flag("restricted"), Flag("guest"), Flag("protected")) is { } made) found.Add(made);
        }
        return found;
    }

    private static PlexHomeUser? Make(string? id, string? uuid, string? friendly, string? title, string? username, string? thumb,
        bool admin, bool restricted, bool guest, bool pin)
    {
        if (string.IsNullOrEmpty(id) || string.IsNullOrEmpty(uuid)) return null;
        var name = new[] { friendly, title, username }.FirstOrDefault(n => !string.IsNullOrWhiteSpace(n))?.Trim() ?? "Plex user";
        return new PlexHomeUser(id, uuid, name, string.IsNullOrWhiteSpace(thumb) ? null : thumb, admin, restricted, guest, pin);
    }

    private static string? Attribute(XElement element, params string[] names)
    {
        foreach (var name in names)
            if (element.Attribute(name)?.Value is { } value) return value;
        return null;
    }
}
