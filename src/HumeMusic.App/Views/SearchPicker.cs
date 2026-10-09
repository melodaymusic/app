using HumeMusic.Core.Models;
using HumeMusic.Core.Plex;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace HumeMusic.Views;

/// <summary>One match in a <see cref="SearchPicker"/>: the item, what to call it, its picture, and what it is when it has none.</summary>
internal sealed record PickResult(MediaItem Item, string Title, string? Detail, string? Thumb, string Icon, CornerRadius Radius);

/// <summary>
/// Finding one thing in the library from a dialog (the Radio Builders, Sonic Adventure): a search box with the matches in a list
/// under it, in the same rows as everywhere, each chosen by a click. The list shows only while there is something typed.
/// </summary>
internal sealed class SearchPicker
{
    private readonly Func<string, Task<IReadOnlyList<PickResult>>> _search;
    private readonly ListView _list;
    private readonly TextBlock _note;
    private int _version;

    public SearchPicker(string placeholder, Func<string, Task<IReadOnlyList<PickResult>>> search, Action<MediaItem> chosen)
    {
        _search = search;
        var resources = Application.Current.Resources;
        Box = new TextBox { PlaceholderText = placeholder };
        _list = new ListView
        {
            MaxHeight = 260,
            SelectionMode = ListViewSelectionMode.None,
            IsItemClickEnabled = true,
            ItemContainerStyle = (Style)resources["HumeTrackItemStyle"],
            Visibility = Visibility.Collapsed,
        };
        _note = Dialogs.Note(string.Empty);
        _note.Visibility = Visibility.Collapsed;
        Root = new StackPanel { Spacing = 8, Children = { Box, _list, _note } };

        Box.TextChanged += async (_, _) =>
        {
            var mine = ++_version;
            var query = Box.Text.Trim();
            if (query.Length < 2)
            {
                Clear();
                return;
            }
            await Task.Delay(250);
            if (mine != _version) return;
            try
            {
                var found = await _search(query);
                if (mine != _version) return;
                _list.ItemsSource = found.Select(r =>
                {
                    var row = Dialogs.MediaRow(r.Thumb, r.Title, r.Detail, r.Icon, r.Radius, 40);
                    row.Tag = r;
                    return (UIElement)row;
                }).ToList();
                _list.Visibility = found.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
                _note.Text = "Nothing matches.";
                _note.Visibility = found.Count > 0 ? Visibility.Collapsed : Visibility.Visible;
            }
            catch (PlexException)
            {
                Clear();
            }
        };
        _list.ItemClick += (_, e) =>
        {
            if (e.ClickedItem is not FrameworkElement { Tag: PickResult picked }) return;
            Reset();
            chosen(picked.Item);
        };
    }

    /// <summary>The search box and its matches.</summary>
    public StackPanel Root { get; }

    public TextBox Box { get; }

    private void Clear()
    {
        _list.ItemsSource = null;
        _list.Visibility = Visibility.Collapsed;
        _note.Visibility = Visibility.Collapsed;
    }

    /// <summary>Empties the box and the matches, for the next search.</summary>
    public void Reset()
    {
        _version++;
        Box.Text = string.Empty;
        Clear();
    }
}
