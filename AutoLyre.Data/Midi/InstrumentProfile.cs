using System;
using System.Collections.Generic;
using System.Linq;

namespace AutoLyre.Data.Midi;

public enum InstrumentMode { Melodic, Chord, Percussion, Vocal }
public enum Articulation { Tap, Hold }
public enum SoundKind { Note, Chord, Ka, Don }

/// <summary>Id is a scheduler token, never a sounding MIDI pitch.</summary>
public sealed record InstrumentAction(int Id, char Key, SoundKind Kind, IReadOnlyList<int> Pitches);

public sealed class InstrumentProfile
{
    public InstrumentProfile(string id, string name, InstrumentMode mode, Articulation articulation,
        IEnumerable<InstrumentAction> actions, string evidence, string? blockedReason = null)
    {
        Id = id; Name = name; Mode = mode; Articulation = articulation;
        Actions = Array.AsReadOnly(actions.Select(a => a with { Pitches = Array.AsReadOnly(a.Pitches.ToArray()) }).ToArray());
        Evidence = evidence; BlockedReason = blockedReason;
        if (Actions.Any(a => a.Id is < 0 or > 127 || a.Key is < 'A' or > 'Z'
                || a.Pitches.Any(p => p is < 0 or > 127))
            || Actions.Select(a => a.Id).Distinct().Count() != Actions.Count
            || Actions.Select(a => a.Key).Distinct().Count() != Actions.Count)
            throw new ArgumentException("Invalid or duplicate instrument action.");
        if (blockedReason is null && (Actions.Count == 0 || string.IsNullOrWhiteSpace(evidence)))
            throw new ArgumentException("Enabled profiles require actions and evidence.");
        if (blockedReason is null && Actions.Any(a => mode == InstrumentMode.Percussion
                ? a.Kind is not (SoundKind.Ka or SoundKind.Don) || a.Pitches.Count != 0
                : a.Kind == SoundKind.Note ? a.Pitches.Count != 1
                : mode != InstrumentMode.Chord || a.Kind != SoundKind.Chord || a.Pitches.Count < 2))
            throw new ArgumentException("Actions do not match the instrument mode.");
    }
    public string Id { get; }
    public string Name { get; }
    public InstrumentMode Mode { get; }
    public Articulation Articulation { get; }
    public IReadOnlyList<InstrumentAction> Actions { get; }
    public string Evidence { get; }
    public string? BlockedReason { get; }
    public bool CanPlay => BlockedReason is null;
    public string DisplayName => CanPlay ? Name : Name + " · Chưa hỗ trợ";
    public void RequirePlayable()
    {
        if (!CanPlay) throw new InvalidOperationException(BlockedReason);
    }
}

public static class InstrumentProfiles
{
    private static readonly int[] Major = {48,50,52,53,55,57,59,60,62,64,65,67,69,71,72,74,76,77,79,81,83};
    private static readonly int[] Vintage = {48,50,51,53,55,57,58,60,62,63,65,67,69,70,72,73,75,77,79,80,82};
    private static InstrumentProfile Melodic(string id, string name, int[] notes, string source) => new(
        id, name, InstrumentMode.Melodic, Articulation.Tap,
        notes.Select((n,i) => new InstrumentAction(i, "ZXCVBNMASDFGHJQWERTYU"[i], SoundKind.Note, new[] {n})), source);
    private static InstrumentProfile Pending(string id, string name, InstrumentMode mode, string reason,
        Articulation articulation = Articulation.Tap) => new(id, name, mode, articulation,
        Array.Empty<InstrumentAction>(), "docs/auto-lyre-instruments-7.1-provisional.json", reason);
    private static InstrumentProfile Drum(string id, string name, string source) => new(id, name,
        InstrumentMode.Percussion, Articulation.Tap, new[] {
            new InstrumentAction(0, 'A', SoundKind.Ka, Array.Empty<int>()),
            new InstrumentAction(1, 'S', SoundKind.Don, Array.Empty<int>()) }, source);

    public static IReadOnlyList<InstrumentProfile> All { get; } = Array.AsReadOnly(new[] {
        Melodic("windsong_lyre", "Đàn Cầm Của Gió", Major, "https://genshin-impact.fandom.com/wiki/Windsong_Lyre"),
        Melodic("floral_zither", "Đàn Hoa", Major, "https://genshin-impact.fandom.com/wiki/Floral_Zither"),
        Melodic("vintage_lyre", "Đàn Lyre Cũ", Vintage, "https://genshin-impact.fandom.com/wiki/Vintage_Lyre"),
        Pending("nightwind_horn", "Tù Và Gió Đêm", InstrumentMode.Melodic, "Chưa xác minh đủ cao độ của Tù Và Gió Đêm.", Articulation.Hold),
        Pending("ukulele", "Ukulele", InstrumentMode.Chord, "Chưa xác minh phím và âm thanh hợp âm của Ukulele."),
        Pending("lingering_euphonia", "Dư Âm", InstrumentMode.Chord, "Chưa xác minh phím và âm thanh hợp âm của Dư Âm."),
        Pending("leaping_spirit_piano", "Đàn Piano Linh Hồn Nhảy Múa", InstrumentMode.Melodic, "Chưa xác minh cao độ và cách giữ âm của đàn piano."),
        Pending("harmonic_keys", "Đàn Hòa Âm", InstrumentMode.Melodic, "Chưa xác minh cao độ và cách giữ âm của Đàn Hòa Âm."),
        Drum("arataki_drum", "Trống Arataki", "https://genshin-impact.fandom.com/wiki/Arataki%27s_Great_and_Glorious_Drum"),
        Drum("festive_drum", "Trống Lễ Hội", "https://genshin-impact.fandom.com/wiki/Festive_Drum"),
        Pending("djem_djem_drum", "Trống Djem Djem", InstrumentMode.Percussion, "Chưa xác minh phím gõ của Trống Djem Djem."),
        Pending("vodyanitsa", "Vodyanitsa · Giọng hát", InstrumentMode.Vocal, "Chưa xác minh 14 cao độ và cách giữ giọng của Vodyanitsa.", Articulation.Hold)
    });
    public static InstrumentProfile Get(string id) => All.Single(p => p.Id == id);
}
