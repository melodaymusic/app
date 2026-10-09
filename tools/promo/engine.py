"""A small compositing engine for the Meloday Music promo: easing, text, perspective-tilted windows with
soft shadows, brand-gradient wave, backgrounds and grain. Everything is drawn with Pillow and numpy."""
import math
from functools import lru_cache
from pathlib import Path

import numpy as np
from PIL import Image, ImageChops, ImageDraw, ImageFilter, ImageFont

W, H, FPS = 1920, 1080, 60
ROOT = Path(__file__).resolve().parents[2]
WORK = ROOT / "artifacts" / "promo"
FONT = ROOT / "src" / "HumeMusic.App" / "Assets" / "Fonts" / "Manrope-Variable.ttf"

# The brand wave's colours, left to right (from the app icon).
BRAND = [(0.0, (235, 30, 85)), (0.28, (150, 70, 255)), (0.55, (45, 215, 190)), (0.8, (150, 205, 40)), (1.0, (250, 195, 25))]
WARM = [(0.0, (255, 90, 80)), (1.0, (255, 200, 60))]
COOL = [(0.0, (60, 220, 200)), (1.0, (150, 110, 255))]


# ------------------------------------------------------------------ easing

def clamp(x, lo=0.0, hi=1.0):
    return lo if x < lo else hi if x > hi else x


def seg(t, a, b):
    """0 before a, 1 after b, linear between."""
    return clamp((t - a) / (b - a)) if b > a else (1.0 if t >= a else 0.0)


def _bezier_lut(x1, y1, x2, y2, n=1024):
    s = np.linspace(0, 1, n * 4)
    x = 3 * (1 - s) ** 2 * s * x1 + 3 * (1 - s) * s ** 2 * x2 + s ** 3
    y = 3 * (1 - s) ** 2 * s * y1 + 3 * (1 - s) * s ** 2 * y2 + s ** 3
    return x, y


_LUTS = {}


def bez(x1, y1, x2, y2):
    key = (x1, y1, x2, y2)
    if key not in _LUTS:
        _LUTS[key] = _bezier_lut(*key)
    xs, ys = _LUTS[key]
    return lambda t: float(np.interp(clamp(t), xs, ys))


OUT = bez(0.2, 0.0, 0.0, 1.0)        # the app's own curve: quick to respond, gentle to settle
IO = bez(0.45, 0.0, 0.15, 1.0)
SOFT = bez(0.25, 0.1, 0.25, 1.0)


def lerp(a, b, t):
    return a + (b - a) * t


def lerp_color(a, b, t):
    return tuple(lerp(x, y, t) for x, y in zip(a, b))


# ------------------------------------------------------------------ text

@lru_cache(maxsize=None)
def font(size, weight):
    f = ImageFont.truetype(str(FONT), int(size))
    f.set_variation_by_axes([weight])
    return f


@lru_cache(maxsize=2048)
def text_mask(text, size, weight, tracking=0.0):
    """An 'L' mask of a line of text, and where its baseline and left edge sit inside it."""
    f = font(size, weight)
    ascent, descent = f.getmetrics()
    pad = int(size * 0.35)
    if tracking == 0:
        width = int(math.ceil(f.getlength(text))) + 2 * pad
    else:
        width = int(math.ceil(sum(f.getlength(c) + tracking * size for c in text))) + 2 * pad
    mask = Image.new("L", (width, ascent + descent + 2 * pad), 0)
    d = ImageDraw.Draw(mask)
    if tracking == 0:
        d.text((pad, pad), text, font=f, fill=255)
    else:
        x = pad
        for c in text:
            d.text((x, pad), c, font=f, fill=255)
            x += f.getlength(c) + tracking * size
    return mask, pad, pad + ascent


def text_width(text, size, weight, tracking=0.0):
    f = font(size, weight)
    if tracking == 0:
        return f.getlength(text)
    return sum(f.getlength(c) + tracking * size for c in text) - tracking * size


