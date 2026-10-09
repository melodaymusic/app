"""Synthesizes the music bed for the Meloday Music promo: an original 112 BPM track, 24 bars, built from
synthesized drums, bass, pads, plucks and a lead (nothing sampled, nothing borrowed).

    python music.py <out.wav>

Sections (bars): 0-1 intro, 2-3 pulse, 4-7 groove, 8-13 arp and clap, 14-15 riser, 16-19 drop,
20-21 breakdown, 22-23 return, 24+ final chord and tail. The video's cuts are timed to these.
"""
import sys
import numpy as np
from scipy import signal
from scipy.io import wavfile

SR = 48000
BPM = 112.0
BEAT = 60.0 / BPM
BAR = 4 * BEAT
BARS = 24
TAIL = 4.5
N = int((BARS * BAR + TAIL) * SR)
rng = np.random.default_rng(7)


def midi(m):
    return 440.0 * 2 ** ((m - 69) / 12)


def t_of(n):
    return np.arange(n) / SR


def place(buf, sig, at_seconds, gain=1.0, pan=0.0):
    """Adds a mono signal to a stereo buffer at a time, with equal-power pan (-1..1)."""
    i = int(at_seconds * SR)
    if i >= len(buf) or i < 0:
        return
    n = min(len(sig), len(buf) - i)
    a = (pan + 1) * np.pi / 4
    buf[i:i + n, 0] += sig[:n] * gain * np.cos(a)
    buf[i:i + n, 1] += sig[:n] * gain * np.sin(a)


def lowpass(x, hz, order=2):
    sos = signal.butter(order, hz / (SR / 2), "low", output="sos")
    return signal.sosfilt(sos, x)


def highpass(x, hz, order=2):
    sos = signal.butter(order, hz / (SR / 2), "high", output="sos")
    return signal.sosfilt(sos, x)


def bandpass(x, lo, hi, order=2):
    sos = signal.butter(order, [lo / (SR / 2), hi / (SR / 2)], "band", output="sos")
    return signal.sosfilt(sos, x)


def saw(f, t, detune_cents=0.0):
    """Band-limited saw by summing harmonics (below 7 kHz)."""
    f = f * 2 ** (detune_cents / 1200)
    k = max(1, int(7000 / f))
    out = np.zeros_like(t)
    for h in range(1, k + 1):
        out += np.sin(2 * np.pi * f * h * t) / h
    return out * (2 / np.pi)


# ------------------------------------------------------------------ instruments

def kick(vol=1.0):
    n = int(0.5 * SR)
    t = t_of(n)
    freq = 46 + 110 * np.exp(-t * 28)
    phase = 2 * np.pi * np.cumsum(freq) / SR
    body = np.sin(phase) * np.exp(-t * 7.5)
    click = np.sin(2 * np.pi * 1800 * t) * np.exp(-t * 180) * 0.25
    return np.tanh((body + click) * 1.6) * 0.9 * vol


def clap(vol=1.0):
    n = int(0.35 * SR)
    t = t_of(n)
    noise = bandpass(rng.standard_normal(n), 900, 5200)
    env = np.zeros(n)
    for off, a in ((0.0, 1.0), (0.012, 0.8), (0.026, 0.65)):
        i = int(off * SR)
        env[i:] += a * np.exp(-(t[: n - i]) * 90)
    env += 0.5 * np.exp(-t * 16)
    return noise * env * 0.5 * vol


def hat(open_=False, vol=1.0):
    n = int((0.28 if open_ else 0.06) * SR)
    t = t_of(n)
    noise = highpass(rng.standard_normal(n), 7000)
    return noise * np.exp(-t * (14 if open_ else 80)) * 0.5 * vol


def pluck(f, length=0.35, bright=4600, vol=1.0):
    n = int(length * SR)
    t = t_of(n)
    s = saw(f, t, 0) * 0.6 + saw(f, t, 6) * 0.4
    env = np.exp(-t * 11)
    # The filter closes as the note decays (done in two steps, cheaply).
    a = lowpass(s, bright) * env
    b = lowpass(s, bright * 0.35) * env
    mix = np.linspace(1, 0, n) ** 1.5
    return (a * mix + b * (1 - mix)) * 0.45 * vol


