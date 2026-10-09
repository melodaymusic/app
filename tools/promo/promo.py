"""Renders the Meloday Music promo video: 1920x1080 at 60 fps, with the music from music.py.

    python promo.py --stills 1.5 6 12 ...      write single frames to artifacts/promo/stills (for checking layout)
    python promo.py --render out.mp4           render everything (uses all your cores) and mux the music

The footage is real: screenshots of the app (artifacts/promo/shots), its own radio artwork videos, the
Meloday covers redrawn from the app's palette. Cuts land on the music's bar lines (112 BPM).
"""
import math
import multiprocessing as mp
import os
import subprocess
import sys
from functools import lru_cache

import numpy as np
from PIL import Image, ImageChops, ImageDraw, ImageFilter

from engine import *  # noqa: F401,F403
from engine import _quad  # noqa: F401

BAR = 4 * 60 / 112
DURATION = 55.9
SHOTS = WORK / "shots"
CROP = (10, 1, 1932, 1092)          # the window's own pixels (the screenshot's edge is empty)

SCREENS = {}
CARDS = {}


# ------------------------------------------------------------------ assets

@lru_cache(maxsize=None)
def _shared_mask(w, h, radius):
    return rounded_mask((w, h), radius)


def screen(name):
    if name not in SCREENS:
        SCREENS[name] = Layer(Image.open(SHOTS / f"{name}.png").convert("RGB").crop(CROP), radius=13)
    return SCREENS[name]


def crop_layer(name, box, radius=20):
    key = (name, box, radius)
    if key not in CARDS:
        CARDS[key] = Layer(Image.open(SHOTS / f"{name}.png").convert("RGB").crop(box), radius=radius)
    return CARDS[key]


# ------------------------------------------------------------------ the sky: Meloday's seven times of day, drawn from the app's own palette

def _rgb(v):
    return ((v >> 16) & 255, (v >> 8) & 255, v & 255)


_OP = [0.30, 0.40, 0.65, 1.0]
SKY = [
    ("Dawn", _rgb(0x2D2350), 0.5, 1.15, [(_rgb(0xFF6E4B), o) for o in _OP], "Hushed Indie Folk Monday Dawn"),
    ("Early Morning", _rgb(0xA77ECD), 0.5, 0.9, [(_rgb(0xBC85B2), 1), (_rgb(0xD39193), 1), (_rgb(0xE29A6F), 1), (_rgb(0xEDA158), 1)], "Golden Soft Pop Monday Early Morning"),
    ("Morning", _rgb(0x3CA5E1), 0.5, 0.5, [(_rgb(0xFFC800), o) for o in _OP], "Bright Feel-Good Pop Monday Morning"),
    ("Afternoon", _rgb(0x1E5ADC), 0.5, 0.1, [(_rgb(0xFFC800), o) for o in _OP], "Sunlit Dance Pop Monday Afternoon"),
    ("Evening", _rgb(0x873769), 0.5, 0.9, [(_rgb(0xFF6E4B), o) for o in _OP], "Warm Neo Soul Monday Evening"),
    ("Night", _rgb(0x4B4178), 0.5, 0.236, [(_rgb(0xA0E1FF), o) for o in _OP], "Sleek Spins and Vibe Cuts Monday Night"),
    ("Late Night", _rgb(0x0A3228), 0.799, 0.212, [(_rgb(0x739BF5), o) for o in _OP], "Dreamy Ambient Monday Late Night"),
]
RING_SIZES = [1.0, 0.75, 0.5, 0.25]
BREATH = [(0.960, 1.040, 8.8, 1.4), (0.972, 1.034, 7.6, 3.1), (0.982, 1.030, 9.5, 5.2), (0.990, 1.024, 7.9, 2.6)]


def _wrap(text, size, weight, width):
    lines, line = [], ""
    for word in text.split():
        trial = (line + " " + word).strip()
        if text_width(trial, size, weight) <= width or not line:
            line = trial
        else:
            lines.append(line)
            line = word
    lines.append(line)
    return lines