def fit_size(text, size, weight, max_width, tracking=0.0):
    while size > 20 and text_width(text, size, weight, tracking) > max_width:
        size -= 2
    return size


@lru_cache(maxsize=256)
def gradient(width, height, stops, vertical=False):
    n = height if vertical else width
    xs = np.linspace(0, 1, max(2, n))
    cols = np.zeros((len(xs), 3), np.float32)
    pos = [p for p, _ in stops]
    for c in range(3):
        cols[:, c] = np.interp(xs, pos, [col[c] for _, col in stops])
    arr = np.repeat(cols[:, None, :], width, axis=1) if vertical else np.repeat(cols[None, :, :], height, axis=0)
    return Image.fromarray(arr.astype(np.uint8), "RGB")


def put_text(canvas, text, x, y, size, weight=800, fill=(255, 255, 255), alpha=1.0, anchor="l", tracking=0.0,
             scale=1.0, blur=0.0, stops=None, dy=0.0):
    """Draws text with its baseline at y. anchor: l (x is the left), c (centre), r (right). fill may be a colour or stops (gradient)."""
    if alpha <= 0.002:
        return
    mask, pad, base = text_mask(text, int(size), int(weight), round(tracking, 4))
    w, h = mask.size
    if scale != 1.0:
        mask = mask.resize((max(1, int(w * scale)), max(1, int(h * scale))), Image.LANCZOS)
        pad, base = pad * scale, base * scale
    if blur > 0.3:
        mask = mask.filter(ImageFilter.GaussianBlur(blur))
    if alpha < 1:
        mask = mask.point(lambda v: int(v * alpha))
    inner = mask.size[0] - 2 * pad
    left = x - pad - (inner / 2 if anchor == "c" else inner if anchor == "r" else 0)
    top = y + dy - base
    box = (int(round(left)), int(round(top)))
    if stops:
        # The gradient spans the text itself, not its padding.
        g = gradient(int(inner) + 1, mask.size[1], tuple(stops))
        full = Image.new("RGB", mask.size, stops[0][1])
        full.paste(g, (int(pad), 0))
        canvas.paste(full, box, mask)
    else:
        canvas.paste(Image.new("RGB", mask.size, tuple(int(c) for c in fill)), box, mask)


def sample_stops(stops, u):
    u = clamp(u)
    for (p0, c0), (p1, c1) in zip(stops, stops[1:]):
        if u <= p1:
            return tuple(int(round(c)) for c in lerp_color(c0, c1, (u - p0) / (p1 - p0) if p1 > p0 else 0))
    return stops[-1][1]


def put_words(canvas, words, cx_or_x, y, size, t, t0, weight=800, stagger=0.11, anchor="c", fill=(255, 255, 255), stops=None,
              exit_at=None, tracking=0.0, rise=46, alpha_max=1.0, dur=0.8):
    """A line of words that rise and sharpen into place one after another (and, optionally, leave together)."""
    gap = text_width(" ", size, weight) * (0.9 if tracking == 0 else 1.0)
    widths = [text_width(w, size, weight, tracking) for w in words]
    total = sum(widths) + gap * (len(words) - 1)
    x = cx_or_x - (total / 2 if anchor == "c" else total if anchor == "r" else 0)
    left = x
    for i, w in enumerate(words):
        p = OUT(seg(t, t0 + i * stagger, t0 + i * stagger + dur))
        q = IO(seg(t, exit_at + i * 0.03, exit_at + i * 0.03 + 0.4)) if exit_at is not None else 0.0
        a = clamp(p * 1.4) * (1 - q) * alpha_max
        piece = None
        if stops:
            u0, u1 = (x - left) / total, (x - left + widths[i]) / total
            piece = [(round(k / 6, 4), sample_stops(stops, lerp(u0, u1, k / 6))) for k in range(7)]
        put_text(canvas, w, x, y, size, weight, fill, a, "l", tracking, scale=lerp(0.94, 1.0, p), blur=(1 - p) * 16 + q * 10,
                 stops=piece, dy=(1 - p) * rise - q * 24)
        x += widths[i] + gap