def lead(f, length, vol=1.0):
    n = int(length * SR)
    t = t_of(n)
    vib = 1 + 0.003 * np.sin(2 * np.pi * 5.2 * t) * np.minimum(1, t * 3)
    s = np.sin(2 * np.pi * f * np.cumsum(vib) / SR) * 0.7 + lowpass(saw(f, t, 4), 3800) * 0.35
    env = np.minimum(1, t / 0.012) * np.exp(-t * 2.6) * np.minimum(1, (length - t) / 0.06 + 0.0)
    return s * np.clip(env, 0, 1) * 0.5 * vol


def sub(f, length, vol=1.0):
    n = int(length * SR)
    t = t_of(n)
    s = np.sin(2 * np.pi * f * t) + 0.25 * np.sin(2 * np.pi * 2 * f * t)
    env = np.minimum(1, t / 0.01) * np.minimum(1, (length - t) / 0.05)
    return np.tanh(s * 1.3) * env * 0.55 * vol


def pad_chord(notes, length):
    n = int(length * SR)
    t = t_of(n)
    out = np.zeros(n)
    for m in notes:
        f = midi(m)
        for d in (-9, 0, 9):
            out += saw(f, t, d) * 0.12
    out = lowpass(out, 2600)
    env = np.minimum(1, t / 0.55) * np.minimum(1, (length - t) / 0.8)
    return out * env


def noise_riser(seconds):
    n = int(seconds * SR)
    t = t_of(n) / seconds
    noise = rng.standard_normal(n)
    out = np.zeros(n)
    # A sweep that opens up: noise filtered in slices of rising cut-off.
    chunk = SR // 20
    for i in range(0, n, chunk):
        hz = 300 * (14000 / 300) ** (i / n)
        out[i:i + chunk] = highpass(noise[max(0, i - 2000):i + chunk], hz, 1)[-len(out[i:i + chunk]):]
    return out * (t ** 2.2) * 0.35


def impact(vol=1.0):
    n = int(3.0 * SR)
    t = t_of(n)
    boom = np.sin(2 * np.pi * (38 + 60 * np.exp(-t * 6)) * t) * np.exp(-t * 1.6)
    air = lowpass(rng.standard_normal(n), 6000) * np.exp(-t * 2.2) * 0.25
    return (boom * 0.9 + air) * vol


def crash(vol=1.0):
    n = int(2.8 * SR)
    t = t_of(n)
    noise = highpass(rng.standard_normal(n), 3500)
    return noise * np.exp(-t * 1.9) * 0.28 * vol


def reverb(x, seconds=2.4, wet=1.0):
    n = int(seconds * SR)
    t = t_of(n)
    ir = rng.standard_normal((n, 2)) * np.exp(-t * (5.5 / seconds))[:, None]
    ir = np.stack([lowpass(ir[:, 0], 5200), lowpass(ir[:, 1], 5200)], axis=1)
    ir[:int(0.012 * SR)] *= np.linspace(0, 1, int(0.012 * SR))[:, None]
    out = np.stack([signal.fftconvolve(x[:, 0], ir[:, 0])[: len(x)], signal.fftconvolve(x[:, 1], ir[:, 1])[: len(x)]], axis=1)
    return out * wet * 0.045


def echo(x, delay, feedback=0.38, taps=4):
    out = x.copy()
    d = int(delay * SR)
    for k in range(1, taps + 1):
        sh = np.zeros_like(x)
        sh[d * k:] = x[: len(x) - d * k] * (feedback ** k)
        # Ping-pong: the repeats swap sides.
        out += sh[:, ::-1] if k % 2 else sh
    return out


# ------------------------------------------------------------------ the arrangement

CHORDS = [
    ([53, 57, 60, 64], 41, [72, 76, 81, 77], "Fmaj7"),   # F A C E
    ([55, 59, 62, 67], 43, [74, 79, 83, 79], "G"),       # G B D G
    ([52, 55, 59, 62], 40, [76, 79, 83, 79], "Em7"),     # E G B D
    ([57, 60, 64, 67], 45, [81, 79, 76, 79], "Am7"),     # A C E G
]

pad_bus = np.zeros((N, 2))
bass_bus = np.zeros((N, 2))
drum_bus = np.zeros((N, 2))
kick_bus = np.zeros((N, 2))
pluck_bus = np.zeros((N, 2))
lead_bus = np.zeros((N, 2))
fx_bus = np.zeros((N, 2))

