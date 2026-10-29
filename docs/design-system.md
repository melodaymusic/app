# Meloday Music design system

Meloday Music is built from one set of tokens and one set of components. A screen should never
restyle a control locally: if a page needs something the system doesn't have, add it to the system
(a token in `Services/UiScale.cs` or `Themes/Design.xaml`, or a component in `Controls/`) and use it
from there, so every screen gets the same thing. The reference is Apple's Human Interface Guidelines
and Apple Music on the Mac.

Search and editor fields use a neutral one-pixel outline around all four edges, with a brighter
neutral outline on focus. Do not add decorative coloured stripes beside or beneath controls or
surfaces. Nested rounded rectangles use outer radius = inner radius + padding. Detail artwork
aligns with the top of its header. Charts share the playlist layout and omit editing controls.

Settings uses eight sidebar sections, with the same choices in a dropdown when the sidebar no longer fits. Actions move below their labels when a row becomes narrow; dropdowns and credential fields stay within the available width. Home hub names remain above their reorder and visibility controls at minimum width. The main sidebar keeps Settings anchored while the other entries scroll.

## Tokens

### Type (Nunito, bundled)

| Style | Size / weight | Use |
| --- | --- | --- |
| `MelodayHeroText` | 48 ExtraBold (64 on artist pages) | Album, playlist and artist titles |
| `MelodayDisplayText` | 36 ExtraBold | Page titles (through `PageHeader`) |
| `MelodayTitleText` | 28 Bold | Titles in sign-in and setup |
| `MelodaySectionText` | 22 Bold | Section headings (through `SectionHeader`) |
| `MelodayHeadlineText` | 20 SemiBold | A title inside a card or panel |
| `MelodayLyricText` | 22 Bold, wraps | A line of lyrics: the line being sung bright and full size, the rest dimmer and slightly smaller with distance |
| `MelodayBodyStrongText` | 15 SemiBold | Emphasis, group headings, card titles |
| `MelodayBodyText` | 15 Medium | Reading text, row titles |
| `MelodaySecondaryText` | 14 Medium, secondary colour | Counts, metadata, captions |
| `MelodayParagraphText` | 15, 24 line height, secondary | Descriptions, notes, biographies |
| `MelodayOverlineText` | 14 SemiBold, secondary | Eyebrows ("Good evening", "Genre") |

Medium is the reading weight (Nunito's Regular is too light on glass). Nothing anyone reads is
under 14px. Title case for titles, headings, buttons and menu items; sentence case for descriptions,
setting labels and tooltips.

Subtitle links under big titles use `MelodayHeroSubtitleLinkStyle` (under a 48 title) or
`MelodayFeatureSubtitleLinkStyle` (under a 40 title): accent, SemiBold. "More" and "Read More" use
`MelodayMoreLinkStyle`.

### Spacing

2 · 4 · 8 · 12 · 16 · 20 · 24 · 32 · 40 · 48

- 2 between a line and its caption (title and subtitle, label and value).
- 8 between buttons in a row, and between a group heading and its group.
- 12 between a section heading and its content.
- 16 or 24 between blocks inside a panel or dialog.
- 32 between setting groups; 48 (`MelodaySectionSpacing`) between page sections.
- 40 page gutters (`MelodayPagePadding`, `MelodayPageHeaderPadding`, `MelodayGutterPadding`).
- Cards in rows and grids sit 20 apart: each card's container is padded 10 on every side.

### Radii (concentric)

A rounded shape inside another with padding p has radius outer − p.
Equivalently, **outer radius = inner radius + padding**. Radius tokens scale with interface size;
host radii add the actual scaled padding after rounding. Row and grid hosts have separate tokens
because fractional row gaps can produce half-pixel padding. Hover, selection and focus use the
same computed host radius. Artwork placeholders and decoded artwork share the same shape.

| Radius | Token | Use |
| --- | --- | --- |
| 4 | `MelodayRadiusThumb` | Track thumbnails |
| 8 | `MelodayRadiusArtwork`, `MelodayRadiusControl` | Artwork and text fields |
| 12 | `MelodayRadiusLargeArtwork`, `MelodayRadiusRow`, `MelodayRadiusGroup`, `MelodayRadiusNavItem` | Hero artwork, track rows (4 + 8), grouped lists, navigation inside glass (20 − 8) |
| 16 | `MelodayRadiusBoard` | Boards, tag cards and glass panels |
| 18 | `MelodayRadiusTileHost` | Card containers and their hover plate (8 + 10) |
| 20 | `MelodayRadiusGlass` | Sidebar, queue and player bar (player: 8 + 12) |
| 26 | `MelodayRadiusBoardHost` | Board containers (16 + 10) |
| height / 2 | `MelodayRadiusPill` | Every button is a capsule |

### Sizes

- Controls: 40 high (`MelodayControlHeight`); icon buttons 32 / 36 / 40 / 44 / 48 / 56 / 72.
- Icons: XS 14 (beside a caption), S 16 (in labelled buttons), M 18 (icon buttons, rows),
  L 20 (navigation, section chevrons), then 22 / 26 / 30 / 36 for transport and empty states.
- Stars: `MelodayStarS` 14 (rows, player bar), `MelodayStarM` 18 (headers), `MelodayStarL` 22 (Now Playing).

### Colour

- Theme brushes (`MelodayTextPrimaryBrush`, `MelodayTextSecondaryBrush`, `MelodayTextTertiaryBrush`,
  `MelodaySubtleFillBrush`, `MelodayDividerBrush`, ...) follow light and dark.
- On colour (`MelodayOnColorPrimaryBrush`, `...Body`, `...Secondary`, `...Tertiary`, `...Fill`,
  `...Panel`, `...Hairline`, `...Edge`): text and controls over artwork or a coloured page, always
  light, at fixed strengths. Never write a colour value in a page.
- The accent follows the page's artwork (`Accent`); buttons, links and selection use it.
- `MelodayChipSelectedBrush`: the chosen chip's fill (Browse's sliding category pill), the same strength
  as a selected sidebar item.
