# Meloday Music

Meloday Music is a Windows desktop player for the music in your own [Plex](https://www.plex.tv) library.
It's written in C# with .NET 9 and WinUI 3.

The app is named after Meloday, a playlist that changes through the day, but it's a full player:
browsing, playlists, radio, mixes, lyrics and a mixing audio engine. Everything it builds comes from
your own library, and it works without Plex Pass.

I'm one person working on this in my spare time, and it's the first full app I've released. I
wanted a Plex music player for Windows that I'd enjoy using every day, so I made the one I wanted.

Meloday started as a Python script I wrote and shared under the name trackstacker
([script](https://github.com/trackstacker/meloday),
[original post on r/plexamp](https://www.reddit.com/r/plexamp/comments/1icefb3/meloday_a_daylist_for_plex/)).
This app grew out of it, and its mood and era maps come from that script.

Meloday Music is an independent project and isn't affiliated with or endorsed by Plex, Inc.

## Help! I'm not technical and don't use GitHub

You don't need to understand any of this page to use Meloday Music. Here's all you need to do.

**Before you start, you need:**

- A Windows 10 or Windows 11 PC
- A Plex account, with your music already in Plex

**1. Download Meloday Music**

Go to the [download page](https://github.com/melodaymusic/app/releases/latest). Under **Assets**, click
the file whose name starts with `MelodayMusic-Setup` and ends in `.exe`. It goes into your Downloads folder.

**2. Install it**

Open the file you downloaded. Windows may show a blue box saying "Windows protected your PC". That's
normal for apps from small developers. Click **More info**, then **Run anyway**. Then click **Next**
until it's finished.

**3. Open it**

Meloday Music is now in your Start menu. Click it to open it.

**4. Sign in**

Click **Sign In with Plex**. Your web browser opens a Plex page. Sign in there (or just approve it if
you're already signed in), then go back to Meloday Music. Your music will appear.

That's it. You never need to come back to this page unless you want a newer version. To update,
download and install the newest file the same way.

## How it was made

Meloday Music was built using AI tools.

The Genre Essentials covers are my recreations of Apple Music's genre artwork, redrawn in Meloday's
own colour palette.

If you notice something that could be done better, I'd be glad to hear it. Please open an issue,
and go easy on me, I'm still learning.

## Features

- Meloday, a mix for the current part of the day (dawn through late night), named from your
  listening and given its own cover. It can be saved to your Plex server as a single playlist that
  updates in place.
- Made for You: daily mixes of about 30 songs based on what you play, plus mood, energy and
  moment playlists and Genre Essentials, refreshed every Monday.
- Live stations that keep playing while you're away, so tuning in joins mid-song. Meloday Pulse
  runs a daily schedule of programs, and you can add your own in the Pulse Guide.
- Radio from any song, album, artist, genre, style, mood or decade.
- The Meloday Engine, which analyses your songs on your PC to learn how they sound. It powers
  Sonic Radio, Sonic Adventure and the sound-based playlists. With Plex Pass, Plex's own sonic
  analysis fills in for songs it hasn't learned yet, and you can switch the engine's learning off.
- AutoMix, an audio engine built on NAudio and WASAPI that beat-matches songs into each other,
  with filter sweeps or echo-outs for pairs that can't be matched.
- Synced lyrics from LRCLIB or Plex, and Lyric Studio for timing or adjusting lyrics yourself.
- Visualizers in Now Playing, including lyric videos and projectM with five MilkDrop presets.
- Trends: your listening by artist, album, song, time of day and day of the week, with a
  listening calendar.
- Optional Last.fm, ListenBrainz and MusicBrainz integration. Each needs your own key or account,
  and the app works without them.
- Pins, a configurable Home page, folders, grid and list views, and a mini player.

## What's not planned

Meloday Music is a personal project I'm sharing, and it's built around what I use: Plex on Windows.
I'm not planning to add Apple Music, a Docker version or a Mac version. I don't have an Apple
developer account or the hardware to test them, and I don't know Docker. The code is open source,
so anyone who wants to take it in those directions is welcome to.

## Installing

Download `MelodayMusic-Setup-<version>-x64.exe` from the
[latest release](https://github.com/melodaymusic/app/releases/latest) and run it. It installs for your
Windows account only, so it doesn't ask for administrator rights.

### Is it safe to install?

The installer isn't code-signed yet, so Windows SmartScreen will probably show "Windows protected your
PC" with an unknown publisher. Select **More info**, then **Run anyway**. Windows shows that for any
unsigned app from a small developer; it doesn't mean anything was found in the file.

If you'd like to check before running it:

- Each release lists the installer's SHA-256 hash. Run `Get-FileHash .\MelodayMusic-Setup-<version>-x64.exe`
  in PowerShell and compare the result with the one in the release notes. If they match, you have the
  exact file I uploaded.
- All of the source is in this repository, and you can build the app yourself (see [Building](#building)).
- Meloday talks to your Plex server and plex.tv. Lyrics can come from LRCLIB, and artwork and artist
  details from MusicBrainz and the Cover Art Archive. Last.fm, ListenBrainz, fanart.tv and OpenAI are
  only used if you add your own account or key.

## Requirements

- Windows 10 version 2004 (build 19041) or later, x64
- A Plex Media Server with a music library
- To build: the [.NET 9 SDK](https://dotnet.microsoft.com/download)
- To build the installer: [Inno Setup 6](https://jrsoftware.org/isinfo.php)

## Building

```powershell
dotnet build MelodayMusic.sln -c Debug -p:Platform=x64
dotnet test MelodayMusic.sln -p:Platform=x64
dotnet run --project src/MelodayMusic.App -p:Platform=x64
```

The Meloday Engine's audio model isn't stored in the repository. Download it once with:

```powershell
./tools/engine/download.ps1
```

To build the per-user installer, written to `artifacts/final/`:

```powershell
./installer/build.ps1
```

## Repository layout

| Folder | Contents |
|---|---|
| `src/MelodayMusic.Core` | Plex client, caching, audio analysis, radio, playlists and settings. No UI code. |
| `src/MelodayMusic.App` | The WinUI 3 app: views, view models, controls, services and assets. |
| `tests/MelodayMusic.Core.Tests` | Unit tests for the core library. |
| `tools/engine` | `download.ps1`, which downloads the audio model. |
| `tools/projectm` | Builds the projectM visualizer library. See its README. |
| `installer` | Inno Setup script and the release build script. |

## Privacy

You sign in with your Plex account from inside the app. Tokens and keys are encrypted with Windows
DPAPI under `%LOCALAPPDATA%\MelodayMusic` and are never written to logs. Meloday reports what you play
to your Plex server, so play counts, history and Now Playing stay up to date in Plex; turn off
**Report listening to Plex** in Settings to stop that. The skips and choices recommendations learn from
stay on your PC.

## Third-party software

The app ships the Nunito typeface (SIL OFL 1.1), Lucide icons (ISC), projectM (LGPL 2.1) and several
MIT-licensed libraries. The full list with licences is in
[src/MelodayMusic.App/Assets/THIRD-PARTY-NOTICES.txt](src/MelodayMusic.App/Assets/THIRD-PARTY-NOTICES.txt).

Cover shapes: Shape Icons by [SVGBackgrounds.com](https://www.svgbackgrounds.com) and Shape Icons by
[shapes.gallery](https://shapes.gallery).

The Genre Essentials covers are based on Apple Music's genre artwork, redesigned in the Meloday palette.
Apple Music is a trademark of Apple Inc.

## License

Meloday Music is licensed under the [GNU General Public License v3.0](LICENSE).