K = kick()
CLAP = clap()
HAT = hat()
OHAT = hat(open_=True)
pad_cache = {}


def bar_t(b, beat=0.0):
    return (b + beat / 4.0) * BAR


def section(b):
    if b < 2: return "intro"
    if b < 4: return "pulse"
    if b < 8: return "groove"
    if b < 14: return "arp"
    if b < 16: return "riser"
    if b < 20: return "drop"
    if b < 22: return "breakdown"
    if b < 24: return "return"
    return "end"


for b in range(BARS):
    sec = section(b)
    notes, root, motif, name = CHORDS[b % 4]

    # Pad: every bar, brighter and louder as the track builds, darker in the intro.
    if name not in pad_cache:
        pad_cache[name] = pad_chord(notes, BAR + 0.9)
    pad_gain = {"intro": 0.7, "pulse": 0.8, "groove": 0.8, "arp": 0.85, "riser": 0.9, "drop": 1.0, "breakdown": 1.05, "return": 1.0}[sec]
    place(pad_bus, pad_cache[name], bar_t(b) - 0.05, pad_gain, 0)

    # Kick
    if sec in ("pulse",):
        for beat in (0, 2):
            place(kick_bus, K, bar_t(b, beat), 0.8)
    elif sec in ("groove", "arp", "drop", "return"):
        for beat in range(4):
            place(kick_bus, K, bar_t(b, beat), 1.0)
    elif sec == "riser":
        for beat in range(4 if b == 14 else 3):
            place(kick_bus, K, bar_t(b, beat), 0.95)
    if b == 16:
        place(kick_bus, K, bar_t(b, 0), 1.2)

    # Hats and claps
    if sec in ("groove", "arp", "drop", "return", "riser"):
        for step in range(8):
            if sec == "groove" and b < 6 and step % 2 == 0:
                continue
            on_off = step % 2 == 1
            place(drum_bus, OHAT if (on_off and sec in ("drop",) and step in (3, 7)) else HAT, bar_t(b, step * 0.5), 0.8 if on_off else 0.45, 0.25 * (1 if step % 4 < 2 else -1))
        if sec == "drop":
            for step in range(16):
                if step % 2:
                    place(drum_bus, HAT, bar_t(b, step * 0.25), 0.35, 0.3)
    if sec in ("arp", "drop", "return") or (sec == "groove" and b >= 6):
        for beat in (1, 3):
            place(drum_bus, CLAP, bar_t(b, beat), 0.8 if sec != "drop" else 1.0)
    if sec == "breakdown":
        for step in range(8):
            if step % 2:
                place(drum_bus, HAT, bar_t(b, step * 0.5), 0.35, 0.2)

    # Bass: 8ths on the root, an octave jump on the off-beats; in the drop it drives.
    if sec in ("groove", "arp", "riser", "drop", "return"):
        vol = 0.9 if sec != "groove" else 0.75
        for step in range(8):
            f = midi(root - (12 if False else 0))
            if step % 4 == 3 and sec in ("drop",):
                f = f * 2
            place(bass_bus, sub(f, BEAT * 0.5 * 0.9), bar_t(b, step * 0.5), vol)

    # Plucked arpeggio, 16ths, panned back and forth; rests during the intro and breakdown dip
    if sec in ("arp", "riser", "drop", "return", "breakdown"):
        tones = [m + 12 for m in notes]
        order = [0, 2, 1, 3, 2, 3, 1, 2]
        g = {"arp": 0.34, "riser": 0.4, "drop": 0.38, "return": 0.4, "breakdown": 0.46}[sec]
        for step in range(16):
            m = tones[order[step % 8]] + (12 if step % 8 == 7 else 0)
            place(pluck_bus, pluck(midi(m), 0.3), bar_t(b, step * 0.25), g, -0.35 if step % 2 else 0.35)
    if sec == "intro" and b == 1:
        for step in range(4):
            m = notes[step] + 24
            place(pluck_bus, pluck(midi(m), 0.6, bright=4200), bar_t(b, step * 1.0), 0.32, -0.4 + step * 0.27)
    if sec == "pulse":
        for step in range(8):
            m = notes[[0, 2, 1, 3][step % 4]] + 12
            place(pluck_bus, pluck(midi(m), 0.4), bar_t(b, step * 0.5), 0.28, -0.3 if step % 2 else 0.3)

    # Lead melody in the drop and in the last bars
    if sec in ("drop", "return"):
        if name == "Am7":
            hits = [(0, 81, 1.0), (1, 79, 1.0), (2, 76, 0.5), (2.5, 79, 0.5), (3, 81, 1.0)]
        else:
            hits = [(i, m, 1.0) for i, m in enumerate(motif)]
        for beat, m, ln in hits:
            place(lead_bus, lead(midi(m), ln * BEAT * 1.15), bar_t(b, beat), 0.55)

