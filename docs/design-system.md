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
| `DetailHeader` | Album, playlist, collection and Meloday headers |
| `SectionHeader` | Every section heading, with an optional subtitle, trailing text, or a "see all" chevron link |
| `MediaRowControl` | Horizontal rows of cards (tiles, boards, billboards) |
| `MediaTile`, `SquareBoard`, `ColourBoard`, `Billboard` | Cards; all share `CardHover` |
| `Billboard` with `IsEditorial` (row style `EditorialBoard`) | An editorial board, after Apple Music: heading (eyebrow, title, subtitle) above the 16:9 backdrop, a short line on it, the album cover bottom left (clear of the play button) |
| `TagCard` | Genre, mood, style and decade boards (Browse and search): three of your most played albums cascaded, the card in the front album's palette colour |
| `CardSlot` | Every card in a grid: insets the card and holds its hover plate (grid items clip anything reaching outside a stretched card) |
| `TagHubPage` | A Browse family (Pop, Chill & Calm): its genres, styles or moods as cards, with Shuffle and All Albums |
| `TrackRow` | Every list of tracks, including the queue |
| `PageStateView` | Loading (skeletons), error with Try Again, and empty states (icon, title, text) |
| `IconLabel` | The content of every button with an icon and a label |
| `CoverLabels`, `CoverType` | The typography of every cover Meloday Music draws itself (Meloday's, every station's, still covers): the name right-aligned top right (92, ExtraBold, up to four lines), the label bottom left (87, Bold), in a 1000-unit square. Live (`MelodayArt`, `CoverLabels`) or as a picture (`MelodayCover`, `StaticCovers`) they share these numbers |
| `StaticCovers` | A still cover for what moves (a station, mix, tag or Meloday) wherever a list shows a still: pins on Home and in the sidebar, "Jump back in" |
| Jump back in tabs (Home) | Cover (56) and name on a glass tab (`HumeGlassFillBrush`, `HumeRadiusControl`), two across beside the featured cover; the last thing played is the large one |
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
