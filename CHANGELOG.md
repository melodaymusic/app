# Changelog

Meloday Music follows semantic versioning. Until the public launch (1.0.0) the version stays below
1.0: a run of work that adds or changes features raises the minor number (0.9.0 → 0.10.0), and a run
of fixes alone raises the patch number (0.9.0 → 0.9.1). The version is set in `Directory.Build.props`.

## 0.33.0 — 2026-10-04

- Meloday's names now draw on the full mood and era maps: each of the 300+ moods has a couple dozen
  descriptors (it was a handful) and each decade its nicknames, so a Meloday's title and description
  vary much more.
- The project folder is tidied for publishing: the unused blank cover images are gone (covers are
  drawn by the app), a README describes the project, and loose source art and scripts were moved
  out of the way.

## 0.32.0 — 2026-10-04

- Playback holds up through longer freezes: the audio output now keeps 800 ms of sound waiting
  (it was 400 ms). In a hard stress test on a computer with little free memory, the app was held up
  for 370 to 630 ms by memory clean-ups, which was longer than the buffer; ordinary use stays under a
  quarter of a second. Pause, seeking, skipping and volume are still instant.
- Home is redesigned after the mockup: the greeting and "Jump back in" above, with how long you've
  listened today counting up on the right (kept on this computer, starting again at midnight). Under
  it, the last thing you played is featured large with Play, Shuffle and Pin, and the eight before
  it sit beside it as glass tabs (cover and name). It follows what you play here (albums, playlists,
  artists, stations, mixes, Meloday) together with what Plex says was played lately. It's the setting
  "Jump back in" in Settings > Home: if you'd switched it off, switch it on to see it.
- Station covers are laid out like Meloday's cover, so they match: the name set large in the top
  right and "Radio" in the bottom left (the small radio icon is gone). One shared piece of
  typography now draws Meloday's cover, every station's, and the still covers below.
- Almost anything can be pinned: stations, Meloday, mixes, genres and moods, songs, and everything
  that could be before. What normally moves is pinned as a still cover, drawn in that same
  typography (Meloday's follows its mix through the day). Pins can be dragged into any order on
  Home as well as in the sidebar, and Move Pin Earlier / Later is in their menus.
- Meloday can be kept on your Plex server from the Meloday page itself: a "Keep on Plex" switch
  (the same as in Settings) keeps one playlist, updated in place with each new Meloday, with its
  cover and description, while Meloday Music is open; the page says when it last updated, and a
  Meloday made while the app was closed is sent the next time it opens. "Save as a Playlist" on
  Meloday now sends the cover and description too, not just the tracks.

## 0.31.0 — 2026-10-04

- Playback no longer stutters when you move around the app. While pages open, the whole app can
  freeze for a quarter of a second (the interface framework forces full memory clean-ups, most of
  all when Home comes back), and every thread stops for it, the audio thread included: the sound
  broke up whenever the freeze outlasted the 180 ms of sound waiting in the device. The audio output
  is now Meloday's own (replacing NAudio's): 400 ms of sound waits, on a high-priority thread in
  Windows' "Pro Audio" class, so a freeze of up to about 350 ms is inaudible. Pause, skip, seeking and
  volume stay instant (pausing stops the device itself, volume is the device's, a seek or a new song
  empties what waits), and the position shown follows what is actually heard. Measured with the
  app held up for 240 ms: no break in the sound.
- Settings is in sections (General, Home, Plex, Playback, Meloday, Artists, About), one at a time,
  remembering the last you had open. The chips are the same as Browse's, now one shared control.
- Settings shows what Plex Pass unlocks in a table: stations, artist mixes, Sonic Radio, Sonic
  Adventure, Meloday's sonic touches and lyrics from Plex, each locked or unlocked for the account,
  and what plays instead without it. Home's "Stations" is no longer marked Plex Pass (Meloday makes
  its own); Sonic Adventure on Radio rests, with a tooltip, rather than disappearing.
