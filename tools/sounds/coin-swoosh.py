#!/usr/bin/env python3
"""Makes Chainsaw's alert sound: a coin swoosh with a rattle (CLAUDE.md, Bevaka).

Synthesised from nothing but numbers, so the sound is Chainsaw's own and needs no licence. Only Python's standard
library is used, and the random numbers are seeded, so the same file comes out every time:

    python3 tools/sounds/coin-swoosh.py src/Chainsaw.Watcher/Sounds/coin-swoosh.wav

The sound has two parts:
- the swoosh: noise through a resonant band-pass filter whose centre sweeps up and back, panned from left to right,
  as a coin flicked past the ear;
- the rattle: coins landing and bouncing, each a few inharmonic partials of a small metal disc that ring out
  exponentially, with a short click when it lands, closer and closer together and quieter as they settle.
"""

import math
import random
import struct
import sys
import wave

RATE = 44100
LENGTH = 1.45
SEED = 20261107


def swoosh(left, right, rng):
    """Adds the swoosh: band-passed noise sweeping up, panned left to right."""
    start, length = 0.0, 0.36
    low, band = 0.0, 0.0
    low_r, band_r = 0.0, 0.0
    count = int(length * RATE)
    for index in range(count):
        t = index / count
        # The centre rises fast and falls a little at the end, like something passing by.
        centre = 350 + 3600 * math.sin(math.pi * min(t * 1.15, 1.0) * 0.5) ** 2 - 900 * max(0.0, t - 0.8) / 0.2
        f = 2 * math.sin(math.pi * centre / RATE)
        q = 0.42
        envelope = math.sin(math.pi * t) ** 1.6
        noise_l = rng.uniform(-1, 1)
        noise_r = rng.uniform(-1, 1)
        # Chamberlin state-variable filters, one per side.
        low += f * band
        high = noise_l - low - q * band
        band += f * high
        low_r += f * band_r
        high_r = noise_r - low_r - q * band_r
        band_r += f * high_r
        pan = t
        position = int((start + t * length) * RATE)
        left[position] += 0.24 * envelope * band * math.cos(pan * math.pi / 2)
        right[position] += 0.24 * envelope * band_r * math.sin(pan * math.pi / 2)


def coin(left, right, at, gain, pitch, pan, rng):
    """Adds one coin landing: a click and a few ringing partials of a small disc."""
    ratios = (1.0, 1.506, 2.089, 2.660, 3.214, 4.05)
    weights = (1.0, 0.62, 0.48, 0.31, 0.22, 0.12)
    decays = (0.30, 0.22, 0.16, 0.12, 0.09, 0.06)
    begin = int(at * RATE)
    ring = int(0.5 * RATE)
    phases = [rng.uniform(0, 2 * math.pi) for _ in ratios]
    detune = [1 + rng.uniform(-0.004, 0.004) for _ in ratios]
    for index in range(ring):
        position = begin + index
        if position >= len(left):
            break
        t = index / RATE
        value = 0.0
        for ratio, weight, decay, phase, wobble in zip(ratios, weights, decays, phases, detune):
            value += weight * math.exp(-t / decay) * math.sin(2 * math.pi * pitch * ratio * wobble * t + phase)
        if index < 90:
            # The click as metal meets metal: a short burst of bright noise.
            value += 1.4 * rng.uniform(-1, 1) * (1 - index / 90) ** 2
        attack = min(1.0, index / 22)
        value *= gain * attack * 0.34
        left[position] += value * math.cos(pan * math.pi / 2)
        right[position] += value * math.sin(pan * math.pi / 2)


def rattle(left, right, rng):
    """Adds the coins: a first ring, then bounces closer together and quieter, and a last tiny settle."""
    at, gain, gap = 0.30, 1.0, 0.11
    for _ in range(11):
        pitch = rng.uniform(2650, 4300)
        pan = min(1.0, max(0.0, 0.62 + rng.uniform(-0.3, 0.3)))
        coin(left, right, at, gain, pitch, pan, rng)
        if rng.random() < 0.45:
            # A second coin a moment later: more than one lands.
            coin(left, right, at + rng.uniform(0.008, 0.025), gain * 0.55, rng.uniform(2500, 4600), 1 - pan, rng)
        at += gap * rng.uniform(0.8, 1.15)
        gap *= 0.78
        gain *= 0.76


def finish(left, right):
    """Fades the tail, takes away any offset and brings the peak to -1 dBFS."""
    count = len(left)
    fade = int(0.12 * RATE)
    for index in range(count - fade, count):
        factor = (count - index) / fade
        left[index] *= factor
        right[index] *= factor
    peak = max(max(abs(v) for v in left), max(abs(v) for v in right))
    scale = 10 ** (-1 / 20) / peak
    return [v * scale for v in left], [v * scale for v in right]


def main(path):
    rng = random.Random(SEED)
    count = int(LENGTH * RATE)
    left = [0.0] * count
    right = [0.0] * count
    swoosh(left, right, rng)
    rattle(left, right, rng)
    left, right = finish(left, right)
    frames = bytearray()
    for l, r in zip(left, right):
        frames += struct.pack('<hh', int(round(l * 32767)), int(round(r * 32767)))
    with wave.open(path, 'wb') as out:
        out.setnchannels(2)
        out.setsampwidth(2)
        out.setframerate(RATE)
        out.writeframes(bytes(frames))


if __name__ == '__main__':
    main(sys.argv[1] if len(sys.argv) > 1 else 'coin-swoosh.wav')