def sky_cover(pos, size, t, title_alpha=None):
    """The Meloday cover at a point of the day (pos 0..6: Dawn to Late Night), as the app draws it: a field, a sun of four rings, the
    name top right and 'Meloday' bottom left. pos between two integers blends the two skies."""
    pos = clamp(pos, 0, 6)
    i = min(5, int(pos))
    f = pos - i
    a, b = SKY[i], SKY[i + 1]
    bg = lerp_color(a[1], b[1], f)
    cx, cy = lerp(a[2], b[2], f), lerp(a[3], b[3], f)
    img = Image.new("RGB", (size, size), tuple(int(c) for c in bg))
    ss = 2
    sun = 0.8 * size
    for r in range(4):
        col = lerp_color(a[4][r][0], b[4][r][0], f)
        op = lerp(a[4][r][1], b[4][r][1], f)
        lo, hi, secs, delay = BREATH[r]
        breath = lerp(lo, hi, 0.5 - 0.5 * math.cos(2 * math.pi * (t + delay) / secs))
        d = sun * RING_SIZES[r] * breath
        m = Image.new("L", (size * ss, size * ss), 0)
        ImageDraw.Draw(m).ellipse(((cx * size - d / 2) * ss, (cy * size - d / 2) * ss, (cx * size + d / 2) * ss, (cy * size + d / 2) * ss), fill=255)
        m = m.resize((size, size), Image.LANCZOS).point(lambda v: int(v * op))
        img.paste(Image.new("RGB", (size, size), tuple(int(c) for c in col)), (0, 0), m)
    k = size / 1000
    # The name, right-aligned in its 700-unit box (92 ExtraBold, 92 line height), and the label
    for which, alpha in ((i, 1 - seg(f, 0.38, 0.62)), (i + 1, seg(f, 0.38, 0.62))):
        if title_alpha is not None:
            alpha *= title_alpha
        if alpha < 0.01:
            continue
        nsize = int(92 * k)
        for n, line in enumerate(_wrap(SKY[which][5], nsize, 800, 700 * k)[:4]):
            put_text(img, line, (1000 - 110) * k, (100 + 74 + n * 92) * k, nsize, 800, (255, 255, 255), alpha, "r")
    put_text(img, "Meloday", 110 * k, 888 * k, int(87 * k), 700, (255, 255, 255), 1.0, "l")
    return img


# ------------------------------------------------------------------ the live stations' own moving artwork

RADIO = [("crimson", "Your Monday", 0), ("sapphire", "Library", 130), ("lagoon", "Deep Cuts", 60), ("green", "Time Travel", 220)]


@lru_cache(maxsize=96)
def _radio_frame(name, index):
    files = sorted((WORK / "radio" / name).glob("*.jpg"))
    return Image.open(files[index % len(files)]).convert("RGB")


def radio_card(name, label, size, t, offset):
    frame = _radio_frame(name, int(t * 60) + offset).resize((size, size), Image.LANCZOS)
    k = size / 1000
    put_text(frame, label, (1000 - 110) * k, (100 + 74) * k, int(92 * k), 800, (255, 255, 255), 1.0, "r")
    put_text(frame, "Live", 110 * k, 888 * k, int(87 * k), 700, (255, 255, 255), 1.0, "l")
    return Layer(frame, radius=int(size * 0.052))


# ------------------------------------------------------------------ scenes (t is seconds since the scene's start)

WHITE = (255, 255, 255)


def sub(img, text, x, y, t, t0, size=44, anchor="l", alpha=0.66, exit_at=None):
    p = OUT(seg(t, t0, t0 + 0.9))
    q = IO(seg(t, exit_at, exit_at + 0.4)) if exit_at is not None else 0
    put_text(img, text, x, y, size, 500, WHITE, alpha * p * (1 - q), anchor, blur=(1 - p) * 8, dy=(1 - p) * 18 - q * 10)


def s_intro(t):
    img = stage(t, [((120, 40, 200), 0.5, 0.45, 0.5, 0.20, 0.0), ((230, 40, 90), 0.3, 0.6, 0.4, 0.10, 2.0), ((40, 200, 190), 0.72, 0.55, 0.4, 0.09, 4.0)])
    p = OUT(seg(t, 0.25, 2.1))
    wave(img, W / 2, 462, 640, progress=p, alpha=1, glow=0.9 + 0.5 * math.sin(t * 2.2), shine=seg(t, 1.6, 3.4))
    put_words(img, ["Meloday", "Music"], W / 2, 690, 74, t, 1.9, weight=700, stagger=0.16, tracking=0.0, rise=24)
    return img


