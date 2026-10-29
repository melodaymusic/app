namespace MelodayMusic.Core.Lyrics;

public static class LyricSyllables
{
    private const string Vowels = "aeiouy";

    public static int Count(string word)
    {
        var letters = new string(word.ToLowerInvariant().Where(char.IsLetter).ToArray());
        if (letters.Length == 0) return word.Any(char.IsDigit) ? 1 : 0;
        if (letters.Length <= 3) return 1;

        var count = 0;
        var inVowel = false;
        foreach (var c in letters)
        {
            var vowel = Vowels.Contains(c);
            if (vowel && !inVowel) count++;
            inVowel = vowel;
        }

        if (letters.EndsWith("e") && !letters.EndsWith("le") && count > 1) count--;
        else if (letters.EndsWith("es") && count > 1 && !"sxzhc".Contains(letters[^3])) count--;
        else if (letters.EndsWith("ed") && count > 1 && !"td".Contains(letters[^3])) count--;
        return Math.Max(1, count);
    }
}
