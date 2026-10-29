using MelodayMusic.Core.Models;
using MelodayMusic.ViewModels;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml.Controls;

namespace MelodayMusic.Views;

public sealed partial class SetupView : UserControl
{
    public SetupView()
    {
        ViewModel = App.Services.GetRequiredService<SetupViewModel>();
        InitializeComponent();
    }

    public SetupViewModel ViewModel { get; }

    private void OnServerClick(object sender, ItemClickEventArgs e) => ViewModel.SelectServerCommand.Execute(e.ClickedItem as PlexServer);

    private void OnSectionClick(object sender, ItemClickEventArgs e) => ViewModel.SelectSectionCommand.Execute(e.ClickedItem as MusicSection);
}