def s_title(t):
    img = stage(t, [((230, 40, 90), 0.2, 0.5, 0.45, 0.17, 0.0), ((130, 70, 255), 0.8, 0.5, 0.45, 0.17, 2.0)])
    size = 128
    put_words(img, ["Your", "Plex", "library."], W / 2, 500, size, t, 0.15, stagger=0.13, exit_at=3.55)
    size2 = fit_size("Never looked like this.", 128, 800, 1660)
    put_words(img, ["Never", "looked", "like", "this."], W / 2, 672, size2, t, 1.25, stagger=0.13, stops=BRAND, exit_at=3.55)
    return img


def s_home(t):
    img = stage(t, [((120, 70, 255), 0.85, 0.2, 0.5, 0.15, 0.0), ((30, 160, 200), 0.1, 0.9, 0.5, 0.11, 2.0)])
    hx = 3.9            # the headline leaves; the window rises to fill the frame
    ex = 8.1
    put_words(img, ["Jump", "back", "in."], 150, 262, 120, t, 0.1, anchor="l", stagger=0.12, exit_at=hx)
    sub(img, "Pick up right where you left off.", 156, 338, t, 0.7, exit_at=hx)
    p = OUT(seg(t, 0.15, 1.9))
    rise = IO(seg(t, hx - 0.1, hx + 1.5))
    xfade = IO(seg(t, 5.2, 6.1))
    top, mid = Image.open(SHOTS / "home_top.png").convert("RGB").crop(CROP), Image.open(SHOTS / "home_mid.png").convert("RGB").crop(CROP)
    base = Layer(Image.blend(top, mid, xfade), radius=13) if 0.001 < xfade < 0.999 else screen("home_mid" if xfade >= 0.999 else "home_top")
    q = IO(seg(t, ex, ex + 0.5))
    cy = lerp(lerp(1600, 930, p), 548, rise) - q * 60
    scale = lerp(lerp(0.78, 0.83, p), 0.945, rise) + 0.02 * seg(t, hx + 1.5, ex)
    place(img, base, lerp(1000 + 18 * seg(t, 0, hx), 960, rise), cy, scale=scale, rx=lerp(lerp(16, 5, p), 0, rise), ry=lerp(lerp(-8, -3, p), 0, rise), alpha=1 - q)
    return img


def s_mfy(t):
    img = stage(t, [((230, 90, 40), 0.18, 0.42, 0.5, 0.14, 0.0), ((40, 190, 170), 0.85, 0.7, 0.5, 0.13, 2.0), ((140, 70, 220), 0.55, 0.15, 0.4, 0.10, 4.0)])
    ex = 8.0
    put_words(img, ["Made", "for", "you."], W / 2, 235, 118, t, 0.1, stagger=0.12, stops=None, exit_at=ex)
    sub(img, "Mixes picked from what you actually play.", W / 2, 312, t, 0.7, anchor="c", exit_at=ex)
    boxes = [(358, 424, 720, 906), (746, 424, 1104, 906), (1130, 424, 1490, 906), (1516, 424, 1874, 906)]
    sc, gap = 1.15, 30
    widths = [(b[2] - b[0]) * sc for b in boxes]
    total = sum(widths) + gap * 3
    x = W / 2 - total / 2
    for i, b in enumerate(boxes):
        layer = crop_layer("mfy", b, radius=22)
        w = widths[i]
        t0 = 0.5 + i * 0.16
        p = OUT(seg(t, t0, t0 + 1.2))
        q = IO(seg(t, ex + i * 0.04, ex + i * 0.04 + 0.5))
        # Each card lifts as the highlight passes (about 0.75 s apiece), then settles.
        hot = math.exp(-(((t - (3.3 + i * 0.75)) / 0.45) ** 2))
        cy = 690 + (1 - p) * 330 - hot * 22 - q * 40 + 8 * math.sin(t * 0.9 + i)
        place(img, layer, x + w / 2 + (1 - p) * 40, cy, scale=sc * (1 + 0.035 * hot), rx=(1 - p) * 10, ry=(1 - p) * 8, alpha=p * (1 - q), shadow=0.6,
              glow=(255, 255, 255), glow_alpha=0.10 * hot)
        x += w + gap
    return img