- Materials: `MelodayGlassBrush` for floating panels (queue, search), `MelodayPlayerAcrylicBrush` (a
  `LiquidGlassBrush`: blurred, slightly saturated backdrop under a tint, no acrylic grain) for the player
  bar and the sidebar, `MelodayOverlayGlassBrush` for controls floating on artwork. The queue and lyrics
  share one side panel (`PanelService`): opening one closes the other.

## Components

| Component | Use it for |
| --- | --- |
| `PageHeader` | Every top-level and list page's header: eyebrow, title, count or description, actions (move under the title when narrow) |
| `DetailHeader` | Album, playlist, collection and Meloday headers; the item's Plex logo (`PlexLogoService`, shown in an `ArtistLogo`) stands in for its name when it has one |
| `MuteButton` (Now Playing) | Mute as a secondary capsule (`MelodaySecondaryButtonStyle`, `BellOff` icon, "Mute", or "Muted" while the song or its artist is) beside the rating, opening a text-only menu: mute the song, mute the artist (each for the Settings length), Another Length (`MuteDialog`), Muted Music (Settings > Meloday); a muted song or artist shows a disabled line saying how long is left and an Unmute item |
| `LyricStudioPage` | Timing a song's lyrics by hand: a glass panel for the words (text box, "tap for each word / line", Start timing), then a glass panel with the line being tapped in `MelodayHeroText` (words done, next underlined in the accent, to come), the primary Tap button and secondary Pause, Undo and Back 5 s buttons; Save timing is the page header's one primary action. The space bar is the only key (pressed as a word begins, held through a held word), handled on the page's tunnelling `PreviewKeyDown`/`PreviewKeyUp` so a focused button never also clicks |
| `BackdropLayer` | The Plex backdrop of a playlist, album or collection laid out as the artist page's hero image (16:9, flush right, `FadedArtwork` fading left and down into the UltraBlur wash, moving with `ScrollSource`); collapsed with none |
| `SectionHeader` | Every section heading, with an optional subtitle, trailing text, or a standard trailing "See More" secondary capsule; actions wrap below the heading when needed |
| `ActionWrapPanel` | Header actions retain their order and target sizes, wrapping into another row when needed |
| `MediaRowControl` | Horizontal rows of cards (tiles, boards, billboards) |
| `MediaTile`, `SquareBoard`, `ColourBoard`, `Billboard` | Cards; all share `CardHover` |
| `Billboard` with `IsEditorial` (row style `EditorialBoard`) | An editorial board, after Apple Music: heading (eyebrow, title, subtitle) above the 16:9 backdrop, a short line on it, the album cover bottom left (clear of the play button) |
| `TagCard` | Genre, mood, style and decade boards (Browse and search): three of your most played albums cascaded, the card in the front album's palette colour |
| `CardSlot` | Every card in a grid: insets the card and holds its hover plate (grid items clip anything reaching outside a stretched card) |
| `TagHubPage` | A Browse family (Pop, Chill & Calm): its genres, styles or moods as cards, with Shuffle and All Albums |
| `LayoutToggle`, `MediaListRow` | Grid or list for a library page: two icon buttons in one capsule (the chosen one filled, `MelodayChipSelectedBrush`) beside the Sort button, kept per page (`AppSettings.LibraryLayouts`); the list shows each album, artist, playlist or collection as a row (52 px art, name, what it is, a detail at the far end, a play button on hover) in `MelodayTrackItemStyle` rows |
| `PageSearch` | A search that belongs to one page (a playlist's): the circle `MelodayRoundActionButtonStyle` opens into a pill field with the cursor in it, as the global search does (`Motion.Emphasized`), folds back when left empty; Esc clears it |
| `TrackRow` | Every list of tracks, including the queue. On a list whose host sets `TrackListBehavior.RemoveCommand` it shows a remove (X) button on hover or selection, and where it sets `DupeCommand` (Meloday's page) a merge button beside it: "this is a dupe of another song" |
| `DupeDialog` | Marking a dupe (Meloday): step one finds the other song (the marked song's card, a search box, the Meloday's songs and library results, each with artwork, title, artist · album); step two lays the two songs side by side as large cards (artwork 190, title, artist, album; each a button, the chosen one gets an accent border and a "Plays" tag, the other dims) and Merge stays off until one is chosen; Back returns to step one. Then the page animates the lower row sliding into the upper one (`Motion.Deliberate`, 640 ms: slide, shrink, fade) while the upper one swells. Settings' **Meloday** section lists each pair (the song that plays, "in place of" the other) with Unmerge |
| `ProfileDialog` (Settings > Plex Account > Switch Profile) | Choosing who is listening from the Plex Home: each member with their Plex picture (initials when they have none) and a line saying who is listening now, Admin, Managed or Guest, and PIN; a member with a PIN gets a four-digit PIN box, checked with plex.tv (a wrong PIN says so in red and clears the box). Switching saves the queue and the rest as Quit does, then the app starts again as them. Each member keeps their own settings (started from the signed-in account's), queue, history, taste, pins, mutes, Meloday, radio and scrobbling sign-ins in `profiles/<their id>` (`AppPaths.Root`); sign-in, logs, keys, downloads and caches stay shared (`AppPaths.Shared`). The whole Home shares the signed-in account's Plex Pass. A member whose sign-in plex.tv later refuses is switched back to the signed-in account |
| `MadePlaylistCover` | A Made by Meloday playlist's cover (mood, energy and moment playlists; Genre Essentials keep their own art, mixes keep `ShapeCovers`): the photo of the first artist in the playlist who has one and isn't already on another Made by Meloday cover that week (the first with a photo if all are), filling the square, a dark fade over the lower half, the name bottom left in white, and the edge bars in the colour `PhotoColour` picks for the photo: mid-tone pixels clustered in Lab, saturated striking hues and big clusters first, small vivid accents and colours also round the edges lifted, skin tones passed over; a grey photo takes its edge accent or a palette neutral of the opposite brightness, a photo of one hue the opposite hue; always snapped to a palette colour. Redrawn when the playlist starts with someone else; the flat colour cover when there's no photo. Shown in the app only (a copy kept on Plex carries it too) |
| `TaskbarPreview`, `TaskbarButtons` | Pointing at Meloday Music on the taskbar: while a song is loaded, Windows shows a 16:9 picture of the artist's backdrop (the album cover when there is none) under a dark fade, with the song in bold and the artist below it bottom left, a thin white line along the bottom for how far in it is, and a PAUSED pill top left while paused, instead of a shot of the window (the peek over the window shows the same picture, full size); it is redrawn when the song changes or plays or pauses, and every five seconds while playing. Previous, Play/Pause and Next sit under it as the thumbnail buttons. With nothing loaded, the usual window shot |
| `ItemDetailsDialog` | The one editor for an album, artist, collection or playlist, as Plex has it: `ChipTabs` (Details, Cover or Photo, Backdrop, Logo) over a fixed-height body. Details is a title box and a summary box; each picture tab is a note and an Upload Image button above a grid of tiles (`MelodayTileButtonStyle`, art at `MelodayRadiusArtwork`, the chosen tile filled with `MelodayChipSelectedBrush`, a "Current" caption). Nothing is sent until Save (`Dialogs.Wide` makes the dialog 680 wide). There are no other places to change a picture or a name |
| `SearchPicker`, `Dialogs.MediaRow` | Finding one thing from a dialog (Radio Builders, Sonic Adventure): a text box with the matches in a `MelodayTrackItemStyle` list under it, each row a 40 px picture, title and detail. What is chosen sits in a grouped list (`MelodayGroupListStyle`, rows `MelodayGroupRowStyle`) with a trailing remove or Change button; suggestions are secondary capsules with an `IconLabel` |
| `EssentialsPage`, `EssentialsService`, `EssentialsCover` | An artist's Essentials: their 25 most popular songs by Plex's popularity score (`Essentials.Pick`, ranked, most popular first), made on the artist's page only: it leads the rail "More With This Artist" (never labelled smart; rebuilt when the page opens and every six hours). It opens on a `DetailHeader` (read-only) with `PageSearch` (Essentials stays local; there is no Plex save button or background sync), over a ranked `TrackRow` list. Its cover (1000-unit square, `CoverType` text boxes): white above the join, the backdrop's most common colour tinted below, the 16:9 backdrop 40 in from the sides and centred on the join, "Essentials" top right in black, the artist's name bottom left on one line (shrunk to fit) in black or white by contrast |
| `TrendsPage` | The listener's listening stats (sidebar, after Radio; Settings > General hides it). `ChipTabs` choose the period (This Week, This Month, This Year, All Time). Sections: a recap panel (`MelodayGlassFillBrush`, `MelodayRadiusBoard`: time listened in `MelodayHeroText`, change from the same point in the previous period with an accent arrow, headline numbers in `MelodayTitleText`); up to three trait cards (accent circle icon); top artists, albums and songs as ranked `MediaRowControl` chart rows; `BarChart`s by hour, weekday and period; highlight tiles; first-time artists; and `ListeningCalendar` |
| `BarChart` | A one-series column chart: accent bars two pixels apart with 4 px rounded tops on a divider baseline. The tallest bar is full strength and the rest half strength. Each whole column is a tooltip target that brightens on hover; labels are thinned with `LabelEvery`. It has no legend because the section heading names the series |
| `ListeningCalendar` | A day grid (weeks across, Monday at the top, ending this week; up to a year that fits the width). Empty days use `MelodaySubtleFillBrush`; listening days use four accent strengths scaled to the busiest day, with a Less–More key. Month names sit above the weeks, and each day has a tooltip |
| `SoundFingerprintDialog` | A song's Meloday Engine data (track menu > Sound Fingerprint), set out as Song Info is: a header (72 art, title, artist · album, length), then sections in `MelodayBodyStrongText` with a note: Fingerprint (`FingerprintView` beside label/value facts), Where ME Listened (`ListenTimeline`), What ME Hears (label/value rows as the facts are: the group in `MelodaySecondaryText`, its three words with confidences side by side, the first in `MelodayBodyStrongText` when ME is sure of it, all `MelodaySecondaryText` when it is only a lean; a caption with the vocabulary's size and revision), Tempo, Key and Beats (two `BeatGrid`s, opening and ending, the tempo in `MelodayTitleText`), Sounds Closest To (rows: 40 art, title, artist, match percentage over a progress bar). Analyse Now when something is missing |
| `SearchPage` Sounds Like | A description search ("rainy day jazz") when every word is one ME knows: after Tracks, a `SectionHeader` titled with the words read ("Sounds Like Rainy Day · Jazz") and a subtitle saying it is ME hearing them, over a `TrackRow` list in `MelodayTrackItemStyle`, as Tracks is |
| `MoodTagDialog` | Tag an album's moods in Plex (album menu > Tag Moods in Plex), wide dialog: a note on what will change, then a row per song: title in `MelodayBodyText`, its moods in Plex and what ME would add (with confidence) in `MelodaySecondaryText`, the change in `MelodayBodyStrongText`. Primary names the count ("Add Moods to 9 Songs"); secondary Remove Meloday's Moods when Meloday has tagged any. The whole library is in Settings > Audio Analysis > Moods in Plex (Tag Whole Library, Remove All with a danger confirm, Stop while running) |
| `FingerprintView` | ME's 512 numbers as a ring of spokes from an inner circle (the sun motif), clockwise from the top, length by strength: positive in the accent, negative in `MelodayTextTertiaryBrush` (the two sides of zero), inner circle `MelodayDividerBrush` |
| `BeatGrid` | An analysed stretch's beats as ticks on a divider baseline, height by loudness (0 to −30 dB), bar lines full accent and others half, start and end times under it; bar lines have tooltips |
| `ListenTimeline` | A whole song as a `MelodaySubtleFillBrush` bar with the stretches listened to on it: ME's samples in the accent (full height), the opening and ending in `MelodayChipSelectedBrush` (inset), each with a tooltip; 0:00 and the length under it |
| `PageStateView` | Loading (skeletons), error with Try Again, and empty states (icon, title, text) |
| `IconLabel` | The content of every button with an icon and a label |
| `CoverLabels`, `CoverTextLayer`, `CoverType`, `CoverFit` | The typography of every cover Meloday Music draws itself (Meloday's, every station's, every mix's, still covers), in a 1000-unit square: the name right-aligned top right (92, ExtraBold, its box 760 wide with its right edge 110 in and its top 100 down, up to four lines) and the label bottom left (87, Bold, its box 760 wide, 110 in from the left, its bottom edge 100 up, growing upward over up to four lines; a mix's name is its label). Always those places and sizes; a text too long for its box is set just small enough to fit it (`CoverFit`, never below 40%), never trimmed. The edge bars (`CoverEdge`): a bar 36 high along the bottom, and a left bar 18 wide (half the bottom bar) and 120 tall whose bottom sits exactly on the label's last baseline (the bottom of its letters, not their tails; y 880 at full size, lower when the label is set smaller), both a shade darker than the ground (lighter on a family's darkest). Live (`MelodayArt`, `CoverLabels`, `MixCover`) or as a picture (`MelodayCover`, `StaticCovers`) they share these numbers |
| `StaticCovers` | A still cover for what moves (a station, mix, tag or Meloday) wherever a list shows a still: pins on Home and in the sidebar, "Jump back in" |
| Jump back in tabs (Home) | Cover (56) and name on a glass tab (`MelodayGlassFillBrush`, `MelodayRadiusControl`), two across beside the featured cover; the last thing played is the large one |
| `ShapeCovers` | The cover of a mix that isn't about artists (genre, mood, niche, sound, Discovery): a flat palette field (stable per mix, white text reads on it), two different shapes from `Assets/Shapes` (each two fifths to three quarters of the side, turned by quarter turns, at most 40% off the cover, at most half of either covered by the other, clear of the name where possible, the larger behind) in the family's two other shades (the higher shape in the edge bars' shade), then the name bottom left, the edge bars and the M-wave in the bars' shade. Shown as the card's artwork, on Top Picks boards, pins and Home |
| `GenreCoverArt` | A Genre Essentials cover: the genre art (`Assets/Genres`) filling the square, "Essentials" top right and the genre bottom left (up to two lines), each black or white by the art under it, the edge bars in the colour of the art along the bottom and the M-wave. Genre Essentials sit in their own Made for You row |
| `MixCover` | The cover of a mix about artists (artist mixes, artist circles, radios built from artists) on a square card (`MediaTile`): the colour of the main artist's photo as a flat field, that artist in a large circle in the middle and two others from the mix in small circles in opposite corners (the same two all day), each small circle ringed in the field's colour. `StaticCovers` draws the same cover as a still for pins, with the small circles overlapping the main one more. On Home a mix keeps its `ColourBoard` |
| `CuratorButton` (player bar, Now Playing) | An icon toggle, lit while a curator is chosen, that opens a menu of curators (radio items, each with its description as a tooltip): how what is playing now is arranged next; Bridge also lays five songs between each two queued ones, leading from one to the next (DJ Stretch). Starting anything else turns it off. Hidden on live radio, where `StationFeedbackButton` stands in its place |
| `StationFeedbackButton` (player bar, Now Playing) | Live radio only: a thumbs-down toggle, "Not for this station", lit while the song on has been taken off that station; pressed again it lets the song back on |
| Live badge (player bar) | On live radio the player bar shows a dot and "LIVE · station" ("OFF AIR · station" once stopped) where the time would be, and the previous, next and position controls are disabled; the play button is a filled square (Stop) |
| `VisualizerView`, `VisualizerButton` (Now Playing), `VisualizerStage` | The music drawn from the mixer's own output (`SpectrumAnalyzer`, `BeatTracker`) on a transparent ground, where the artwork was on Now Playing, in the artwork's colours (`VisualPalette`): Bars (a row of twelve wide rounded pills swelling from the middle, a gradient down each from the light tone to the deep one), Halo, M-Wave (the logo's thick line, made of a few slow waves that ease to the sound, with echoes and a colour that shifts along it, a brighter band running through on each beat and lyric line), Sun (the playlist cover's four rings, each swelling with its own part of the sound), Pillars (columns across the whole picture, more of them when it is wider, each a chain of rounded pills listening to its own part of the sound, bass at the left: a column's pills stretch and light up with it, rush along as it gets busier and jump on every beat) and Tiles (a patchwork of tiles packed on a square grid that fills the whole picture, so full screen gets more tiles, with a wave of the music sweeping across it in a line or out from a point: where the wave and the sound are strong, tiles round off, shrink or light up and their stripes run inwards; each tile listens to the part of the sound at its place along the wave; six looks, solid, outlined or striped, one fixed per song and changing on a beat every half a minute or so). Both measure each part of the sound against its own recent loudest and average (`BandFollower`), so they move to the song's hits rather than its overall loudness; their looks are drawn in the spirit of Maksim Vasilyev's Generative Art gallery, with none of its code, Lyric Video and Lyric Glow (`LyricPainter`: the line being sung in big type, set in Manrope alone (`LyricFonts`) at a weight that follows the energy, each word arriving as sung, timed across the line by `LyricTiming`, kicking with the beat and moving at its pace; over the artist's backdrop drifting and breathing, or soft colour clouds; the song's name and artist only when the song has no lyrics at all: in a break in the singing (the intro, an instrumental, the end) nothing but the picture shows; a word held on a long note, timed in the Lyric Studio, stays in the artwork's colour and swells a little until it lets go). 60 fps on Win2D's render thread; draws nothing when none is chosen, the music has stopped and settled, the window is in the background or Motion is off. The button is an icon toggle that opens a menu of the styles and "Full screen". All pictures draw with the artwork's four colours (`VisualPalette.Tone(0..3)`, from `ArtworkTones`: the UltraBlur corners, darkest first and brightened; the darkest, deepened, is the ground): Lyric Glow's ground is one and its three clouds the others, each of the Sun's rings is one, M-Wave runs through all four. A song's lyric weight is shared by every painter showing it. Pictures are drawn onto a composition surface (a sprite over the element; never a Win2D swap-chain control, which shows an opaque black square) on their own 60 fps thread. The Wave flows left to right (the sound enters at the left and travels across, the waves travelling at the tempo's pace, a swell on each beat). The lyric videos have no shadow on the type, show nothing of an unsung line, and cycle the artist's backdrop, the album cover and the artist's portrait (the square ones blurred to a haze) behind the words. The stage (F11, the menu, or a double-tap) takes the window full screen with the picture filling it on a very dark shade of the artwork's colour; its controls (song, style menu, play/stop, leave) show on movement and fade after three seconds of stillness; Esc or a double-tap leaves |
| `ChipTabs` | Choosing one of a few sections or categories (Browse's categories and editor picture tabs): chips whose pill slides to the chosen one |
| `PlexPassBadge` | A quiet gold tag on a feature that needs Plex Pass. A feature Meloday can do without Plex Pass isn't tagged; one with no stand-in rests (disabled, with a tooltip saying why) instead of disappearing. Settings lists what Plex Pass unlocks, locked or unlocked |
| `SortButton` | How a library list is sorted (a capsule pop-up menu) |
| `RepeatToggle` | Repeat, wherever the transport appears |
| `CastButton` | Where the music plays (this computer or a device), wherever the transport appears |
| `StarRating` | Every rating |
| Grouped list styles (`MelodayGroupStyle`, `MelodayGroupListStyle`, `MelodayGroupRowsStyle`, `MelodayGroupRowStyle`) | Settings-style inset grouped lists; the group rounds its own corners |

Buttons: `MelodayPrimaryButtonStyle` (one main action per view), `MelodaySecondaryButtonStyle` (the rest),
`MelodayRoundActionButtonStyle` (icon-only, same height), `MelodayIconButtonStyle` (transparent icon
buttons), `MelodayIconToggleStyle` (on: an accent glyph on a filled accent circle at 20% opacity, with a dot), `MelodayGlassButtonStyle` and
`MelodayHeroPlayButtonStyle` on coloured pages, `MelodayDangerButtonStyle` only to confirm a deletion.
Pop-up menus and combo boxes are capsules; text fields are rounded rectangles.

### Card layouts

What a row's `CardStyle` calls each card, and what each is for:

| Layout (`CardStyle`) | Control | Shape | For |
| --- | --- | --- | --- |
| Tile | `MediaTile` | Square artwork, title and subtitle underneath | Albums, artists (round), tracks; mixes on Made for You (with a `MixCover`) |
| ColourBoard | `ColourBoard` | Square cover over a panel in its colour, floating circles for mixes | Playlists, collections; the four mixes on Home |
| SquareBoard | `SquareBoard` | Art filling a square, the name laid over it | The live stations and the tag stations on the Radio page |
| Billboard | `Billboard` | 16:9 artist backdrop with a badge | Spotlight on Home |
| EditorialBoard | `Billboard` with `IsEditorial` | Heading above the backdrop, album cover in the corner | Browse highlights |
| (TagCard) | `TagCard` | Three albums cascaded on a palette colour | Genres, moods, styles and decades |

### Meloday, mixes, live stations and radios

Five different things, each with one home and one defining feature, never named alike:

| It is | Defining feature | Lives on | Cover |
| --- | --- | --- | --- |
| **Meloday** | One playlist for this part of your day, made from what you play at this time | Made for You (always the first card) and its own page | Its sky cover, a colour board |
| **A mix** | About 30 songs Meloday picks for you to start, made fresh each day; as it plays it keeps going in its own vein (more of the artist, genre, mood, niche or sound), never ending, without repeating what the queue has. Niche mixes are named in two words about the music at most ("Late Night R&B", "Dreamy Indie"), are always anchored in a genre, an era, two artists, a song or ME's sounds, and never repeat a Find Your Mood playlist | Made for You (Home shows three beside Meloday) | A `MixCover` |
| **A live station** | Always on, as if it had been playing all along: tuning in joins mid-song, and it moves on while you're away. Headliners (artist blocks, new releases, trending and genre blocks), Hits ('80s to 2000s), Club, Chill, Library Radio, Deep Cuts | The Radio page's "Live" row (and Home's) | Its colour field with the M-wave moving across it (`MotionArtwork`, drawn by `WaveField` from `StationWave`: a thick front line with two ribbons trailing it, in a lighter shade of the field, looping every six seconds; Club and Chill lay their own colour on the neutral field): the name top right, "Meloday Live" bottom left |
| **A radio** | You choose where it starts (an artist, song, album, genre, decade, or the Radio Builders); it starts from its first song and never ends | The Radio page's other rows and the "Radio" menu items | A colour field labelled "Radio" |
| **A Made by Meloday playlist** | A playlist for a mood (Feel Good, Relax, Energy, Focus, Heartbreak, Feeling Blue) or a moment (Wake Up, Sunny Afternoon, Dinner, Late Night, Workout, Road Trip, Rainy Day): songs whose sound has the playlist's wanted dimensions and none of its avoided ones (ME's words), that the listener loves (the Taste Engine); made every Monday, the same all week; can be kept on Plex | Made for You: Find Your Mood; the time of day's leads Right Now, Workout joins By Energy, Road Trip For the Weekend, the rest For Every Moment; opens as a read-only playlist (`MadePlaylistPage`) | A flat palette colour (its own, always the same), the edge bars and the M-wave a shade darker (lighter on a family's darkest shade), its name bottom left |

