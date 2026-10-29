using MelodayMusic.ViewModels;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml.Controls;

namespace MelodayMusic.Views;

public sealed partial class SignInView : UserControl
{
    public SignInView()
    {
        ViewModel = App.Services.GetRequiredService<SignInViewModel>();
        InitializeComponent();
    }

    public SignInViewModel ViewModel { get; }
}
