using HumeMusic.Core.Logos;
using HumeMusic.Core.Models;
using HumeMusic.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;

namespace HumeMusic.Views;

/// <summary>
/// Choosing an artist's logo: any of fanart.tv's logos for the artist, an image of the listener's
/// own, or just the artist's name.
/// </summary>
public static class LogoPickerDialog
{
    public static async Task ShowAsync(Artist artist, XamlRoot root)
    {
        var logos = App.Services.GetRequiredService<ArtistLogoService>();
        var log = App.Services.GetRequiredService<ILoggerFactory>().CreateLogger("LogoPicker");
        var dialog = Dialogs.Styled(new ContentDialog
        {
            XamlRoot = root,
            Style = (Style)Application.Current.Resources["DefaultContentDialogStyle"],
            Title = "Artist Logo",
            PrimaryButtonText = "Use This Logo",
            CloseButtonText = "Cancel",
            DefaultButton = ContentDialogButton.Primary,
            IsPrimaryButtonEnabled = false,
        });

        var status = new TextBlock { Style = (Style)Application.Current.Resources["HumeSecondaryText"], TextWrapping = TextWrapping.WrapWholeWords };
        var busy = new ProgressRing { IsActive = true, Width = 24, Height = 24, HorizontalAlignment = HorizontalAlignment.Left };
        var grid = new GridView
        {
            SelectionMode = ListViewSelectionMode.Single,
            MaxHeight = 360,
            Visibility = Visibility.Collapsed,
        };
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(grid, "fanart.tv logos");

        var upload = new Button { Content = "Upload Image…", Style = (Style)Application.Current.Resources["HumeSecondaryButtonStyle"] };
        var useName = new Button { Content = "Show Name", Style = (Style)Application.Current.Resources["HumeSecondaryButtonStyle"] };
        var reset = new Button { Content = "Reset", Style = (Style)Application.Current.Resources["HumeSecondaryButtonStyle"] };
        var actions = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, Children = { upload, useName, reset } };

        dialog.Content = new StackPanel
        {
            Spacing = 16,
            MinWidth = 480,
            Children =
            {
                new TextBlock
                {
                    Text = $"Pick one of fanart.tv's logos for {artist.Title}, upload your own, or show the name. Reset goes back to fanart.tv's most popular logo.",
                    TextWrapping = TextWrapping.WrapWholeWords,
                    Style = (Style)Application.Current.Resources["HumeBodyText"],
                },
                busy,
                status,
                grid,
                actions,
            },
        };

        var options = new List<FanartLogo>();
        grid.SelectionChanged += (_, _) => dialog.IsPrimaryButtonEnabled = grid.SelectedIndex >= 0;

        upload.Click += async (_, _) =>
        {
            // The Windows App SDK's own picker, made for desktop apps (it needs only the window's id).
            var picker = new Microsoft.Windows.Storage.Pickers.FileOpenPicker(App.MainWindow!.AppWindow.Id)
            {
                SuggestedStartLocation = Microsoft.Windows.Storage.Pickers.PickerLocationId.PicturesLibrary,
                ViewMode = Microsoft.Windows.Storage.Pickers.PickerViewMode.Thumbnail,
            };
            foreach (var type in new[] { ".png", ".webp", ".jpg", ".jpeg" }) picker.FileTypeFilter.Add(type);
            Microsoft.Windows.Storage.Pickers.PickFileResult? file;
            try
            {
                file = await picker.PickSingleFileAsync();
            }
            catch (Exception ex)
            {
                log.LogWarning(ex, "The file picker couldn't be opened");
                status.Text = "The file picker couldn't be opened.";
                return;
            }
            if (file is not { Path: { Length: > 0 } }) return;
            try
            {
                var bytes = await File.ReadAllBytesAsync(file.Path);
                if (await logos.UseOwnAsync(artist, bytes)) dialog.Hide();
                else status.Text = "That image couldn't be used as a logo.";
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or System.Runtime.InteropServices.COMException)
            {
                status.Text = "That image couldn't be opened.";
            }
        };
        useName.Click += async (_, _) =>
        {
            await logos.UseNameAsync(artist);
            dialog.Hide();
        };
        reset.Click += async (_, _) =>
        {
            await logos.ResetAsync(artist);
            dialog.Hide();
        };
        dialog.PrimaryButtonClick += async (_, e) =>
        {
            if (grid.SelectedIndex < 0 || grid.SelectedIndex >= options.Count) return;
            var deferral = e.GetDeferral();
            status.Text = "Preparing the logo…";
            if (!await logos.ChooseAsync(artist, options[grid.SelectedIndex]))
            {
                status.Text = "That logo couldn't be downloaded. Try another.";
                e.Cancel = true;
            }
            deferral.Complete();
        };

        var show = dialog.ShowAsync();
        await LoadOptionsAsync();
        await show;

        async Task LoadOptionsAsync()
        {
            if (!logos.HasKey || artist.MusicBrainzId is null)
            {
                busy.IsActive = false;
                busy.Visibility = Visibility.Collapsed;
                status.Text = !logos.HasKey
                    ? "Add your fanart.tv key in Settings to choose from fanart.tv's logos."
                    : "Plex doesn't have a MusicBrainz id for this artist, so fanart.tv can't be searched.";
                return;
            }
            var (result, found, current) = await logos.GetOptionsAsync(artist);
            busy.IsActive = false;
            busy.Visibility = Visibility.Collapsed;
            if (result == FanartResult.InvalidKey) status.Text = "fanart.tv didn't accept your key. Check it in Settings.";
            else if (result == FanartResult.Unavailable) status.Text = "fanart.tv couldn't be reached. Try again in a moment.";
            else if (found.Count == 0) status.Text = "fanart.tv has no logos for this artist yet.";
            if (found.Count == 0) return;

            options.AddRange(found);
            foreach (var logo in found)
            {
                var tile = new Border
                {
                    Width = 200,
                    Height = 96,
                    Padding = new Thickness(12),
                    CornerRadius = new CornerRadius(8),
                    // Logos are mostly light: a dark card shows every one of them.
                    Background = new SolidColorBrush(Windows.UI.Color.FromArgb(255, 0x1C, 0x1B, 0x20)),
                    Child = new Image
                    {
                        Source = new BitmapImage(new Uri(logo.Url)) { DecodePixelWidth = 400 },
                        Stretch = Stretch.Uniform,
                    },
                };
                ToolTipService.SetToolTip(tile, $"{(logo.IsHd ? "HD" : "Standard")} · {logo.Likes} likes");
                Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(tile, $"Logo {options.IndexOf(logo) + 1}, {(logo.IsHd ? "HD" : "standard")}, {logo.Likes} likes");
                grid.Items.Add(tile);
            }
            grid.Visibility = Visibility.Visible;
            grid.SelectedIndex = current is null ? -1 : options.FindIndex(o => o.Url == current);
            dialog.IsPrimaryButtonEnabled = false;
        }
    }
}
