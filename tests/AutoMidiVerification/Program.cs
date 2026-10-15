using System.Diagnostics;
using System.IO;
using System.Security.Cryptography;
using System.Text.Json;
using AutoLyre.Data.Midi;
using AutoLyre.App.Core;
using Melanchall.DryWetMidi.Common;
using Melanchall.DryWetMidi.Core;
using Melanchall.DryWetMidi.Interaction;
using RawMidi = Melanchall.DryWetMidi.Core.MidiFile;

internal static class Program
{
    private static int _passed;
    private static int _failed;
    private static readonly int[] Default = Keyboard.GetNotes(Keyboard.Instrument.WindsongLyre).ToArray();

    [STAThread]
    private static int Main(string[] args)
    {
        Test("Identity preserves all playable notes", () =>
        {
            var pitch = AutoPitch.Analyze(Default, Default);
            Check(pitch.Offset == 0);
            foreach (var n in Default) Check(pitch.Map(n, out var kind) == n && kind == PitchChange.Exact);
        });
        Test("D-major shifted scale chooses +2 and lowers two semitones", () =>
        {
            var input = Default.Select(n => n + 2).ToArray();
            var pitch = AutoPitch.Analyze(input, Default);
            Check(pitch.Offset == 2 && pitch.Map(62, out _) == 60);
        });
        Test("Octave and nearest-note tie rules", () =>
        {
            var pitch = AutoPitch.Direct(Default);
            Check(pitch.Map(36, out var kind) == 48 && kind == PitchChange.Octave);
            Check(pitch.Map(96, out kind) == 72 && kind == PitchChange.Octave);
            Check(pitch.Map(61, out kind) == 60 && kind == PitchChange.Rounded);
            foreach (var instrument in Enum.GetValues<Keyboard.Instrument>())
            {
                var allowed = Keyboard.GetNotes(instrument);
                var mapping = AutoPitch.Direct(allowed);
                for (var n = 0; n < 128; n++) Check(allowed.Contains(mapping.Map(n, out _)));
            }
        });
        Test("Vintage high octave matches documented C Phrygian", () =>
            Check(Keyboard.GetNotes(Keyboard.Instrument.VintageLyre).Skip(14)
                .SequenceEqual(new[] { 72, 73, 75, 77, 79, 80, 82 })));
        Test("Empty input, invalid mapping and cancellation", () =>
        {
            Check(AutoPitch.Analyze(Array.Empty<int>(), Default).Offset == 0);
            Throws<ArgumentException>(() => AutoPitch.Analyze(new[] { 60 }, Array.Empty<int>()));
            Throws<ArgumentException>(() => AutoPitch.Direct(new[] { 128 }));
            Throws<ArgumentOutOfRangeException>(() => AutoPitch.Analyze(new[] { -1 }, Default));
            Throws<OperationCanceledException>(() => AutoPitch.Analyze(Default, Default, new CancellationToken(true)));
        });
        Test("Independent exhaustive oracle on 200 randomized melodies", () =>
        {
            var random = new Random(1310);
            for (var trial = 0; trial < 200; trial++)
            {
                var allowed = Keyboard.GetNotes((Keyboard.Instrument)(trial % 3)).ToArray();
                var input = Enumerable.Range(0, 50).Select(_ => random.Next(128)).ToArray();
                var result = AutoPitch.Analyze(input, allowed);
                var chosen = Score(input, allowed, result.Offset);
                for (var k = -27; k <= 24; k++) Check(chosen.CompareTo(Score(input, allowed, k)) >= 0);
                Check(AutoPitch.Analyze(input, allowed).Offset == result.Offset);
            }
        });
        Test("Automatic melody priority fixes audit counterexample and ignores track order", () =>
        {
            var melody = Enumerable.Range(0, 56).Select(i => new Note((SevenBitNumber)new[] {72,74,76,77,79,81,83}[i % 7], 360, i * 480L)).ToArray();
            var backing = Enumerable.Range(0, 200).Select(i => new Note((SevenBitNumber)new[] {61,63,66,68,70}[i % 5], 90, i * 120L)).ToArray();
            foreach (var reversed in new[] { false, true })
            {
                var chunks = new[] { melody.ToTrackChunk(), backing.ToTrackChunk() };
                var source = new RawMidi(reversed ? chunks.Reverse().ToArray() : chunks);
                var bytes = Bytes(source);
                var result = AutoArrangement.Create(source, new[] {true, true}, Default);
                Check(result.V1Offset == 1 && result.V1MelodyExact == 16);
                Check(result.Pitch.Offset == 0 && result.MelodyTotal == 56 && result.MelodyExact == 56);
                Check(result.Total == 256 && bytes.SequenceEqual(Bytes(source)));
            }
        });
        Test("Sustained lower melody beats high chord accompaniment", () =>
        {
            var main = Enumerable.Range(0, 16).Select(i => new Note((SevenBitNumber)(60 + new[] {0,2,4,5}[i%4]), 400, i*480L)).ToArray();
            var backing = Enumerable.Range(0, 64).SelectMany(i => new[] {73,78,82}.Select(p => new Note((SevenBitNumber)p, 60, i*120L))).ToArray();
            var result = AutoArrangement.Create(new RawMidi(main.ToTrackChunk(), backing.ToTrackChunk()), new[] {true,true}, Default);
            // Several transpositions preserve this four-note melody; offset zero is not uniquely required.
            Check(result.MelodyTotal == 16 && result.MelodyExact == 16,
                $"Melody={result.MelodyTotal}, exact={result.MelodyExact}, offset={result.Pitch.Offset}");
        });
        Test("Separate channels in one track retain melody evidence", () =>
        {
            var melody = Enumerable.Range(0, 56).Select(i => new Note((SevenBitNumber)new[] {72,74,76,77,79,81,83}[i%7], 360, i*480L)).ToArray();
            var backing = Enumerable.Range(0, 200).Select(i => new Note((SevenBitNumber)new[] {61,63,66,68,70}[i%5], 90, i*120L) { Channel = (FourBitNumber)1 }).ToArray();
            var source = new RawMidi(melody.Concat(backing).ToTrackChunk());
            var result = AutoArrangement.Create(source, new[] {true}, Default);
            Check(result.Pitch.Offset == 0 && result.MelodyTotal == 56 && result.MelodyExact == 56);
        });
        Test("Single stream and ambiguous voices fall back without inventing melody", () =>
        {
            var line = Enumerable.Range(0, 16).Select(i => new Note((SevenBitNumber)(60+i%7), 120, i*240L)).ToArray();
            var single = AutoArrangement.Create(new RawMidi(line.ToTrackChunk()), new[] {true}, Default);
            Check(single.MelodyTotal == 0 && single.Pitch.Offset == single.V1Offset);
            var tied = AutoArrangement.Create(new RawMidi(line.ToTrackChunk(), line.ToTrackChunk()), new[] {true,true}, Default);
            Check(tied.MelodyMethod == "fallback-ambiguous" && tied.Pitch.Offset == tied.V1Offset);
            Throws<ArgumentException>(() => AutoPitch.AnalyzeWeighted(new[] {(60, double.NaN)}, Default));
            Throws<OperationCanceledException>(() => AutoPitch.AnalyzeWeighted(new[] {(60, 1.0)}, Default, new CancellationToken(true)));
        });
        Test("Selected tracks preserve tempo and original MIDI", () =>
        {
            var source = new RawMidi(
                new TrackChunk(new SetTempoEvent(600000), new NoteOnEvent((SevenBitNumber)40, (SevenBitNumber)80),
                    new NoteOffEvent((SevenBitNumber)40, (SevenBitNumber)0) { DeltaTime = 480 },
                    new SetTempoEvent(400000) { DeltaTime = 120 }),
                new[] { N(62, 240, 480), N(66, 960, 120) }.ToTrackChunk());
            var before = Bytes(source);
            var result = AutoArrangement.Create(source, new[] { false, true }, Default);
            Check(result.Total == 2);
            Check(result.Midi.GetTimedEvents().Where(t => t.Event is SetTempoEvent).Select(t => t.Time)
                .SequenceEqual(new long[] { 0, 600 }));
            Check(result.Midi.GetNotes().Select(n => n.Time).SequenceEqual(new long[] { 240, 960 }));
            Check(before.SequenceEqual(Bytes(source)));
            var restored = AutoArrangement.Create(source, new[] { true, true }, Default);
            Check(restored.Total == 3 && before.SequenceEqual(Bytes(source)));
            Check(AutoArrangement.Create(source, new[] { false, false }, Default).Total == 0);
        });
        Test("Velocity-zero NoteOn closes a note; percussion is excluded", () =>
        {
            var source = new RawMidi(new TrackChunk(
                new NoteOnEvent((SevenBitNumber)60, (SevenBitNumber)80),
                new NoteOnEvent((SevenBitNumber)60, (SevenBitNumber)0) { DeltaTime = 120 }),
                new[] { N(35, 0, 240, 9) }.ToTrackChunk());
            var result = AutoArrangement.Create(source, new[] { true, true }, Default);
            Check(result.Total == 1 && result.Percussion == 1 && result.DroppedEvents == 0);
            Check(result.Midi.GetNotes().Single().Length == 120);
            Check(result.Midi.GetNotes().All(n => n.Channel != 9));
        });
        Test("Unpaired note events cannot leave keys held", () =>
        {
            var source = new RawMidi(new TrackChunk(new NoteOnEvent((SevenBitNumber)1, (SevenBitNumber)80),
                new NoteOffEvent((SevenBitNumber)2, (SevenBitNumber)0) { DeltaTime = 120 }));
            var result = AutoArrangement.Create(source, new[] { true }, Default);
            Check(result.Total == 0 && result.DroppedEvents == 2);
            Check(!result.Midi.GetTimedEvents().Any(e => e.Event is NoteEvent));
        });
        Test("Empty, percussion-only and corrupt MIDI inputs", () =>
        {
            Check(AutoArrangement.Create(new RawMidi(), Array.Empty<bool>(), Default).Total == 0);
            var drums = new RawMidi(new[] { N(35, 0, 120, 9) }.ToTrackChunk());
            var result = AutoArrangement.Create(drums, new[] { true }, Default);
            Check(result.Total == 0 && result.Percussion == 1);
            Throws<Exception>(() => RawMidi.Read(new MemoryStream(new byte[] { 1, 2, 3, 4 })));
        });
        Test("Merging is repeatable and does not mutate source", () =>
        {
            var source = new RawMidi(new[] { N(60, 0, 480), N(60, 10, 480), N(64, 600, 120) }.ToTrackChunk());
            var before = Bytes(source);
            var first = AutoArrangement.Create(source, new[] { true }, Default, true, 50);
            var second = AutoArrangement.Create(source, new[] { true }, Default, true, 50);
            Check(Bytes(first.Midi).SequenceEqual(Bytes(second.Midi)));
            Check(before.SequenceEqual(Bytes(source)));
            Check(AutoArrangement.Create(source, new[] { true }, Default).Total == 3);
        });
        Test("Reference counts retain shared key until last NoteOff", () =>
        {
            var held = new HeldNotes<int>();
            Check(held.Press(60)); Check(!held.Press(60));
            Check(!held.Release(60)); Check(held.Release(60)); Check(!held.Release(60));
            held.Press(60); held.Press(60); held.Press(64);
            Check(held.Drain().OrderBy(n => n).SequenceEqual(new[] { 60, 64 }));
            Check(held.Drain().Length == 0 && !held.Release(60));
        });
        Test("Collisions and statistics cover all source notes", () =>
        {
            var source = new RawMidi(new[] { N(60, 0, 480), N(60, 120, 600, 1), N(61, 120, 100) }.ToTrackChunk());
            var result = AutoArrangement.Create(source, new[] { true }, Default);
            Check(result.Total == 3 && result.Collisions >= 1);
            Check(result.Midi.GetNotes().Count == result.Total);
            Check(result.Midi.GetNotes().All(n => Default.Contains((int)n.NoteNumber)));
        });
        Test("Transformed MIDI events have balanced lifetimes including zero length", () =>
        {
            var source = new RawMidi(new[] { N(60, 0, 0), N(61, 10, 20), N(60, 20, 100), N(48, 20, 30) }.ToTrackChunk());
            var result = AutoArrangement.Create(source, new[] { true }, Default);
            var held = new HeldNotes<int>();
            foreach (var timed in result.Midi.GetTimedEvents())
                if (timed.Event is NoteEvent note)
                {
                    if (note is NoteOnEvent && note.Velocity > 0) held.Press(note.NoteNumber);
                    else held.Release(note.NoteNumber);
                }
            Check(held.Count == 0, "An output note has no matching release");
        });

        if (args.Length > 0 && args[0] == "--corpus")
            Test("Corpus benchmark", () => Benchmark(args[1], args.Length > 2 ? args[2] : "artifacts/auto-midi-benchmark.json"));
        if (args.Contains("--ui")) Test("WPF integration", UiVerification.Run);
        Console.WriteLine($"RESULT: {_passed} passed, {_failed} failed");
        return _failed == 0 ? 0 : 1;
    }