- Pins can be put in any order: drag them in the sidebar, or use Move Pin Earlier / Later in their
  menu (the sidebar's and Home's). New pins still go first.
- The sidebar scrolls when the window is short (it was clipped, with Settings out of reach); tall, it
  still fills the panel with Settings at the bottom.
- A Meloday kept on Plex now gets its cover as the playlist's own picture (Plex is asked to use it,
  rather than leaving its collage), and the playlist is remembered even if the cover fails, so a
  second playlist is never made. Turning "Keep as a playlist" on keeps today's Meloday straight away.
- A radio started from the song already playing keeps it playing and queues the radio after it.
- For speed checks, MELODAY_PERF=1 now also logs when the audio thread ran late, why each memory
  clean-up ran, and what is allocating large buffers.

## 0.30.0 — 2026-10-04

Meloday's own radio, so the best of Radio and Mixes no longer needs Plex Pass.

- Without Plex Pass (or where Plex has no station for something), Radio now has Library Radio and
  Deep Cuts Radio, and every genre, style, mood and decade of the library, each made by Meloday
  from the library itself. Your favourites of each kind lead, as before. Where Plex has its own
  stations they're still used first.
- Artist mixes in Mixes for You, and a new Radio on any song, album or artist (the menu, and the
  button on an artist's page), work without Plex Pass: the artist's own music woven with music from
  artists near them: those Plex lists as similar, those who share playlists with them, and those who
  share their styles, moods and genres. With Plex Pass, Radio stays Plex's Sonic Radio.
- What the radio chooses: songs you play and rate well, not played lately, never repeating an
  artist too soon, never more than two of an artist in a batch, nothing rated one star or less. It
  tops itself up as it plays.
- Meloday now learns from what you skip: a song skipped in its first half-minute is played less, and
  a little less of its artist; songs played through count for them. This is kept on this computer
  only (nothing is sent to Plex), and fades over a month or two.
- Sonic Adventure stays a Plex Pass feature.

## 0.29.0 — 2026-10-03

A speed and cache pass: everything already known is shown at once and brought up to date behind it.

- Every main page stays alive between visits (the app kept only four, so moving around rebuilt pages
  from scratch). Coming back to a page is quick: Home re-opens in about a quarter of a second
  instead of up to a second, Radio in about a seventh, the library pages in a few hundredths.
- An answer that has grown old is still shown at once while a newer one is fetched behind it, from
  memory as well as from disk; kept answers are read off the UI thread; and catching up runs a few
  requests at a time so it never crowds out what a page is waiting for.
- Home no longer waits for Mixes for You or Stations (they join it when ready) and no longer rebuilds
  the mixes each time it refreshes; their boards reuse their last sample of artists while a new one
  is fetched, and an artist mix's board asks Plex's radio once instead of three times. Station
  pictures on Radio work the same way. Music to play is always picked fresh.
- Revisiting a playlist checks just that playlist with Plex, instead of making every kept answer be
  checked again; Home picks up playlist edits the same way.
- Lighter cards: a card's play button is made when it's first hovered, a station's moving field and
  a smart playlist's sparkle only where they're shown, and an artwork's placeholder icon only when
  there's no picture (while one loads the placeholder is plain). Home is about 40% fewer elements.
- Station videos start only for boards that can be seen, one at a time, and a page left behind
  keeps its paused videos for half a minute, so coming back doesn't set them all up again.
- For speed checks, MELODAY_PERF=1 logs UI-thread hold-ups and how long each page takes to open.

## 0.28.0 — 2026-10-03

- AutoMix shares more of the music where songs suit each other, as Spotify's mixing does: when the
  playing song ends instrumentally and the next opens instrumentally, in a key that sits well with it
  and with the same sort of energy at the join, they overlap across both instrumental stretches, up
  to eight bars, beat-matched, the bass swapped halfway; the singing still never overlaps. Where the
  singing isn't known, blends stay as cautious as before. Blends are at most eight bars.
- Smoother mixing: volumes glide within each 5 ms block rather than stepping, the bass filter's
  cutoff glides, and the filter runs warmed up alongside the song so switching it on for a bass swap
  no longer jolts the sound. A song plays exactly as it is whenever no swap is filtering it.
- Checked offline and live: rendered mixes show no clicks, flutter or whine beyond the songs' own,
  and the song after a mix is sample-for-sample the original.

## 0.27.0 — 2026-10-03

- Editorial boards, after Apple Music's: a billboard told as a story, its heading (what it is, the
  album and the artist) above the backdrop rather than on it, a short line on the backdrop, and the
  album's cover in the bottom-left corner, clear of the play button. Browse hubs' billboards use them.

