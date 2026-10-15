# Auto Lyre — MIDI Key/Transposition Algorithm Review

Repository: https://github.com/dangminhtai/auto-lyre
Read-only review of commit `af6e72cb5c114427d38da11cc556daf660da8bd8` (`main`), 2026-10-15.

**Limitation:** sandbox could not run `git clone` (DNS: Could not resolve host github.com); source was inspected through connected GitHub access. The 415 MIDI corpus was not available, and .NET build/Windows game playback could not be run here. This review is *not* a full local clone.

## Confirmed findings in current code

1. `AutoLyre.Data/Midi/AutoPitch.cs` lines 25–48 groups only note numbers, then maximizes a lexicographic tuple whose highest-priority coordinate is unweighted `Exact` count. It ignores track identity, note duration, velocity, structural position and melodic function.
2. `AutoLyre.Data/Midi/AutoArrangement.cs` lines 66–83 combines all selected non-percussion notes, calls `AutoPitch.Analyze`, and maps each note independently; `Map` preserves pitch class via octave when possible, else rounds to nearest permitted semitone. This can change melody contours and harmonies.
3. `AutoArrangement.cs` lines 90–99 measures overlapping notes mapped to the same physical key **after** selecting offset; collision count is diagnostic and does not influence optimization.
4. `LyrePlayerViewModel.cs` lines 466–478 includes all checked tracks in the arrangement, without melody-track weighting; lines 591–604 hold/release keys with reference counting. Some mapped collisions can suppress re-attacks.
5. `tests/AutoMidiVerification/Program.cs` lines 57–68 check optimality **with the same mathematical scoring objective** using uniform random notes 0–127. This verifies correctness of the implementation relative to the chosen objective, but not musical quality.
6. `Keyboard.cs` defines exact allowed MIDI notes (21 keys, C3..B5 for Windsong/Floral; Vintage special pitch classes in upper register). Arrangement must use the exact instrument-specific list rather than assume all instruments are identical.

## Counterexample: accompaniment dominates melody

Input: 56 melody notes (C-major scale, MIDI 72,74,76,77,79,81,83, repeated), plus 200 accompaniment notes (61,63,66,68,70, repeated).

Output from a faithful independent reproduction of the current scoring logic:

```
Input notes: 256
Unweighted offset: 1
Direct matches overall: 216 / 256
Direct matches in melody: 16 / 56
Offset=0 melody direct matches: 56 / 56
Melody-priority weighted offset: 0
```

The global offset +1 maximizes overall direct-note matches, but now only 16 of 56 primary melody notes match exactly (28.57%). Offset 0 makes all 56 melody notes directly playable. The synthetic example is not a measure of performance on real user files.

## Recommended pipeline (v2)

1. Parse full MIDI note metadata: pitch, onset, duration, velocity, track/channel, instrument and tempo; exclude channel 10 percussion. Normalize overlapping duplicate events.
2. Select or infer melody voices using monophony, continuity, contour, register, rhythmic salience, track/instrument tags. Expose a user override for melody track.
3. Generate candidates for all 12 pitch classes and register choices (or exhaustively assess a bounded shift range). Use weighted pitch-class compatibility rather than count each note equally.
4. For each candidate, assign each main melody note to a playable pitch via dynamic programming (Viterbi), penalizing pitch-class changes heavily, octave jumps moderately, and distortion of consecutive intervals/contour. Preserve onsets and durations.
5. Map accompaniment using chord-aware assignment; permit octave shifts, selective voice reduction or note omission when the 21-key constraint makes exact reproduction impossible. Penalize same-key overlapping/retrigger collisions.
6. Optimize a weighted end-to-end quality loss. Hard constraints: output pitch belongs to the instrument's actual allowed key set; non-percussion only; bounded note density/retrigger; reproducible tie-breaking; source MIDI remains unchanged.
7. Add time-local key estimation only when justified by clear modulation. Penalize key switches to prevent unstable note mapping.
8. Compare against existing v1 on annotated MIDI excerpts and audio playbacks. Report melody pitch-class retention, interval contour accuracy, note coverage, collision/retrigger count, onset timing, and human A/B preferences. Do not report a percentage of 'correct keys' without annotated ground truth.

Recommended engineering sequence: melody-aware weighted chroma scoring (fast MVP), then Viterbi melody mapping, then chord optimization/voice reduction, then optional per-segment modulation.

## Code pointers

- https://github.com/dangminhtai/auto-lyre/blob/main/AutoLyre.Data/Midi/AutoPitch.cs#L23-L65
- https://github.com/dangminhtai/auto-lyre/blob/main/AutoLyre.Data/Midi/AutoArrangement.cs#L65-L100
- https://github.com/dangminhtai/auto-lyre/blob/main/tests/AutoMidiVerification/Program.cs#L57-L68
- https://github.com/dangminhtai/auto-lyre/blob/main/AutoLyre.App/Core/Keyboard.cs
