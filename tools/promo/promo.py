"""Renders the Meloday Music promo video: 1920x1080 at 60 fps, with the music from music.py.

    python promo.py --stills 1.5 6 12 ...      write single frames to artifacts/promo/stills (for checking layout)
    python promo.py --render out.mp4           render everything (uses all your cores) and mux the music

The footage is real: screenshots of the app (artifacts/promo/shots2), its own radio artwork videos, the
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
DURATION = 77.3
SHOTS = WORK / "shots2"

SCREENS = {}
CARDS = {}


# ------------------------------------------------------------------ assets

@lru_cache(maxsize=None)
def _shared_mask(w, h, radius):
    return rounded_mask((w, h), radius)


@lru_cache(maxsize=None)
def shot(name):
    """A screenshot with the Plex server's name under "Music" painted out, using the empty sidebar beside it."""
    img = Image.open(SHOTS / f"{name}.png").convert("RGB")
    patch = img.crop((150, 108, 260, 138))
    img.paste(patch, (40, 108))
    if name == "home_top":
        img.paste(img.crop((900, 150, 1100, 182)), (340, 150))
    return img


def screen(name):
    if name not in SCREENS:
        SCREENS[name] = Layer(shot(name), radius=13)
    return SCREENS[name]


def crop_layer(name, box, radius=20):
    key = (name, box, radius)
    if key not in CARDS:
        CARDS[key] = Layer(shot(name).crop(box), radius=radius)
    return CARDS[key]


# ------------------------------------------------------------------ sky

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


# ------------------------------------------------------------------ station artwork

RADIO = [("crimson", "Pulse", 0), ("green", "Hits", 130), ("sapphire", "Library", 60), ("lagoon", "Deep Cuts", 220)]


@lru_cache(maxsize=96)
def _radio_frame(name, index):
    files = sorted((WORK / "radio" / name).glob("*.jpg"))
    return Image.open(files[index % len(files)]).convert("RGB")


@lru_cache(maxsize=8)
def _symbol(width):
    src = Image.open(WORK / "assets" / "MelodayMusic-symbol.png").convert("RGBA")
    h = int(width * src.height / src.width)
    alpha = src.resize((width, h), Image.LANCZOS).split()[3]
    white = Image.new("RGBA", (width, h), (255, 255, 255, 0))
    white.putalpha(alpha)
    return white


def radio_card(name, label, size, t, offset):
    frame = _radio_frame(name, int(t * 60) + offset).resize((size, size), Image.LANCZOS)
    k = size / 1000
    mark = _symbol(int(120 * k))
    frame.paste(mark, (int(70 * k), int(70 * k)), mark)
    put_text(frame, "Live", (70 + 120 + 18) * k, (70 + 53) * k, int(76 * k), 900, (255, 255, 255), 1.0, "l")
    put_text(frame, label, 110 * k, 888 * k, int(88 * k), 700, (255, 255, 255), 1.0, "l")
    return Layer(frame, radius=int(size * 0.052))


# ------------------------------------------------------------------ scenes (t is seconds since the scene's start)

WHITE = (255, 255, 255)


def sub(img, text, x, y, t, t0, size=44, anchor="l", alpha=0.66, exit_at=None):
    p = OUT(seg(t, t0, t0 + 0.9))
    q = IO(seg(t, exit_at, exit_at + 0.4)) if exit_at is not None else 0
    put_text(img, text, x, y, size, 500, WHITE, alpha * p * (1 - q), anchor, blur=(1 - p) * 8, dy=(1 - p) * 18 - q * 10)


def headline(img, words, t, ex, x=W / 2, y=235, size=118, anchor="c", line=None, stops=None):
    put_words(img, words, x, y, size, t, 0.1, anchor=anchor, stagger=0.12, stops=stops, exit_at=ex)
    if line:
        sub(img, line, x + (6 if anchor == "l" else 0), y + 76, t, 0.7, anchor=anchor, exit_at=ex)