Meloday is a playlist, not a station: it never appears among the stations. Each live station has
its own character: Headliners is programmed in turns (artist blocks of three songs, runs of new releases and of what is trending
on Last.fm, and genre blocks, artists it has featured lately rested) and Now Playing features the artist whose block is on; Hits draws on albums of the
'80s, '90s and 2000s, the best known first; Club and Chill draw on the genres (and, for Chill, the quiet
moods) of your library; Library Radio is everything, leaning to what you play; Deep Cuts the songs you
rarely reach for. A library is only offered the stations it can fill. Live radio is live: its songs
can't be skipped, sought, restarted, queued over or rearranged, shuffle and guest curators don't apply, and
it has no pause (its play button is a stop button, "Tune in" once stopped, and playing again joins the station where it is by then). A mix is never in
two rows of Made for You, and two mixes whose leading artists mostly repeat are never both made.
Another mix's cover never shows the face of an artist who has a mix of their own.

Everything Meloday picks for you keeps itself varied. **Rotation** (`Exposure`): every song and artist Meloday, a mix, a radio, a live station or Made by Meloday puts forward is remembered, fading over days (songs over five, artists over seven), and the taste engine marks down what has been shown a lot, up to about half a song's liking, everywhere at once; suggesting a song never makes it more liked, only playing it does. **Variety in mixes and radios** (`ArtistVariety`): each batch takes a fair share per artist (two when there are ten or more to go round) and the same artist doesn't come back within three songs, counting the songs just played; the rules loosen only as far as needed to fill a narrow mix. **Names** (`MelodayNaming`): genre tags are summed by family first, so the family the mix mostly is leads the name, by its own biggest genre when that genre is a quarter of the mix or more, otherwise by the family ("R&B").

