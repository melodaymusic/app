using MelodayMusic.Core.Models;
using MelodayMusic.Core.Plex;
using MelodayMusic.Core.Services;
using MelodayMusic.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace MelodayMusic.Views;

public static class MoodTagDialog
{
    public static async Task ShowAsync(Album album)
    {
        var service = App.Services.GetRequiredService<MoodTagService>();
        var session = App.Services.GetRequiredService<PlexSession>();
        var dialog = Dialogs.Create("Tag Moods in Plex");
        Dialogs.Wide(dialog, 640);
        var note = Dialogs.Note("Reading this album's songs…");
        var list = new StackPanel { Spacing = 10 };
        var body = new StackPanel { Spacing = 16, Width = Dialogs.ContentWidth(600), Children = { note, list } };
        dialog.Content = new ScrollViewer { MaxHeight = Dialogs.ContentHeight(560), Content = body, Padding = new Thickness(0, 0, 12, 0) };
        dialog.CloseButtonText = "Cancel";
        dialog.PrimaryButtonText = "Add Moods to Plex";
        dialog.IsPrimaryButtonEnabled = false;
        dialog.IsSecondaryButtonEnabled = false;

        IReadOnlyList<MoodRow> rows = [];
        using var lifetime = new CancellationTokenSource();
        dialog.Closed += (_, _) => lifetime.Cancel();

        async Task LoadAsync()
        {
            try
            {
                if (session.Client is not { } client) return;
                var tracks = await client.GetAlbumTracksAsync(album.RatingKey, lifetime.Token);
                rows = await service.PreviewAsync(tracks, lifetime.Token);
                if (lifetime.IsCancellationRequested) return;
                Fill(list, rows);
                var adding = rows.Count(r => r.Plan.HasChange);
                var tagged = rows.Count(r => r.ByMeloday is not null);
                note.Text = adding > 0
                    ? $"Meloday would add moods to {adding} of {rows.Count} songs. Moods the songs already have stay as they are, and the moods are locked so Plex keeps them. You can take them back out any time."
                    : tagged > 0 ? "Meloday has already added the moods it hears clearly to this album."
                    : "The Meloday Engine doesn't hear a clear mood in these songs yet (or they aren't analysed), so there's nothing to add.";
                dialog.IsPrimaryButtonEnabled = adding > 0;
                dialog.PrimaryButtonText = adding > 0 ? $"Add Moods to {adding} {(adding == 1 ? "Song" : "Songs")}" : "Add Moods to Plex";
                if (tagged > 0)
                {
                    dialog.SecondaryButtonText = "Remove Meloday's Moods";
                    dialog.IsSecondaryButtonEnabled = true;
                }
            }
            catch (Exception ex) when (ex is PlexException or HttpRequestException or TaskCanceledException or OperationCanceledException)
            {
                if (!lifetime.IsCancellationRequested) note.Text = "Couldn't read this album from Plex. Please try again.";
            }
        }

        _ = LoadAsync();
        var result = await dialog.ShowAsync();
        var notifications = App.Services.GetRequiredService<NotificationService>();
        try
        {
            if (result == ContentDialogResult.Primary)
            {
                var changed = await service.ApplyAsync(rows);
                notifications.ShowSuccess("Moods added in Plex", $"{changed} {(changed == 1 ? "song" : "songs")} on {album.Title} now carry the moods Meloday hears.");
            }
            else if (result == ContentDialogResult.Secondary)
            {
                var changed = await service.UndoAsync(rows.Where(r => r.ByMeloday is not null).Select(r => r.Track.RatingKey));
                notifications.ShowInfo("Meloday's moods removed", $"{changed} {(changed == 1 ? "song" : "songs")} on {album.Title} are back as they were.");
            }
        }
        catch (Exception ex) when (ex is PlexException or HttpRequestException or TaskCanceledException)
        {
            notifications.ShowError("Couldn't change the moods in Plex", "Check your Plex connection and try again. Nothing half-done is left unrecorded.");
        }
    }

    private static void Fill(StackPanel list, IReadOnlyList<MoodRow> rows)
    {
        var resources = Application.Current.Resources;
        list.Children.Clear();
        foreach (var row in rows)
        {
            string detail;
            if (row.Plan.HasChange)
                detail = "Adds " + string.Join(", ", row.Plan.Add.Select(a => $"{a.Mood} ({a.Confidence:P0} sure)"));
            else if (row.ByMeloday is { } by)
                detail = "Meloday added " + string.Join(", ", by.Added);
            else if (!row.Analysed)
                detail = "Not analysed by ME yet";
            else
                detail = "No clear mood to add";
            var has = row.Current.Count > 0 ? "In Plex: " + string.Join(", ", row.Current) : "No moods in Plex";
            var text = new StackPanel
            {
                Spacing = 2,
                Children =
                {
                    new TextBlock { Text = row.Track.Title, Style = (Style)resources["MelodayBodyText"], TextTrimming = TextTrimming.CharacterEllipsis },
                    new TextBlock { Text = has, Style = (Style)resources["MelodaySecondaryText"], TextWrapping = TextWrapping.WrapWholeWords },
                    new TextBlock
                    {
                        Text = detail,
                        Style = (Style)resources[row.Plan.HasChange ? "MelodayBodyStrongText" : "MelodaySecondaryText"],
                        TextWrapping = TextWrapping.WrapWholeWords,
                    },
                },
            };
            list.Children.Add(text);
        }
    }
}
