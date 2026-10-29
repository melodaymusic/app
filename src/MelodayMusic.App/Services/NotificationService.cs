using CommunityToolkit.Mvvm.ComponentModel;
using Microsoft.UI.Xaml.Controls;

namespace MelodayMusic.Services;

public sealed partial class NotificationService : ObservableObject
{
    private CancellationTokenSource? _autoHide;

    [ObservableProperty] public partial bool IsOpen { get; set; }
    [ObservableProperty] public partial string Title { get; set; } = string.Empty;
    [ObservableProperty] public partial string Message { get; set; } = string.Empty;
    [ObservableProperty] public partial InfoBarSeverity Severity { get; set; } = InfoBarSeverity.Informational;

    public void ShowError(string title, string message, bool autoHide = false) => Show(title, message, InfoBarSeverity.Error, autoHide);
    public void ShowWarning(string title, string message, bool autoHide = true) => Show(title, message, InfoBarSeverity.Warning, autoHide);
    public void ShowInfo(string title, string message) => Show(title, message, InfoBarSeverity.Informational, true);
    public void ShowSuccess(string title, string message) => Show(title, message, InfoBarSeverity.Success, true);

    public void Show(string title, string message, InfoBarSeverity severity, bool autoHide)
    {
        _autoHide?.Cancel();
        Title = title;
        Message = message;
        Severity = severity;
        IsOpen = true;
        if (autoHide)
        {
            var cts = new CancellationTokenSource();
            _autoHide = cts;
            _ = HideLaterAsync(cts.Token);
        }
    }

    public void Dismiss()
    {
        _autoHide?.Cancel();
        IsOpen = false;
    }

    private async Task HideLaterAsync(CancellationToken ct)
    {
        try
        {
            await Task.Delay(TimeSpan.FromSeconds(6), ct);
            IsOpen = false;
        }
        catch (OperationCanceledException) { }
    }
}