def s_meloday(t):
    day = clamp((t - 0.35) / 3.2)
    pos = 6 * IO(day)
    tint = SKY[min(6, int(round(pos)))][1]
    img = stage(t, [(tuple(int(c * 1.0) for c in lerp_color(SKY[min(5, int(pos))][4][3][0], SKY[min(6, int(pos) + 1)][4][3][0], pos - int(pos))), 0.28, 0.5, 0.55, 0.20, 0.0),
                    (tuple(int(c) for c in lerp_color(SKY[min(5, int(pos))][1], SKY[min(6, int(pos) + 1)][1], pos - int(pos))), 0.7, 0.4, 0.6, 0.28, 2.0)])
    ex = 4.05
    p = OUT(seg(t, 0.0, 1.0))
    q = IO(seg(t, ex, ex + 0.35))
    cover = Layer(sky_cover(pos, 620, t), radius=26)
    place(img, cover, 470 + (1 - p) * -120, 548, scale=lerp(0.94, 1.0, p), rx=0, ry=lerp(18, 7, p) + 2.5 * math.sin(t * 0.8), alpha=p * (1 - q), shadow=0.6,
          glow=lerp_color(SKY[min(5, int(pos))][1], SKY[min(6, int(pos) + 1)][1], pos - int(pos)), glow_alpha=0.35)
    put_words(img, ["Meloday."], 900, 420, 150, t, 0.2, anchor="l", stops=BRAND, exit_at=ex)
    sub(img, "A playlist for this very moment,", 906, 498, t, 0.55, size=44, exit_at=ex)
    sub(img, "made from what you play at this time of day.", 906, 556, t, 0.65, size=44, exit_at=ex)
    # The seven parts of the day, the highlight following the sun
    names = [s[0] for s in SKY]
    rows = [names[:4], names[4:]]
    for r, row in enumerate(rows):
        x = 906
        for name in row:
            idx = names.index(name)
            w = text_width(name, 27, 600) + 44
            a = OUT(seg(t, 0.9 + idx * 0.06, 1.5 + idx * 0.06)) * (1 - q)
            active = clamp(1 - abs(pos - idx) * 1.6)
            cy = 660 + r * 66
            pill(img, x + w / 2, cy, w, 48, fill=lerp_color((255, 255, 255), (255, 255, 255), 0), alpha=(0.08 + 0.78 * active) * a)
            put_text(img, name, x + w / 2, cy + 9, 27, 600, lerp_color((255, 255, 255), (16, 16, 24), active), a, "c")
            x += w + 14
    return img


def s_live(t):
    img = stage(t, [((235, 40, 80), 0.18, 0.5, 0.5, 0.20, 0.0), ((30, 90, 230), 0.5, 0.8, 0.5, 0.14, 2.0), ((40, 200, 150), 0.85, 0.4, 0.5, 0.17, 4.0)])
    ex = 4.05
    put_words(img, ["Always", "on."], W / 2, 250, 128, t, 0.05, stagger=0.13, exit_at=ex)
    sub(img, "Live radio. Tune in and join mid-song.", W / 2, 330, t, 0.6, anchor="c", exit_at=ex)
    size, gap = 372, 36
    total = 4 * size + 3 * gap
    x = W / 2 - total / 2
    for i, (name, label, off) in enumerate(RADIO):
        p = OUT(seg(t, 0.25 + i * 0.12, 1.45 + i * 0.12))
        q = IO(seg(t, ex + i * 0.04, ex + i * 0.04 + 0.4))
        layer = radio_card(name, label, size, t + 4, off)
        place(img, layer, x + size / 2 + (1 - p) * 220, 640 + 12 * math.sin(t * 1.1 + i * 0.9) - q * 30, scale=lerp(0.9, 1.0, p), ry=(1 - p) * -22 + 3 * math.sin(t * 0.7 + i),
              alpha=p * (1 - q), shadow=0.55)
        x += size + gap
    return img


def _wavebars(n, seed, phase, kind):
    rng = np.random.default_rng(seed)
    amps = []
    for i in range(n):
        beat = ((i / n) * 64 + phase) % 1.0
        kick = math.exp(-beat * 4.0)
        body = 0.28 + 0.18 * math.sin(i * 0.31 + seed) + 0.1 * math.sin(i * 0.07)
        amps.append(clamp(body + 0.55 * kick * rng.uniform(0.6, 1.0) + rng.uniform(-0.05, 0.08), 0.1, 1.0))
    return amps