## 0.26.2 — 2026-10-03

- AutoMix's playback sounds right again. Its mixer cleared only a quarter of each block it sent to
  Windows (NAudio passes a byte buffer seen as floats, and the clear counted bytes), so every 10 ms
  the last block echoed back and built up: hollow and robotic, louder and louder until it distorted.
  The mixer's output is now the song exactly (checked against a clean decode of it).
- After a beat-matched mix, the song is exactly as written once back at its own tempo (the tempo
  stretcher passes it through untouched), and stretching itself matches its slices more precisely.
- A limiter keeps two loud songs overlapping in a mix from going past full scale.

## 0.26.1 — 2026-10-03

- Updating no longer stalls on "Setup was unable to automatically close all applications": Meloday
  Music closes within three seconds however long Plex or a phone following it takes to answer, and
  the installer closes it itself if it still hasn't.

## 0.26.0 — 2026-10-03

- Browse hubs (Pop, Chill & Calm...) open with billboards from your own library in that corner of
  it, each shown only when it's true: New in Pop (added in the last six months), your top album and
  your top artist there (when they've really been played), a favourite to rediscover (played a lot,
  not for three months), and an album you haven't played yet (by an artist you already love there, if
  there is one). Up to five, in a row that scrolls; a hub with nothing to say shows none.
- Lyrics read by hand flow again: scrolling glides with the wheel, and a few seconds later the lyrics
  glide back to the singing instead of snapping there.

## 0.25.0 — 2026-10-03