def showcase(img, name, t, ex, cy=640, scale=0.66, ry=0.0, delay=0.25, drift=0.0):
    """One screen of the app rising into place, tilted a touch, then leaving."""
    p = OUT(seg(t, delay, delay + 1.4))
    q = IO(seg(t, ex, ex + 0.45))
    place(img, screen(name), W / 2 + drift * seg(t, 0, ex), lerp(cy + 420, cy, p) - q * 60 + 6 * math.sin(t * 0.8),
          scale=scale * lerp(0.94, 1.0, p) * (1 + 0.02 * seg(t, delay, ex)), rx=lerp(10, 2, p), ry=ry * (0.4 + 0.6 * p),
          alpha=p * (1 - q), shadow=0.6)


def s_open(t):
    img = stage(t, [((120, 40, 200), 0.5, 0.45, 0.5, 0.20 * OUT(seg(t, 0, 1.5)), 0.0), ((40, 200, 190), 0.72, 0.55, 0.4, 0.09, 4.0)])
    put_words(img, ["Meet"], W / 2, 470, 64, t, 0.35, weight=600, rise=18, alpha_max=0.7, exit_at=3.7)
    put_words(img, ["Meloday", "Music."], W / 2, 610, 150, t, 0.8, stagger=0.16, stops=BRAND, exit_at=3.7)
    return img


def s_title(t):
    img = stage(t, [((230, 40, 90), 0.2, 0.5, 0.45, 0.17, 0.0), ((130, 70, 255), 0.8, 0.5, 0.45, 0.17, 2.0)])
    put_words(img, ["A", "music", "player"], W / 2, 500, 128, t, 0.15, stagger=0.13, exit_at=3.55)
    size2 = fit_size("for your Plex library.", 128, 800, 1660)
    put_words(img, ["for", "your", "Plex", "library."], W / 2, 672, size2, t, 1.1, stagger=0.13, stops=BRAND, exit_at=3.55)
    return img


def s_home(t):
    img = stage(t, [((120, 70, 255), 0.85, 0.2, 0.5, 0.15, 0.0), ((30, 160, 200), 0.1, 0.9, 0.5, 0.11, 2.0)])
    hx, ex = 2.6, 6.0
    put_words(img, ["Jump", "back", "in."], 150, 262, 120, t, 0.1, anchor="l", stagger=0.12, exit_at=hx)
    sub(img, "Home picks up where you left off.", 156, 338, t, 0.7, exit_at=hx)
    p = OUT(seg(t, 0.15, 1.9))
    rise = IO(seg(t, hx - 0.1, hx + 1.4))
    xfade = IO(seg(t, 4.0, 4.8))
    base = screen("home_top") if xfade < 0.001 else screen("home_2") if xfade > 0.999 else Layer(Image.blend(shot("home_top"), shot("home_2"), xfade), radius=13)
    q = IO(seg(t, ex, ex + 0.4))
    cy = lerp(lerp(1600, 930, p), 548, rise) - q * 60
    scale = lerp(lerp(0.78, 0.83, p), 0.945, rise) + 0.02 * seg(t, hx + 1.4, ex)
    place(img, base, lerp(1000 + 18 * seg(t, 0, hx), 960, rise), cy, scale=scale, rx=lerp(lerp(16, 5, p), 0, rise), ry=lerp(lerp(-8, -3, p), 0, rise), alpha=1 - q)
    return img


