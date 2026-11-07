#!/usr/bin/env python3
"""Synthesise the alert clips: python tools/audio/gen_alert_sounds.py

Writes Assets/GameData/Audio/Alerts/Alert_*.wav and their metas (the guid follows from the
file name, so AlertSounds.asset keeps its references across a re-run). The sounds belong to
the game: horns and bells built here from partials, with no sample from any asset pack.
docs/Design/Alert_Sounds.md says which event plays which. Needs numpy.
"""
import hashlib, os, wave
import numpy as np

ROOT = os.path.abspath(os.path.join(os.path.dirname(__file__), "..", ".."))
OUT = os.path.join(ROOT, "Assets", "GameData", "Audio", "Alerts")
SR = 44100
BELL = [(0.56, 1.0, 1.5), (0.92, 0.7, 1.15), (1.19, 0.6, 0.9), (1.71, 0.45, 0.7), (2.0, 0.55, 0.62), (2.74, 0.3, 0.42), (3.0, 0.25, 0.34), (3.76, 0.14, 0.24), (4.07, 0.12, 0.2)]

META = """fileFormatVersion: 2
guid: %s
AudioImporter:
  externalObjects: {}
  serializedVersion: 6
  defaultSettings:
    loadType: 0
    sampleRateSetting: 0
    sampleRateOverride: 44100
    compressionFormat: 1
    quality: 0.7
    conversionMode: 0
  platformSettingOverrides: {}
  forceToMono: 1
  normalize: 1
  preloadAudioData: 1
  loadInBackground: 0
  ambisonic: 0
  3D: 0
  userData:
  assetBundleName:
  assetBundleVariant:
"""

def silence(seconds):
    return np.zeros(int(seconds * SR))

def horn(freq, seconds, brightness=1.0, vibrato=0.004):
    """A brass-like note: harmonics that open up as the note speaks, with a slight lip rise."""
    t = np.arange(int(seconds * SR)) / SR
    rise = 1.0 - 0.035 * np.exp(-t / 0.05)
    wobble = 1.0 + vibrato * np.sin(2 * np.pi * 5.2 * t) * np.minimum(t / 0.25, 1.0)
    phase = 2 * np.pi * np.cumsum(freq * rise * wobble) / SR
    opening = np.minimum(t / 0.12, 1.0)
    out = np.zeros_like(t)
    for n in range(1, 13):
        if freq * n > 6000: break
        out += np.sin(n * phase) * (opening ** (0.4 * n)) * brightness ** (n - 1) / n
    env = np.minimum(t / 0.045, 1.0) * np.minimum((seconds - t) / 0.13, 1.0) ** 1.5
    return out * env

def bell(freq, seconds, shimmer=0.0):
    """A struck bell: inharmonic partials, each with its own decay."""
    t = np.arange(int(seconds * SR)) / SR
    out = np.zeros_like(t)
    for ratio, amp, decay in BELL:
        f = freq * ratio
        if f > 9000: continue
        out += amp * np.sin(2 * np.pi * f * t) * np.exp(-t / (decay * seconds / 2.5))
        if shimmer:
            out += shimmer * amp * np.sin(2 * np.pi * f * 1.004 * t) * np.exp(-t / (decay * seconds / 2.5))
    strike = 0.25 * np.exp(-t / 0.006) * np.sin(2 * np.pi * freq * 6.1 * t)
    fade = np.minimum((seconds - t) / 0.2, 1.0)
    return (out + strike) * np.minimum(t / 0.002, 1.0) * fade

def mix(length, *parts):
    """parts: (seconds offset, samples, gain)"""
    out = np.zeros(int(length * SR))
    for at, s, g in parts:
        i = int(at * SR)
        n = min(len(s), len(out) - i)
        out[i:i + n] += s[:n] * g
    return out

def chord(freqs, seconds, brightness=1.0):
    return sum(horn(f, seconds, brightness, 0.004 + 0.0008 * i) for i, f in enumerate(freqs)) / len(freqs)

CLIPS = {
    # Two urgent low blasts: short-long, and unlike anything else the game plays.
    "UnderAttack": mix(1.35, (0.0, chord([174.6, 261.6], 0.3, 0.9), 1.0), (0.38, chord([174.6, 261.6], 0.95, 0.92), 1.0)),
    # One deep bell with a beating shimmer: the rarest and heaviest.
    "Shardroot": mix(3.0, (0.0, bell(196.0, 3.0, shimmer=0.7), 1.0), (0.0, bell(784.0, 1.6), 0.18)),
    # Two light chimes, rising.
    "Monument": mix(1.6, (0.0, bell(659.3, 1.2), 0.9), (0.2, bell(880.0, 1.4), 1.0)),
    # A low double toll: a clock running out.
    "Countdown": mix(2.6, (0.0, bell(146.8, 1.7), 1.0), (0.75, bell(146.8, 1.85), 1.0)),
    # A rising fanfare, held on the top note.
    "Victory": mix(2.9, (0.0, chord([261.6, 392.0], 0.34, 0.95), 0.9), (0.34, chord([329.6, 493.9], 0.34, 0.95), 0.9), (0.68, chord([392.0, 523.3, 784.0], 2.2, 0.97), 1.0)),
    # A slow falling horn.
    "Defeat": mix(3.0, (0.0, chord([196.0, 293.7], 0.8, 0.82), 1.0), (0.8, chord([155.6, 233.1], 0.8, 0.8), 1.0), (1.6, chord([130.8, 196.0], 1.4, 0.78), 1.0)),
}

def main():
    os.makedirs(OUT, exist_ok=True)
    for name, samples in CLIPS.items():
        peak = float(np.max(np.abs(samples)))
        data = samples / peak * 0.8
        path = os.path.join(OUT, "Alert_%s.wav" % name)
        with wave.open(path, "wb") as w:
            w.setnchannels(1); w.setsampwidth(2); w.setframerate(SR)
            w.writeframes((data * 32767).astype(np.int16).tobytes())
        guid = hashlib.md5(("AlertSounds/" + name).encode()).hexdigest()
        open(path + ".meta", "w", encoding="utf8", newline="\n").write(META % guid)
        rms = float(np.sqrt(np.mean(data ** 2)))
        print("%-12s %.2fs peak %.2f rms %.3f guid %s" % (name, len(data) / SR, np.max(np.abs(data)), rms, guid))

if __name__ == "__main__":
    main()
