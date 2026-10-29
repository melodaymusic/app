using MelodayMusic.Services;
using MelodayMusic.ViewModels;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;

namespace MelodayMusic.Views;

public sealed partial class TrendsPage : Page
{
    private const double SideBySide = 720;
    private static readonly TimeSpan RefreshAfter = TimeSpan.FromSeconds(2);
    private readonly ListeningService _listening = App.Services.GetRequiredService<ListeningService>();
    private bool _refreshQueued;

    public TrendsPage()
    {
        ViewModel = new TrendsViewModel(_listening.Log, App.Services.GetRequiredService<Core.Services.PlexSession>());
        InitializeComponent();
    }

    public TrendsViewModel ViewModel { get; }

    protected override void OnNavigatedTo(NavigationEventArgs e)
    {
        base.OnNavigatedTo(e);
        _listening.Log.Changed += OnChanged;
        _ = ViewModel.LoadAsync();
    }

    protected override void OnNavigatedFrom(NavigationEventArgs e)
    {
        base.OnNavigatedFrom(e);
        _listening.Log.Changed -= OnChanged;
    }

    private void OnChanged(object? sender, EventArgs e)
    {
        DispatcherQueue.TryEnqueue(async () =>
        {
            if (_refreshQueued) return;
            _refreshQueued = true;
            await Task.Delay(RefreshAfter);
            _refreshQueued = false;
            await ViewModel.LoadAsync();
        });
    }

    private void OnWhenSizeChanged(object sender, SizeChangedEventArgs e)
    {
        var wide = e.NewSize.Width >= SideBySide;
        Grid.SetColumn(DayPanel, wide ? 1 : 0);
        Grid.SetRow(DayPanel, wide ? 0 : 1);
        Grid.SetColumnSpan(HourPanel, wide ? 1 : 2);
        Grid.SetColumnSpan(DayPanel, wide ? 1 : 2);
    }
}
