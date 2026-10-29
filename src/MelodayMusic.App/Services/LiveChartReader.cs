using System.Text.Json;
using MelodayMusic.Core;
using MelodayMusic.Core.LastFm;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Web.WebView2.Core;
using Microsoft.UI.Xaml.Controls;

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
                    browser.CoreWebView2.NewWindowRequested += (_, e) => e.Handled = true;
                    var loaded = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
                    browser.CoreWebView2.NavigationCompleted += (_, e) => loaded.TrySetResult(e.IsSuccess);
                    browser.CoreWebView2.Navigate("https://www.last.fm/charts");
                    if (!await loaded.Task.WaitAsync(TimeSpan.FromSeconds(25))) throw new LastFmException("Last.fm's live chart couldn't load.");
                    var html = JsonSerializer.Deserialize<string>(await browser.CoreWebView2.ExecuteScriptAsync("document.documentElement.outerHTML")) ?? "";
                    source.TrySetResult(LiveChart.Parse(html));
                }
                catch (Exception ex) { source.TrySetException(new LastFmException("Last.fm's live chart is unavailable: " + ex.Message)); }
                finally { if (browser is not null) { browser.Close(); host?.Children.Remove(browser); } }
            }) != true) source.TrySetException(new LastFmException("The chart browser is not ready."));
            return _load;
        }
    }
}
