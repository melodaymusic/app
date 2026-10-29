using MelodayMusic.ViewModels;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml.Controls;

namespace MelodayMusic.Views;

public sealed partial class LoadingView : UserControl
{
    public LoadingView()
    {
        ViewModel = App.Services.GetRequiredService<ShellViewModel>();
        InitializeComponent();
    }

    public ShellViewModel ViewModel { get; }
}
