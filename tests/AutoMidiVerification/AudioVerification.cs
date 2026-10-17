using System.IO;
using System.Reflection;
using AutoLyre.App.Audio;
using AutoLyre.Data.Midi;
using Melanchall.DryWetMidi.Core;

internal static class AudioVerification
{
    internal static int Worker(string[] args)
    {
        Console.OutputEncoding = new System.Text.UTF8Encoding(false);
        if (args[1] == "fail") { Console.Error.WriteLine("Expected worker failure"); return 3; }
        if (args[1] == "wait") { Thread.Sleep(30000); return 0; }
        Console.WriteLine(args[2]);
        for (var i=0; i<1000; i++) Console.Error.WriteLine("stderr drain test " + i);
        return 0;
    }
    internal static async Task Processes()
    {
        Directory.CreateDirectory("artifacts/audio-tests");
        var assembly = Assembly.GetExecutingAssembly().Location;
        var weird = "D:\\Piano thử\\bài 'mới' & $(literal).wav";
        var lines = new List<string>();
        await AudioTranscriber.RunProcessAsync("dotnet", new[] {assembly,"--audio-worker","ok",weird},
            "artifacts/audio-tests/process.log", CancellationToken.None, lines.Add);
        Program.Check(lines.SequenceEqual(new[] {weird}), "Argument escaping or Unicode changed");
        try
        {
            await AudioTranscriber.RunProcessAsync("dotnet", new[] {assembly,"--audio-worker","fail"},
                "artifacts/audio-tests/fail.log", CancellationToken.None);
            throw new Exception("Process failure was ignored");
        }
        catch (InvalidOperationException) { }
        using var cancellation = new CancellationTokenSource(500);
        var watch = System.Diagnostics.Stopwatch.StartNew();
        try
        {
            await AudioTranscriber.RunProcessAsync("dotnet", new[] {assembly,"--audio-worker","wait"},
                "artifacts/audio-tests/cancel.log", cancellation.Token);
            throw new Exception("Process cancellation was ignored");
        }
        catch (OperationCanceledException) { Program.Check(watch.Elapsed.TotalSeconds < 10); }
    }
    internal static async Task Real(string path)
    {
        var result = await new AudioTranscriber().ConvertAsync(path, new Progress<string>(Console.WriteLine), CancellationToken.None);
        var midi = Melanchall.DryWetMidi.Core.MidiFile.Read(result);
        var plan = InstrumentPlan.Create(midi, midi.GetTrackChunks().Select(_=>true).ToArray(), InstrumentProfiles.All[0]);
        Program.Check(plan.Actions.Count > 0);
        Console.WriteLine($"Real audio conversion: {plan.Actions.Count} playable actions; MIDI: {result}");
    }
}
