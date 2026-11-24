using MelodayMusic.Core.Models;
using MelodayMusic.Core.Settings;

namespace MelodayMusic.Services;

/// <summary>
/// The listener's own choice of family for a genre, style or mood, kept in settings and applied everywhere families are used:
/// Browse, family pages, mix names and anything else that asks <see cref="TagFamilies"/>.
/// </summary>
public sealed class TagFamilyMoves(SettingsStore settings)
{
    public void Apply() => TagFamilies.UseOverrides(settings.Current.TagFamilyMoves);

    /// <summary>Puts a tag in a family, or in none with null. Choosing the family it would get anyway clears the move.</summary>
    public void Move(TagItem tag, string? family)
    {
        var key = TagFamilies.OverrideKey(tag.Title, tag.TagKind);
        var moves = settings.Current.TagFamilyMoves;
        if (family == TagFamilies.AutomaticFamilyOf(tag.Title, tag.TagKind)) moves.Remove(key);
        else moves[key] = family ?? string.Empty;
        Save();
    }

    public void Reset(TagItem tag)
    {
        if (settings.Current.TagFamilyMoves.Remove(TagFamilies.OverrideKey(tag.Title, tag.TagKind))) Save();
    }

    public bool HasMoves => settings.Current.TagFamilyMoves.Count > 0;

    public void ResetAll()
    {
        if (settings.Current.TagFamilyMoves.Count == 0) return;
        settings.Current.TagFamilyMoves.Clear();
        Save();
    }

    private void Save()
    {
        settings.Save();
        Apply();
    }
}
