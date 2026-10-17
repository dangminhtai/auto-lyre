"""Generate a small decaying harmonic test signal, not a piano quality benchmark."""
import math
from pathlib import Path
import struct
import wave

target = Path("artifacts/audio-tests/piano-like.wav")
target.parent.mkdir(parents=True, exist_ok=True)
rate = 22050
frames = []
for i in range(rate * 3):
    t = i / rate
    sample = 0.0
    for onset, midi in [(0.1, 60), (0.9, 64), (1.7, 67)]:
        dt = t - onset
        if 0 <= dt < 1.1:
            frequency = 440 * 2 ** ((midi - 69) / 12)
            envelope = min(1, dt / 0.005) * math.exp(-4 * dt)
            sample += 0.25 * envelope * sum(math.sin(2 * math.pi * frequency * k * dt) / (k * k) for k in range(1, 8))
    frames.append(struct.pack("<h", int(max(-1, min(1, sample)) * 32767)))
with wave.open(str(target), "wb") as output:
    output.setparams((1, 2, rate, 0, "NONE", "not compressed"))
    output.writeframes(b"".join(frames))
print(target)