    private static (int, int, long, int, int) Score(int[] input, int[] allowed, int k)
    {
        int exact = 0, missing = 0;
        long distance = 0;
        foreach (var original in input)
        {
            var n = original - k;
            if (allowed.Contains(n)) { exact++; continue; }
            var eligible = allowed.Where(a => (a - n) % 12 == 0).ToArray();
            if (eligible.Length == 0) missing++;
            else distance += eligible.Min(a => Math.Abs(a - n));
        }
        return (exact, -missing, -distance, -Math.Abs(k), k);
    }

    private static void Benchmark(string root, string output)
    {
        var rows = new List<object>();
        int failures = 0, count = 0, exactFiles = 0, nearFiles = 0;
        var elapsed = Stopwatch.StartNew();
        foreach (var path in Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories)
                     .Where(p => new[] { ".mid", ".midi" }.Contains(Path.GetExtension(p).ToLowerInvariant())).OrderBy(p => p))
        {
            var relative = Path.GetRelativePath(root, path);
            var hash = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path)));
            try
            {
                var midi = RawMidi.Read(path);
                var original = Bytes(midi);
                var selected = midi.GetTrackChunks().Select(_ => true).ToArray();
                foreach (var instrument in Enum.GetValues<Keyboard.Instrument>())
                {
                    var allowed = Keyboard.GetNotes(instrument).ToArray();
                    var timer = Stopwatch.StartNew();
                    var result = AutoArrangement.Create(midi, selected, allowed);
                    var ms = timer.Elapsed.TotalMilliseconds;
                    // v2 may trade accompaniment matches for melody; retain v1 as a measured baseline.
                    Check(result.V1Exact >= result.BaselineExact, "v1 baseline regressed against offset zero");
                    Check(result.Midi.GetNotes().All(n => allowed.Contains((int)n.NoteNumber)), "Invalid output pitch");
                    Check(result.Total == result.Midi.GetNotes().Count, "Note count changed");
                    Check(original.SequenceEqual(Bytes(midi)), "Input mutated");
                    if (instrument == Keyboard.Instrument.WindsongLyre)
                    {
                        if (result.Total > 0 && result.Exact == result.Total) exactFiles++;
                        if (result.Total > 0 && 100L * result.Exact >= 95L * result.Total) nearFiles++;
                    }
                    rows.Add(new
                    {
                        File = relative,
                        Sha256 = hash,
                        Instrument = instrument.ToString(),
                        Mapping = allowed,
                        result.Total,
                        result.Exact,
                        result.Octave,
                        result.Rounded,
                        result.Collisions,
                        result.Percussion,
                        result.DroppedEvents,
                        result.BaselineExact,
                        result.Pitch.Offset,
                        result.MelodyMethod,
                        result.MelodyTotal,
                        result.MelodyExact,
                        result.V1MelodyExact,
                        result.V1Exact,
                        result.V1Offset,
                        Milliseconds = ms
                    });
                }
                count++;
            }
            catch (Exception ex) { failures++; rows.Add(new { File = relative, Sha256 = hash, Error = ex.ToString() }); }
        }
        var report = new
        {
            Algorithm = AutoPitch.Version,
            Parser = typeof(RawMidi).Assembly.FullName,
            Utc = DateTimeOffset.UtcNow,
            Files = count,
            Failures = failures,
            ExactFiles = exactFiles,
            AtLeast95Percent = nearFiles,
            Seconds = elapsed.Elapsed.TotalSeconds,
            Results = rows
        };
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(output))!);
        File.WriteAllText(output, JsonSerializer.Serialize(report, new JsonSerializerOptions { WriteIndented = true }));
        Console.WriteLine($"Corpus: {count} files, {failures} failures; Windsong exact={exactFiles}, >=95%={nearFiles}; {elapsed.Elapsed.TotalSeconds:F2}s");
        Check(count > 0 && failures == 0, "See local benchmark report for failures");
    }

    internal static void Check(bool condition, string message = "Assertion failed")
    { if (!condition) throw new InvalidOperationException(message); }
    private static void Throws<T>(Action action) where T : Exception
    { try { action(); } catch (T) { return; } throw new InvalidOperationException($"Expected {typeof(T).Name}"); }
    private static Note N(int pitch, long time, long length, int channel = 0) =>
        new((SevenBitNumber)pitch, length, time) { Channel = (FourBitNumber)channel };
    private static byte[] Bytes(RawMidi midi)
    { using var stream = new MemoryStream(); midi.Write(stream); return stream.ToArray(); }
    private static void Test(string name, Action action)
    {
        try { action(); _passed++; Console.WriteLine("PASS " + name); }
        catch (Exception ex) { _failed++; Console.WriteLine("FAIL " + name + ": " + ex); }
    }
}
