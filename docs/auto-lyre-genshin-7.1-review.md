# Auto Lyre — Genshin 7.1 inventory and code review

Reviewed GitHub `dangminhtai/auto-lyre` branch `main` at commit `c09056e95ace60cca864e7c9ca9c2c86d494bc81` (2026-10-15). Clone was attempted but blocked by DNS in the execution container (`Could not resolve host github.com`); source code was instead fetched and examined via the connected GitHub service. The full repo was **not** cloned locally and no .NET tests were executed here.

## Count and categorization

11 music gadgets in the community inventory: Windsong Lyre, Floral Zither, Vintage Lyre, Nightwind Horn, Ukulele, Lingering Euphonia, Leaping Spirit Piano, Harmonic Keys, Arataki's Great and Glorious Drum, Festive Drum, Djem Djem Drum. Separately Vodyanitsa (introduced with Genshin 7.1) has a playable singing skill. Hence 12 interactive musical modes (11 gadgets + 1 character skill), not 12 gadgets.

- Melodic: Windsong, Floral, Vintage, Nightwind, Leaping Spirit Piano, Harmonic Keys (6).
- Melodic + chord triggers: Ukulele, Lingering Euphonia (2).
- Percussion: Arataki Drum, Festive Drum, Djem Djem Drum (3).
- Character vocal mode: Vodyanitsa (1).

## Code review — current v2, NOT obsolete v1

- `AutoLyre.App/Core/Keyboard.cs`: `Instrument` enum and `GetNotes()` only describe three gadgets (Windsong, Floral, Vintage). All other IDs must be modeled deliberately; the current fallback to C-major 21 keys is invalid for chords, percussion and vocal mode.
- `AutoLyre.Data/Midi/MelodyAnalysis.cs`: v2 detects a melody candidate per track/channel with heuristic scoring, with an ambiguous/single-stream fallback. Weights melody 80% and other voices 20%. This can improve pitch-offset selection but does not verify that inferred voice is the actual melody.
- `AutoLyre.Data/Midi/AutoPitch.cs`: v2 `AnalyzeWeighted` still selects offsets via an exact-note-first objective; `Map` independently remaps each note by octave or nearest playable pitch. It has no notion of chord-trigger semantics, concert-pitch calibration, articulation, or retrigger collisions.
- `AutoLyre.Data/Midi/AutoArrangement.cs`: filters MIDI percussion channel 10 before analysis and rendering, so it cannot arrange drum instruments correctly even if UI adds them. Collisions are measured after mapping but not optimized.
- `AutoLyre.App/ViewModels/LyrePlayerViewModel.cs`: `hold=false` sends instant `KeyPress`; `hold=true` holds keys to `NoteOff`, but the choice is global and not instrument-specific. Nightwind Horn sustains while plucked instruments do not; per-mode lifecycle must be explicit.
- `AutoLyre.App/Core/LyrePlayer.cs`: maps `MIDI pitch -> keyboard index`; cannot express a single keyboard key that plays a chord or a drum strike.

## In-game vs SoundFont distinction

StarryCosmosPiano publishes virtual SoundFont expansion with 128 MIDI pitches for 9 instruments/vocals. **These are not actual in-game available keys.** Their separate Original with Chords SoundFonts indicate 14 single-note keys + 7 chord sample triggers each for Ukulele and Lingering Euphonia; the 7 chord labels are C, Dm, Em, F, G, Am, G7. These are useful research leads but are not in-game ground truth, especially for chord voicings or pitch octaves. In percussion sample maps, Arataki sample triggers 60/62; Djem samples eight MIDI triggers; these maps similarly must not be mistaken for user input key count.

Vodyanitsa community discussions indicate two rows of seven diatonic pitches. Sounding octaves are not rigorously verified and require calibration. Do not initialize vocal mode from SoundFont's 128-note full-range preset.

## Recommended design

1. A distinct `InstrumentProfile` registry: id, `Mode` (Melodic, Chord, Percussion, Vocal), physical PC key layout, playable *actions*, sound output per action (one pitch, set of simultaneous pitches/chord, drum kind), hold/retrigger/polyphony rules, pitch calibration evidence, and version.
2. Separate **MIDI analysis** from **instrument action planning**. Input MIDI notes/channels do not map 1:1 to Genshin keyboard actions.
3. Melody instruments: optimize global transposition plus octave/register and voice-leading; use exact per-instrument note sets and time-sensitive note overlap constraints.
4. Chord gadgets: infer chord vocabulary and inversions; use integer/beam-search assignment to actual chord buttons; protect melody with single-note keys. Never treat the G7 trigger key as an isolated B note.
5. Drums: use MIDI channel 10 and event classification, choose Ka/Don strikes; do not perform tonal transposition or drop percussion at ingestion.
6. Vocal mode: first calibrate all 14 live key pitches, confirm polyphony and sustain; only then run melody-only mapping, contour-aware dynamic programming and enforce one note at a time if truly monophonic.
7. Validation: every playable action has a verified input key; chord output has named pitches and voicing calibration; per-note time and onset preserved; selected-note events within physical polyphony/retrigger limits; no stuck key; no source MIDI mutation.
8. Ground truth: compare capture/video from actual game and manually annotated melody by instrument. Corpus counts are useful but do not establish audible similarity or correct key percentages.

## Data file

`auto-lyre-instruments-7.1-provisional.json` is a **research manifest, not an engine-ready mapping**. It includes verified source references and flags provisional keyboard pitches. In particular, do not enable Vodyanitsa or uncertain melodic profiles for accuracy-critical play until calibrated.

## Sources

- https://genshin-impact.fandom.com/wiki/Music_Gadget
- https://genshin.hoyoverse.com/m/vi/news/detail/166110
- https://genshin-impact.fandom.com/wiki/Vintage_Lyre
- https://genshin-impact.fandom.com/wiki/Floral_Zither
- https://genshin-impact.fandom.com/wiki/Windsong_Lyre
- https://genshin-impact.fandom.com/wiki/Ukulele
- https://genshin-impact.fandom.com/wiki/%22Lingering_Euphonia%22
- https://genshin-impact.fandom.com/wiki/Tutorial/Adventure
- https://genshin-impact.fandom.com/wiki/Arataki%27s_Great_and_Glorious_Drum
- https://www.hoyodex.com/wiki/genshin-impact/festive-drum
- https://game8.co/games/Genshin-Impact/archives/436691
- https://github.com/StarryCosmosPiano/Genshin-Impact-Instrument-SoundFont/blob/main/docs/MIDI-Mapping.md
- https://www.reddit.com/r/VodyanitsaMains/comments/1wqmnhj/how_to_find_musical_notes_to_sing_with_her/
