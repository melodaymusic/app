using HumeMusic.Controls;
using HumeMusic.Core.Models;
using HumeMusic.Core.Plex;
using HumeMusic.Core.Services;
using HumeMusic.Helpers;
using HumeMusic.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace HumeMusic.Views;

/// <summary>
/// A song's details, as Apple Music's Get Info shows them: the song, your listening, and its file
/// (format, quality, size and where it is on the server).
/// </summary>
public static class SongInfoDialog
{
    public static async Task ShowAsync(Track track)
    {
        var session = App.Services.GetRequiredService<PlexSession>();
        // Fresh details (plays, file) where the server can give them.
        if (session.Client is { } client)
        {
            try
            {
                if (await client.GetTrackAsync(track.RatingKey) is { } full) track = full;
            }
            catch (PlexException)
            {
            }
        }
        var level = await App.Services.GetRequiredService<TransitionPlanner>().LoudnessAsync(track);

        var dialog = Dialogs.Create("Song Info");
        dialog.Resources["ContentDialogMaxWidth"] = 640.0;
        var art = new ArtworkImage
        {
            Width = 88,
            Height = 88,
            DecodeSize = 176,
            SourcePath = track.DisplayThumb,
            PlaceholderIcon = "Music",
            ArtworkCornerRadius = (CornerRadius)Application.Current.Resources["HumeRadiusThumb"],
        };
        var heading = new StackPanel
        {
            VerticalAlignment = VerticalAlignment.Center,
            Spacing = 2,
            Children =
            {
                new TextBlock { Text = track.Title, Style = Style("HumeHeadlineText"), TextWrapping = TextWrapping.WrapWholeWords, IsTextSelectionEnabled = true },
                new TextBlock { Text = track.DisplayArtist, Style = Style("HumeSecondaryText") },
            },
        };
        Grid.SetColumn(heading, 1);
        var top = new Grid { ColumnSpacing = 16, Children = { art, heading } };
        top.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        top.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

        var m = track.Media;
        var body = new StackPanel { Spacing = 20, Width = 540, Children = { top } };
        body.Children.Add(Section("Song",
            ("Album", track.AlbumTitle),
            ("Album artist", track.ArtistTitle != track.DisplayArtist ? track.ArtistTitle : null),
            ("Track", track.TrackNumber is > 0 ? (track.DiscNumber is > 1 ? $"{track.TrackNumber} (disc {track.DiscNumber})" : track.TrackNumber.ToString()) : null),
            ("Year", track.Year is > 0 ? track.Year.ToString() : null),
            ("Genre", track.Genres.Count > 0 ? string.Join(", ", track.Genres) : null),
            ("Mood", track.Moods.Count > 0 ? string.Join(", ", track.Moods.Take(6)) : null),
            ("Length", Format.Duration(track.Duration))));
        body.Children.Add(Section("Your Listening",
            ("Plays", track.ViewCount == 0 ? "Not played yet" : track.ViewCount == 1 ? "Once" : $"{track.ViewCount:N0} times"),
            ("Skips", track.SkipCount > 0 ? track.SkipCount.ToString() : null),
            ("Last played", track.LastViewedAt is { } last ? Format.Ago(last) : null),
            ("Added", track.AddedAt is { } added ? added.LocalDateTime.ToString("d MMMM yyyy") : null),
            ("Rating", track.UserRating is > 0 and var r ? Stars(r) : "Not rated")));
        body.Children.Add(Section("File",
            ("Format", m?.Codec is { } codec ? codec.ToUpperInvariant() + (m.Container is { } c && !c.Equals(codec, StringComparison.OrdinalIgnoreCase) ? $" in {c.ToUpperInvariant()}" : string.Empty) : null),
            ("Quality", m is null ? null : Quality(m)),
            ("Channels", m?.Channels switch { 1 => "Mono", 2 => "Stereo", { } n and > 2 => $"{n} channels", _ => null }),
            ("Size", m?.Size is > 0 ? Format.Bytes(m.Size.Value) : null),
            ("Sound Check", level?.GainDb is { } gain ? $"{gain:+0.0;-0.0;0} dB" : null),
            ("Sonic analysis", track.HasSonicAnalysis ? "Yes" : null),
            ("On this computer", App.Services.GetRequiredService<DownloadService>().IsDownloaded(track.RatingKey) ? "Downloaded" : null),
            ("Location", m?.FilePath)));

        dialog.Content = new ScrollViewer { MaxHeight = 600, Content = body };
        dialog.CloseButtonText = "Done";
        Dialogs.PreferClose(dialog);
        await dialog.ShowAsync();
    }

    private static Style Style(string key) => (Style)Application.Current.Resources[key];

    private static string Stars(double rating)
    {
        var stars = (int)Math.Round(rating / 2);
        return stars == 1 ? "1 star" : $"{stars} stars";
    }

    private static string Quality(AudioMediaInfo m)
    {
        var parts = new List<string>();
        if (m.BitDepth is > 0) parts.Add($"{m.BitDepth}-bit");
        if (m.SampleRate is > 0) parts.Add($"{m.SampleRate / 1000.0:0.#} kHz");
        if (m.Bitrate is > 0) parts.Add($"{m.Bitrate:N0} kbps");
        return string.Join(" · ", parts);
    }

    /// <summary>A titled group of label and value rows (rows without a value are left out).</summary>
    private static StackPanel Section(string title, params (string Label, string? Value)[] rows)
    {
        var grid = new Grid { ColumnSpacing = 16, RowSpacing = 6 };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(130) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        var row = 0;
        foreach (var (label, value) in rows)
        {
            if (string.IsNullOrWhiteSpace(value)) continue;
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            var name = new TextBlock { Text = label, Style = Style("HumeSecondaryText") };
            var text = new TextBlock { Text = value, Style = Style("HumeBodyText"), TextWrapping = TextWrapping.Wrap, IsTextSelectionEnabled = true };
            Grid.SetRow(name, row);
            Grid.SetRow(text, row);
            Grid.SetColumn(text, 1);
            grid.Children.Add(name);
            grid.Children.Add(text);
            row++;
        }
        return new StackPanel
        {
            Spacing = 8,
            Children = { new TextBlock { Text = title, Style = Style("HumeBodyStrongText") }, grid },
        };
    }
}
