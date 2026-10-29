using CommunityToolkit.Mvvm.Input;

namespace MelodayMusic.ViewModels;

public sealed partial class PlayerViewModel
{
    [RelayCommand]
    private async Task OptimizeFlowAsync() => await _player.OptimizeUpNextAsync();
}
