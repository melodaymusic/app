using CommunityToolkit.Mvvm.Input;

namespace HumeMusic.ViewModels;

public sealed partial class PlayerViewModel
{
    [RelayCommand]
    private async Task OptimizeFlowAsync() => await _player.OptimizeUpNextAsync();
}
