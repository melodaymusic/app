using MelodayMusic.Core.Plex;
using MelodayMusic.Core.Services;
using MelodayMusic.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media.Imaging;

namespace MelodayMusic.Views;

/// <summary>
/// Choosing who is listening from the Plex Home: each member with their picture, a PIN box for members who have one.
/// Switching saves everything, then the app starts again with that member's own settings, history and taste.
/// </summary>
public static class ProfileDialog
{
    public static async Task ShowAsync()
    {
        var session = App.Services.GetRequiredService<PlexSession>();
        var notifications = App.Services.GetRequiredService<NotificationService>();
        IReadOnlyList<PlexHomeUser> users;
        try
        {
            users = await session.GetHomeUsersAsync();
        }
        catch (PlexException ex)
        {
            App.Services.GetRequiredService<ILogger<PlexSession>>().LogWarning(ex, "Couldn't read the Plex Home");
            notifications.ShowError("Couldn't load your Plex Home", "Check your connection and try again.");
            return;
        }

        var dialog = Dialogs.Create("Switch Profile");
        dialog.CloseButtonText = users.Count > 1 ? "Cancel" : "Close";
        if (users.Count <= 1)
        {
            dialog.Content = new StackPanel
            {
                Spacing = 8,
                Width = Dialogs.ContentWidth(380),
                Children =
                {
                    Dialogs.Text("This Plex account isn't sharing a Plex Home with anyone yet."),
                    Dialogs.Note("Add people to your Plex Home at plex.tv, under Plex Home, and they'll appear here to switch to, each with their own history, ratings and Meloday."),
                },
            };
            await dialog.ShowAsync();
            return;
        }

        var current = session.ListenerUuid;
        var list = new ListView { SelectionMode = ListViewSelectionMode.Single, MaxHeight = Dialogs.ContentHeight(380) };
        foreach (var user in users) list.Items.Add(Row(user, user.Uuid == current));

        var pin = new PasswordBox
        {
            Header = "PIN",
            PlaceholderText = "4 digits",
            MaxLength = 4,
            InputScope = new Microsoft.UI.Xaml.Input.InputScope { Names = { new Microsoft.UI.Xaml.Input.InputScopeName(Microsoft.UI.Xaml.Input.InputScopeNameValue.NumericPin) } },
            Visibility = Visibility.Collapsed,
        };
        var note = Dialogs.Note("Your queue and place are saved, then Meloday Music opens again as them.");
        var error = Dialogs.Note(string.Empty);
        error.Foreground = (Microsoft.UI.Xaml.Media.Brush)Application.Current.Resources["SystemFillColorCriticalBrush"];
        error.Visibility = Visibility.Collapsed;

        PlexHomeUser? Chosen() => list.SelectedIndex >= 0 ? users[list.SelectedIndex] : null;
        void Refresh()
        {
            var chosen = Chosen();
            pin.Visibility = chosen?.HasPin == true ? Visibility.Visible : Visibility.Collapsed;
            dialog.IsPrimaryButtonEnabled = chosen is not null && chosen.Uuid != current && (!chosen.HasPin || pin.Password.Length == 4);
            dialog.PrimaryButtonText = chosen is null || chosen.Uuid == current ? "Switch" : $"Switch to {chosen.Name}";
        }
        list.SelectionChanged += (_, _) =>
        {
            pin.Password = string.Empty;
            error.Visibility = Visibility.Collapsed;
            Refresh();
            if (pin.Visibility == Visibility.Visible) pin.Focus(FocusState.Programmatic);
        };
        pin.PasswordChanged += (_, _) =>
        {
            error.Visibility = Visibility.Collapsed;
            Refresh();
        };

        dialog.Content = new StackPanel { Spacing = 12, Width = Dialogs.ContentWidth(400), Children = { list, pin, error, note } };
        dialog.PrimaryButtonText = "Switch";
        Refresh();
        dialog.PrimaryButtonClick += async (_, args) =>
        {
            if (Chosen() is not { } chosen) return;
            var deferral = args.GetDeferral();
            dialog.IsPrimaryButtonEnabled = false;
            list.IsEnabled = false;
            note.Text = $"Switching to {chosen.Name}…";
            try
            {
                await session.SwitchProfileAsync(chosen, chosen.HasPin ? pin.Password : null);
            }
            catch (PlexHomePinException)
            {
                args.Cancel = true;
                Show("That PIN isn't right. Try again.");
                pin.Password = string.Empty;
                pin.Focus(FocusState.Programmatic);
            }
            catch (PlexException ex)
            {
                args.Cancel = true;
                App.Services.GetRequiredService<ILogger<PlexSession>>().LogWarning(ex, "Couldn't switch Plex Home profile");
                Show($"Couldn't switch to {chosen.Name}. Check your connection and try again.");
            }
            finally
            {
                if (args.Cancel)
                {
                    list.IsEnabled = true;
                    note.Text = "Your queue and place are saved, then Meloday Music opens again as them.";
                    Refresh();
                }
                deferral.Complete();
            }

            void Show(string message)
            {
                error.Text = message;
                error.Visibility = Visibility.Visible;
            }
        };
        await dialog.ShowAsync();
    }

    private static Grid Row(PlexHomeUser user, bool current)
    {
        var row = new Grid { ColumnSpacing = UiScale.Size(12), Padding = new Thickness(UiScale.Size(4)) };
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        var picture = new PersonPicture { Width = UiScale.Size(40), Height = UiScale.Size(40), DisplayName = user.Name };
        if (user.Thumb is { } thumb && Uri.TryCreate(thumb, UriKind.Absolute, out var uri))
            picture.ProfilePicture = new BitmapImage(uri) { DecodePixelWidth = 80, DecodePixelType = DecodePixelType.Logical };
        row.Children.Add(picture);

        var detail = new List<string>();
        if (current) detail.Add("Listening now");
        if (user.IsAdmin) detail.Add("Admin");
        else if (user.IsManaged) detail.Add("Managed");
        else if (user.IsGuest) detail.Add("Guest");
        if (user.HasPin) detail.Add("PIN");
        var text = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        text.Children.Add(new TextBlock { Text = user.Name, Style = (Style)Application.Current.Resources["MelodayBodyStrongText"], TextTrimming = TextTrimming.CharacterEllipsis });
        if (detail.Count > 0) text.Children.Add(new TextBlock { Text = string.Join(" · ", detail), Style = (Style)Application.Current.Resources["MelodaySecondaryText"] });
        Grid.SetColumn(text, 1);
        row.Children.Add(text);
        return row;
    }
}
