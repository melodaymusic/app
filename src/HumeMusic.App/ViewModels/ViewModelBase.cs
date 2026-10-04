using CommunityToolkit.Mvvm.ComponentModel;
using HumeMusic.Core.Plex;
using HumeMusic.Core.Services;
using Microsoft.Extensions.Logging;

namespace HumeMusic.ViewModels;

/// <summary>Loading and error state shared by page view models.</summary>
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

    /// <summary>Runs a load operation, translating failures into a friendly message for the page.</summary>
    protected async Task RunAsync(Func<Task> work, bool showSpinner = true)
    {
        if (showSpinner) IsLoading = true;
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
            ErrorMessage = Describe(ex);
            Log.LogWarning("Load failed: {Message}", ex.Message);
            if (ex is PlexUnauthorizedException) _ = Session.HandleUnauthorizedAsync();
        }
        catch (Exception ex)
        {
            ErrorMessage = "Something went wrong while loading this page.";
            Log.LogError(ex, "Unexpected error while loading");
        }
        finally
        {
            if (showSpinner) IsLoading = false;
        }
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
