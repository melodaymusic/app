namespace MelodayMusic.Core.Models;

public static class ArtistCredit
{
    public static bool Mentions(string? credit, string? artist)
    {
        if (string.IsNullOrWhiteSpace(credit) || string.IsNullOrWhiteSpace(artist)) return false;
        var name = artist.Trim();
        for (var i = credit.IndexOf(name, StringComparison.OrdinalIgnoreCase); i >= 0;
             i = credit.IndexOf(name, i + 1, StringComparison.OrdinalIgnoreCase))
        {
            var end = i + name.Length;
            var startsClean = i == 0 || !char.IsLetterOrDigit(credit[i - 1]);
            var endsClean = end == credit.Length || !char.IsLetterOrDigit(credit[end]) && !IsJoinedName(credit, end);
            if (startsClean && endsClean) return true;
        }
        return false;
    }

    private static readonly string[] Strong = [" feat. ", " feat ", " ft. ", " ft ", " featuring ", " with ", " x ", " vs. ", " vs "];

    public static IReadOnlyList<IReadOnlyList<string>> Others(string? credit, string? title, string? mainArtist)
    {
        var groups = new List<IReadOnlyList<string>>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var main = Plain(mainArtist);

        void Add(string raw)
        {
            var name = raw.Trim().Trim('(', ')', '[', ']', ',', ' ');
            if (name.Length < 2) return;
            var candidates = new List<string> { name };
            foreach (var part in name.Split([" & ", " and ", ", ", " + "], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
                if (part.Length >= 2 && part != name) candidates.Add(part);
            if (candidates.All(c => Plain(c) == main || Plain(c) == "various artists")) return;
            if (seen.Add(Plain(candidates[0]))) groups.Add(candidates.Where(c => Plain(c) != main).ToList());
        }

        if (!string.IsNullOrWhiteSpace(credit))
        {
            var text = credit;
            foreach (var word in Strong) text = text.Replace(word, "/", StringComparison.OrdinalIgnoreCase);
            foreach (var part in text.Split(['/', ';'], StringSplitOptions.RemoveEmptyEntries)) Add(part);
        }
        if (!string.IsNullOrWhiteSpace(title))
        {
            var match = System.Text.RegularExpressions.Regex.Match(title, @"[\(\[](?:feat\.?|ft\.?|featuring|with)\s+([^\)\]]+)[\)\]]", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
            if (match.Success)
                foreach (var part in match.Groups[1].Value.Split(['/', ';', '&', ','], StringSplitOptions.RemoveEmptyEntries)) Add(part);
        }
        return groups;
    }

    private static string Plain(string? name) => (name ?? string.Empty).Trim().ToLowerInvariant();

    public static bool NamedIn(string? title, string? artist)
    {
        if (string.IsNullOrWhiteSpace(title) || string.IsNullOrWhiteSpace(artist)) return false;
        var name = artist.Trim();
        for (var i = title.IndexOf(name, StringComparison.OrdinalIgnoreCase); i >= 0;
             i = title.IndexOf(name, i + 1, StringComparison.OrdinalIgnoreCase))
        {
            var end = i + name.Length;
            if ((i == 0 || !char.IsLetterOrDigit(title[i - 1])) && (end == title.Length || !char.IsLetterOrDigit(title[end]))) return true;
        }
        return false;
    }

    private static bool IsJoinedName(string credit, int end) =>
        end + 1 < credit.Length && credit[end] == ' ' && char.IsUpper(credit[end + 1])
        && !StartsSeparatorWord(credit, end + 1);

    private static readonly string[] SeparatorWords = ["And ", "Feat", "Ft.", "Ft ", "With ", "X ", "Vs"];

    private static bool StartsSeparatorWord(string credit, int at) =>
        SeparatorWords.Any(w => string.Compare(credit, at, w, 0, w.Length, StringComparison.Ordinal) == 0);
}