NB = 128
AMP_A = _wavebars(NB, 11, 0.0, "a")
AMP_B = _wavebars(NB, 29, 0.35, "b")


def s_automix(t):
    img = stage(t, [((140, 60, 255), 0.22, 0.35, 0.5, 0.20, 0.0), ((40, 210, 190), 0.8, 0.75, 0.5, 0.17, 2.0), ((255, 90, 80), 0.15, 0.8, 0.4, 0.10, 4.0)])
    ex = 8.1
    slam = OUT(seg(t, 0.0, 0.9))
    put_text(img, "Seamless.", 150, 238, 128, 800, WHITE, clamp(slam * 1.6) * (1 - IO(seg(t, ex, ex + 0.4))), "l", scale=lerp(1.06, 1.0, slam), blur=(1 - slam) * 18)
    sub(img, "AutoMix blends songs beat for beat.", 156, 308, t, 0.55, exit_at=3.9)
    sub(img, "Tempo and key matched. Up to sixteen bars long.", 156, 308, t, 4.2, exit_at=ex)

    # The mix, drawn as two decks over sixteen bars
    px, py, pw, ph = 150, 392, 1620, 560
    appear = OUT(seg(t, 0.5, 1.6))
    out_t = IO(seg(t, ex, ex + 0.45))
    a_all = appear * (1 - out_t)
    S = 2
    panel = Image.new("RGBA", (pw * S, ph * S), (0, 0, 0, 0))
    d = ImageDraw.Draw(panel)
    d.rounded_rectangle((0, 0, pw * S - 1, ph * S - 1), 30 * S, fill=(255, 255, 255, 14), outline=(255, 255, 255, 30), width=2 * S)
    lanes = [(70, 190, AMP_A, WARM, "OUTGOING", "124 BPM · A minor"), (322, 190, AMP_B, COOL, "NEXT UP", "126 BPM · E minor")]
    left, right = 44, pw - 44
    n = NB
    pitch = (right - left) / n
    head = clamp((t - 1.0) / 6.6)           # the playhead sweeps the sixteen bars
    t_in, t_out = 6 / 16, 10 / 16
    for li, (ly, lh, amps, stops, title, info) in enumerate(lanes):
        d.text((left * S, (ly - 44) * S), "", fill=(255, 255, 255, 0))
        mid = ly + lh / 2
        cols = [gradient(n, 1, tuple(stops))] if False else None
        for i in range(n):
            x = i / n
            if li == 0:
                g = 1.0 if x < t_out else math.cos(clamp((x - t_out) / (1 - t_out)) * math.pi / 2)
            else:
                g = math.sin(clamp(x / t_in) * math.pi / 2)
            h = max(3.0, amps[i] * lh * 0.5 * g * (1.0 + 0.06 * math.sin(t * 9 + i)) if abs(x - head) < 0.02 else amps[i] * lh * 0.5 * g)
            played = x <= head
            c0, c1 = stops[0][1], stops[1][1]
            col = lerp_color(c0, c1, x)
            a = 255 if played else 70
            bx = (left + i * pitch) * S
            d.rounded_rectangle((bx, (mid - h) * S, bx + pitch * 0.62 * S, (mid + h) * S), pitch * 0.31 * S, fill=tuple(int(c) for c in col) + (a,))
    # Bar lines and numbers
    for b in range(16):
        x = left + (right - left) * b / 16
        d.line((x * S, 50 * S, x * S, (ph - 62) * S), fill=(255, 255, 255, 22), width=S)
    # Both in full, and the bass swap
    band_a, band_b = left + (right - left) * t_in, left + (right - left) * t_out
    d.rounded_rectangle((band_a * S, 54 * S, band_b * S, (ph - 66) * S), 20 * S, outline=(255, 255, 255, 46), width=2 * S)
    sw = left + (right - left) * 0.5
    for yy in range(54, ph - 66, 14):
        d.line((sw * S, yy * S, sw * S, (yy + 7) * S), fill=(255, 255, 255, 130), width=2 * S)
    # The playhead
    hx = left + (right - left) * head
    d.rounded_rectangle(((hx - 1.5) * S, 40 * S, (hx + 1.5) * S, (ph - 56) * S), 2 * S, fill=(255, 255, 255, 235))
    panel = panel.resize((pw, ph), Image.LANCZOS)
    alpha = panel.split()[3].point(lambda v: int(v * a_all))
    panel.putalpha(alpha)
    img.paste(panel, (px, int(py + (1 - appear) * 60)), panel)
    oy = int(py + (1 - appear) * 60)
    # Text over it
    for li, (ly, lh, amps, stops, title, info) in enumerate(lanes):
        put_text(img, title, px + 44, oy + ly - 12, 21, 800, lerp_color(stops[0][1], (255, 255, 255), 0.35), 0.9 * a_all, "l", tracking=0.14)
        put_text(img, info, px + 44 + text_width(title, 21, 800, 0.14) + 22, oy + ly - 12, 21, 600, WHITE, 0.55 * a_all, "l")
    for b in range(16):
        x = px + left + (right - left) * (b + 0.5) / 16
        put_text(img, str(b + 1), x, oy + ph - 24, 20, 600, WHITE, 0.45 * a_all, "c")
    swp = OUT(seg(head, 0.47, 0.53)) * a_all
    pill(img, px + sw, oy + 24, 168, 40, fill=(255, 255, 255), alpha=0.92 * swp)
    put_text(img, "Bass swap", px + sw, oy + 24 + 8, 21, 700, (16, 16, 24), swp, "c")
    both = OUT(seg(head, t_in, t_in + 0.05)) * a_all * (1 - seg(head, t_out + 0.02, t_out + 0.07))
    put_text(img, "Both in full", px + (band_a + band_b) / 2, oy + ph - 76 - 4, 21, 700, WHITE, 0.0 + 0.8 * both, "c", tracking=0.02)
    fin = OUT(seg(head, 0.94, 1.0)) * a_all
    put_text(img, "16 bars", px + right - 6, oy + 24 + 8, 24, 800, WHITE, fin, "r", stops=None)
    return img


