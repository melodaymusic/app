# Meloday Music

A fast, good-looking desktop music player for your own [Plex](https://www.plex.tv) music library,
built with C# / .NET 9 and WinUI 3 for Windows 11. It adds **Meloday**, a daylist-style mix that
changes with the time of day, its own radio and mixing engine, and a design of its own.

> Meloday Music is an independent project. It is not affiliated with or endorsed by Plex, Inc.

## Highlights

- **Meloday** — a mix for the part of the day (dawn to late night), named from your listening
  (mood and era maps in `src/HumeMusic.App/Assets/Meloday`), with its own cover. It can be kept on
  your Plex server as one playlist that is updated in place.
- **Radio without Plex Pass** — stations, artist mixes and "Radio" on songs, albums and artists run on
  Meloday Music's own algorithm, with skip learning kept on your computer. Plex Pass features are
  marked in Settings.
- **AutoMix** — an own audio engine (NAudio, WASAPI) with beat-matched blends and long instrumental
  transitions, and an output that rides out UI freezes.
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
