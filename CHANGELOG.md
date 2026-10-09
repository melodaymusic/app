# Changelog

Meloday Music follows semantic versioning. Until the public launch (1.0.0) the version stays below
1.0: a run of work that adds or changes features raises the minor number (0.9.0 → 0.10.0), and a run
of fixes alone raises the patch number (0.9.0 → 0.9.1). The version is set in `Directory.Build.props`.

## 0.57.0 — 2026-10-08

- **One Edit Details window, as Plex has it.** Albums, artists, collections and playlists have a single "Edit Details…"
  (the artist page's pencil opens the same window): a Details tab for the title and summary, then Cover (Photo for an artist),
  Backdrop and Logo tabs, each a grid of the images Plex found with an Upload Image button. Nothing is sent until Save, and
  everything is kept on the Plex server. The separate Artwork submenu, the three "Change …" items and the fanart.tv logo
  picker (two ways to change a logo) are gone; a logo chosen in the old picker is replaced by the server's when a new one is saved.
- **The builders and Sonic Adventure match the rest of the app.** The Artist and Album Radio Builders and Sonic Adventure
  search in a plain text box with the matches listed under it in the app's rows (picture, title, detail), chosen ones sit in the
  grouped list used by Settings, and suggestions are capsule buttons with an icon.
- **Settings uses the page's full width, and every section is reachable.** The page was held to 980 px, which cut the
  last chip ("About") off; the content now fills the page like the others. The chips also keep their pill inside the row,
  bring the chosen chip into view, and scroll with the mouse wheel when a narrow window can't show them all.

## 0.56.0 — 2026-10-08

- **Everything Meloday makes follows the same rules.** Nothing rated one star or less (the song, its album or its artist), no
  muted music, no song that is a dupe of another (the keeper plays), and songs played lately are left out where there are
  enough others and otherwise held back (a song played an hour ago is very unlikely to come, one a week ago is held back, a
  month ago it counts as any): in the daily Meloday, the mixes, the live stations, the radios and what Made for You is drawn from.
- **Live stations no longer open on the same artists after an update.** A station's running order is remembered between
  visits, which meant a restart rejoined the same songs it had already shown; now the first time a station is tuned in during
  a run of the app it keeps the song that is on and plans the rest afresh. Every station also remembers, between runs, the
  artists it has played lately (Headliners did already) and lets others come first. Random artists and albums for stations
  (Hits, Chill's moods, Deep Cuts, genre and decade radios) were being served from the cache, so they came back the same after a
  restart: they are fetched anew each time, and Deep Cuts chooses its thirty artists by chance from the ninety most played.
- **Club Radio no longer takes pop.** Dance Pop, Dance-Pop, Pop Dance and Electropop (and Indie/Australian/Canadian Electropop)
  are pop with a beat, too broad for a club. A song's tempo isn't known for most of a library (Plex doesn't hold it, and
  Meloday only measures songs as they are played or queued), so it isn't used.
- **Every cover Meloday makes has its text in the same places at the same sizes.** The name top right and the label bottom left
  (a mix's name is its label, no longer a smaller line with its own margins) each have a box 760 wide in a 1000-unit square, at
  a fixed size; a text too long for its box is set just small enough to fit it, instead of being cut short with dots.
  Meloday's cover, the station covers, the mix covers and the still covers (pins, Jump back in) all use the one layout.
- **Page changes never show two pages at once.** The page on show fades away (a tenth of a second), then the next takes its
  place and fades in, so the new page can't be seen over the old one, whatever it takes to build it.

## 0.55.1 — 2026-10-07

- **Club Radio and Chill Radio read a real library's tags better.** Tested against the 731 genres and 293 moods of a
  36,000-song library. Club now also takes the bass and big-room families (Future Bass, Melodic Bass, Brazilian Bass,
  Moombahton, Big Room, Big Beat, Complextro, Brostep, Neurostep, Ballroom Vogue, Aggrotech, Uk Funky, Kuduro, Amapiano,
  Jungle) and leaves out Witch House. Chill now also takes Vaporwave and Vapor Soul, Float House (moved from Club),
  Escape Room, Study Beats, Focus Beats, Neo Mellow, Mellow Gold and Slowed And Reverb. Chill's artist moods are the calm
  ones (plus Meditative, Ethereal and Reassuring/Consoling); Reflective, Wistful and Introspective no longer count, since
  they describe a lot of singer-songwriter music that isn't Chill.

## 0.55.0 — 2026-10-07

- **Mute a song or an artist for a while.** For when you don't want to hear them for now but don't want to rate them down:
  a muted song or artist stays out of everything Meloday makes (the daily Meloday, the mixes, the live stations, the radios,
  Sonic Radio and adventures) until the time is up, then comes back by itself. What you choose yourself (an album, a
  playlist, a song you play on purpose) still plays. Press **Mute** on Now Playing (beside the rating) to mute the song or
  its artist for the length set in Settings, to choose Another Length (any number of days), or to unmute; the song and
  artist menus everywhere have Mute too. Muting takes the muted music out of what is queued up next, moves on from a muted
  song that is playing (not on live radio, where the song carries on and the station leaves it out from then on), and takes
  it out of the Meloday on show.
- **Settings > Muting**: how long a mute lasts (1 week, 1 month, or a number of days of your own) and the list of what is
  muted now, each with the days left and an Unmute button.
- **Now Playing fits.** The format and bitrate ("MP3 · 320 kbps"), which were cut short beside the buttons, now sit between
  the two times, and the actions are in two tidy rows: the rating with Mute, then the visualizer, curator, radio buttons
  on the left and lyrics, sleep timer and cast on the right.

## 0.54.0 — 2026-10-07

- **Lyric Studio shows what the space bar is doing.** The word being pressed for is shown large; when you press and hold,
  that word stays the active one (lit, with a bar under it that fills the longer you hold, and a hint that says to let go
  when the word ends) and the next word is only asked for once you let go. Before, holding moved the display on to the next
  word while timing the previous one. In the lines, the word being held is the brightest, words done are dimmest.
- **Timing never carries you off the song.** While a song is being timed it neither starts over nor moves to the next song:
  at its end playback stops, still on that song, and you are asked whether to save the timing (Save timing, Don't save, or
  Keep timing). The same question, with playback paused, is asked if you press Back, go to another page, choose Change the
  words, or move to another song while some of it is tapped.

## 0.53.0 — 2026-10-07

- **Lyric Studio is played with the space bar alone**: press it as each word begins, and keep it down through a word that is
  held on a long note. A held word is kept with its length, and the lyric pictures show it in the artwork's colour,
  swelling a little, until it lets go. (Backspace, Left and Enter no longer do anything; Undo, Back 5 s, Pause and Tap
  remain as buttons, and holding the Tap button with the mouse holds the word too.)
- **Breaks in the singing are clear of text** in the lyric pictures: in the intro, an instrumental passage or the end,
  nothing shows, not even the song's name. A song with no lyrics at all still shows its name and artist.
- **The lyric pictures are set in Manrope only.** The other five typefaces (Space Grotesk, Anton, Cormorant Garamond,
  Bebas Neue, Fraunces) are gone, with their files and licences, which also makes the installer smaller.

## 0.52.0 — 2026-10-07

- **Lyric Studio**: time a song's lyrics yourself. Open it from a song's menu (Time Lyrics) or the timer button on the lyrics
  panel. The song plays from the start, the next word is shown, and you tap the space bar (or the Tap button) as each one
  begins; Backspace takes a tap back and replays the moment before it, Left goes back five seconds, Enter plays or pauses.
  Tap for every word (exact) or only every line (quicker; the words in each line are spaced for you). You can correct the
  words first, or paste them in for a song that has none. The timing is kept on this computer and replaces the source's
  for that song in the lyrics panel and the lyric pictures, and "Use original timing" takes it away.
- **Shift a song's lyrics**: the lyrics panel's timer button moves a song's lyrics 0.1 s earlier or later at a time, for a
  version of the song (another master, a longer intro) the timing wasn't made for, and undoes the shift.
- **Better guessed timing** for lyrics timed only by line: words are spaced by their syllables rather than their letters
  (so "beautiful" takes three beats and "strengths" one), the last word of a line is held, a comma or full stop leaves a
  pause, and a line in a slow song fills the time it has instead of finishing early. Lyrics with a time for each word are
  untouched.
- Not done: snapping words to the song's beats, or aligning words to the audio itself. Edits are never deleted: taking
  one away writes it empty.

## 0.51.1 — 2026-10-07

- **Backdrops and logos on playlist, album and collection pages are laid out as the artist page lays out an artist's.**
  The backdrop is the 16:9 picture flush with the right edge (the full width when the window is narrow), dissolving to
  the left and downward into the page's UltraBlur wash, which now stays behind it; it moves up with the page as you scroll
  instead of staying fixed under a dark shade. The logo takes the artist logo's size (smaller once the header condenses)
  and its colour treatment: in the page's colour with Colour Drop, in its own colours otherwise.

## 0.51.0 — 2026-10-06

- **Backdrops and logos come from the Plex server**, as artists' do. A playlist, album or collection shows the backdrop
  its Plex item has across the top of its page, and its logo (Plex's clear logo) in place of its name. This replaces
  0.50.0's pictures kept only on this computer, which are gone.
- **Edit the artwork from Meloday**: a playlist, album, artist or collection's menu has Artwork (cover or photo,
  backdrop, logo): choose one of the images Plex offers or upload your own, and it is saved on the server for every
  app. Albums, artists and collections also have Edit Details for their name and description (playlists already did).
- An artist's logo on Plex is used before fanart.tv's, unless a logo was chosen here.
- Not exercised against a real server in testing: saving artwork and details writes to Plex, so those paths were built
  and unit-tested (the requests they make) but never run. Showing the backdrop and logo was checked live.

## 0.50.0 — 2026-10-06

- **Folders** in the sidebar (under Collections): the library as it is on disk. The top of it lists its folders; each
  opens into the folders and songs inside it, with the path above its name, and a folder with songs has Play and
  Shuffle. (Plex lists a folder's folders as untyped items, which is what the page reads.)
- **Mixes that sound like a song** are now called "<Song> Mix" (no "Sounds Like"), and their covers show the song's album
  in the middle and the albums of the mix around it, instead of the artists' photos.
- **Mix cards** (More for You and the rest) are named on their cover, bottom left, as other covers are: whole, wrapped,
  and cut short only if very long. The circles sit higher to leave room, and pinned mixes' still covers are drawn the same way.
- **Time-of-day and seasonal shelves** also hold the playlists named for them: a small dictionary of words and phrases
  ("morning", "waking up", "rise & shine", "evening", "chill", "sunset", "late night", "can't sleep"... and for the
  seasons "summer", "beach", "road trip", "snow day", "christmas", "halloween", "fall vibes"...) matched as whole words
  in the playlist's name. Such a shelf is shown even when your listening history is thin.
- **Browse** can sort its categories (as Default, A–Z, Z–A, largest or smallest first), and the albums of a genre, mood,
  style or decade (the hub's sub pages) can be sorted like the Albums page. The hub pages themselves are as they were.
- **Library pages** (Artists, Albums, Playlists, Collections) switch between a grid and a list (the capsule beside Sort);
  each page keeps its choice.
- **Playlist pages**: a circle with the search icon opens into a field (as the global search does) that searches only
  that playlist's songs by title, artist and album; every playlist, smart or not, can be viewed sorted by title, artist,
  album, recently added, most played, rating or duration (a view only: the playlist's own order is untouched, and
  dragging to reorder is offered only in that order).
- **Playlist backgrounds**: "Set Background..." on a playlist's menu puts a picture of your own behind its page, fading
  into it under a shade for legibility ("Remove Background" takes it away). It is kept on this computer, never sent to Plex.

## 0.49.0 — 2026-10-06

- **AutoMix gets more of what Spotify's Mix does.** It already matched tempos (stretching a song up to 8%), lined the next
  song up on a bar, swapped the bass between the two and picked how long to blend by key and energy. Now each join has a
  style: a pair that fits perfectly is still blended long with the bass changing hands; any other beat-matched pair gets a
  **filter sweep** (the outgoing song loses its lows and then its mids as it goes, while the next one, dull at first,
  opens up); and songs whose tempos can't be matched get an **echo out** (the outgoing song repeats itself, fading, to its
  own beat) instead of a plain fade. The log says which was chosen and why.
- **Headliners is programmed like a flagship radio show**, not artist after artist: artist blocks, runs of new releases
  (albums from the last year and a half) and of what's trending on Last.fm (when that is switched on), and genre blocks
  (a few songs of one genre by different artists). It draws on a fresh sample of your hundred and twenty most played
  artists and the thirty played lately (it was always the same forty), and artists it has featured are rested for a day
  and a half, remembered between runs of the app, so it no longer opens on the same artists every time.
- **The visualizers all use the artwork's four colours** (the same ones the window's backdrop is made of): Lyric Glow's
  ground is one and its three clouds are the other three, the Sun's four rings are one each (darkest outermost,
  lightest at the core), and M-Wave, Halo and Bars run through them too.
- **Lyric videos:** the type is smaller; words arrive across the line as sung again (as before 0.48.0), except where the
  lyrics time each word; the credits-style roll for untimed lyrics stays. A song reads in the same typeface and weight
  wherever it is shown (page or full screen), because the choice belongs to the song, not to the picture that shows it.

## 0.48.0 — 2026-10-06

- **Efficiency first.** The visualizers keep to what the computer can do: the picture is never drawn at more than about
  a 1080p screen's pixels (a 4K window is drawn at half size and scaled up by the compositor); frames wait on a timer
  of their own instead of raising the whole system's timer resolution; the drawing stops while the window is in the
  background; when Windows' energy saver is on it runs at 30 frames a second; and when frames cost too much (a weak
  computer, or software drawing with no graphics card) it steps down on its own, to 30 frames a second and then to a
  smaller picture, coming back up after twenty calm seconds. The backgrounds and brushes of the lyric pictures are made
  once, not each frame, and only the part of the screen a cloud reaches is painted. On this computer a frame costs
  about half a millisecond (M-Wave a little under one). The drawing uses only features of Windows 10 (version 2004)
  onward (not tried on Windows 10 itself yet).
- **Ridgelines is gone** (a saved Ridgelines choice becomes M-Wave), **Corona is now Halo**, and **Meloday Wave is M-Wave**.
- **M-Wave's colour pulses**: on every beat, and at the start of every line of the lyrics, a brighter band of colour
  runs through the line from the left. The line is also a little calmer.
- **Full screen** is on the same backdrop as the rest of the interface (not a darker shade), with the interface itself
  out of the way, and **Esc leaves it** wherever the focus is.
- **Lyrics:** the beat-keeping pulse of the type is gone (the fades and arrivals stay). With lyrics that are timed by
  the line only (nearly all of them) the whole line now arrives together, instead of words guessed at; lyrics that time
  each word (Plex's, where it provides it) still arrive word by word. **Lyrics with no timing at all roll up the picture
  over the length of the song, as credits do**, faded at the top and the foot.
- **The artists on a track are shown too**: for "Timbaland/Keri Hilson/D.O.E." or a "(feat. ...)" in the title, each
  other artist the library has (up to three) adds their backdrop and portrait to the pictures cycled behind the lyrics.
  Artists the library doesn't have are skipped; each is looked up once and remembered.

## 0.47.0 — 2026-10-06

- Visualizers are **really transparent** now. They were drawn on Win2D's animated control, whose swap chain showed as an
  opaque black square whatever the clear colour; they are now drawn onto a composition surface (with its own 60 fps
  drawing thread) shown over the page, which keeps its alpha, so the page's own backdrop shows through. Checked in the
  running app this time.
- The **Meloday Wave** flows from left to right, and follows the music: what is heard enters at the left and travels
  across, the waves themselves travel the same way at a pace set by the song's tempo (once the beat is found), the
  bass, low mids, voice and highs each carry their own wave, and every beat gives the line a soft swell. Gentler
  overall.
- **Lyric videos:** no drop shadow on the words (they sit in the picture), nothing of the next line shows before it is
  sung (the faint ghost words are gone), and the line before fades away completely before the next one begins. The Lyric
  Video now cycles through the **artist's backdrop, the album's cover and the artist's portrait**, each drifting and
  breathing, crossfading every fifteen seconds; the square ones are softened to a haze so they fill the screen
  gracefully.

## 0.46.0 — 2026-10-06

- **Dupes**, for Meloday: when two songs are the same song ("Sexy Chick" and "Sexy Bitch", a single and its album
  version), press the merge button on either one's row. A window asks which song it is the same as (pick it from the
  Meloday, or search the library), then lays the two side by side, with artwork, title, artist and album, and asks which
  one should play when the song comes up. Merge, and the two rows animate into one. From then on every Meloday holds
  the song you chose wherever either would have turned up, never both. Pairs are kept on this computer (per server),
  can't chain, and are listed in Settings, in the new **Dupes** tab, each with an Unmerge button.
- **Last.fm**, two switches, both off until you turn them on once connected: **Scrobble what I play** (nothing is sent
  while it is off; turning it off also drops anything waiting to be sent) and **Show what's trending** (Home gets a
  "Trending on Last.fm" row: the songs of Last.fm's global chart that are in your library; only the chart is read,
  nothing about you is sent). Those who scrobbled before keep scrobbling until they choose otherwise.
