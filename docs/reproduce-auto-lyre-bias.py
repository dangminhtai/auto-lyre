#!/usr/bin/env python3
"""Independent reproduction of the scoring strategy read in AutoPitch.cs (commit af6e72c)."""
from collections import Counter

PLAYABLE = [48,50,52,53,55,57,59,60,62,64,65,67,69,71,72,74,76,77,79,81,83]

def score(notes, offset):
    exact = unmatched = distance = 0
    for note, count in Counter(notes).items():
        pitch = note - offset
        if pitch in PLAYABLE:
            exact += count
            continue
        same_class = [n for n in PLAYABLE if (n - pitch) % 12 == 0]
        if not same_class:
            unmatched += count
        else:
            distance += count * min(abs(n - pitch) for n in same_class)
    return (exact, -unmatched, -distance, -abs(offset), offset)

def pick(notes):
    return max(range(-27, 25), key=lambda offset: score(notes, offset))

melody = [72,74,76,77,79,81,83] * 8   # 56 notes, C-major melody
accompaniment = [61,63,66,68,70] * 40  # 200 notes, contrasting harmony
notes = melody + accompaniment

chosen = pick(notes)
corrected_main = sum((n - chosen) in PLAYABLE for n in melody)
print("Input notes:", len(notes))
print("Unweighted offset:", chosen)
print("Direct matches overall:", score(notes, chosen)[0], "/", len(notes))
print("Direct matches in melody:", corrected_main, "/", len(melody))
print("Offset=0 melody direct matches:", sum(n in PLAYABLE for n in melody), "/", len(melody))

# Separate demonstration: a melody-priority score (NOT a production v2 model).
def weighted_exact(offset):
    return sum(6 for n in melody if n-offset in PLAYABLE) + sum(1 for n in accompaniment if n-offset in PLAYABLE)
weighted_choice = max(range(-27,25), key=lambda offset: (weighted_exact(offset),-abs(offset),offset))
print("Melody-priority weighted offset:", weighted_choice)
