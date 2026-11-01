using MelodayMusic.Controls;
using MelodayMusic.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;

namespace MelodayMusic.Views;

internal static class Dialogs
{
    public static XamlRoot? Root => App.MainWindow?.Content?.XamlRoot;

    public static ContentDialog Create(string title, XamlRoot? root = null) => Styled(new ContentDialog
    {
        XamlRoot = root ?? Root,
        Title = title,
        DefaultButton = ContentDialogButton.Primary,
    });

    public static ContentDialog Styled(ContentDialog dialog)
    {
        var resources = Application.Current.Resources;
        dialog.Style = (Style)resources["DefaultContentDialogStyle"];
        dialog.PrimaryButtonStyle = (Style)resources["MelodayPrimaryButtonStyle"];
        dialog.SecondaryButtonStyle = (Style)resources["MelodaySecondaryButtonStyle"];
        dialog.CloseButtonStyle = (Style)resources["MelodaySecondaryButtonStyle"];
        dialog.FontFamily = (FontFamily)resources["MelodayFont"];
        dialog.FontSize = UiScale.Text(15);
        dialog.CornerRadius = new CornerRadius(UiScale.BoardRadius);
        dialog.Resources["OverlayCornerRadius"] = dialog.CornerRadius;
        dialog.Resources["ContentDialogTitleFontSize"] = UiScale.Text(24);
        dialog.Resources["ContentDialogTitleFontWeight"] = Microsoft.UI.Text.FontWeights.Bold;
        if (dialog.DefaultButton == ContentDialogButton.Primary)
        {
            dialog.DefaultButton = ContentDialogButton.None;
            dialog.KeyDown += (_, e) =>
            {
                if (e.Key != Windows.System.VirtualKey.Enter || e.Handled) return;
                for (var node = Microsoft.UI.Xaml.Input.FocusManager.GetFocusedElement(dialog.XamlRoot) as DependencyObject;
                     node is not null && !ReferenceEquals(node, dialog); node = VisualTreeHelper.GetParent(node))
                {
                    if (node is TextBox { AcceptsReturn: true } or RichEditBox or ComboBox
                        or Microsoft.UI.Xaml.Controls.Primitives.ButtonBase
                        or AutoSuggestBox { IsSuggestionListOpen: true }) return;
                }
                if (CloseOnEnter.TryGetValue(dialog, out _))
                {
                    dialog.Hide();
                    e.Handled = true;
                    return;
                }
                if (!dialog.IsPrimaryButtonEnabled || string.IsNullOrEmpty(dialog.PrimaryButtonText)) return;
                if (Find(dialog, "PrimaryButton") is Button primary && primary.IsEnabled)
                {
                    new Microsoft.UI.Xaml.Automation.Peers.ButtonAutomationPeer(primary).Invoke();
                    e.Handled = true;
                }
            };
        }
        return dialog;
    }

    private static readonly System.Runtime.CompilerServices.ConditionalWeakTable<ContentDialog, object> CloseOnEnter = [];

    public static async Task<bool> SaveAsync(ContentDialogButtonClickEventArgs args, Func<Task<bool>> save)
    {
        var deferral = args.GetDeferral();
        try
        {
            var saved = await save();
            if (!saved) args.Cancel = true;
            return saved;
        }
        catch (Exception ex)
        {
            args.Cancel = true;
            Microsoft.Extensions.Logging.LoggerExtensions.LogWarning(
                App.Services.GetRequiredService<Microsoft.Extensions.Logging.ILogger<LibraryActions>>(), ex, "Dialog save failed");
            App.Services.GetRequiredService<NotificationService>().ShowError("Couldn't save changes", "Please try again. Your changes are still here.");
            return false;
        }
        finally { deferral.Complete(); }
    }

    public static void PreferClose(ContentDialog dialog) => CloseOnEnter.AddOrUpdate(dialog, true);