- **Lyric videos set in six typefaces**, chosen for the song: Manrope (the app's own; pop and R&B), Space Grotesk
  (electronic and indie), Anton (hip-hop and rock, tall and heavy), Cormorant Garamond (ballads and soul, a light
  serif), Bebas Neue (dance and fast songs, all capitals) and Fraunces (jazz and folk, a warm serif). The song's genres
  and moods pick the face (the tempo, once found, breaks ties and settles songs with no tags), and the weight follows
  the energy of the music, light in the quiet and heavy in the loud, on faces that have weights.

## 0.45.0 — 2026-10-06

- The **Meloday Wave** is loose and liquid: not the raw waveform (which is all edges) but a few slow waves laid over one
  another, each carried by its own part of the sound (the bass swells the long one, the highs ripple the short ones) and
  easing to the music like a fluid, still with the logo's thick line, glow, echoes and shifting colour.
- **Ridgelines** glide back smoothly between moments instead of stepping, and every visualizer now draws at 60 frames a
  second (it was 30), at a cost of a few percent of one core.
- Visualizers no longer sit in a filled square: they are drawn on a transparent ground over the page's own backdrop
  (where the artwork was).
- Two **lyric videos**, after the ones artists put out: the line being sung in big type, each word arriving as it is sung
  (timed across the line), the line before it lifting away, everything kicking with the beat and the pace following the
  tempo (found from the bass as it plays). **Lyric Video** has the artist's backdrop drifting and breathing slowly behind
  the words under a wash of the artwork's colour; **Lyric Glow** a lively gradient of soft clouds of the artwork's colours.
  Between lines, and for songs without timed lyrics, the song's name and artist are set the same way.
- **Full screen** for every visualizer (the menu's "Full screen", F11, or a double-tap on the picture): the window goes
  full screen with the picture filling it, and after three seconds of stillness the controls fade away, leaving only the
  picture. Moving the mouse or pressing a key brings them back; Esc or a double-tap leaves.

## 0.44.0 — 2026-10-06

- The waveform visualizer is now the **Meloday Wave**: the sound as the thick rounded line of the startup animation,
  with a soft glow, fading echoes trailing it and a colour that shifts along its length (neighbours of the artwork's
  colour), its ends settling to the middle and, in silence, coming to rest as the flat bar the animation starts from.
- The spokes visualizer, "Radial", is now **Corona**.
- The orbs are replaced by **Sun**: the four concentric rings of the Meloday playlist's cover (the same sizes and
  opacities, breathing as they do), in the artwork's colour, each ring swelling with its own part of the sound (the core
  with the bass, the outer rings with the mids and highs).
