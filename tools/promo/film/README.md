# Meloday Music product film

A 32-second, 1920 × 1080, 60 fps motion-design film, authored separately from the
older screenshot-based promo. It uses a lit, physical 3D version of the app's
actual symbol curve, continuously animated Meloday artwork, moving 3D waveforms,
and a reconstructed Home interface with animated playback and pointer interaction.
The interface sequence is a promotional reconstruction, not a screen recording.

## Story

| Time | Sequence |
| --- | --- |
| 0–5 s | Macro camera move over the brand symbol; “Your music. A new rhythm.” |
| 5–14 s | Meloday artwork and player, progressing from morning to night |
| 14–21 s | Two streams of waveform bars blend; “One song. Into the next.” |
| 21–26 s | Animated Home interface; “Your Plex library. Rediscovered.” |
| 26–32 s | Brand resolution and “Find your Meloday.” |

`film.js` owns the animation, `index.html` hosts it, and `render.cjs` renders
deterministic frames into ffmpeg. `score.py` synthesizes a new stereo score,
transition effects, click sounds, and a closing four-note signature without
third-party music or samples. The app's Nunito font and genre artwork are reused.
Nothing in the application or its installed build is changed by this workflow.

## Render on this workstation

Requirements: Node, Python with numpy/scipy, ffmpeg, Chrome, and the bundled
Playwright package. `render.cjs` currently points to this workstation's Chrome
and bundled Playwright paths.

From the repository root:

```powershell
npm install --prefix artifacts/promo/film-runtime three --no-audit --no-fund
python tools/promo/film/score.py
node tools/promo/film/render.cjs --stills
node tools/promo/film/render.cjs
```

Outputs are under `artifacts/promo/film/`, including
`MelodayMusic-Product-Film.mp4` and `soundtrack.wav`. All frame times are explicit,
so browser timing and hardware speed cannot change the final animation.