def s_mfy(t):
    img = stage(t, [((230, 90, 40), 0.18, 0.42, 0.5, 0.14, 0.0), ((40, 190, 170), 0.85, 0.7, 0.5, 0.13, 2.0), ((140, 70, 220), 0.55, 0.15, 0.4, 0.10, 4.0)])
    ex = 6.0
    headline(img, ["Made", "for", "you."], t, ex, line="Mixes and playlists built from what you actually play.")
    boxes = [(351, 366, 711, 846), (738, 366, 1098, 846), (1119, 366, 1479, 846), (1503, 366, 1863, 846)]
    sc, gap = 1.12, 30
    widths = [(b[2] - b[0]) * sc for b in boxes]
    x = W / 2 - (sum(widths) + gap * 3) / 2
    for i, b in enumerate(boxes):
        layer = crop_layer("mfy", b, radius=22)
        w = widths[i]
        t0 = 0.5 + i * 0.16
        p = OUT(seg(t, t0, t0 + 1.2))
        q = IO(seg(t, ex + i * 0.04, ex + i * 0.04 + 0.4))
        hot = math.exp(-(((t - (2.6 + i * 0.6)) / 0.4) ** 2))
        cy = 690 + (1 - p) * 330 - hot * 22 - q * 40 + 8 * math.sin(t * 0.9 + i)
        place(img, layer, x + w / 2 + (1 - p) * 40, cy, scale=sc * (1 + 0.035 * hot), rx=(1 - p) * 10, ry=(1 - p) * 8, alpha=p * (1 - q), shadow=0.6,
              glow=(255, 255, 255), glow_alpha=0.10 * hot)
        x += w + gap
    return img


def s_meloday(t):
    day = clamp((t - 0.35) / 3.2)
    pos = 6 * IO(day)
    i0, i1, f = min(5, int(pos)), min(6, int(pos) + 1), pos - int(pos)
    img = stage(t, [(tuple(int(c) for c in lerp_color(SKY[i0][4][3][0], SKY[i1][4][3][0], f)), 0.28, 0.5, 0.55, 0.20, 0.0),
                    (tuple(int(c) for c in lerp_color(SKY[i0][1], SKY[i1][1], f)), 0.7, 0.4, 0.6, 0.28, 2.0)])
    ex = 4.05
    p = OUT(seg(t, 0.0, 1.0))
    q = IO(seg(t, ex, ex + 0.35))
    cover = Layer(sky_cover(pos, 620, t), radius=26)
    place(img, cover, 470 + (1 - p) * -120, 548, scale=lerp(0.94, 1.0, p), rx=0, ry=lerp(18, 7, p) + 2.5 * math.sin(t * 0.8), alpha=p * (1 - q), shadow=0.6,
          glow=lerp_color(SKY[i0][1], SKY[i1][1], f), glow_alpha=0.35)
    put_words(img, ["Meloday."], 900, 420, 150, t, 0.2, anchor="l", stops=BRAND, exit_at=ex)
    sub(img, "A playlist that changes through the day,", 906, 498, t, 0.55, size=44, exit_at=ex)
    sub(img, "from what you play at that time.", 906, 556, t, 0.65, size=44, exit_at=ex)
    names = [s[0] for s in SKY]
    for r, row in enumerate([names[:4], names[4:]]):
        x = 906
        for name in row:
            idx = names.index(name)
            w = text_width(name, 27, 600) + 44
            a = OUT(seg(t, 0.9 + idx * 0.06, 1.5 + idx * 0.06)) * (1 - q)
            active = clamp(1 - abs(pos - idx) * 1.6)
            cy = 660 + r * 66
            pill(img, x + w / 2, cy, w, 48, fill=(255, 255, 255), alpha=(0.08 + 0.78 * active) * a)
            put_text(img, name, x + w / 2, cy + 9, 27, 600, lerp_color((255, 255, 255), (16, 16, 24), active), a, "c")
            x += w + 14
    return img


