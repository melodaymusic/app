# Meloday Music

A fast, good-looking desktop music player for your own [Plex](https://www.plex.tv) music library,
built with C# / .NET 9 and WinUI 3 for Windows 11. It adds **Meloday**, a daylist-style mix that
changes with the time of day, its own radio and mixing engine, and a design of its own.

> Meloday Music is an independent project. It is not affiliated with or endorsed by Plex, Inc.

## Highlights

- **Meloday** — a mix for the part of the day (dawn to late night), named from your listening
  (mood and era maps in `src/HumeMusic.App/Assets/Meloday`), with its own cover. It can be kept on
  your Plex server as one playlist that is updated in place.
- **Made for You** — its own tab: mixes of about 30 songs that Meloday picks from what you play (by
  artist, genre, mood, time of day and more), each with its own artists on its cover. Radio, by
  contrast, starts from something you choose and never ends.
- **Live stations** — Headliners (your biggest artists in blocks of three songs, with new releases, what's trending
  and genre blocks between them; Now Playing features the artist whose block is on), Hits (the '80s, '90s and 2000s), Club, Chill, Library Radio and Deep Cuts are always
  on: tune in and you join mid-song, and the station carries on while you're away. Each has its own moving cover. It's live radio, so its songs can't be skipped, sought, restarted or paused: its play button is a stop button, and playing again tunes in to where the station is by then.
- **Visualizers** — Now Playing can draw the music as it plays, in the artwork's own colours, from the player's real
  output and light enough to leave running: Bars, Halo, M-Wave (the logo's wave, loose and liquid, with its colour pulsing
  through on the beat), Sun (the playlist cover's rings, each swelling with its own part of the sound), and two lyric videos
  (the words set big and arriving as they are sung, in Manrope, over the pictures of the artist, the album and the artists featured on the track in turn, or a lively gradient;
  lyrics with no timing roll up the picture like credits). They scale themselves to the computer (a smaller or slower
  picture on a weak one, and 30 frames a second with the energy saver on). Any of
  them goes full screen (F11): the interface fades away after a few seconds of stillness and only the picture is left.
- **Dupes** — on a Meloday, press the merge button on a song that is the same song as another and choose which
  one plays: the two rows animate into one, and from then on the Meloday holds only that one. Pairs are listed
  (and can be unmerged) in Settings, under Dupes.
- **Last.fm** — your own Last.fm API account, with a switch for each use, both off until you turn them on: scrobble
  what you play, and a "Trending on Last.fm" row on Home (the songs of the chart that are in your library).
- **Guest curators** — for anything but live radio, choose how what plays next is arranged (after Plexamp's
  guest DJs): Double Up, Twin, Drift, Time Machine, Smooth or Spread, from the player bar's curator button.
- **Not for this station** — on live radio the curator button becomes a thumbs-down: it takes the song on off that
  station for good (it stays welcome on the others), and Settings can let everything back on.
- **Radio without Plex Pass** — stations, artist mixes and "Radio" on songs, albums and artists run on
  Meloday Music's own algorithm, with skip learning kept on your computer. Plex Pass features are
  marked in Settings.
- **Folders, grid or list, playlist search** — the library by folder, library pages as a grid or a list, and a search
  inside each playlist and a view sorted any way.
- **Mute for a while** — keep a song or an artist out of Meloday, the mixes, the live stations and the radios for a week,
  a month or a length of your own (set in Settings), without rating them down; they come back by themselves.
- **Lyric Studio** — time a song's lyrics yourself by tapping the space bar as each word is sung (or each line), shift a
  song's lyrics earlier or later, and better-spaced guesses for lyrics timed only by line.
- **Covers, backdrops and logos from Plex** — playlists, albums, collections and artists show the backdrop and logo
  their Plex item has, and each can have its cover, backdrop, logo, name and description set from Meloday (saved on
  your Plex server, so every app shows them).
- **AutoMix** — an own audio engine (NAudio, WASAPI) with beat-matched blends, up to sixteen bars for a
  perfect fit and long instrumental transitions, a filter sweep for pairs that merely match and an echo out for
  those that can't be matched, and an output that rides out UI freezes.
- **Fast** — answers are cached on disk and shown at once, then refreshed in the background.
- **Home, pins and browse** — "Jump back in", today's listening time, and pins of almost anything
  (artists, albums, playlists, stations, mixes, genres, songs) in any order.

See [CHANGELOG.md](CHANGELOG.md) for the full history and [docs/design-system.md](docs/design-system.md)
for the tokens and components the UI is built from.

## Building

Requirements: Windows 10 (22621) or newer, the [.NET 9 SDK](https://dotnet.microsoft.com/download),
and optionally [Inno Setup 6](https://jrsoftware.org/isinfo.php) for the installer.

```powershell
dotnet build HumeMusic.sln -c Debug -p:Platform=x64
dotnet test tests/HumeMusic.Core.Tests
dotnet run --project src/HumeMusic.App -p:Platform=x64
```

Release installer (self-contained, per-user), written to `artifacts/final/`:

```powershell
./installer/build.ps1
```

## Layout

| Folder | What it holds |
|---|---|
| `src/HumeMusic.Core` | Plex client, caching, radio, Meloday logic, pins, settings — no UI |
| `src/HumeMusic.App` | The WinUI 3 app: views, view models, controls, audio engine, assets |
| `tests/HumeMusic.Core.Tests` | Unit tests |
| `tools/HumeMusic.DevCli` | Developer command line for trying the Plex integration |
| `tools/promo` | Python that renders the promo video (screenshots, motion graphics and an original synthesized soundtrack); see its README |
| `installer` | Inno Setup script and build script |
| `docs` | The design system and reference documentation |

## Your data and secrets

Nothing is hard-coded: you sign in with Plex from the app. The Plex token is stored encrypted with
Windows DPAPI under `%LOCALAPPDATA%` and is never logged. Do not commit tokens, keys or a `config.yml`
(it is git-ignored).

## Third-party

Manrope (SIL OFL 1.1) and Lucide icons (ISC) ship with the app; notices are in
`src/HumeMusic.App/Assets/THIRD-PARTY-NOTICES.txt`. `docs/plex` holds reference documentation for
the Plex API client library it was built against.

## License

No license has been chosen yet; until one is added, all rights are reserved by the author.