- A visualizer chosen under its old name (Radial, Orbs) still reads from saved settings.

## 0.43.0 — 2026-10-06

- **Visualizers** on Now Playing: a button beside the rating (the waveform icon) chooses Bars, Radial, Waveform,
  Ridgelines or Orbs, drawn in the artwork's place from what the speakers are playing, in the artwork's own colour on a
  dark shade of it. They are Meloday Music's own drawings of the long-loved classics (the spectrum analyser, the
  oscilloscope, the circular spectrum, the ridgeline plot, drifting orbs), made to be light: the sound is only copied
  on the audio thread, analysed and painted on Win2D's own thread at 30 frames a second, and nothing is drawn while none
  is chosen, the music has stopped and settled, the window is in the background or Motion is off (about a tenth of
  one core while showing). Off until chosen. They need this computer's own mixer, so choosing one while Windows' player
  has the song moves it to the mixer at once; they don't show while casting.
- Live radio can't be paused. Its play button is a stop button (a square); stopping leaves the station on the air without
  you ("OFF AIR · station"), and the button then says "Tune in": pressing it joins the station where it is by then, never
  where you left it.

## 0.42.0 — 2026-10-05

- The live stations are back on their moving covers, their name top right and "Meloday Live" bottom left. Headliners,
  Hits, Library and Deep Cuts keep a colour field each; Club and Chill share a new neutral (grey) field with a colour
  laid over it, video and still alike.
