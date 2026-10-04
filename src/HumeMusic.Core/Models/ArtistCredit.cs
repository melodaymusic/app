namespace HumeMusic.Core.Models;

/// <summary>Reading artist credits such as "Maroon 5/Megan Thee Stallion" or "Calvin Harris ft. Katy Perry".</summary>
public static class ArtistCredit
{
    /// <summary>
    /// Whether a credit names the artist as a whole name (so "Nicki Minaj" is found in "Maroon 5/Nicki Minaj",
    /// but "Ava" is not found in "Ava Max" or "Java").
    /// </summary>
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

    /// <summary>
    /// Whether a title names the artist as whole words ("Nicki Minaj Radio", "Adele Essentials",
    /// "Featuring Nicki Minaj"). Unlike a credit, a title often goes on after the name.
    /// </summary>
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

    // "Ava Max" is a different artist from "Ava": a space followed by a capitalised word continues the name.
    private static bool IsJoinedName(string credit, int end) =>
        end + 1 < credit.Length && credit[end] == ' ' && char.IsUpper(credit[end + 1])
        && !StartsSeparatorWord(credit, end + 1);

    private static readonly string[] SeparatorWords = ["And ", "Feat", "Ft.", "Ft ", "With ", "X ", "Vs"];

    private static bool StartsSeparatorWord(string credit, int at) =>
        SeparatorWords.Any(w => string.Compare(credit, at, w, 0, w.Length, StringComparison.Ordinal) == 0);
}
