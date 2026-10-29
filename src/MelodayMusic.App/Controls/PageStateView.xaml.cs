using System.Windows.Input;
using MelodayMusic.Core.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace MelodayMusic.Controls;

public sealed partial class PageStateView : UserControl
{
    public PageStateView()
    {
        InitializeComponent();
        Helpers.Anim.SetAppear(this, true);
        Update();
    }

    public static readonly DependencyProperty IsLoadingProperty = Register(nameof(IsLoading), typeof(bool), false);
    public static readonly DependencyProperty ErrorMessageProperty = Register(nameof(ErrorMessage), typeof(string), null);
    public static readonly DependencyProperty IsEmptyProperty = Register(nameof(IsEmpty), typeof(bool), false);
    public static readonly DependencyProperty EmptyTextProperty = Register(nameof(EmptyText), typeof(string), "Nothing here yet.");
    public static readonly DependencyProperty EmptyTitleProperty = Register(nameof(EmptyTitle), typeof(string), null);
    public static readonly DependencyProperty EmptyIconProperty = Register(nameof(EmptyIcon), typeof(string), "Music");
    public static readonly DependencyProperty RetryCommandProperty = Register(nameof(RetryCommand), typeof(ICommand), null);
    public static readonly DependencyProperty SkeletonKindProperty = Register(nameof(SkeletonKind), typeof(SkeletonKind), SkeletonKind.Tiles);
    public static readonly DependencyProperty SkeletonRoundProperty = Register(nameof(SkeletonRound), typeof(bool), false);
    public static readonly DependencyProperty SkeletonPaddingProperty = Register(nameof(SkeletonPadding), typeof(Thickness), new Thickness(0));

    private static DependencyProperty Register(string name, Type type, object? def) =>
        DependencyProperty.Register(name, type, typeof(PageStateView), new PropertyMetadata(def, (d, _) => ((PageStateView)d).Update()));

    public bool IsLoading { get => (bool)GetValue(IsLoadingProperty); set => SetValue(IsLoadingProperty, value); }
    public string? ErrorMessage { get => (string?)GetValue(ErrorMessageProperty); set => SetValue(ErrorMessageProperty, value); }
    public bool IsEmpty { get => (bool)GetValue(IsEmptyProperty); set => SetValue(IsEmptyProperty, value); }
    public string EmptyText { get => (string)GetValue(EmptyTextProperty); set => SetValue(EmptyTextProperty, value); }
    public string? EmptyTitle { get => (string?)GetValue(EmptyTitleProperty); set => SetValue(EmptyTitleProperty, value); }
    public string EmptyIcon { get => (string)GetValue(EmptyIconProperty); set => SetValue(EmptyIconProperty, value); }
    public ICommand? RetryCommand { get => (ICommand?)GetValue(RetryCommandProperty); set => SetValue(RetryCommandProperty, value); }

    public SkeletonKind SkeletonKind { get => (SkeletonKind)GetValue(SkeletonKindProperty); set => SetValue(SkeletonKindProperty, value); }
    public bool SkeletonRound { get => (bool)GetValue(SkeletonRoundProperty); set => SetValue(SkeletonRoundProperty, value); }
    public Thickness SkeletonPadding { get => (Thickness)GetValue(SkeletonPaddingProperty); set => SetValue(SkeletonPaddingProperty, value); }

    public event EventHandler? RetryRequested;

    private void Update()
    {
        var hasError = !string.IsNullOrEmpty(ErrorMessage);
        var loading = IsLoading && !hasError;
        var skeleton = loading && SkeletonKind != SkeletonKind.None;
        Skeleton.Kind = SkeletonKind;
        Skeleton.Round = SkeletonRound;
        Skeleton.Margin = SkeletonPadding;
        Skeleton.Visibility = skeleton ? Visibility.Visible : Visibility.Collapsed;
        Skeleton.SetActive(skeleton);
        Spinner.IsActive = loading && !skeleton;
        Spinner.Visibility = Spinner.IsActive ? Visibility.Visible : Visibility.Collapsed;
        ErrorPanel.Visibility = hasError && !IsLoading ? Visibility.Visible : Visibility.Collapsed;
        ErrorText.Text = ErrorMessage ?? string.Empty;
        EmptyPanel.Visibility = IsEmpty && !IsLoading && !hasError ? Visibility.Visible : Visibility.Collapsed;
        EmptyTextBlock.Text = EmptyText ?? string.Empty;
        EmptyTitleBlock.Text = EmptyTitle ?? string.Empty;
        EmptyTitleBlock.Visibility = string.IsNullOrEmpty(EmptyTitle) ? Visibility.Collapsed : Visibility.Visible;
        EmptyIconGlyph.Icon = EmptyIcon;
        Visibility = skeleton || Spinner.IsActive || ErrorPanel.Visibility == Visibility.Visible || EmptyPanel.Visibility == Visibility.Visible
            ? Visibility.Visible
            : Visibility.Collapsed;
    }

    private async void OnRetryClick(object sender, RoutedEventArgs e)
    {
        var session = App.Services.GetRequiredService<PlexSession>();
        RetryButton.IsEnabled = false;
        try
        {
            if (session.Client is null || ErrorMessage?.Contains("reached", StringComparison.OrdinalIgnoreCase) == true)
                await Task.Run(() => session.ReconnectAsync());
            if (RetryCommand is CommunityToolkit.Mvvm.Input.IAsyncRelayCommand asyncCommand && asyncCommand.CanExecute(null))
                await asyncCommand.ExecuteAsync(null);
            else if (RetryCommand?.CanExecute(null) == true) RetryCommand.Execute(null);
            else RetryRequested?.Invoke(this, EventArgs.Empty);
        }
        catch (Exception ex)
        {
            Microsoft.Extensions.Logging.LoggerExtensions.LogWarning(
                App.Services.GetRequiredService<Microsoft.Extensions.Logging.ILogger<PageStateView>>(), ex, "Page retry failed");
            ErrorMessage = "Couldn't reconnect or reload this page. Check your connection and try again.";
        }
        finally
        {
            RetryButton.IsEnabled = true;
        }
    }
}
