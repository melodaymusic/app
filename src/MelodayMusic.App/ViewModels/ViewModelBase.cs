using CommunityToolkit.Mvvm.ComponentModel;
using MelodayMusic.Core.Plex;
using MelodayMusic.Core.Services;
using Microsoft.Extensions.Logging;

namespace MelodayMusic.ViewModels;

public abstract partial class ViewModelBase : ObservableObject
{
    protected ViewModelBase(PlexSession session, ILogger log)
    {
        Session = session;
        Log = log;
    }

    protected PlexSession Session { get; }
    protected ILogger Log { get; }

    [ObservableProperty] public partial bool IsLoading { get; set; }
    [ObservableProperty] public partial string? ErrorMessage { get; set; }
    [ObservableProperty] public partial bool IsEmpty { get; set; }

    public bool HasError => !string.IsNullOrEmpty(ErrorMessage);

    partial void OnErrorMessageChanged(string? value) => OnPropertyChanged(nameof(HasError));

    protected PlexServerClient Client => Session.Client ?? throw new PlexUnreachableException("Not connected to a Plex server.");
    protected string SectionKey => Session.Section?.Key ?? throw new PlexApiException("No music library selected.");
    private int _loadVersion;
    private int _spinnerLoads;

    protected async Task RunAsync(Func<Task> work, bool showSpinner = true, Func<bool>? isCurrent = null)
    {
        var version = ++_loadVersion;
        using var spinnerDelay = new CancellationTokenSource();
        if (showSpinner)
        {
            ++_spinnerLoads;
            _ = RevealSpinnerAsync(spinnerDelay.Token);
        }
        ErrorMessage = null;
        try
        {
            await work();
        }
        catch (OperationCanceledException)
        {
        }
        catch (PlexException ex)
        {
            if (version == _loadVersion && (isCurrent?.Invoke() ?? true)) ErrorMessage = Describe(ex);
            Log.LogWarning("Load failed: {Message}", ex.Message);
            if (ex is PlexUnauthorizedException) _ = Session.HandleUnauthorizedAsync();
        }
        catch (Exception ex)
        {
            if (version == _loadVersion && (isCurrent?.Invoke() ?? true)) ErrorMessage = "Something went wrong while loading this page.";
            Log.LogError(ex, "Unexpected error while loading");
        }
        finally
        {
            spinnerDelay.Cancel();
            if (showSpinner && --_spinnerLoads == 0) IsLoading = false;
        }
    }

    private async Task RevealSpinnerAsync(CancellationToken ct)
    {
        try
        {
            await Task.Delay(150, ct);
            if (!ct.IsCancellationRequested && _spinnerLoads > 0) IsLoading = true;
        }
        catch (OperationCanceledException) { }
    }

    public static string Describe(Exception ex) => ex switch
    {
        PlexUnreachableException => "Your Plex server can't be reached. Check that it's running and that you're online.",
        PlexUnauthorizedException => "Your Plex authorization has expired.",
        PlexApiException { StatusCode: System.Net.HttpStatusCode.NotFound } => "This item no longer exists on your Plex server.",
        PlexException p => p.Message,
        _ => "Something went wrong.",
    };
}