def s_live(t):
    img = stage(t, [((235, 40, 80), 0.18, 0.5, 0.5, 0.20, 0.0), ((30, 90, 230), 0.5, 0.8, 0.5, 0.14, 2.0), ((40, 200, 150), 0.85, 0.4, 0.5, 0.17, 4.0)])
    ex = 4.05
    headline(img, ["Always", "on."], t, ex, y=250, size=128, line="Live stations. Tune in and you join mid-song.")
    size, gap = 372, 36
    x = W / 2 - (4 * size + 3 * gap) / 2
    for i, (name, label, off) in enumerate(RADIO):
        p = OUT(seg(t, 0.25 + i * 0.12, 1.45 + i * 0.12))
        q = IO(seg(t, ex + i * 0.04, ex + i * 0.04 + 0.4))
        layer = radio_card(name, label, size, t + 4, off)
        place(img, layer, x + size / 2 + (1 - p) * 220, 640 + 12 * math.sin(t * 1.1 + i * 0.9) - q * 30, scale=lerp(0.9, 1.0, p), ry=(1 - p) * -22 + 3 * math.sin(t * 0.7 + i),
              alpha=p * (1 - q), shadow=0.55)
        x += size + gap
    return img


def s_pulse(t):
    img = stage(t, [((40, 200, 150), 0.2, 0.3, 0.5, 0.16, 0.0), ((255, 90, 140), 0.8, 0.7, 0.5, 0.14, 2.0)])
    ex = 4.05
    headline(img, ["Your", "station,", "your", "schedule."], t, ex, y=190, size=96, line="Plan programs in the Pulse Guide.")
    p = OUT(seg(t, 0.3, 1.6))
    q = IO(seg(t, ex, ex + 0.4))
    layer = crop_layer("pulse", (351, 150, 1875, 950), radius=22)
    place(img, layer, W / 2 + 40 * seg(t, 0, ex), lerp(1200, 680, p) - q * 50, scale=0.92 * (1 + 0.025 * seg(t, 0.3, ex)), rx=lerp(12, 4, p), ry=-4,
          alpha=p * (1 - q), shadow=0.6)
    return img


def _wavebars(n, seed, phase):
    rng = np.random.default_rng(seed)
    amps = []
    for i in range(n):
        beat = ((i / n) * 64 + phase) % 1.0
        kick = math.exp(-beat * 4.0)
        body = 0.28 + 0.18 * math.sin(i * 0.31 + seed) + 0.1 * math.sin(i * 0.07)
        amps.append(clamp(body + 0.55 * kick * rng.uniform(0.6, 1.0) + rng.uniform(-0.05, 0.08), 0.1, 1.0))
    return amps


NB = 128
AMP_A = _wavebars(NB, 11, 0.0)
AMP_B = _wavebars(NB, 29, 0.35)


