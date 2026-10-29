using CommunityToolkit.Mvvm.ComponentModel;
using Microsoft.UI.Dispatching;

namespace MelodayMusic.Services;

public sealed partial class SleepTimerService : ObservableObject
{
    public static readonly TimeSpan FadeLength = TimeSpan.FromSeconds(12);

    private readonly PlayerService _player;
    private readonly DispatcherQueueTimer _timer;

    public SleepTimerService(PlayerService player)
    {
        _player = player;
        _timer = DispatcherQueue.GetForCurrentThread().CreateTimer();
        _timer.Interval = TimeSpan.FromSeconds(0.5);
        _timer.Tick += (_, _) => OnTick();
        _player.StoppedAfterCurrent += (_, _) => Clear();
    }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsActive), nameof(Label))]
    public partial DateTime? EndsAt { get; private set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsActive), nameof(Label))]
    public partial bool AtEndOfSong { get; private set; }

    public bool IsActive => EndsAt is not null || AtEndOfSong;

    public string Label => AtEndOfSong ? "Stops at the end of this song"
        : EndsAt is { } end ? $"Stops in {Describe(end - DateTime.UtcNow)}"
        : "Sleep timer";

    public TimeSpan? Chosen { get; private set; }

    public void Start(TimeSpan length)
    {
        Clear();
        Chosen = length;
        EndsAt = DateTime.UtcNow + length;
        _timer.Start();
    }

    public void StartAtEndOfSong()
    {
        Clear();
        AtEndOfSong = true;
        _player.StopAfterCurrent = true;
    }

    public void Cancel() => Clear();

    private void Clear()
    {
        _timer.Stop();
        if (EndsAt is not null) _player.SetFadeLevel(1);
        if (AtEndOfSong) _player.StopAfterCurrent = false;
        Chosen = null;
        EndsAt = null;
        AtEndOfSong = false;
    }

    private void OnTick()
    {
        if (EndsAt is not { } end) return;
        var left = end - DateTime.UtcNow;
        if (left <= TimeSpan.Zero)
        {
            _player.Pause();
            Clear();
            return;
        }
        if (left < FadeLength) _player.SetFadeLevel(Math.Sin(left / FadeLength * Math.PI / 2));
        OnPropertyChanged(nameof(Label));
    }

    private static string Describe(TimeSpan left)
    {
        var minutes = (int)Math.Ceiling(left.TotalMinutes);
        if (minutes <= 1) return "a minute";
        if (minutes < 60) return $"{minutes} minutes";
        var hours = minutes / 60;
        var rest = minutes % 60;
        var h = hours == 1 ? "1 hour" : $"{hours} hours";
        return rest == 0 ? h : $"{h} {rest} min";
    }
}
