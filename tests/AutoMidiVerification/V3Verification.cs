using AutoLyre.Data.Midi;
using System.IO;
using Melanchall.DryWetMidi.Common;
using Melanchall.DryWetMidi.Core;
using Melanchall.DryWetMidi.Interaction;
using RawMidi = Melanchall.DryWetMidi.Core.MidiFile;

internal static class V3Verification
{
    private static Note N(int pitch, long time, long length, int channel = 0) =>
        new((SevenBitNumber)pitch, length, time) { Channel = (FourBitNumber)channel };
    private static byte[] Bytes(RawMidi midi) { using var s = new MemoryStream(); midi.Write(s); return s.ToArray(); }
    internal static void Profiles()
    {
        Program.Check(InstrumentProfiles.All.Count == 12 && InstrumentProfiles.All.Select(p => p.Id).Distinct().Count() == 12);
        Program.Check(InstrumentProfiles.All.Count(p => p.CanPlay) == 5);
        foreach (var p in InstrumentProfiles.All.Where(p => !p.CanPlay))
        {
            try { InstrumentPlan.Create(new RawMidi(), Array.Empty<bool>(), p); throw new Exception("Unverified profile was enabled"); }
            catch (InvalidOperationException) { }
            Program.Check(p.Actions.Count == 0);
        }
        foreach (var p in InstrumentProfiles.All.Take(3))
        {
            var input = new RawMidi(p.Actions.Select((a,i) => N(a.Pitches[0], i*240L, 120)).ToTrackChunk());
            var plan = InstrumentPlan.Create(input, new[] {true}, p);
            Program.Check(plan.Actions.Select(a => a.ActionId).SequenceEqual(p.Actions.Select(a => a.Id)));
            Program.Check(plan.Transport.GetNotes().All(n => p.Actions.Any(a => a.Id == n.NoteNumber)));
        }
    }
    internal static void Drums()
    {
        var raw = new RawMidi(new TrackChunk(new SetTempoEvent(600000)),
            new[] {N(36,0,120,9), N(38,240,120,9), N(60,240,120), N(42,240,120,9)}.ToTrackChunk());
        var bytes = Bytes(raw);
        foreach (var profile in InstrumentProfiles.All.Where(p => p.CanPlay && p.Mode == InstrumentMode.Percussion))
        {
            var plan = InstrumentPlan.Create(raw, new[] {true,true}, profile);
            Program.Check(plan.Actions.Count == 2, "Simultaneous Ka duplicates should coalesce");
            Program.Check(profile.Actions.Single(a => a.Id == plan.Actions[0].ActionId).Kind == SoundKind.Don);
            Program.Check(profile.Actions.Single(a => a.Id == plan.Actions[1].ActionId).Kind == SoundKind.Ka);
            Program.Check(plan.Actions.Select(a => a.Time).SequenceEqual(new long[] {0,240}));
            Program.Check(plan.Transport.GetTimedEvents().Any(e => e.Time == 0 && e.Event is SetTempoEvent t && t.MicrosecondsPerQuarterNote == 600000));
            Program.Check(bytes.SequenceEqual(Bytes(raw)));
            var empty = InstrumentPlan.Create(new RawMidi(new[] {N(60,0,120)}.ToTrackChunk()), new[] {true}, profile);
            Program.Check(empty.Actions.Count == 0, "Pitched notes must not silently become drums");
        }
    }
    private static InstrumentProfile Synthetic(InstrumentMode mode, Articulation articulation, bool chords = false)
    {
        var actions = new[] {60,62,64,65,67,69,71,72}.Select((n,i) =>
            new InstrumentAction(i, "ASDFGHJQ"[i], SoundKind.Note, new[] {n})).ToList();
        if (chords) actions.Add(new(42, 'Z', SoundKind.Chord, new[] {60,64,67}));
        return new("test-only", "Synthetic fixture", mode, articulation, actions, "Synthetic unit test, not a game mapping");
    }
    internal static void ChordsAndVocal()
    {
        var chordProfile = Synthetic(InstrumentMode.Chord, Articulation.Tap, true);
        var chordInput = new RawMidi(new[] {N(60,0,240),N(64,0,240),N(67,0,240)}.ToTrackChunk());
        var plan = InstrumentPlan.Create(chordInput, new[] {true}, chordProfile);
        Program.Check(plan.Actions.Count == 1 && plan.Actions[0].ActionId == 42, "One chord action, not a fake B note");
        var highMelody = InstrumentPlan.Create(new RawMidi(new[] {N(60,0,240),N(64,0,240),N(67,0,240),N(72,0,240)}.ToTrackChunk()), new[] {true}, chordProfile);
        Program.Check(highMelody.Actions.Any(a=>a.ActionId==42) && highMelody.Actions.Any(a=>a.ActionId==7),
            "Chord trigger must not replace the upper-register melody");
        var vocal = Synthetic(InstrumentMode.Vocal, Articulation.Hold);
        var raw = new RawMidi(new[] {N(60,0,480),N(64,120,480),N(67,120,480),N(62,360,240)}.ToTrackChunk());
        var bytes = Bytes(raw);
        var voice = InstrumentPlan.Create(raw, new[] {true}, vocal);
        Program.Check(voice.Actions.Count == 3);
        Program.Check(voice.Actions.Zip(voice.Actions.Skip(1),(a,b) => a.Time + a.Length <= b.Time).All(x=>x));
        Program.Check(bytes.SequenceEqual(Bytes(raw)));
    }
    internal static void Lifetime()
    {
        var sent = new List<string>();
        var output = new InstrumentOutput(k=>sent.Add("down:"+k), k=>sent.Add("up:"+k), k=>sent.Add("tap:"+k));
        var held = Synthetic(InstrumentMode.Melodic, Articulation.Hold);
        output.Send(held, held.Actions[0], true);
        output.Send(held, held.Actions[0], true);
        output.ReleaseAll(); output.ReleaseAll();
        Program.Check(sent.SequenceEqual(new[] {"down:A","up:A","down:A","up:A"}));
        sent.Clear();
        var tapped = Synthetic(InstrumentMode.Melodic, Articulation.Tap);
        output.Send(tapped, tapped.Actions[0], true); output.Send(tapped, tapped.Actions[0], false); output.ReleaseAll();
        Program.Check(sent.SequenceEqual(new[] {"tap:A"}));
        var raw = new RawMidi(new[] {N(60,0,480),N(60,120,480),N(60,120,240)}.ToTrackChunk());
        var plan = InstrumentPlan.Create(raw,new[] {true},held);
        Program.Check(plan.Actions.Count==2 && plan.Actions[0].Time+plan.Actions[0].Length<=plan.Actions[1].Time);
        var boundary = plan.Transport.GetTimedEvents().Where(e=>e.Time==120 && e.Event is NoteEvent).ToArray();
        Program.Check(boundary.First().Event is NoteOffEvent && boundary.Last().Event is NoteOnEvent);
    }
    internal static void Contour()
    {
        var allowed = InstrumentProfiles.All[0].Actions.Select(a=>a.Pitches[0]).ToArray();
        Program.Check(MelodyContour.Map(new[] {60,62,64,65,67},0,allowed).SequenceEqual(new[] {60,62,64,65,67}));
        var result = MelodyContour.Map(new[] {83,84,86,88},0,allowed);
        Program.Check(result.All(allowed.Contains));
        Program.Check(result.Zip(result.Skip(1),(a,b)=>b>a).All(x=>x), "Ascending phrase must not fold down at range boundary");
        try { MelodyContour.Map(new[] {60},0,allowed,new CancellationToken(true)); throw new Exception("Cancellation ignored"); }
        catch (OperationCanceledException) { }
    }
    internal static void Corpus(string root)
    {
        int files=0, plans=0, actions=0;
        foreach(var file in Directory.EnumerateFiles(root,"*",SearchOption.AllDirectories).Where(f=>new[]{".mid",".midi"}.Contains(Path.GetExtension(f).ToLowerInvariant())))
        {
            var source=RawMidi.Read(file); var bytes=Bytes(source);
            var selected=source.GetTrackChunks().Select(_=>true).ToArray();
            foreach(var profile in InstrumentProfiles.All.Where(p=>p.CanPlay))
            {
                var plan=InstrumentPlan.Create(source,selected,profile);
                Program.Check(plan.Actions.All(a=>a.Length>0 && profile.Actions.Any(p=>p.Id==a.ActionId)));
                foreach(var group in plan.Actions.GroupBy(a=>a.ActionId))
                    Program.Check(group.Zip(group.Skip(1),(a,b)=>a.Time+a.Length<=b.Time).All(x=>x));
                Program.Check(bytes.SequenceEqual(Bytes(source)));
                plans++; actions+=plan.Actions.Count;
            }
            files++;
        }
        Program.Check(files>0);
        Console.WriteLine($"V3 corpus: {files} files, {plans} plans, {actions} actions; valid keys, no same-key overlap, source preserved.");
    }
}
