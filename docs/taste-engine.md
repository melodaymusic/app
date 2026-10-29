# Taste Engine

Implemented October 14, 2026 (0.77.0); sound taste added in 0.79.0. One view of the listener's taste, used by everything
Meloday Music makes for them. Code: `src/MelodayMusic.Core/Taste` (engine, profile, memory, sound) and
`src/MelodayMusic.App/Services/TasteService.cs`.

## Sources

Anything Plex already records is read from Plex and never stored a second time. Meloday only keeps what Plex can't know.

| Signal | Source | Used for |
| --- | --- | --- |
| Rating (stars) | Plex, per track | Liking; one star or less is a strong "no" |
| Play count, last played | Plex, per track | Liking (log scale), freshness, novelty |
| Lifetime skip count | Plex, per track | A background "no" that never fades |
| Listening history (each play, timestamped) | Plex `/status/sessions/history/all`, last 400 days | How much each song, album and artist has been played lately; when in the day each artist and genre is played |
| Genres, styles, moods, year | Plex, per album (looked up for the most played albums) | Taste for a sound and an era; stands in for a song's own tags, which Plex rarely has |
| Early skip (first 30 s, before halfway) | Meloday, `taste/memory.json` | A recent "no" that fades (half after 30 days); the artist gets half |
| Played through | Meloday | Completion: how much of a song is actually heard |
| Chosen on purpose | Meloday | Picked from a list (not shuffled, not from a card), Play Next / Add to Queue (five songs or fewer), Previous to go back, restarting after 10 s |
| Sound fingerprint | Meloday Engine (ME), one 512-number CLAP vector per analysed song | How much an unplayed song sounds like the music the listener plays and finishes (see Sound taste) |

Plex's history only says a song played, not whether it was heard through. That is where Meloday's own signals add the most.
The memory replaces the earlier radio skip memory (`radio/skips.json` is read once on first run).

## What it answers

- `Like(track)`: about −1.5 (avoid) to 1.5 (a favourite). Combines rating, plays, Plex skips, history (song, album, artist),
  tags (genres and moods, era), recent skips here, songs played through or chosen here, and 0.3 × `SoundFit`.
- `SoundFit(track)`: 0 to 1. How much the song sounds like the music the listener loves, from ME's fingerprints. 0 for songs
  without a fingerprint, or when ME isn't the chosen engine.
- `Fit(track, time)`: 0 to 1. How much more the artist or genre is played around this hour than music in general (three or
  more plays needed), or how much more the song sounds like this part of the day's listening than the listener's taste in
  general (× 0.6), whichever is most.
- `Novelty(track)`: 1 when never played, falling with plays and recent plays.
- `Score(track, time)`: Like + Fit + Adventure × Novelty − Freshness. A general "should Meloday play this now".
- `Why(track)`: the strongest reasons in a sentence each (shown in Song Info).

**Adventure** (Settings > Meloday > Taste Engine, Familiar to Adventurous, default 35%) sets how much the unheard counts.

## Sound taste

`SoundTaste` turns ME's fingerprints into taste, so songs never played can surface when they sound like favourites.

1. **Samples.** Each song in Plex's history weighs what `TasteProfile.Track` says (recent plays count most, log scale, 0 to 1).
   Songs played through or chosen here add up to 0.5 (their warmth). Songs skipped early here lately take away up to 1.5 ×
   their skip risk, so a song mostly skipped counts against its sound. When in the day each song is played comes from the
   history, in four parts: night (0–5), morning (6–11), afternoon (12–17) and evening (18–23).
2. **Centre.** All music fingerprints point roughly the same way, so every comparison is measured from the library's average
   fingerprint. Without this, everything looks alike.
3. **Clusters.** Liked songs are grouped with weighted spherical k-means: 1 cluster under 24 songs, 3 under 60, 4 under 150, then
   5. This keeps varied taste apart instead of averaging it into the middle. It starts from the heaviest song, then the most
   different one, so results are repeatable. A cluster with less than 8% of the liked weight is dropped as a stray.
4. **Skips.** Each skipped song's sound is taken away from its nearest cluster, capped at half that cluster's liked weight.
   Skips sharpen a taste but never reverse it.
5. **Parts of the day.** A part of the day with at least 8 liked songs and 10% of the weight gets its own taste vector.
6. **Scoring.** Every fingerprinted song is scored once, when built: cosine to the nearest cluster, ranked against the library.
   The less-alike 60% get 0, then it rises (squared) to 1 for the closest. So `SoundFit` is a lookup. The engine is relative:
   the closest-sounding songs always score highly, whatever the library sounds like.

At least 8 liked songs with fingerprints are needed; otherwise there's no sound term. Song Info says "It sounds like music you
love" when the sound term is among the strongest reasons, and "It sounds like what you play around this time" when the time
of day lift is.

`TasteService` builds the sound with each rebuild, on a background thread at below-normal priority, only when ME is the
chosen engine and has fingerprints. It builds again when the engine is switched, and when ME has learnt 10% (and at least
200) more songs since the last build. That check runs every 5 minutes, mostly while the library is first read after starting.
Building from about 36,000 fingerprints takes around a second.

## Who uses it

| Surface | How |
| --- | --- |
| Meloday (daily playlist) | The period profile's completion ratio comes from the memory (songs skipped early count less), and history and filler picks lean to liked songs (and, for filler, the unheard at the chosen adventure) |
| Mixes (niche) | Song score = Like + Fit + Adventure × Novelty, plus each mix's own focus, circle and energy |
| Artist mixes | Which recent artists get a mix: weighted by the engine's artist weight (history, less recent skips) |
| Radios (`RadioEngine`) | Like + Fit + Adventure × Novelty replace the separate rating, play and skip rules |
| Live stations, Headliners (`ArtistBlocks`, `HeadlinerProgram`) | Hit score = plays + how widely known + Like |
| Home and Made for You discovery | Albums from artists and a sound the listener likes (`ArtistWeight`, `Like`) |

The engine is rebuilt from Plex every 30 minutes and when the library connects. The memory counts the moment something
happens, so a skip affects every surface at once. Settings can forget the memory; Plex's own record is never changed.
