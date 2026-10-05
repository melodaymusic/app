# Meloday Music

A Windows music player for Plex, built around a native desktop listening experience.

## Personal listening

Meloday Music includes several different personalised listening systems rather than treating every recommendation as radio:

- **Mixes for You** uses recent listening to build artist, genre and mood mixes.
- **Niche Mixes** are finite, tightly focused mixes built from combinations that actually appear in the listener's taste. They can use genre, mood, decade, recent listening, rediscovery, deep cuts, listening context, cached tempo analysis, artist-session patterns and Plex sonic analysis where available.
- **Made for You** groups those personalised mixes by why Meloday made them, including current taste, time-of-day patterns, rediscovery, energy, artist circles, sonic neighbourhoods and moods.
- **Radio** starts from a seed and keeps going, allowing the station to move outward through related music.
- **Meloday** adapts to what the listener tends to play at the current part of the day.

Niche mixes are deliberately finite and stay inside their recipe. Recommendation generation only reads audio analysis already cached by AutoMix; it does not trigger bulk audio analysis by itself.

## Development

The solution targets .NET 9 and the Windows App SDK. CI runs on Windows, restores and builds the x64 Release solution, then runs the core test suite.