# ------------------------------------------------------------------ backgrounds and finishing

_VIGNETTE = None


def _vignette():
    global _VIGNETTE
    if _VIGNETTE is None:
        yy, xx = np.mgrid[0:135, 0:240]
        d = np.sqrt(((xx - 120) / 120) ** 2 + ((yy - 67) / 72) ** 2)
        _VIGNETTE = (1 - 0.55 * np.clip(d - 0.35, 0, 1) ** 1.6)[..., None].astype(np.float32)
    return _VIGNETTE


def stage(t, blobs, base=(8, 8, 12), drift=1.0):
    """A dark stage with a few big, slow, soft colour glows. blobs: (colour, x, y, radius, intensity, phase)."""
    yy, xx = np.mgrid[0:135, 0:240].astype(np.float32)
    img = np.zeros((135, 240, 3), np.float32) + np.array(base, np.float32)
    for colour, bx, by, br, inten, ph in blobs:
        cx = (bx + 0.05 * math.sin(t * 0.35 * drift + ph)) * 240
        cy = (by + 0.06 * math.cos(t * 0.29 * drift + ph * 1.7)) * 135
        r = br * 135
        g = np.exp(-(((xx - cx) ** 2 + (yy - cy) ** 2) / (2 * r * r)))[..., None]
        img += g * np.array(colour, np.float32) * inten
    img *= _vignette()
    return Image.fromarray(np.clip(img, 0, 255).astype(np.uint8), "RGB").resize((W, H), Image.BICUBIC)


_GRAIN = None


def grain(i):
    global _GRAIN
    if _GRAIN is None:
        rng = np.random.default_rng(3)
        _GRAIN = [rng.normal(0, 1.1, (H, W, 1)).astype(np.float32) for _ in range(6)]
    return _GRAIN[i % 6]


def finish(img, i):
    """Film-like dither: kills banding in the gradients."""
    a = np.asarray(img, np.float32) + grain(i)
    return np.clip(a, 0, 255).astype(np.uint8)


# ------------------------------------------------------------------ rounded things, windows, shadows

def rounded_mask(size, radius, ss=4):
    w, h = size
    m = Image.new("L", (w * ss, h * ss), 0)
    ImageDraw.Draw(m).rounded_rectangle((0, 0, w * ss - 1, h * ss - 1), radius * ss, fill=255)
    return m.resize((w, h), Image.LANCZOS)


@lru_cache(maxsize=64)
def _mask_and_ring(w, h, radius, edge):
    mask = rounded_mask((w, h), radius)
    ring = None
    if edge:
        # A hairline of light round the edge, as a glass window has.
        r = Image.new("L", (w, h), 0)
        ImageDraw.Draw(r).rounded_rectangle((0, 0, w - 1, h - 1), radius, outline=255, width=2)
        ring = ImageChops.multiply(r.point(lambda v: int(v * 0.10)), mask)
    return mask, ring


class Layer:
    """An RGBA picture (a window, a card, a cover) that can be placed, scaled and tilted."""

    PAD = 3

    def __init__(self, rgb, radius=14, edge=True):
        rgb = rgb.convert("RGB")
        w, h = rgb.size
        mask, ring = _mask_and_ring(w, h, radius, edge)
        img = Image.new("RGBA", (w + 2 * self.PAD, h + 2 * self.PAD), (0, 0, 0, 0))
        rgba = rgb.convert("RGBA")
        rgba.putalpha(mask)
        if ring is not None:
            white = Image.new("RGBA", (w, h), (255, 255, 255, 0))
            white.putalpha(ring)
            rgba = Image.alpha_composite(rgba, white)
        img.paste(rgba, (self.PAD, self.PAD))
        self.img = img
        self.size = (w, h)