**Don't Use for My Taste Profile** (the More menu of a playlist, album, mix or station; `TasteExclusions`): listening to it still counts in Plex and still scrobbles, but no skips, finishes or picks are remembered for it and its Plex plays are left out of the taste profile, matched by the song and when it started. A queue's source is the item it was started from (or the page it was started on); a song from a marked album counts as marked wherever it plays. Detail pages of marked items say "Not used for your taste profile" under the details.

## States

- Hover and press: a translucent overlay over the control's own fill (never a colour change of the
  fill), easing in over Quick. Cards grow a soft plate out of their centre (`CardHover`).
- Focus: the system focus visual, which follows each control's radius.
- Disabled: the whole control at 38% opacity.
- Selected: a quiet fill (lists) or an accent glyph on a filled accent circle with a dot (toggles).
- Loading: the page's own shapes, pulsing (`SkeletonView`); a spinner only where no shape fits.
- Empty and error: `PageStateView`, centred, with at most one action.

## Motion (`Helpers/Motion.cs`, `Helpers/Anim.cs`)

| Duration | Value | Use |
| --- | --- | --- |
| Quick | 120 ms | Hover and press feedback, glyph swaps, things disappearing |
| Standard | 200 ms | Controls and content appearing or changing state, card hover, tab selection |
| Emphasized | 320 ms | Page content fading in, headers condensing, larger surfaces |
| Ambient | 700 ms | The backdrop's colour crossfading between artworks |

Easing is a decelerating curve (0.2, 0, 0, 1): quick to respond, gentle to settle.

- `helpers:Anim.Appear="True"` on anything whose visibility changes: it fades and rises 6px in,
  and fades out.
- `helpers:Anim.Fade="True"` on anything whose opacity or background changes on hover.
- Pages fade in on navigation; nothing slides.
- The station M-wave and mix-card circles are the only continuous motion.
- Everything follows the Motion setting and Windows' animation effects; with either off, changes
  are instant.

### projectM presets

The Windows x64 visualizer menu has a projectM section: Airhandler, Neon Space, Spiral Artifact, Cosmic Dust and Molten Glass. These are unmodified community presets from Cream of the Crop, rendered by projectM 4.1.8 on an offscreen OpenGL 3.3 pbuffer and composited through the same Win2D surface. They keep their authored colours, use the audible stereo output, and inherit the existing full-screen, visibility, energy and adaptive-quality controls. Readback is capped at 720p worth of pixels. See `tools/projectm/README.md` for rebuild instructions and attribution.