    private static DependencyObject? Find(DependencyObject parent, string name)
    {
        for (var i = 0; i < Microsoft.UI.Xaml.Media.VisualTreeHelper.GetChildrenCount(parent); i++)
        {
            var child = Microsoft.UI.Xaml.Media.VisualTreeHelper.GetChild(parent, i);
            if (child is FrameworkElement { Name: var n } && n == name) return child;
            if (Find(child, name) is { } found) return found;
        }
        return null;
    }

    public static TextBlock Text(string text) => new()
    {
        Text = text,
        TextWrapping = TextWrapping.WrapWholeWords,
        Style = (Style)Application.Current.Resources["MelodayBodyText"],
    };

    public static TextBlock Note(string text) => new()
    {
        Text = text,
        TextWrapping = TextWrapping.WrapWholeWords,
        Style = (Style)Application.Current.Resources["MelodaySecondaryText"],
    };

    public static Button SecondaryButton(string text) => new() { Content = text, Style = (Style)Application.Current.Resources["MelodaySecondaryButtonStyle"] };

    public static void Wide(ContentDialog dialog, double width) => dialog.Resources["ContentDialogMaxWidth"] = ContentWidth(width);

    public static double ContentWidth(double width) => Math.Min(UiScale.Size(width), Math.Max(280, (Root?.Size.Width ?? 1000) - UiScale.Size(96)));
    public static double ContentHeight(double height) => Math.Min(UiScale.Size(height), Math.Max(160, (Root?.Size.Height ?? 800) - UiScale.Size(220)));

    public static CornerRadius ArtistRadius => new(UiScale.Size(40) / 2);

    public static CornerRadius AlbumRadius => (CornerRadius)Application.Current.Resources["MelodayRadiusThumb"];

    public static Grid MediaRow(string? thumb, string title, string? detail, string icon, CornerRadius radius, double size, FrameworkElement? trailing = null)
    {
        var resources = Application.Current.Resources;
        var row = new Grid { ColumnSpacing = UiScale.Size(12), Padding = new Thickness(UiScale.Size(4), UiScale.Size(2), UiScale.Size(4), UiScale.Size(2)) };
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        row.Children.Add(new ArtworkImage
        {
            Width = UiScale.Size(size), Height = UiScale.Size(size), DecodeSize = (int)UiScale.Size(size) * 2, SourcePath = thumb, ArtworkCornerRadius = radius, PlaceholderIcon = icon,
        });
        var text = new StackPanel
        {
            Spacing = 2,
            VerticalAlignment = VerticalAlignment.Center,
            Children = { new TextBlock { Text = title, Style = (Style)resources["MelodayBodyStrongText"], TextTrimming = TextTrimming.CharacterEllipsis } },
        };
        if (!string.IsNullOrEmpty(detail))
            text.Children.Add(new TextBlock { Text = detail, Style = (Style)resources["MelodaySecondaryText"], TextTrimming = TextTrimming.CharacterEllipsis });
        Grid.SetColumn(text, 1);
        row.Children.Add(text);
        if (trailing is not null)
        {
            trailing.VerticalAlignment = VerticalAlignment.Center;
            Grid.SetColumn(trailing, 2);
            row.Children.Add(trailing);
        }
        return row;
    }

    public static async Task<byte[]?> PickImageAsync()
    {
        if (App.MainWindow is not { } window) return null;
        var picker = new Microsoft.Windows.Storage.Pickers.FileOpenPicker(window.AppWindow.Id)
        {
            SuggestedStartLocation = Microsoft.Windows.Storage.Pickers.PickerLocationId.PicturesLibrary,
            ViewMode = Microsoft.Windows.Storage.Pickers.PickerViewMode.Thumbnail,
        };
        foreach (var type in new[] { ".png", ".webp", ".jpg", ".jpeg" }) picker.FileTypeFilter.Add(type);
        var file = await picker.PickSingleFileAsync();
        return file is { Path: { Length: > 0 } path } ? await File.ReadAllBytesAsync(path) : null;
    }
}