# Risers, impacts, crashes
place(fx_bus, noise_riser(2 * BAR), bar_t(14), 0.8)
place(fx_bus, impact(), bar_t(16), 1.0)
place(fx_bus, crash(), bar_t(16), 0.9)
place(fx_bus, crash(0.7), bar_t(22), 0.7)
place(fx_bus, impact(0.9), bar_t(0) + 0.0, 0.6)
place(fx_bus, noise_riser(BAR * 0.9), bar_t(1, 0.4), 0.35)
# Scene-change whooshes (short noise swells ending on the bar lines the video cuts at)
for b in (4, 8, 12, 20):
    place(fx_bus, noise_riser(0.9), bar_t(b) - 0.9, 0.28)
# The last chord: everything rings out
final_notes = [53, 57, 60, 64, 69, 72]
place(pad_bus, pad_chord(final_notes, TAIL + 1.0), bar_t(24) - 0.02, 1.3, 0)
place(bass_bus, sub(midi(41), 3.0), bar_t(24), 0.8)
place(kick_bus, K, bar_t(24), 1.0)
for i, m in enumerate([77, 81, 84, 88]):
    place(pluck_bus, pluck(midi(m), 1.4, bright=5000), bar_t(24) + i * 0.09, 0.4, -0.5 + i * 0.33)

# ------------------------------------------------------------------ mixing

# Sidechain: the kick ducks the pad, plucks, bass and lead.
kick_env = np.zeros(N)
for b in range(BARS + 1):
    sec = section(b)
    beats = {"pulse": (0, 2), "groove": range(4), "arp": range(4), "drop": range(4), "return": range(4), "riser": range(4 if b == 14 else 3)}.get(sec, ())
    if b == 24:
        beats = (0,)
    for beat in beats:
        i = int(bar_t(b, beat) * SR)
        n = int(0.28 * SR)
        if i + n < N:
            kick_env[i:i + n] = np.maximum(kick_env[i:i + n], np.exp(-np.arange(n) / (0.07 * SR)))
duck = (1 - 0.55 * kick_env)[:, None]

music = pad_bus * duck + pluck_bus * (1 - 0.35 * kick_env)[:, None] + bass_bus * duck + lead_bus * (1 - 0.3 * kick_env)[:, None]
wet = reverb(pad_bus * 0.9 + pluck_bus * 0.9 + lead_bus * 1.2, 2.6) + reverb(fx_bus * 0.5, 3.5)
lead_echo = echo(lead_bus, BEAT * 0.75, 0.35, 3) - lead_bus
mix = music + drum_bus * 0.9 + kick_bus * 1.0 + fx_bus + wet + lead_echo * 0.5

# Master: gentle low-cut, glue, soft limit, fades
mix[:, 0] = highpass(mix[:, 0], 28, 2)
mix[:, 1] = highpass(mix[:, 1], 28, 2)
peak = np.max(np.abs(mix))
mix = mix / peak * 1.25
mix = np.tanh(mix * 0.95) / np.tanh(0.95)
mix *= 0.89 / np.max(np.abs(mix))
fade_in = int(0.05 * SR)
mix[:fade_in] *= np.linspace(0, 1, fade_in)[:, None]
fade_out = int(1.2 * SR)
mix[-fade_out:] *= np.linspace(1, 0, fade_out)[:, None] ** 1.5

out = sys.argv[1] if len(sys.argv) > 1 else "music.wav"
wavfile.write(out, SR, (mix * 32767).astype(np.int16))
print(f"wrote {out}: {len(mix) / SR:.2f} s, peak {np.max(np.abs(mix)):.2f}, rms {np.sqrt(np.mean(mix ** 2)):.3f}")
