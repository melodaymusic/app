using MelodayMusic.Core.Models;

namespace MelodayMusic.Services;

// What Now Playing shows of a mix: the song coming in, the move it comes in on, and how far along the mix is.
public sealed record MixCue(Track Next, string Move, string Status, double Progress, bool Started);
