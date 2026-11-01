using System.Text.Json;
using MelodayMusic.Core;
using MelodayMusic.Core.LastFm;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml.Controls;
using Microsoft.Web.WebView2.Core;

namespace MelodayMusic.Services;

public static class LiveChartReader
{
    private static Task<LiveChart>? _load;
    private static DateTimeOffset _at;
    private static readonly object Gate = new();
    public static Task<LiveChart> GetAsync()
    {
        if (!App.Services.GetRequiredService<LastFmService>().HasApiKey)
            throw new LastFmException("Add your own Last.fm API key in Settings to use charts.");
        lock (Gate)
        {
            if (_load is { IsFaulted: false, IsCanceled: false } && DateTimeOffset.UtcNow - _at < TimeSpan.FromHours(6)) return _load;
            var source = new TaskCompletionSource<LiveChart>(TaskCreationOptions.RunContinuationsAsynchronously);
            _at = DateTimeOffset.UtcNow;
            _load = source.Task;
            if (App.MainWindow?.DispatcherQueue.TryEnqueue(async () =>
            {
                WebView2? browser = null;
                Panel? host = null;
                try
                {
                    var environment = await CoreWebView2Environment.CreateWithOptionsAsync(null, Path.Combine(AppPaths.Shared, "lastfm-browser"), null);
                    host = App.MainWindow.Content as Panel ?? throw new LastFmException("The chart browser is not ready.");
                    browser = new WebView2 { Width = 1, Height = 1, Opacity = 0, IsHitTestVisible = false, IsTabStop = false };
                    host.Children.Insert(0, browser);
                    await browser.EnsureCoreWebView2Async(environment);
                    var core = browser.CoreWebView2;
                    core.NewWindowRequested += (_, e) => e.Handled = true;
                    // The chart tables are in the page's own HTML, so pictures, fonts and media only slow the read down.
                    core.AddWebResourceRequestedFilter("*", CoreWebView2WebResourceContext.Image);
                    core.AddWebResourceRequestedFilter("*", CoreWebView2WebResourceContext.Media);
                    core.AddWebResourceRequestedFilter("*", CoreWebView2WebResourceContext.Font);
                    core.WebResourceRequested += (_, e) => e.Response = core.Environment.CreateWebResourceResponse(null, 204, "No Content", "");
                    var finished = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
                    async Task TryReadAsync()
                    {
                        try
                        {
                            var html = JsonSerializer.Deserialize<string>(await core.ExecuteScriptAsync("document.documentElement.outerHTML")) ?? "";
                            if (source.TrySetResult(LiveChart.Parse(html))) finished.TrySetResult(true);
                        }
                        catch (Exception) { }
                    }
                    // Read as soon as the document is parsed rather than waiting for every ad and tracker to finish.
                    core.DOMContentLoaded += async (_, _) => await TryReadAsync();
                    core.NavigationCompleted += async (_, e) =>
                    {
                        if (e.IsSuccess) await TryReadAsync();
                        else finished.TrySetResult(false);
                    };
                    core.Navigate("https://www.last.fm/charts");
                    try { await finished.Task.WaitAsync(TimeSpan.FromSeconds(30)); }
                    catch (TimeoutException) { }
                    if (!source.Task.IsCompleted) await TryReadAsync();
                    if (!source.Task.IsCompleted) throw new LastFmException("Last.fm's live chart couldn't load.");
                }
                catch (Exception ex) { source.TrySetException(new LastFmException("Last.fm's live chart is unavailable: " + ex.Message)); }
                finally { if (browser is not null) { browser.Close(); host?.Children.Remove(browser); } }
            }) != true) source.TrySetException(new LastFmException("The chart browser is not ready."));
            return _load;
        }
    }
}