def s_fan(t):
    img = stage(t, [((255, 70, 110), 0.2, 0.6, 0.5, 0.15, 0.0), ((70, 110, 255), 0.82, 0.4, 0.5, 0.16, 2.0), ((255, 160, 60), 0.5, 0.95, 0.4, 0.09, 4.0)])
    ex = 3.95
    put_words(img, ["Every", "detail,", "considered."], W / 2, 220, 100, t, 0.05, stagger=0.12, exit_at=ex)
    specs = [("albums", 235, 0.52, 0.18, 34), ("browse", 1685, 0.52, 0.28, -34), ("nowplaying2", 960, 0.645, 0.0, 0)]
    for name, cx, sc, delay, ry in specs:
        p = OUT(seg(t, 0.15 + delay, 1.6 + delay))
        q = IO(seg(t, ex, ex + 0.45))
        drift = seg(t, 0, 4.4)
        place(img, screen(name), cx + (-30 if ry > 0 else 30 if ry < 0 else 0) * drift, lerp(1450, 672, p) + 10 * math.sin(t * 0.8 + cx) - q * 80,
              scale=sc * lerp(0.95, 1.0, p) * (1 + 0.03 * drift), rx=lerp(8, 1, p), ry=ry * (0.4 + 0.6 * p), alpha=(1 - q) * clamp(p * 1.5), shadow=0.65)
    return img


def s_outro(t):
    glow = 0.55 + 0.45 * math.exp(-((t - 4.3) / 0.5) ** 2) * 1.0
    img = stage(t, [((130, 70, 255), 0.5, 0.4, 0.5, 0.17 * glow, 0.0), ((235, 40, 90), 0.25, 0.65, 0.45, 0.10 * glow, 2.0), ((40, 205, 185), 0.76, 0.6, 0.45, 0.10 * glow, 4.0)])
    p = OUT(seg(t, 0.2, 2.1))
    pulse = math.exp(-((t - 4.29) / 0.22) ** 2)
    wave(img, W / 2, 425, 560 * (1 + 0.04 * pulse), progress=p, alpha=1, glow=0.8 + 1.1 * pulse, shine=seg(t, 1.8, 3.6))
    put_words(img, ["Meloday", "Music"], W / 2, 640, 88, t, 2.3, weight=800, stagger=0.15, rise=26)
    put_words(img, ["Your", "music.", "Your", "server."], W / 2, 735, 46, t, 4.25, weight=600, stagger=0.1, rise=14, alpha_max=0.82, dur=0.7)
    sub(img, "For Windows 11  ·  Works with your Plex server", W / 2, 818, t, 5.1, size=30, anchor="c", alpha=0.68)
    return img


