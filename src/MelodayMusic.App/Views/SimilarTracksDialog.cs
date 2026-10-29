using MelodayMusic.Controls;
using MelodayMusic.Core.Models;
using MelodayMusic.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace MelodayMusic.Views;

public static class SimilarTracksDialog
{
    public static async Task ShowAsync(Track seed)
    {
        var intelligence = App.Services.GetRequiredService<MusicIntelligence>();
        var dialog = Dialogs.Create("Similar Tracks");
        dialog.CloseButtonText = "Done";
        var note = new TextBlock { Text = "Loading matches from " + intelligence.Name + "...", TextWrapping = TextWrapping.WrapWholeWords,
            Style = (Style)Application.Current.Resources["MelodaySecondaryText"] };
        var body = new StackPanel { Spacing = 12, Width = Dialogs.ContentWidth(540), Children = { note } };
        dialog.Content = new ScrollViewer { MaxHeight = Dialogs.ContentHeight(540), Content = body };
        using var lifetime = new CancellationTokenSource();
        dialog.Closed += (_, _) => lifetime.Cancel();
        var selected = intelligence.Kind;
        async Task Load()
        {
            try
            {
                var tracks = new Core.Meloday.SongSet([seed]).AddNew(await intelligence.SimilarAsync<Track>(seed.RatingKey, 25, ct: lifetime.Token));
                if (lifetime.IsCancellationRequested || intelligence.Kind != selected) return;
                note.Text = tracks.Count == 0 ? intelligence.IsMeloday
                    ? "No matches yet. This track is prioritised for analysis."
                    : "No Plex sonic matches are available for this track." : intelligence.Name;
                foreach (var track in tracks)
                {
                    var art = new ArtworkImage { Width = UiScale.Size(40), Height = UiScale.Size(40), DecodeSize = 80, SourcePath = track.DisplayThumb,
                        ArtworkCornerRadius = (CornerRadius)Application.Current.Resources["MelodayRadiusThumb"] };
                    var labels = new StackPanel { VerticalAlignment = VerticalAlignment.Center, Children =
                    {
                        new TextBlock { Text = track.Title, Style = (Style)Application.Current.Resources["MelodayBodyText"], TextTrimming = TextTrimming.CharacterEllipsis },
                        new TextBlock { Text = track.DisplayArtist, Style = (Style)Application.Current.Resources["MelodaySecondaryText"], TextTrimming = TextTrimming.CharacterEllipsis }
                    }};
                    var row = new Grid { ColumnSpacing = 12, Children = { art, labels } };
                    row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
                    row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                    Grid.SetColumn(labels, 1);
                    var button = new Button { Content = row, HorizontalContentAlignment = HorizontalAlignment.Stretch,
                        HorizontalAlignment = HorizontalAlignment.Stretch, Style = (Style)Application.Current.Resources["MelodayTileButtonStyle"] };
                    Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(button, $"Play {track.Title} by {track.DisplayArtist}");
                    button.Click += async (_, _) =>
                    {
                        dialog.Hide();
                        await App.Services.GetRequiredService<PlayerService>().PlayTracksAsync(tracks, tracks.ToList().IndexOf(track));
                    };
                    body.Children.Add(button);
                }
            }
            catch (OperationCanceledException) { }
            catch (Exception ex)
            {
                if (!lifetime.IsCancellationRequested) note.Text = "Matches are unavailable right now. Please try again.";
                Microsoft.Extensions.Logging.LoggerExtensions.LogDebug(App.Services.GetRequiredService<Microsoft.Extensions.Logging.ILogger<MusicIntelligence>>(), ex, "Similar-track view unavailable");
            }
        }
        dialog.Opened += async (_, _) => await Load();
        await dialog.ShowAsync();
    }
}
