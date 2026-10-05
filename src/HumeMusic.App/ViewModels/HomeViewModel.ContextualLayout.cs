namespace HumeMusic.ViewModels;

public sealed partial class HomeViewModel
{
    /// <summary>Places the temporary context shelves ahead of the normal configurable Home rows.</summary>
    public void ApplyContextualRows()
    {
        for (var i = Rows.Count - 1; i >= 0; i--)
            if (Rows[i].Identifier?.StartsWith("context.", StringComparison.Ordinal) == true)
                Rows.RemoveAt(i);

        var insertAt = Rows.Count > 0 && Rows[0].Identifier == PinnedId ? 1 : 0;
        foreach (var row in ContextualRows.Reverse()) Rows.Insert(insertAt, row);
    }
}