def s_automix(t):
    img = stage(t, [((140, 60, 255), 0.22, 0.35, 0.5, 0.20, 0.0), ((40, 210, 190), 0.8, 0.75, 0.5, 0.17, 2.0), ((255, 90, 80), 0.15, 0.8, 0.4, 0.10, 4.0)])
    ex = 8.1
    slam = OUT(seg(t, 0.0, 0.9))
    put_text(img, "Beat for beat.", 150, 238, 128, 800, WHITE, clamp(slam * 1.6) * (1 - IO(seg(t, ex, ex + 0.4))), "l", scale=lerp(1.06, 1.0, slam), blur=(1 - slam) * 18)
    sub(img, "AutoMix blends one song into the next.", 156, 308, t, 0.55, exit_at=3.9)
    sub(img, "Tempo and key matched, up to sixteen bars long.", 156, 308, t, 4.2, exit_at=ex)

    px, py, pw, ph = 150, 392, 1620, 560
    appear = OUT(seg(t, 0.5, 1.6))
    a_all = appear * (1 - IO(seg(t, ex, ex + 0.45)))
    S = 2
    panel = Image.new("RGBA", (pw * S, ph * S), (0, 0, 0, 0))
    d = ImageDraw.Draw(panel)
    d.rounded_rectangle((0, 0, pw * S - 1, ph * S - 1), 30 * S, fill=(255, 255, 255, 14), outline=(255, 255, 255, 30), width=2 * S)
    lanes = [(70, 190, AMP_A, WARM, "OUTGOING", "124 BPM · A minor"), (322, 190, AMP_B, COOL, "NEXT UP", "126 BPM · E minor")]
    left, right = 44, pw - 44
    pitch = (right - left) / NB
    head = clamp((t - 1.0) / 6.6)
    t_in, t_out = 6 / 16, 10 / 16
    for li, (ly, lh, amps, stops, title, info) in enumerate(lanes):
        mid = ly + lh / 2
        for i in range(NB):
            x = i / NB
            if li == 0:
                g = 1.0 if x < t_out else math.cos(clamp((x - t_out) / (1 - t_out)) * math.pi / 2)
            else:
                g = math.sin(clamp(x / t_in) * math.pi / 2)
            h = max(3.0, amps[i] * lh * 0.5 * g)
            col = lerp_color(stops[0][1], stops[1][1], x)
            bx = (left + i * pitch) * S
            d.rounded_rectangle((bx, (mid - h) * S, bx + pitch * 0.62 * S, (mid + h) * S), pitch * 0.31 * S, fill=tuple(int(c) for c in col) + (255 if x <= head else 70,))
    for b in range(16):
        x = left + (right - left) * b / 16
        d.line((x * S, 50 * S, x * S, (ph - 62) * S), fill=(255, 255, 255, 22), width=S)
    band_a, band_b = left + (right - left) * t_in, left + (right - left) * t_out
    d.rounded_rectangle((band_a * S, 54 * S, band_b * S, (ph - 66) * S), 20 * S, outline=(255, 255, 255, 46), width=2 * S)
    sw = left + (right - left) * 0.5
    for yy in range(54, ph - 66, 14):
        d.line((sw * S, yy * S, sw * S, (yy + 7) * S), fill=(255, 255, 255, 130), width=2 * S)
    hx = left + (right - left) * head
    d.rounded_rectangle(((hx - 1.5) * S, 40 * S, (hx + 1.5) * S, (ph - 56) * S), 2 * S, fill=(255, 255, 255, 235))
    panel = panel.resize((pw, ph), Image.LANCZOS)
    panel.putalpha(panel.split()[3].point(lambda v: int(v * a_all)))
    oy = int(py + (1 - appear) * 60)
    img.paste(panel, (px, oy), panel)
    for ly, lh, amps, stops, title, info in lanes:
        put_text(img, title, px + 44, oy + ly - 12, 21, 800, lerp_color(stops[0][1], (255, 255, 255), 0.35), 0.9 * a_all, "l", tracking=0.14)
        put_text(img, info, px + 44 + text_width(title, 21, 800, 0.14) + 22, oy + ly - 12, 21, 600, WHITE, 0.55 * a_all, "l")
    for b in range(16):
        put_text(img, str(b + 1), px + left + (right - left) * (b + 0.5) / 16, oy + ph - 24, 20, 600, WHITE, 0.45 * a_all, "c")
    swp = OUT(seg(head, 0.47, 0.53)) * a_all
    pill(img, px + sw, oy + 24, 168, 40, fill=(255, 255, 255), alpha=0.92 * swp)
    put_text(img, "Bass swap", px + sw, oy + 24 + 8, 21, 700, (16, 16, 24), swp, "c")
    both = OUT(seg(head, t_in, t_in + 0.05)) * a_all * (1 - seg(head, t_out + 0.02, t_out + 0.07))
    put_text(img, "Both in full", px + (band_a + band_b) / 2, oy + ph - 80, 21, 700, WHITE, 0.8 * both, "c", tracking=0.02)
    put_text(img, "16 bars", px + right - 6, oy + 32, 24, 800, WHITE, OUT(seg(head, 0.94, 1.0)) * a_all, "r")
    return img


