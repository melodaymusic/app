# Meloday Music design system

Meloday Music is built from one set of tokens and one set of components. A screen should never
restyle a control locally: if a page needs something the system doesn't have, add it to the system
(a token in `Services/UiScale.cs` or `Themes/Design.xaml`, or a component in `Controls/`) and use it
from there, so every screen gets the same thing. The reference is Apple's Human Interface Guidelines
and Apple Music on the Mac.

## Tokens

### Type (Manrope, bundled)

| Style | Size / weight | Use |
| --- | --- | --- |
| `HumeHeroText` | 48 ExtraBold (64 on artist pages) | Album, playlist and artist titles |
| `HumeDisplayText` | 36 ExtraBold | Page titles (through `PageHeader`) |
| `HumeTitleText` | 28 Bold | Titles in sign-in and setup |
| `HumeSectionText` | 22 Bold | Section headings (through `SectionHeader`) |
| `HumeHeadlineText` | 20 SemiBold | A title inside a card or panel |
| `HumeLyricText` | 22 Bold, wraps | A line of lyrics: the line being sung bright and full size, the rest dimmer and slightly smaller with distance |
| `HumeBodyStrongText` | 15 SemiBold | Emphasis, group headings, card titles |
| `HumeBodyText` | 15 Medium | Reading text, row titles |
| `HumeSecondaryText` | 14 Medium, secondary colour | Counts, metadata, captions |
| `HumeParagraphText` | 15, 24 line height, secondary | Descriptions, notes, biographies |
| `HumeOverlineText` | 14 SemiBold, secondary | Eyebrows ("Good evening", "Genre") |

Medium is the reading weight (Manrope's Regular is too light on glass). Nothing anyone reads is
under 14px. Title case for titles, headings, buttons and menu items; sentence case for descriptions,
setting labels and tooltips.

Subtitle links under big titles use `HumeHeroSubtitleLinkStyle` (under a 48 title) or
`HumeFeatureSubtitleLinkStyle` (under a 40 title): accent, SemiBold. "More" and "Read More" use
`HumeMoreLinkStyle`.

### Spacing

2 · 4 · 8 · 12 · 16 · 20 · 24 · 32 · 40 · 48

- 2 between a line and its caption (title and subtitle, label and value).
- 8 between buttons in a row, and between a group heading and its group.
- 12 between a section heading and its content.
- 16 or 24 between blocks inside a panel or dialog.
- 32 between setting groups; 48 (`HumeSectionSpacing`) between page sections.
- 40 page gutters (`HumePagePadding`, `HumePageHeaderPadding`, `HumeGutterPadding`).
- Cards in rows and grids sit 20 apart: each card's container is padded 10 on every side.

### Radii (concentric)

A rounded shape inside another with padding p has radius outer − p.

| Radius | Token | Use |
| --- | --- | --- |
| 4 | `HumeRadiusThumb` | Track thumbnails |
| 8 | `HumeRadiusArtwork`, `HumeRadiusControl` | Artwork, text fields, nav items, tag cards |
| 12 | `HumeRadiusLargeArtwork`, `HumeRadiusRow`, `HumeRadiusGroup` | Hero artwork, track rows (4 + 8), grouped lists |
| 16 | `HumeRadiusBoard` | Boards and glass panels |
| 18 | `HumeRadiusTileHost` | Card containers and their hover plate (8 + 10) |
| 20 | `HumeRadiusGlass` | Sidebar, queue and player bar (player: 8 + 12) |
| 26 | `HumeRadiusBoardHost` | Board containers (16 + 10) |
| height / 2 | `HumeRadiusPill` | Every button is a capsule |

### Sizes

- Controls: 40 high (`HumeControlHeight`); icon buttons 32 / 36 / 40 / 44 / 48 / 56 / 72.
- Icons: XS 14 (beside a caption), S 16 (in labelled buttons), M 18 (icon buttons, rows),
  L 20 (navigation, section chevrons), then 22 / 26 / 30 / 36 for transport and empty states.
- Stars: `HumeStarS` 14 (rows, player bar), `HumeStarM` 18 (headers), `HumeStarL` 22 (Now Playing).

### Colour

- Theme brushes (`HumeTextPrimaryBrush`, `HumeTextSecondaryBrush`, `HumeTextTertiaryBrush`,
  `HumeSubtleFillBrush`, `HumeDividerBrush`, ...) follow light and dark.
- On colour (`HumeOnColorPrimaryBrush`, `...Body`, `...Secondary`, `...Tertiary`, `...Fill`,
  `...Panel`, `...Hairline`, `...Edge`): text and controls over artwork or a coloured page, always
  light, at fixed strengths. Never write a colour value in a page.
- The accent follows the page's artwork (`Accent`); buttons, links and selection use it.
- `HumeChipSelectedBrush`: the chosen chip's fill (Browse's sliding category pill), the same strength
  as a selected sidebar item.