- AutoMix mixes like a DJ (and like Apple Music's AutoMix), no longer just crossfading:
  - Meloday Music listens to each song itself: how the playing song ends and the next begins, their
    tempo, beats, bar lines, key and energy (Plex has no beat analysis). Only those stretches are
    fetched; what's found is kept on this computer.
  - Songs within 8% of each other's tempo (or half or double it) are beat-matched: the next song
    comes in on its first bar line exactly as the playing song reaches one of its own, plays at the
    playing song's tempo (stretched without changing its pitch), and eases back to its own once alone.
  - Their bass is swapped on a bar line halfway through the blend, so two kick drums never fight.
  - Blends are measured in bars: longer for songs in keys that sit well together (by the Camelot
    wheel), shorter for clashing keys, and never with two voices singing over each other.
  - Songs too far apart in tempo keep AutoMix's loudness blend, brought onto a bar line.
  - AutoMix plays through Meloday Music's own mixer, so mixes start on the exact sample; None and
    Crossfade, and casting, play as before.

## 0.24.1 — 2026-10-03

- Lyrics no longer jump to the top and back: a stray position reading (the player re-buffering for a
  moment) isn't taken as the song going back, and lines are measured where they're laid out rather
  than where they float.

## 0.24.0 — 2026-10-02

- Lyrics float, as Apple Music's do: the line being sung sits at the reading point, bright and full
  size, and each line glides to its place on its own spring, those nearest the singing first and the
  rest a beat behind, so the lyrics drift up rather than scroll. Lines away from the singing grow
  dimmer and a little smaller with distance. Breaks in the singing, and a long intro, show three
  breathing dots. Scroll by hand to read ahead; the lyrics return to the singing a few seconds later.

## 0.23.0 — 2026-10-02

- Lyrics for far more songs: when Plex has none (or only unsynced ones), Meloday Music looks them up
  on LRCLIB (lrclib.net), the free, open lyrics database. In a sample of 40 songs from this library,
  Plex had synced lyrics for 2; with LRCLIB, 28 have them. Matches go by title, artist, album and
  length, also trying the lead artist and the title without its featured artists. What's found is
  kept on this computer, so each song is looked up once (a miss is retried after a week). Settings →
  Playback → Lyrics From LRCLIB turns it off; only the song's title, artist, album and length are sent.
- The lyrics panel says where the lyrics came from, and says so when a song is an instrumental.
- AutoMix follows the singing with lyrics from either source.

## 0.22.0 — 2026-10-02

- Faster: Meloday Music keeps what Plex sends on this computer, so it starts and opens pages from
  what it already knows instead of asking the server for everything again. Kept answers show at
  once and are brought up to date quietly behind them; Home catches up a few seconds after launch.
  Measured on this library: everything Home needs is in hand 2 seconds after connecting, down from
  5, and the slow requests at startup (twelve of over a second, Mixes for You's artist stations) are
  gone. Kept answers also stand in when the server can't be reached.
- Staying in step with the library: Meloday Music notices when Plex says the library's content has
  changed and checks what it kept before using it again. Settings → Server and Library → Keep In
  Step With Your Library has Refresh Library (load everything afresh) and Scan for New Music (ask
  Plex to look through your music folders).
- Lighter in the background: moving artwork rests while the window is minimised, as it already did
  while another window was in use.
- Lighter overall: the four pages visited last stay ready, and older ones are let go (they reopen
  from what's kept on disk) rather than every page staying in memory for the whole session.

## 0.21.0 — 2026-10-02

- Previous, play/pause and next under Meloday Music's taskbar thumbnail.
- The player bar fits every window size: its buttons sit in two short rows when space runs low
  (Cast stays in the bar now), and in the narrowest windows the mini player, Cast, shuffle and repeat
  step out to Now Playing rather than being pushed off the bar.
- Browse's category pills: the chosen one's highlight slides to it from the last.

## 0.20.0 — 2026-10-02

- Song Info, in every song's menu: the song (album, track, year, genre, mood, length), your
  listening (plays, skips, when last played and added, rating) and its file (format, quality,
  channels, size, Sound Check level, sonic analysis, whether it's downloaded, and where it is).
- Song notifications: a quiet notification with the artwork as each song starts, while Meloday
  Music is in the background; each replaces the last. Settings → Playback → Song Notifications.
- Last.fm scrobbling, with your own Last.fm API account: Now Playing as each song starts, and a
  scrobble once it has played half its length or four minutes. Songs that can't be sent wait and go
  with the next. If your Plex account already scrobbles to Last.fm, leave it blank and keep Report
  listening to Plex on.

## 0.19.0 — 2026-10-02

- Downloads: keep songs, albums and playlists on this computer to play offline: Download in any
  song, album or playlist's … menu. Downloaded songs play from the computer instead of the server
  (the original files where Windows can play them, otherwise 320 kbps MP3s). Library → Downloads
  lists them, with Shuffle and a remove button on each; Settings shows how much is downloaded and
  can remove it all.
- Lyrics have their own quote icon (the microphone stays with Artists).

## 0.18.0 — 2026-10-02

- Smart playlists can be made and edited: Playlists → New Smart Playlist, or Edit Rules… in a smart
  playlist's menu. Rules look at a song's title, artist, album, genre, style, mood, year, decade,
  rating, plays, skips, when it was last played or added; they can all have to match, or any one.
  Choose an order and, if you like, a limit. A count of the songs the rules match follows every
  change. Rules made elsewhere that the editor can't show as one (groups of alternatives) are listed
  in plain words and kept exactly as they were unless removed.
- Smart playlists' songs no longer leave room for a remove button they can't have.

## 0.17.0 — 2026-10-01

- Control Meloday Music from your phone: Plex and Plexamp (and Plex Web) on the same network now
  find this computer as a player, the way they find Plexamp. Choose it as the player in the app, then
  start music from the app's own library view and use its controls: play, pause, skip, seek, volume,
  shuffle and repeat, with the app following what plays here. On by default; Settings → Playback →
  Control From Your Phone turns it off. Windows asks once to let Meloday Music talk on your network;
  allow it on private networks. Only devices on your own network can control the player.

## 0.16.0 — 2026-10-01

- Mini player: a small window that stays above everything else, with the artwork, the song and its
  controls, from the player bar's mini player button (or Ctrl+Shift+M). The full window steps aside
  while it's open and comes back from the mini player's expand button or when it's closed.
- Sleep timer, on Now Playing: stop the music after 15 minutes to 2 hours, or at the end of the song
  playing. A timed stop fades the music out over its last seconds; your volume is as you left it next
  time. The moon is lit while a timer runs, and its tooltip says when the music stops.
- Toggle buttons' dots no longer stay lit after a change made from their menu.
- Up Next says "1 track" rather than "1 tracks".

## 0.15.0 — 2026-10-01

- Lyrics: a Lyrics button in the player bar (and on Now Playing, or Ctrl+Shift+L) opens the song's
  lyrics beside the page, where the queue opens. Synced lyrics follow the song, the line being sung
  bright and the rest receding, and a press on a line plays from it; scrolling by hand holds the
  lyrics where they were put for a few seconds. Lyrics come from Plex (LyricFind, with Plex Pass).
- The queue and lyrics panels share one place: opening one closes the other, and their buttons are
  lit only while their panel is open.

## 0.14.1 — 2026-10-01

- Track rows in playlists and the queue no longer shift when hovered: the remove button's column is
  kept for every row of a list that allows removal, including rows that appeared while a removal was
  still being made.

## 0.14.0 — 2026-10-01

- AutoMix, rebuilt in the spirit of Plexamp's Sweet Fades: every pair of songs gets its own handover.
  Each possible overlap, from half a second to twenty, is played out on the two songs' loudness
  (now read at two points a second) as it would be heard, fades and all, and the cleanest wins: no
  two songs loud at once, no sag in the music between them, and (with synced lyrics) never two voices
  singing together. A song that fades out is left to fade while the next comes in under it; a quiet
  intro slips in at once beneath the last song's ending; a song that stops dead into a loud start gets
  a tight, DJ-style cut. Only what is still loud is eased away, and handovers start on the moment
  rather than the next quarter-second.
- An album played in order isn't mixed: its tracks run into one another as the album does.

## 0.13.0 — 2026-10-01

- AutoMix listens for the singing: with synced lyrics (LyricFind, through Plex Pass) it knows when a
  song's vocals end and when the next one's begin. An instrumental outro running into an instrumental
  intro now overlaps across both (up to 20 seconds), finishing as the outgoing song does so the next
  song's singing only starts once it has gone; where the singing runs to the end and the next song
  sings at once, the handover stays short so two voices never clash. Songs without synced lyrics (or
  whose lyrics Plex can't fetch at the moment) are mixed from loudness alone, as before.

## 0.12.0 — 2026-10-01

- Cast: play to Google Cast devices (Chromecast, Google/Nest speakers, Google TV) and to UPnP/DLNA
  renderers such as Sonos speakers and many smart TVs, from a Cast button in the player bar and on Now
  Playing. The song carries on where it was; the queue, shuffle, repeat, media keys and listening
  reports all work as usual. Devices get a transient Plex token, never the account's own.
- Sonic Adventure (Plex Pass): choose a track to start with and one to end up at, and the music travels
  between them by small sonic steps. From a track's … menu, or the Radio page.
- AutoMix: transitions shaped to each pair of songs from Plex's loudness analysis. The next song
  comes in where the outgoing one starts to fade, for as long as that fade lasts (a short, tight
  handover for songs that end cold), and starts at its first sound. Settings → Playback → Transitions.
- Sound Check: every song at the same loudness, from Plex's gain analysis (only ever turning down).
- Meloday is made as the app starts (a couple of seconds after the library is ready), so it's ready
  to play by the time you open it.

## 0.11.0 — 2026-10-01

- New Meloday cover art: each part of the day has its own sky and sun of four soft rings, set where
  the sun sits at that time (low at dawn, high in the afternoon, off to the side late at night).
- The Meloday radio card is the cover itself, live: the rings breathe slowly, each on its own cycle,
  with the mix's name in the top right and "Meloday" in the bottom left, as on the cover.
- The Meloday page's cover breathes too. The saved cover (for Plex and thumbnails) uses the same art,
  still. The breathing follows the Motion setting and pauses off screen or when the window isn't in use.

## 0.10.0 — 2026-09-30

- Browse gathers genres, styles and moods into families: Pop (Dance Pop, Soft Pop, Bedroom Pop…),
  Hip-Hop, Electronic, Chill & Calm, Sad & Reflective and so on. A family card opens its hub, with
  every genre, style or mood in it as a card, plus Shuffle and All Albums for the whole family. On
  this library 607 of 618 genres, 326 of 330 styles and 292 of 293 moods join a family; the rest keep
  their own card. Decades are left as they are, and Plex's "ArtistPopular" marker is no longer shown.
- Family cards show the top album from each of their biggest members, so families that share a few
  much-played albums no longer all show the same covers.
- The queue is the app's own floating panel now: the queue button opens and closes it, a press on the
  page or Escape closes it, and it no longer has its own close button. It slides in from the right.
- Grid cards (Albums, Artists, Playlists, Collections, Browse, genre pages, stations) now show the
  same rounded hover plate around the card as rows do; it was being clipped at the card's edges.
- Browse cards are properly rounded at the bottom (the last few pixels were cut off), their titles
  wrap up to three lines, and titles stop short of however many albums the card shows.

## 0.9.1 — 2026-09-30

- The queue button now reliably closes the queue when it floats over the page: a click acts on
  whether the queue was open when it was pressed, so the press that dismisses the queue no longer
  reopens it. Its dot follows the queue the moment it opens or closes.
- Clearer icons for the queue and its actions (Lucide):
  - Queue button: a list with a play mark (`list-video`), no longer the same icon as Playlists.
  - Save queue as a playlist, and a mix's "Save as a Playlist…": `save-plus`, distinct from
    "Add to Playlist" (`list-plus`).
  - Close the queue: `panel-right-close`, closing the panel at the right.
  - Clear Up Next (`list-x`), Play Next (`list-start`) and Add to Queue (`list-end`) are unchanged.
  - Settings: "Restore queue on launch" uses `list-restart`; Home's "Sections" uses `layout-list`.

## 0.9.0 — 2026-09-30

The first numbered pre-release, covering everything built so far, and this run of work:

- Browse: genre, mood, style and decade cards are boards now, in the manner of Spotify's browse
  cards. Each shows three of your most played albums with that tag, cascaded and tilted at the lower
  right, and takes the colour of the album on top. Search results use the same cards.
- The queue button opens and closes the queue, and its dot shows only while the queue is open.
- Hover plates are concentric with every kind of card (boards as well as tiles).
- Proper versioning: one version for the app and installer, shown in Settings > About.

### Earlier work in this release

- One design system: shared page headers, section headings, buttons, sort menus, tag cards, repeat
  control, grouped settings lists and empty states; concentric radii; a spacing, type and motion
  scale; graceful transitions throughout (see docs/design-system.md).
- Artist pages: backdrop colours sampled from where the photo meets the page, larger logos, your own
  logos imported from a folder, rankings, release types and appearances.
- Meloday, the daylist-style radio, with optional AI titles.
- Glass sidebar, floating queue and player bar, rows that slide under the sidebar, moving station
  artwork, and the rename to Meloday Music.