- Meloday One is now **Headliners** (it plays an artist at a time), and the live stations are named Headliners Radio,
  Hits Radio, Club Radio and Chill Radio (pins made under the old names read with the new).
- **Not for this station**, on live radio: a thumbs-down in the player bar and on Now Playing (where the guest
  curator's button is, which live radio has no use for) takes the song on off that station for good. The song finishes,
  as live radio can't be skipped, then never comes back to that station (it stays welcome on every other). Pressed
  again, it lets the song back on; Settings > Playback > Live Station Feedback counts them and resets them all.
- The guest curator's button moves from the queue panel to the player bar (and Now Playing).
- Sound Check no longer starts a song loud and then turns it down: the song's level is looked up as it loads and
  it opens at that level (where Plex is slow, it opens turned down and eases up, never down).
- The Mix panel and mix styles of 0.40.0 are removed; AutoMix is as it was.

## 0.41.0 — 2026-10-05

- The live stations are now Meloday One, Hits, Club and Chill, with Library Radio and Deep Cuts after them (six
  in all, shown as the regular square cards). Each has its own logo: a big wordmark on a warm white card, a LIVE
  mark and "Meloday Radio" beneath, in the app's own typeface; they replace the colour fields.
- **Meloday One** replaces Your Day. It plays an artist at a time: three of an artist's biggest songs back to
  back (what you play and rate most, helped by how widely each song is known), then the next artist, never one
  featured lately. While a block is on, Now Playing features the artist, with their photo and "Song 2 of 3".
- **Meloday Hits** replaces Time Travel: songs from albums of the '80s, '90s and 2000s in your library, the best
  known first.
- **Meloday Club** plays your library's dance, electronic and club genres; **Meloday Chill** its ambient, downtempo
  and lo-fi genres and the artists whose moods are quiet ones. A library that has none of a station's music isn't
  offered the station.
- Now Playing says "LIVE · station" above the song on any live station.
- Anything pinned of Your Day or Time Travel becomes Meloday One or Meloday Hits. The large station card layout
  (StationBoard) is gone; the live row uses the same square cards as the rest of the Radio page.

## 0.40.0 — 2026-10-05

- A Mix panel (the new waveform button in the player bar, or Ctrl+Shift+X) draws the next transition as the
  mixer is going to make it: the song that is ending above, the one coming in below, every bar as tall as the
  music is loud there (beat by beat, from the analysis of the songs themselves) and as loud as the mix has it, so
  you see one fade away and the other rise. For a beat-matched blend the bars are numbered, a band marks where
  both songs play in full, a dashed line marks where the bass changes hands, and a playhead follows the music
  through it (a blend that has begun stays on screen until it's over). It says what it is ("16-bar blend", "Soft
  fade · 13 s") and why, such as the tempos it matched or that two songs were too far apart to. What you see is
  the real plan, not a picture.
- You choose how songs blend, from the panel or from Settings > Playback > Mix style: **Auto** (each pair gets what
  suits it), **Long blends** (as long as two songs sit together at all, up to sixteen bars, even where the songs'
  singing isn't known), **Quick cuts** (a bar or two, on the beat) and **Soft fades** (no beat-matching: each song
  fades out the way its ending allows). The next transition is planned again the moment you choose. When AutoMix
  is off, the panel says so and turns it on from the next song.

## 0.39.1 — 2026-10-05

- A promo video of Meloday Music (56 s, 1080p at 60 fps) and the code that makes it, in `tools/promo`: real
  footage of the app (Home, Made for You, Meloday through its seven skies, the live stations' own moving
  artwork, AutoMix's sixteen-bar blend drawn out, Now Playing), cut to the bar lines of an original
  soundtrack that is synthesized by the same code. The app itself is unchanged in this version.

## 0.39.0 — 2026-10-05

- The AI DJ is removed completely: its Settings tab, its setup, its saved phrases and its voice are gone from
  the app. (Anything it saved on this computer, in `dj` and `cache\dj` under `%LOCALAPPDATA%\MelodayMusic`
  and `dj.bin`, is no longer used and can be deleted.)
- Live radio is live. On Your Day, Library Radio, Deep Cuts and Time Travel the songs can't be skipped,
  sought or restarted: Previous, Next and the position bar are off (the player bar says "LIVE · station"), and
  so are the media keys, Windows' media controls, remote controls, lyric clicks, shuffle, and anything that would
  rearrange or add to the queue. Pausing is allowed, but after a short pause the station is rejoined where it is
  by then rather than resumed from the past. Playing anything else replaces it as before.
- Guest curators, after Plexamp's guest DJs: for any playback except live radio, choose how what plays next is
  arranged from the new button in the queue panel. Double Up (two songs from each artist), Twin (stays close to
  what's playing), Drift (starts close, then slowly wanders), Time Machine (oldest to newest), Smooth (each song
  flows into the next by tempo, key and energy: the old "Optimize flow") and Spread (never the same artist twice
  in a row). The curator you choose stays until you change it and arranges what you queue and what a radio or mix
  adds as it goes. It only ever reorders: the song playing never changes, and nothing is added or dropped.
- AutoMix can blend up to sixteen bars now, for a pair that fits as well as two songs can: the same key or a
  fifth away, tempos within 3% (so the stretch is never heard), the same energy at the join, and room for it
  (the songs' singing kept apart where it's known, and the next song analysed that far in). Such a blend takes its
  time at both ends: the next song is up by bar six, both play in full from there to bar ten with the bass
  changing hands in the middle, and the outgoing song is gone by bar sixteen. Any other pair blends as before
  (eight bars at most), and a long blend never begins before the middle of a short song.

## 0.38.1 — 2026-10-05

The DJ no longer holds the music up.

- The DJ's voice now plays through the music's own output, not a second one. Before, each line opened a
  separate audio stream on your sound device and turned the music down by changing the device's volume in a
  few steps from the interface thread; that's gone. The line is added to the music inside the mixer, which
  eases the music down under it and back up smoothly, so there is no stepping and nothing else to start up.
- All the heavy work of a line (the voice engine, turning the sound into what the mixer plays) happens
  before the line is needed, on a thread of its own at low priority, so it can't compete with the music for
  the processor. The Windows voice engine is set up once instead of for every line.
- A spoken line is kept in small blocks instead of one big piece of memory. Big pieces bring on the
  full memory clean-ups that hold the whole app, and the music with it, up for a moment.
- Skipping to the next song on a live station (rather than letting it end) no longer loses the DJ's welcome.
- If anything is slow, the log now says how long each step took (voice made, line decoded, ready).

## 0.38.0 — 2026-10-05

The DJ is now the host of the live stations, like a real radio DJ.

- It only speaks on the live stations (Your Day, Library Radio, Deep Cuts, Time Travel). What you play
  yourself (albums, playlists, mixes, a radio you picked) is left alone.
- Tune in and the song that's on plays by itself. As the next one begins, the DJ welcomes whoever has just
  tuned in and says where they are ("You're listening to Library Radio on Meloday Music"), then speaks every
  few songs: looking back at the song just played, leading into the next, a station ID, a word about the time of
  day. It speaks to everyone listening, never to you by name, and has a radio host's rhythm and manner (warm,
  upbeat, calm or dry, as you choose). The phrases are rewritten the first time after this update (one request).
- It no longer lags or talks over the first song. The line, and its voice, are made while the song before it
  plays, so it begins the moment the song changes over; if it isn't ready by then it stays quiet instead of
  speaking late. (Making the voice at the start of a song also held the window up for a moment; that's gone.)
- It works with AutoMix and crossfade. Before, a changeover made by AutoMix's mixer wasn't noticed at all,
  and one that began part-way into the next song was taken for a song joined in the middle, so the DJ stayed
  silent. It now talks over the changeover, with the music dipping under the voice.

## 0.37.0 — 2026-10-05

- A new, optional AI DJ (Settings > DJ): a voice that greets you when you start listening and, every few
  songs, says what's coming up over the start of the next song, with the music dipping under it. It never
  speaks over a live station you've just joined, when casting, or while paused, and if anything isn't ready
  it stays quiet and the music plays on.
- Built to cost next to nothing. All of the DJ's phrases for a style are asked of the AI in one request, with
  blanks for the song, artist and moment, and saved: filling them in is free, so the DJ talks all day from
  one request. Each spoken line is kept as a sound file, so it's only voiced once. Optionally, now and then, a
  short remark about the coming song is asked for while the song before it plays (a few cents per hundred),
  and each remark is kept so it's never asked twice. Nothing about your listening is sent, only a song's
  title, artist and album for a remark.
- Setup is made for someone who has never done it. Three choices with step-by-step instructions and links:
  OpenAI (account, a few dollars of credit, one key; the key is shared with Meloday's AI titles), an AI on your
  own computer (install Ollama, one command, then "Find my AI": free, and nothing leaves your PC), or any
  other service that works like OpenAI's (OpenRouter, Groq...). "Test the DJ" tries everything and says in plain
  words what's wrong if it fails (a key that wasn't accepted, an account out of credit, Ollama not running).
- Choose the DJ's style (warm, upbeat, calm, dry), how often it talks, how often it makes a remark, and the
  voice: Windows' own voices (free) or OpenAI's natural voice (about a cent for six or seven lines). Settings
  shows what's saved and what it has cost so far.
- Meloday's mood and era maps are checked: every Meloday name is built from the bundled mood map (a descriptor
  from the mix's dominant mood) and era map (a nickname when one decade dominates), matched without regard to
  case, and a test confirms it.

## 0.36.0 — 2026-10-05

Meloday, mixes, live stations and radios now each have their own job and their own place.

- Live stations: Library Radio, Deep Cuts and Time Travel, and a new one, Your Day, are always on, as
  if they had been playing all along. Tune in and you join the song that is on, in the middle of it;
  the station carries on while you're away (it remembers its running order), so coming back later
  joins where it has got to. They say "Live" on their covers, in a "Live" row on Radio and on Home.
  Any other radio (a genre, an artist, the radio builders) still starts at its first song.
- Your Day replaces Meloday among the stations. It's named for the day ("Your Monday") and plays what
  you play at this time of day on this day of the week (over the last ten weeks, widening to
  weekdays or weekends only when there's too little to go on); as the day moves on it moves with it.
- Meloday is no longer a station: it's a playlist, so it moves to Made for You and is always the first
  card there and on Home. Made for You opens with "Top Picks": Meloday and the three best mixes as
  large cards (Home shows the same four). The other artist mixes are under "More for You".
- Mix covers: the two small artist circles now have a ring of the cover's colour round them so they
  stand apart, and on a pinned (still) cover they overlap the big circle more. Another mix's cover no
  longer shows the face of an artist who already has a mix of their own, so the covers look more
  different.
- "Neighbourhood" mixes are now "Sounds Like ..." ("Sounds Like Adventure of a Lifetime Mix").

## 0.35.0 — 2026-10-05

- Made for You is now its own tab in the navigation (the button on Home is gone), and the one place
  for everything Meloday makes for you. Its mixes sit in rows that say why they exist: Your Mixes,
  Right Now, Your Sounds, Discover, the day and week, By Energy, Artist Circles, and the albums and
  playlists that fit this season or this time last year. A mix appears in only one row. Home shows a
  taste of it: four mixes in the floating colour boards, and "Made for You" opens the tab. The
  extra shelves that used to crowd Home are gone from it (so Home also opens faster).
- A mix and a radio are now different things. A mix is a set of about 30 songs Meloday picks from
  what you play: it plays through and ends, and a fresh one is made each day. A radio starts from
  something you choose and never ends. So the mixes you build from artists or albums are now
  radios ("Artist Radio Builder", "Album Radio Builder", and "...Radio" in their names), and the Radio
  page says what a radio is.
- Mixes that were really the same one renamed are gone: a mix whose leading artists mostly repeat
  an earlier mix's is no longer made.
- Mix covers follow the radio-cover look: on Made for You each mix is a square card with the main
  artist in a large circle in the middle and two others from the mix in small circles, on a field
  taken from the main artist's photo. Pinned mixes use the same cover as a still.
- A fix behind the pins: with several mixes or stations pinned, a still cover could fail to draw
  because its colour was read from the wrong thread; it is now read safely.
- The documentation names every card layout (Tile, ColourBoard, SquareBoard, StationBoard,
  Billboard, EditorialBoard, TagCard) and says what each is for.

## 0.34.0 — 2026-10-05

- Made for You: a new page (button at the top of Home) with personal mixes built from what you play
  and when you listen: niche mixes by genre, mood and era, a Discovery Mix, and mixes for the time
  of day and the week. Home shows the same rows, and each recommended song says why it was picked.
- Optimize flow in the queue reorders what's coming up so songs lead into each other well for
  AutoMix; the order only changes the up-next list.
- A build-and-test check runs on GitHub for every push and pull request.
- These changes were made in a separate copy of the project and have been brought into this one.

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