def s_sound(t):
    img = stage(t, [((150, 70, 255), 0.25, 0.4, 0.5, 0.18, 0.0), ((45, 215, 190), 0.8, 0.6, 0.5, 0.15, 2.0)])
    ex = 4.05
    headline(img, ["Knows", "how", "it", "sounds."], t, ex, y=190, size=104, line="The Meloday Engine listens to your songs on your PC. No Plex Pass needed.")
    p = OUT(seg(t, 0.3, 1.6))
    q = IO(seg(t, ex, ex + 0.4))
    layer = crop_layer("layers", (366, 68, 1554, 880), radius=22)
    place(img, layer, W / 2, lerp(1250, 680, p) - q * 50, scale=0.86 * (1 + 0.03 * seg(t, 0.3, ex)), rx=lerp(10, 3, p), ry=5, alpha=p * (1 - q), shadow=0.6)
    return img


def s_artist(t):
    img = stage(t, [((255, 170, 60), 0.3, 0.3, 0.5, 0.16, 0.0), ((60, 150, 90), 0.7, 0.7, 0.5, 0.14, 2.0)])
    ex = 4.05
    headline(img, ["Every", "artist,", "up", "close."], t, ex, y=170, size=96)
    showcase(img, "artist", t, ex, cy=640, scale=0.7, ry=-6, drift=-30)
    return img


def s_trends(t):
    img = stage(t, [((255, 80, 120), 0.2, 0.6, 0.5, 0.15, 0.0), ((60, 120, 255), 0.82, 0.4, 0.5, 0.16, 2.0)])
    ex = 4.05
    headline(img, ["See", "how", "you", "listen."], t, ex, y=170, size=96)
    xfade = IO(seg(t, 2.0, 2.6))
    base = screen("trends") if xfade < 0.001 else screen("trends_3") if xfade > 0.999 else Layer(Image.blend(shot("trends"), shot("trends_3"), xfade), radius=13)
    p = OUT(seg(t, 0.25, 1.65))
    q = IO(seg(t, ex, ex + 0.45))
    place(img, base, W / 2 + 30 * seg(t, 0, ex), lerp(1060, 640, p) - q * 60, scale=0.7 * lerp(0.94, 1.0, p), rx=lerp(10, 2, p), ry=6 * (0.4 + 0.6 * p),
          alpha=p * (1 - q), shadow=0.6)
    return img


def s_nowplaying(t):
    img = stage(t, [((200, 200, 210), 0.3, 0.45, 0.5, 0.10, 0.0), ((150, 70, 255), 0.75, 0.6, 0.5, 0.14, 2.0)])
    ex = 4.05
    headline(img, ["Watch", "it", "play."], t, ex, y=170, size=104, line="Visualizers draw the music as you hear it.")
    showcase(img, "np", t, ex, cy=650, scale=0.7, ry=-5, drift=-25)
    return img


def s_mfy_rails(t):
    img = stage(t, [((255, 130, 60), 0.25, 0.35, 0.5, 0.16, 0.0), ((70, 110, 255), 0.8, 0.65, 0.5, 0.15, 2.0)])
    ex = 4.05
    headline(img, ["A", "playlist", "for", "every", "mood."], t, ex, y=170, size=96)
    showcase(img, "mfy_3", t, ex, cy=640, scale=0.7, ry=6, drift=30)
    return img


FEATURES = [
    "Meloday", "Made for You", "Live radio", "Pulse Guide", "AutoMix", "Sonic Radio", "Sonic Adventure", "Meloday Engine",
    "Find Your Mood", "By Energy", "For Every Moment", "Genre Essentials", "Synced lyrics", "Lyric Studio", "Visualizers",
    "Trends", "Search by sound", "Smart playlists", "Pins", "Mini player", "Last.fm", "ListenBrainz", "MusicBrainz",
    "Guest curators", "Downloads", "Sleep timer", "Cast", "Artist logos", "Folders",
]
MONTAGE = ["home_top", "mfy_3", "radio", "browse", "artist", "trends_2", "mfy_3", "radio_2", "home_3", "trends_3",
           "np", "home_3", "pulse", "browse", "mfy", "artist", "trends", "layers", "home_2", "mfy_3", "radio", "home_top", "home_3", "trends_2",
           "np", "browse", "pulse", "radio_2", "home_top", "mfy", "artist", "trends_3"]


