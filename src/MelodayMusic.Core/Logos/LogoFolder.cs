using System.Globalization;
using System.Text;
using MelodayMusic.Core.Models;

namespace MelodayMusic.Core.Logos;

public sealed record LogoFile(string Name, string Path);

public static class LogoFolder
{
    private static readonly string[] ImageExtensions = [".png", ".webp", ".jpg", ".jpeg"];

    public static IReadOnlyList<LogoFile> Find(string folder)
    {
        var found = new List<LogoFile>();
        foreach (var dir in Directory.EnumerateDirectories(folder).Order(StringComparer.OrdinalIgnoreCase))
        {
            var images = Images(dir).ToList();
            var pick = images.FirstOrDefault(f => Path.GetFileNameWithoutExtension(f).Equals("logo", StringComparison.OrdinalIgnoreCase)) ?? images.FirstOrDefault();
            if (pick is not null) found.Add(new LogoFile(Path.GetFileName(dir), pick));
        }
        foreach (var file in Images(folder).Order(StringComparer.OrdinalIgnoreCase))
            found.Add(new LogoFile(Path.GetFileNameWithoutExtension(file), file));
        return found;
    }

    private static IEnumerable<string> Images(string dir) =>
        Directory.EnumerateFiles(dir).Where(f => ImageExtensions.Contains(Path.GetExtension(f).ToLowerInvariant()));

    public static (IReadOnlyList<(Artist Artist, LogoFile Logo)> Matched, IReadOnlyList<LogoFile> Unmatched) Match(
        IEnumerable<LogoFile> logos, IEnumerable<Artist> artists)
    {
        var byName = new Dictionary<string, Artist>();
        foreach (var artist in artists)
            byName.TryAdd(Normalize(artist.Title), artist);

        var matched = new List<(Artist, LogoFile)>();
        var unmatched = new List<LogoFile>();
        var used = new HashSet<string>();
        foreach (var logo in logos)
        {
            if (byName.TryGetValue(Normalize(logo.Name), out var artist) && used.Add(artist.RatingKey)) matched.Add((artist, logo));
            else unmatched.Add(logo);
        }
        return (matched, unmatched);
    }

    public static string Normalize(string name)
    {
        var decomposed = name.Normalize(NormalizationForm.FormD);
        var sb = new StringBuilder(decomposed.Length);
        foreach (var c in decomposed)
        {
            if (CharUnicodeInfo.GetUnicodeCategory(c) == UnicodeCategory.NonSpacingMark) continue;
            if (c == '&') sb.Append(" and ");
            else if (char.IsLetterOrDigit(c)) sb.Append(char.ToLowerInvariant(c));
            else sb.Append(' ');
        }
        var words = sb.ToString().Split(' ', StringSplitOptions.RemoveEmptyEntries).ToList();
        if (words.Count > 1 && words[0] == "the") words.RemoveAt(0);
        return string.Concat(words);
    }
}
