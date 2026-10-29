using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MelodayMusic.Core.Plex;
using MelodayMusic.Core.Services;
using Microsoft.Extensions.Logging;

namespace MelodayMusic.ViewModels;

public sealed partial class SignInViewModel : ObservableObject
{
    private readonly PlexSession _session;
    private readonly ILogger _log;
    private CancellationTokenSource? _cts;

    public SignInViewModel(PlexSession session, ILogger<SignInViewModel> log)
    {
        _session = session;
        _log = log;
        var dispatcher = Microsoft.UI.Dispatching.DispatcherQueue.GetForCurrentThread();
        _session.StateChanged += (_, _) => dispatcher.TryEnqueue(() => OnPropertyChanged(nameof(StatusMessage)));
    }

    [ObservableProperty] public partial bool IsWaiting { get; private set; }
    [ObservableProperty] public partial string? PinCode { get; private set; }
    [ObservableProperty] public partial string? ErrorMessage { get; private set; }
    [ObservableProperty] public partial Uri? AuthUri { get; private set; }

    public string? StatusMessage => _session.State == SessionState.SignedOut ? _session.StatusMessage : null;

    [RelayCommand]
    private async Task SignInAsync()
    {
        ErrorMessage = null;
        _cts?.Cancel();
        var cts = new CancellationTokenSource();
        _cts = cts;
        try
        {
            IsWaiting = true;
            var pin = await _session.CreatePinAsync(cts.Token);
            PinCode = pin.Code;
            AuthUri = _session.BuildAuthUri(pin);
            await Windows.System.Launcher.LaunchUriAsync(AuthUri);
            await Task.Run(() => _session.CompleteSignInAsync(pin, cts.Token));
        }
        catch (OperationCanceledException)
        {
        }
        catch (TimeoutException)
        {
            ErrorMessage = "The sign-in request expired. Please try again.";
        }
        catch (PlexUnreachableException)
        {
            ErrorMessage = "Plex.tv can't be reached. Check your internet connection and try again.";
        }
        catch (PlexException ex)
        {
            _log.LogWarning("Sign-in failed: {Message}", ex.Message);
            ErrorMessage = "Plex couldn't complete the sign-in. Please try again.";
        }
        finally
        {
            if (_cts == cts) IsWaiting = false;
        }
    }

    [RelayCommand]
    private async Task OpenBrowserAgainAsync()
    {
        if (AuthUri is not null) await Windows.System.Launcher.LaunchUriAsync(AuthUri);
    }

    [RelayCommand]
    private void Cancel()
    {
        _cts?.Cancel();
        IsWaiting = false;
        PinCode = null;
    }
}
