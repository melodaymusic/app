using CommunityToolkit.Mvvm.Input;
using MelodayMusic.Controls;
using MelodayMusic.Helpers;
using MelodayMusic.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;

namespace MelodayMusic.Views;

public sealed partial class DownloadsPage : Page
{
    private readonly DownloadService _downloads = App.Services.GetRequiredService<DownloadService>();

    public DownloadsPage()
    {
        InitializeComponent();
        TrackListBehavior.SetRemoveCommand(List, new RelayCommand<object?>(item =>
        {
            if (TrackListBehavior.ExtractTrack(item) is { } track) _ = _downloads.RemoveAsync([track.RatingKey]);
        }));
    }

    protected override void OnNavigatedTo(NavigationEventArgs e)
    {
        base.OnNavigatedTo(e);
        _downloads.Changed += OnChanged;
        _downloads.PropertyChanged += OnProgress;
        Refresh();
    }

    protected override void OnNavigatedFrom(NavigationEventArgs e)
    {
        base.OnNavigatedFrom(e);
        _downloads.Changed -= OnChanged;
        _downloads.PropertyChanged -= OnProgress;
    }

    private void OnChanged(object? sender, EventArgs e) => Refresh();

    private void OnProgress(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(DownloadService.Remaining)) UpdateHeader();
    }

    private void Refresh()
    {
        var tracks = _downloads.Tracks;
        List.ItemsSource = tracks;
        State.IsEmpty = tracks.Count == 0 && _downloads.Remaining == 0;
        ShuffleButton.IsEnabled = tracks.Count > 0;
        UpdateHeader();
    }

    private void UpdateHeader()
    {
        var count = _downloads.Count;
        var text = count == 0 ? "Nothing downloaded" : $"{(count == 1 ? "1 song" : $"{count:N0} songs")} · {Format.Bytes(_downloads.TotalBytes)}";
        if (_downloads.Remaining > 0) text += $" · {_downloads.Remaining} downloading";
        Header.Subtitle = text;
    }

    private void OnShuffle(object sender, RoutedEventArgs e)
    {
        var tracks = _downloads.Tracks;
        if (tracks.Count > 0) _ = App.Services.GetRequiredService<PlayerService>().PlayTracksAsync(tracks, 0, shuffle: true);
    }
}