def s_montage(t):
    img = stage(t, [((150, 70, 255), 0.3, 0.3, 0.6, 0.16, 0.0), ((235, 40, 90), 0.75, 0.7, 0.6, 0.14, 2.0), ((45, 215, 190), 0.5, 0.95, 0.5, 0.10, 4.0)])
    ex = 5 * BAR - 0.3
    fade = OUT(seg(t, 0.0, 0.6)) * (1 - IO(seg(t, ex, ex + 0.3)))
    # The feature list fills in behind the screens, row by row.
    rows = [FEATURES[i::6] for i in range(6)]
    for r, words in enumerate(rows):
        text = "   ·   ".join(words)
        y = 150 + r * 150
        speed = (60 + r * 14) * (1 if r % 2 else -1)
        shown = OUT(seg(t, 0.2 + r * 0.45, 1.0 + r * 0.45))
        x = W / 2 + speed * t + (-1 if r % 2 else 1) * 220
        put_text(img, text, x, y, 92, 800, WHITE, 0.16 * shown * fade, "c", stops=BRAND if r % 3 == 1 else None)
    beat = BAR / 4
    k = int(t / beat) if t < 2 * BAR else 8 + int((t - 2 * BAR) / (beat / 2))
    k = min(k, len(MONTAGE) - 1)
    local = t - (k * beat if k < 8 else 2 * BAR + (k - 8) * beat / 2)
    p = OUT(seg(local, 0.0, 0.16))
    tilt = (-1) ** k
    place(img, screen(MONTAGE[k]), W / 2 + tilt * 18, 560 + (1 - p) * 30, scale=0.6 * lerp(1.04, 1.0, p), rx=3, ry=tilt * 7, alpha=fade, shadow=0.7)
    return img


INTRO = WORK / "intro" / "frames"
INTRO_FIRST, INTRO_LAST = 50, 235
INTRO_AT = BAR - (150 - INTRO_FIRST) / FPS     # the wave turns white on the final chord


@lru_cache(maxsize=4)
def _intro_frame(n):
    return Image.open(INTRO / f"{n:04d}.png").convert("RGB")


def s_outro(t):
    """The app's own opening animation, recorded from the screen, then where it runs."""
    n = INTRO_FIRST + int(max(0.0, t - INTRO_AT) * FPS)
    img = _intro_frame(min(n, INTRO_LAST)).copy()
    sub(img, "Free and open source  ·  For Windows 10 and 11", W / 2, 890, t, INTRO_AT + 2.4, size=42, anchor="c", alpha=0.9)
    sub(img, "Works with your Plex server", W / 2, 955, t, INTRO_AT + 2.7, size=32, anchor="c", alpha=0.65)
    return img


# ------------------------------------------------------------------ the cut

SCENES = [  # (start, end, function), in bars of the music
    (0.0, 2 * BAR, s_open),
    (2 * BAR, 4 * BAR, s_title),
    (4 * BAR, 7 * BAR, s_home),
    (7 * BAR, 10 * BAR, s_mfy),
    (10 * BAR, 12 * BAR, s_meloday),
    (12 * BAR, 14 * BAR, s_live),
    (14 * BAR, 16 * BAR, s_pulse),
    (16 * BAR, 18 * BAR, s_sound),
    (18 * BAR, 22 * BAR, s_automix),
    (22 * BAR, 24 * BAR, s_artist),
    (24 * BAR, 26 * BAR, s_trends),
    (26 * BAR, 28 * BAR, s_nowplaying),
    (28 * BAR, 33 * BAR, s_montage),
    (33 * BAR, DURATION, s_outro),
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
        workers = int(os.environ.get("PROMO_WORKERS", max(1, min(8, (os.cpu_count() or 4) - 4))))
        batch = 24
        with mp.Pool(workers, initializer=_init, maxtasksperchild=240) as pool:
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
