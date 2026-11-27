"""Render original mono AnimeCS demo cues. Run with Python 3, using only its standard library."""
from array import array
import math
from pathlib import Path
import random
import sys
import wave

RATE = 32000
OUT = Path(__file__).resolve().parents[2] / "Assets/Content/Audio/Courtyard"
# frequency, transient decay, musical tail frequency, noise fraction, duration
VOICES = {
    "dash9": (182, 44, 530, .55, .21), "street20": (142, 37, 620, .65, .26),
    "heavy50": (109, 23, 380, .73, .34), "verdict57": (216, 46, 840, .57, .23),
    "whitefalcon12s": (253, 66, 440, .25, .18), "hummingbird9": (211, 60, 710, .52, .16),
    "spark10": (176, 52, 950, .60, .18), "fold44": (133, 26, 583, .80, .32),
    "ember47": (99, 22, 488, .77, .38), "watch41s": (171, 48, 340, .34, .23),
    "feather08": (103, 19, 980, .70, .42), "longglass96": (81, 14, 659, .78, .55),
}
# Start time, frequency, downward chirp interval, decay. The ripple motif is shared by the objective cues.
CHIMES = {
    "inspect-side": [(0, 1340, 790, 58), (.062, 1780, 240, 67)],
    "inspect-long": [(0, 815, 470, 53), (.091, 1090, 625, 49)],
    "inspect-glass": [(0, 1720, 160, 40), (.125, 2150, 95, 35)],
    "inspect-edge": [(0, 1310, 760, 25), (.12, 1980, 70, 31)],
    "inspect-kit": [(0, 998, 68, 42), (.078, 1495, 66, 42)],
    "rubyedge": [(0, 900, 1450, 15), (.11, 1280, 380, 61)],
    "edge-heavy": [(0, 460, 1670, 10), (.15, 880, 310, 25)],
    "edge-rear": [(0, 450, 1480, 12), (.095, 1568, 90, 22), (.15, 2093, 80, 21)],
    "smoke": [(0, 295, 180, 8), (.034, 433, 190, 6)],
    "flash": [(0, 1350, 1100, 15), (.025, 2250, 300, 16)],
    "echo": [(0, 659, 140, 28), (.092, 784, 140, 32), (.19, 988, 90, 26)],
    "objective": [(0, 554, 40, 32), (.10, 830, 68, 26), (.19, 1108, 50, 24)],
    "objective-pulse": [(0, 830, 45, 27), (.083, 1108, 50, 22)],
    "objective-release": [(0, 87, 160, 7), (.052, 277, 160, 9), (.12, 554, 58, 8), (.22, 830, 80, 7)],
}


def render(name, duration, fn):
    rng = random.Random(name)
    samples = []
    low = previous = 0
    for n in range(round(RATE * duration)):
        t = n / RATE
        white = rng.uniform(-1, 1)
        low = .77 * low + .23 * white
        value = fn(t, low, white - previous)
        previous = white
        envelope = min(1, 1250 * t, max(0, (duration - t) * 100))
        samples.append(math.tanh(value) * envelope)
    gain = .83 / max(abs(x) for x in samples)
    pcm = array("h", (round(x * gain * 32767) for x in samples))
    if sys.byteorder != "little":
        pcm.byteswap()
    with wave.open(str(OUT / (name + ".wav")), "wb") as f:
        f.setparams((1, 2, RATE, 0, "NONE", "not compressed"))
        f.writeframes(pcm.tobytes())


def shot(t, low, high, voice):
    base, decay, ring, grit, _ = voice
    body = math.sin(math.tau * (base*t + 2.8 * (1-math.exp(-80*t))))
    sheen = math.sin(math.tau * ring*t) * math.exp(-2*decay*t)
    return (body*.74 + low*grit*3.6)*math.exp(-decay*t) + sheen*.23 + high*grit*.56*math.exp(-100*t)


def chime(t, low, high, notes, airy):
    result = 0
    for start, pitch, sweep, decay in notes:
        age = t - start
        if age < 0:
            continue
        phase = math.tau * (pitch*age + sweep*(1-math.exp(-17*age))/17)
        ring = math.sin(phase) + .14*math.sin(2.01*phase)
        attack = min(1, 750*age)
        result += (ring*(.18 if airy else .70) + low*(2.8 if airy else .52) + high*.045)*math.exp(-age*decay)*attack
    return result


if __name__ == "__main__":
    OUT.mkdir(parents=True, exist_ok=True)
    for name, voice in VOICES.items():
        render(name, voice[-1], lambda t, lo, hi: shot(t, lo, hi, voice))
    for name, notes in CHIMES.items():
        duration = max(start + 6/decay for start, _, _, decay in notes)
        airy = name.startswith("edge") or name in {"smoke", "rubyedge", "objective-release"}
        render(name, duration, lambda t, lo, hi: chime(t, lo, hi, notes, airy))