# ------------------------------------------------------------------ the cut

SCENES = [  # (start, end, function)
    (0.0, 2 * BAR, s_intro),
    (2 * BAR, 4 * BAR, s_title),
    (4 * BAR, 8 * BAR, s_home),
    (8 * BAR, 12 * BAR, s_mfy),
    (12 * BAR, 14 * BAR, s_meloday),
    (14 * BAR, 16 * BAR, s_live),
    (16 * BAR, 20 * BAR, s_automix),
    (20 * BAR, 22 * BAR, s_fan),
    (22 * BAR, DURATION, s_outro),
]
OVERLAP = 0.28


def frame_at(T, index):
    layers = []
    for n, (s, e, fn) in enumerate(SCENES):
        if T < s - OVERLAP or T >= e + OVERLAP:
            continue
        a = 1.0
        if n > 0:
            a = min(a, SOFT(seg(T, s - OVERLAP, s + 0.06)))
        if n < len(SCENES) - 1:
            a = min(a, 1 - SOFT(seg(T, e - 0.06, e + OVERLAP)))
        layers.append((fn, s, a))
    out = None
    for fn, s, a in layers:
        img = fn(T - s)
        if out is None:
            out = img
        else:
            out = Image.blend(out, img, a) if a < 1 else img
    # Fade to black at the very end
    fade = 1 - seg(T, DURATION - 1.5, DURATION - 0.1)
    if fade < 1:
        out = Image.blend(Image.new("RGB", out.size, (0, 0, 0)), out, fade)
    return finish(out, index)


def _init():
    pass


def _render(i):
    return i, frame_at(i / FPS, i).tobytes()


def main():
    args = sys.argv[1:]
    if args and args[0] == "--stills":
        out = WORK / "stills"
        out.mkdir(exist_ok=True)
        for s in args[1:]:
            T = float(s)
            Image.fromarray(frame_at(T, int(T * FPS))).save(out / f"t{T:06.2f}.png")
            print("wrote", out / f"t{T:06.2f}.png")
        return
    if args and args[0] == "--render":
        target = args[1]
        total = int(DURATION * FPS)
        cmd = ["ffmpeg", "-y", "-v", "error", "-stats", "-f", "rawvideo", "-pix_fmt", "rgb24", "-s", f"{W}x{H}", "-r", str(FPS), "-i", "-",
               "-i", str(WORK / "music.wav"),
               "-vf", "scale=out_color_matrix=bt709:out_range=tv,format=yuv420p",
               "-c:v", "libx264", "-preset", "medium", "-crf", "16", "-threads", "4", "-profile:v", "high", "-colorspace", "bt709", "-color_primaries", "bt709", "-color_trc", "bt709", "-color_range", "tv",
               "-af", "loudnorm=I=-15:TP=-1.5:LRA=7", "-c:a", "aac", "-b:a", "256k", "-ar", "48000", "-movflags", "+faststart", "-shortest", target]
        proc = subprocess.Popen(cmd, stdin=subprocess.PIPE)
        workers = max(1, min(8, (os.cpu_count() or 4) - 4))
        batch = 24
        with mp.Pool(workers, initializer=_init) as pool:
            # Never more than two batches ahead of the encoder, so memory stays flat.
            pending = []
            starts = list(range(0, total, batch))
            def submit(k):
                pending.append(pool.map_async(_render, range(k, min(total, k + batch)), chunksize=2))
            for k in starts[:2]:
                submit(k)
            nxt = 2
            done = 0
            while pending:
                for i, data in pending.pop(0).get():
                    proc.stdin.write(data)
                    done += 1
                if nxt < len(starts):
                    submit(starts[nxt])
                    nxt += 1
                if (done // batch) % 10 == 0:
                    print(f"frame {done}/{total}", flush=True)
        proc.stdin.close()
        proc.wait()
        print("done", target)
        return
    print(__doc__)


if __name__ == "__main__":
    main()