- Materials: `HumeGlassBrush` for floating panels (sidebar, queue, search), `HumePlayerAcrylicBrush`
  for the player bar, `HumeOverlayGlassBrush` for controls floating on artwork. The queue and lyrics
  share one side panel (`PanelService`): opening one closes the other.

## Components

| Component | Use it for |
| --- | --- |
| `PageHeader` | Every top-level and list page's header: eyebrow, title, count or description, actions (move under the title when narrow) |
| `DetailHeader` | Album, playlist, collection and Meloday headers; the item's Plex logo (`PlexLogoService`, shown in an `ArtistLogo`) stands in for its name when it has one |
| `MuteButton` (Now Playing) | Mute as a secondary capsule (`HumeSecondaryButtonStyle`, `BellOff` icon, "Mute", or "Muted" while the song or its artist is) beside the rating, opening a text-only menu: mute the song, mute the artist (each for the Settings length), Another Length (`MuteDialog`), Muted Music (Settings > Muting); a muted song or artist shows a disabled line saying how long is left and an Unmute item |
| `LyricStudioPage` | Timing a song's lyrics by hand: a glass panel for the words (text box, "tap for each word / line", Start timing), then a glass panel with the line being tapped in `HumeHeroText` (words done, next underlined in the accent, to come), the primary Tap button and secondary Pause, Undo and Back 5 s buttons; Save timing is the page header's one primary action. The space bar is the only key (pressed as a word begins, held through a held word), handled on the page's tunnelling `PreviewKeyDown`/`PreviewKeyUp` so a focused button never also clicks |
| `BackdropLayer` | The Plex backdrop of a playlist, album or collection laid out as the artist page's hero image (16:9, flush right, `FadedArtwork` fading left and down into the UltraBlur wash, moving with `ScrollSource`); collapsed with none |
| `SectionHeader` | Every section heading, with an optional subtitle, trailing text, or a "see all" chevron link |
| `MediaRowControl` | Horizontal rows of cards (tiles, boards, billboards) |
| `MediaTile`, `SquareBoard`, `ColourBoard`, `Billboard` | Cards; all share `CardHover` |
| `Billboard` with `IsEditorial` (row style `EditorialBoard`) | An editorial board, after Apple Music: heading (eyebrow, title, subtitle) above the 16:9 backdrop, a short line on it, the album cover bottom left (clear of the play button) |
| `TagCard` | Genre, mood, style and decade boards (Browse and search): three of your most played albums cascaded, the card in the front album's palette colour |
| `CardSlot` | Every card in a grid: insets the card and holds its hover plate (grid items clip anything reaching outside a stretched card) |
| `TagHubPage` | A Browse family (Pop, Chill & Calm): its genres, styles or moods as cards, with Shuffle and All Albums |
| `LayoutToggle`, `MediaListRow` | Grid or list for a library page: two icon buttons in one capsule (the chosen one filled, `HumeChipSelectedBrush`) beside the Sort button, kept per page (`AppSettings.LibraryLayouts`); the list shows each album, artist, playlist or collection as a row (52 px art, name, what it is, a detail at the far end, a play button on hover) in `HumeTrackItemStyle` rows |
| `PageSearch` | A search that belongs to one page (a playlist's): the circle `HumeRoundActionButtonStyle` opens into a pill field with the cursor in it, as the global search does (`Motion.Emphasized`), folds back when left empty; Esc clears it |
| `TrackRow` | Every list of tracks, including the queue. On a list whose host sets `TrackListBehavior.RemoveCommand` it shows a remove (X) button on hover or selection, and where it sets `DupeCommand` (Meloday's page) a merge button beside it: "this is a dupe of another song" |
| `DupeDialog` | Marking a dupe (Meloday): step one finds the other song (the marked song's card, a search box, the Meloday's songs and library results, each with artwork, title, artist · album); step two lays the two songs side by side as large cards (artwork 190, title, artist, album; each a button, the chosen one gets an accent border and a "Plays" tag, the other dims) and Merge stays off until one is chosen; Back returns to step one. Then the page animates the lower row sliding into the upper one (`Motion.Deliberate`, 640 ms: slide, shrink, fade) while the upper one swells. Settings' **Dupes** tab lists each pair (the song that plays, "in place of" the other) with Unmerge |
| `ItemDetailsDialog` | The one editor for an album, artist, collection or playlist, as Plex has it: `ChipTabs` (Details, Cover or Photo, Backdrop, Logo) over a fixed-height body. Details is a title box and a summary box; each picture tab is a note and an Upload Image button above a grid of tiles (`HumeTileButtonStyle`, art at `HumeRadiusArtwork`, the chosen tile filled with `HumeChipSelectedBrush`, a "Current" caption). Nothing is sent until Save (`Dialogs.Wide` makes the dialog 680 wide). There are no other places to change a picture or a name |
| `SearchPicker`, `Dialogs.MediaRow` | Finding one thing from a dialog (Radio Builders, Sonic Adventure): a text box with the matches in a `HumeTrackItemStyle` list under it, each row a 40 px picture, title and detail. What is chosen sits in a grouped list (`HumeGroupListStyle`, rows `HumeGroupRowStyle`) with a trailing remove or Change button; suggestions are secondary capsules with an `IconLabel` |
| `PageStateView` | Loading (skeletons), error with Try Again, and empty states (icon, title, text) |
| `IconLabel` | The content of every button with an icon and a label |
| `CoverLabels`, `CoverTextLayer`, `CoverType`, `CoverFit` | The typography of every cover Meloday Music draws itself (Meloday's, every station's, every mix's, still covers), in a 1000-unit square: the name right-aligned top right (92, ExtraBold, its box 760 wide with its right edge 110 in and its top 100 down, up to four lines) and the label bottom left (87, Bold, its box 760 wide, 110 in from the left, its bottom edge 100 up, growing upward over up to four lines; a mix's name is its label). Always those places and sizes; a text too long for its box is set just small enough to fit it (`CoverFit`, never below 40%), never trimmed. Live (`MelodayArt`, `CoverLabels`, `MixCover`) or as a picture (`MelodayCover`, `StaticCovers`) they share these numbers |
| `StaticCovers` | A still cover for what moves (a station, mix, tag or Meloday) wherever a list shows a still: pins on Home and in the sidebar, "Jump back in" |
| Jump back in tabs (Home) | Cover (56) and name on a glass tab (`HumeGlassFillBrush`, `HumeRadiusControl`), two across beside the featured cover; the last thing played is the large one |
| `MixCover` | The cover of a mix on a square card (`MediaTile`): the colour of the main artist's photo as a flat field, that artist in a large circle in the middle and two others from the mix in small circles in opposite corners (the same two all day), each small circle ringed in the field's colour. `StaticCovers` draws the same cover as a still for pins, with the small circles overlapping the main one more. On Home a mix keeps its `ColourBoard` |
| `CuratorButton` (player bar, Now Playing) | An icon toggle, lit while a curator is chosen, that opens a menu of curators (radio items, each with its description as a tooltip): how what plays next is arranged. Hidden on live radio, where `StationFeedbackButton` stands in its place |
| `StationFeedbackButton` (player bar, Now Playing) | Live radio only: a thumbs-down toggle, "Not for this station", lit while the song on has been taken off that station; pressed again it lets the song back on |
| Live badge (player bar) | On live radio the player bar shows a dot and "LIVE · station" ("OFF AIR · station" once stopped) where the time would be, and the previous, next and position controls are disabled; the play button is a filled square (Stop) |
| `VisualizerView`, `VisualizerButton` (Now Playing), `VisualizerStage` | The music drawn from the mixer's own output (`SpectrumAnalyzer`, `BeatTracker`) on a transparent ground, where the artwork was on Now Playing, in the artwork's colours (`VisualPalette`): Bars, Halo, M-Wave (the logo's thick line, made of a few slow waves that ease to the sound, with echoes and a colour that shifts along it, a brighter band running through on each beat and lyric line), Sun (the playlist cover's four rings, each swelling with its own part of the sound), Lyric Video and Lyric Glow (`LyricPainter`: the line being sung in big type, set in Manrope alone (`LyricFonts`) at a weight that follows the energy, each word arriving as sung, timed across the line by `LyricTiming`, kicking with the beat and moving at its pace; over the artist's backdrop drifting and breathing, or soft colour clouds; the song's name and artist only when the song has no lyrics at all: in a break in the singing (the intro, an instrumental, the end) nothing but the picture shows; a word held on a long note, timed in the Lyric Studio, stays in the artwork's colour and swells a little until it lets go). 60 fps on Win2D's render thread; draws nothing when none is chosen, the music has stopped and settled, the window is in the background or Motion is off. The button is an icon toggle that opens a menu of the styles and "Full screen". All pictures draw with the artwork's four colours (`VisualPalette.Tone(0..3)`, from `ArtworkTones`: the UltraBlur corners, darkest first and brightened; the darkest, deepened, is the ground): Lyric Glow's ground is one and its three clouds the others, each of the Sun's rings is one, M-Wave runs through all four. A song's lyric weight is shared by every painter showing it. Pictures are drawn onto a composition surface (a sprite over the element; never a Win2D swap-chain control, which shows an opaque black square) on their own 60 fps thread. The Wave flows left to right (the sound enters at the left and travels across, the waves travelling at the tempo's pace, a swell on each beat). The lyric videos have no shadow on the type, show nothing of an unsung line, and cycle the artist's backdrop, the album cover and the artist's portrait (the square ones blurred to a haze) behind the words. The stage (F11, the menu, or a double-tap) takes the window full screen with the picture filling it on a very dark shade of the artwork's colour; its controls (song, style menu, play/stop, leave) show on movement and fade after three seconds of stillness; Esc or a double-tap leaves |
| `ChipTabs` | Choosing one of a few sections or categories (Browse's categories, Settings' sections): chips whose pill slides to the chosen one |
| `PlexPassBadge` | A quiet gold tag on a feature that needs Plex Pass. A feature Meloday can do without Plex Pass isn't tagged; one with no stand-in rests (disabled, with a tooltip saying why) instead of disappearing. Settings lists what Plex Pass unlocks, locked or unlocked |
| `SortButton` | How a library list is sorted (a capsule pop-up menu) |
| `RepeatToggle` | Repeat, wherever the transport appears |
| `CastButton` | Where the music plays (this computer or a device), wherever the transport appears |
| `StarRating` | Every rating |
| Grouped list styles (`HumeGroupStyle`, `HumeGroupListStyle`, `HumeGroupRowsStyle`, `HumeGroupRowStyle`) | Settings-style inset grouped lists; the group rounds its own corners |

Buttons: `HumePrimaryButtonStyle` (one main action per view), `HumeSecondaryButtonStyle` (the rest),
`HumeRoundActionButtonStyle` (icon-only, same height), `HumeIconButtonStyle` (transparent icon
buttons), `HumeIconToggleStyle` (on/off with accent and dot), `HumeGlassButtonStyle` and
`HumeHeroPlayButtonStyle` on coloured pages, `HumeDangerButtonStyle` only to confirm a deletion.
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

Four different things, each with one home and one defining feature, never named alike:

| It is | Defining feature | Lives on | Cover |
| --- | --- | --- | --- |
| **Meloday** | One playlist for this part of your day, made from what you play at this time | Made for You (always the first card) and its own page | Its sky cover, a colour board |
| **A mix** | About 30 songs Meloday picks for you; plays through and ends; made fresh each day | Made for You (Home shows three beside Meloday) | A `MixCover` |
| **A live station** | Always on, as if it had been playing all along: tuning in joins mid-song, and it moves on while you're away. Headliners (artist blocks, new releases, trending and genre blocks), Hits ('80s to 2000s), Club, Chill, Library Radio, Deep Cuts | The Radio page's "Live" row (and Home's) | Its colour field, moving (`MotionArtwork`; Club and Chill share the neutral field with a colour laid over it, still and video alike): the name top right, "Meloday Live" bottom left |
| **A radio** | You choose where it starts (an artist, song, album, genre, decade, or the Radio Builders); it starts from its first song and never ends | The Radio page's other rows and the "Radio" menu items | A colour field labelled "Radio" |

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

## States

- Hover and press: a translucent overlay over the control's own fill (never a colour change of the
  fill), easing in over Quick. Cards grow a soft plate out of their centre (`CardHover`).
- Focus: the system focus visual, which follows each control's radius.
- Disabled: the whole control at 38% opacity.
- Selected: a quiet fill (lists) or an accent glyph with a dot (toggles).
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
- The moving station artwork and mix-card circles are the only continuous motion.
- Everything follows the Motion setting and Windows' animation effects; with either off, changes
  are instant.
