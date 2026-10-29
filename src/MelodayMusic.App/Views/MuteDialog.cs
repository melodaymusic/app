using MelodayMusic.Core.Models;
using MelodayMusic.Core.Mutes;
using MelodayMusic.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace MelodayMusic.Views;

public static class MuteDialog
{
    public static async Task ShowAsync(Track track)
    {
        var mutes = App.Services.GetRequiredService<MuteService>();
        var artistName = track.ArtistTitle ?? track.DisplayArtist;
        var canArtist = track.ArtistRatingKey is not null;

        var what = new ComboBox { Header = "Mute", MinWidth = Dialogs.ContentWidth(320), HorizontalAlignment = HorizontalAlignment.Stretch };
        what.Items.Add($"This song, “{track.Title}”");
        if (canArtist) what.Items.Add($"The artist, {artistName}");
        what.SelectedIndex = 0;

        var days = new NumberBox
        {
            Header = "For how many days",
            Minimum = MuteLengths.Shortest,
            Maximum = MuteLengths.Longest,
            SmallChange = 1,
            LargeChange = 7,
            Value = mutes.DefaultDays,
            SpinButtonPlacementMode = NumberBoxSpinButtonPlacementMode.Inline,
            MinWidth = Dialogs.ContentWidth(320),
            HorizontalAlignment = HorizontalAlignment.Stretch,
        };
        var summary = Dialogs.Note(string.Empty);
        void Describe()
        {
            var length = double.IsNaN(days.Value) ? mutes.DefaultDays : (int)days.Value;
            summary.Text = $"Stays out of Meloday for {MuteLengths.Describe(length)}, until {DateTimeOffset.Now.AddDays(length):MMMM d}. Songs you choose yourself still play.";
        }
        days.ValueChanged += (_, _) => Describe();
        Describe();

        var dialog = Dialogs.Create("Mute for a While");
        dialog.Content = new StackPanel { Spacing = 16, Width = Dialogs.ContentWidth(360), Children = { what, days, summary } };
        dialog.PrimaryButtonText = "Mute";
        dialog.CloseButtonText = "Cancel";
        if (await dialog.ShowAsync() != ContentDialogResult.Primary) return;

        var length2 = double.IsNaN(days.Value) ? mutes.DefaultDays : (int)Math.Clamp(Math.Round(days.Value), MuteLengths.Shortest, MuteLengths.Longest);
        if (what.SelectedIndex == 0 || !canArtist) await mutes.MuteSongAsync(track, length2);
        else await mutes.MuteArtistAsync(track, length2);
    }
}
