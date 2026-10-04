using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using HumeMusic.Core.Models;
using HumeMusic.Core.Plex;
using HumeMusic.Core.Services;
using Microsoft.Extensions.Logging;
using Microsoft.UI.Dispatching;

namespace HumeMusic.ViewModels;

/// <summary>Server and music library selection, plus the "server unavailable" / "no library" states.</summary>
public sealed partial class SetupViewModel : ObservableObject
{
    private readonly PlexSession _session;
    private readonly ILogger _log;
    private readonly DispatcherQueue _dispatcher;

    public SetupViewModel(PlexSession session, ILogger<SetupViewModel> log)
    {
        _session = session;
        _log = log;
        _dispatcher = DispatcherQueue.GetForCurrentThread();
        _session.StateChanged += (_, _) => _dispatcher.TryEnqueue(Refresh);
    }

    public ObservableCollection<PlexServer> Servers { get; } = [];
    public ObservableCollection<MusicSection> Sections { get; } = [];

    [ObservableProperty] public partial string Heading { get; private set; } = string.Empty;
    [ObservableProperty] public partial string Message { get; private set; } = string.Empty;
    [ObservableProperty] public partial bool ShowServers { get; private set; }
    [ObservableProperty] public partial bool ShowSections { get; private set; }
    [ObservableProperty] public partial bool ShowRetry { get; private set; }
    [ObservableProperty] public partial bool IsBusy { get; private set; }

    public string AccountName => _session.Account?.Name ?? string.Empty;

    private void Refresh()
    {
        OnPropertyChanged(nameof(AccountName));
        switch (_session.State)
        {
            case SessionState.ChooseServer:
                Heading = "Choose a Plex server";
                Message = "Your Plex account has access to more than one server. Pick the one with your music.";
                Fill(Servers, _session.Servers);
                ShowServers = true;
                ShowSections = false;
                ShowRetry = false;
                break;
            case SessionState.ChooseLibrary:
                Heading = "Choose a music library";
                Message = $"{_session.Server?.Name} has several music libraries. You can change this later in Settings.";
                Fill(Sections, _session.Sections);
                ShowServers = false;
                ShowSections = true;
                ShowRetry = false;
                break;
            case SessionState.NoMusicLibrary:
                Heading = "No music library found";
                Message = _session.StatusMessage ?? "This server doesn't have a music library.";
                Fill(Servers, _session.Servers);
                ShowServers = _session.Servers.Count > 1;
                ShowSections = false;
                ShowRetry = true;
                break;
            case SessionState.ServerUnavailable:
                Heading = "Can't reach your Plex server";
                Message = _session.StatusMessage ?? "The server is offline or unreachable.";
                Fill(Servers, _session.Servers);
                ShowServers = _session.Servers.Count > 1;
                ShowSections = false;
                ShowRetry = true;
                break;
        }
        IsBusy = false;
    }

    private static void Fill<T>(ObservableCollection<T> target, IEnumerable<T> source)
    {
        target.Clear();
        foreach (var item in source) target.Add(item);
    }

    [RelayCommand]
    private async Task SelectServerAsync(PlexServer? server)
    {
        if (server is null) return;
        IsBusy = true;
        await Task.Run(() => _session.SelectServerAsync(server));
        IsBusy = false;
    }

    [RelayCommand]
    private void SelectSection(MusicSection? section)
    {
        if (section is null) return;
        _session.SelectSection(section);
    }

    [RelayCommand]
    private async Task RetryAsync()
    {
        IsBusy = true;
        try
        {
            await Task.Run(async () =>
            {
                try { await _session.RefreshServersAsync(); }
                catch (PlexException ex) { _log.LogWarning("Refreshing servers failed: {Message}", ex.Message); }
                await _session.ReconnectAsync();
            });
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    private async Task SignOutAsync() => await _session.SignOutAsync();
}
