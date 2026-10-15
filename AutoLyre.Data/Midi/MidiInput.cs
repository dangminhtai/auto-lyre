namespace AutoLyre.Data.Midi;

public class MidiInput
{
    public MidiInput(string deviceName) { DeviceName = deviceName; }

    public string DeviceName { get; }
}