def _homography(dst, src):
    a = []
    b = []
    for (x, y), (u, v) in zip(dst, src):
        a.append([x, y, 1, 0, 0, 0, -u * x, -u * y])
        a.append([0, 0, 0, x, y, 1, -v * x, -v * y])
        b += [u, v]
    return np.linalg.solve(np.array(a, np.float64), np.array(b, np.float64))


def _quad(w, h, cx, cy, scale, rx, ry, dist=2600.0):
    hw, hh = w * scale / 2, h * scale / 2
    pts = []
    sy, cyaw = math.sin(math.radians(ry)), math.cos(math.radians(ry))
    sx, cxp = math.sin(math.radians(rx)), math.cos(math.radians(rx))
    for px, py in ((-hw, -hh), (hw, -hh), (hw, hh), (-hw, hh)):
        x, y, z = px, py, 0.0
        x, z = x * cyaw + z * sy, -x * sy + z * cyaw      # turn about the vertical axis
        y, z = y * cxp - z * sx, y * sx + z * cxp         # tip about the horizontal axis
        k = dist / (dist + z)
        pts.append((cx + x * k, cy + y * k))
    return pts


def place(canvas, layer, cx, cy, scale=1.0, rx=0.0, ry=0.0, alpha=1.0, shadow=0.55, shadow_dy=34, glow=None, glow_alpha=0.0, sharpen=True):
    """Draws a Layer on the canvas, scaled and tilted in perspective, with a soft shadow (and optionally a coloured glow)."""
    if alpha <= 0.003 or scale <= 0.01:
        return
    w, h = layer.img.size
    quad = _quad(w, h, cx, cy, scale, rx, ry)
    xs, ys = [p[0] for p in quad], [p[1] for p in quad]
    x0, y0 = int(math.floor(min(xs))) - 2, int(math.floor(min(ys))) - 2
    x1, y1 = int(math.ceil(max(xs))) + 2, int(math.ceil(max(ys))) + 2
    bw, bh = x1 - x0, y1 - y0
    if bw < 4 or bh < 4 or x1 < -200 or x0 > W + 200 or y1 < -200 or y0 > H + 200:
        return
    dst = [(p[0] - x0, p[1] - y0) for p in quad]
    src = [(0, 0), (w, 0), (w, h), (0, h)]
    coeffs = _homography(dst, src)
    resample = Image.BICUBIC
    moved = layer.img.transform((bw, bh), Image.PERSPECTIVE, tuple(coeffs), resample)
    if sharpen and scale > 1.04:
        moved_rgb = moved.convert("RGB").filter(ImageFilter.UnsharpMask(radius=1.4, percent=int(min(90, 60 * (scale - 1) * 4)), threshold=2))
        moved_rgb = moved_rgb.convert("RGBA")
        moved_rgb.putalpha(moved.split()[3])
        moved = moved_rgb
    a = moved.split()[3]
    if alpha < 1:
        a = a.point(lambda v: int(v * alpha))
        moved.putalpha(a)

    # Shadow and glow: made small and blurred, then scaled up (cheap, and soft anyway).
    if shadow > 0 or (glow is not None and glow_alpha > 0):
        margin = 150
        sw, sh = (bw + 2 * margin) // 4, (bh + 2 * margin) // 4
        small = Image.new("L", (sw, sh), 0)
        small.paste(a.resize((max(1, bw // 4), max(1, bh // 4)), Image.BILINEAR), (margin // 4, margin // 4))
        if shadow > 0:
            blurred = small.filter(ImageFilter.GaussianBlur(11)).resize((sw * 4, sh * 4), Image.BILINEAR)
            blurred = blurred.point(lambda v: int(v * shadow * alpha))
            canvas.paste(Image.new("RGB", blurred.size, (0, 0, 0)), (x0 - margin, y0 - margin + int(shadow_dy)), blurred)
        if glow is not None and glow_alpha > 0:
            g = small.filter(ImageFilter.GaussianBlur(16)).resize((sw * 4, sh * 4), Image.BILINEAR).point(lambda v: int(v * glow_alpha * alpha))
            region = canvas.crop((x0 - margin, y0 - margin, x0 - margin + g.size[0], y0 - margin + g.size[1]))
            tinted = Image.new("RGB", g.size, tuple(int(c) for c in glow))
            region.paste(tinted, (0, 0), g)
            canvas.paste(region, (x0 - margin, y0 - margin))
    canvas.paste(moved, (x0, y0), moved)


# ------------------------------------------------------------------ the brand wave

@lru_cache(maxsize=8)
def _wave_alpha(width):
    sym = Image.open(WORK / "assets" / "MelodayMusic-symbol.png").convert("RGBA")
    a = sym.split()[3]
    h = int(round(a.size[1] * width / a.size[0]))
    return a.resize((width, h), Image.LANCZOS)


def wave(canvas, cx, cy, width, progress=1.0, alpha=1.0, glow=1.0, shine=0.0):
    """The brand wave in its gradient, drawn on from the left (progress 0-1), with a glow. shine (0-1) sweeps a bright highlight along it."""
    if alpha <= 0.002 or progress <= 0.001:
        return
    a = _wave_alpha(int(width))
    w, h = a.size
    grad = gradient(w, h, tuple(BRAND))
    reveal = np.zeros((h, w), np.float32)
    edge = max(1, int(w * 0.06))
    xs = np.arange(w)
    front = progress * (w + edge)
    reveal[:] = np.clip((front - xs) / edge, 0, 1)[None, :]
    m = np.asarray(a, np.float32) / 255 * reveal * alpha
    rgb = np.asarray(grad, np.float32) * m[..., None]
    if shine > 0:
        pos = (shine * 1.4 - 0.2) * w
        hl = np.exp(-((xs - pos) / (w * 0.08)) ** 2)[None, :, None]
        rgb = rgb + (255 - rgb) * hl * 0.55 * m[..., None]
    layer = Image.fromarray(np.clip(rgb, 0, 255).astype(np.uint8), "RGB")
    lmask = Image.fromarray(np.clip(m * 255, 0, 255).astype(np.uint8), "L")
    pad = 100
    box = Image.new("RGB", (w + 2 * pad, h + 2 * pad), (0, 0, 0))
    box.paste(layer, (pad, pad))
    x, y = int(cx - box.size[0] / 2), int(cy - box.size[1] / 2)
    region = canvas.crop((x, y, x + box.size[0], y + box.size[1]))
    if glow > 0:
        # The glow is the colour's own, spread wide and faint; the wave then sits over it in full strength.
        g = box.filter(ImageFilter.GaussianBlur(34)).point(lambda v: int(min(255, v * 0.55 * glow)))
        region = ImageChops.add(region, g)
    region.paste(layer, (pad, pad), lmask)
    canvas.paste(region, (x, y))


def pill(canvas, cx, cy, w, h, fill=(255, 255, 255), alpha=0.12, outline=None, outline_alpha=0.0):
    """A soft capsule (for chips and captions)."""
    ss = 3
    m = Image.new("L", (int(w * ss), int(h * ss)), 0)
    ImageDraw.Draw(m).rounded_rectangle((0, 0, m.size[0] - 1, m.size[1] - 1), h * ss / 2, fill=255)
    m = m.resize((int(w), int(h)), Image.LANCZOS)
    x, y = int(cx - w / 2), int(cy - h / 2)
    canvas.paste(Image.new("RGB", m.size, tuple(int(c) for c in fill)), (x, y), m.point(lambda v: int(v * alpha)))
    if outline is not None and outline_alpha > 0:
        ring = Image.new("L", (int(w * ss), int(h * ss)), 0)
        ImageDraw.Draw(ring).rounded_rectangle((0, 0, ring.size[0] - 1, ring.size[1] - 1), h * ss / 2, outline=255, width=ss * 2)
        ring = ring.resize((int(w), int(h)), Image.LANCZOS)
        canvas.paste(Image.new("RGB", ring.size, tuple(int(c) for c in outline)), (x, y), ring.point(lambda v: int(v * outline_alpha)))
