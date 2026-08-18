using System.ComponentModel;
using System.Runtime.InteropServices;
using GenshinPiano.Application.Abstractions;

namespace GenshinPiano.Infrastructure.Input;

public sealed class WindowsMidiOutput : IMidiOutput
{
    private const uint MidiMapper = 0xFFFFFFFF;
    private IntPtr _handle;

    public WindowsMidiOutput()
    {
        var result = MidiOutOpen(out _handle, MidiMapper, IntPtr.Zero, IntPtr.Zero, 0);
        if (result != 0)
        {
            throw new Win32Exception((int)result, "Unable to open the Windows MIDI synthesizer.");
        }
    }

    public void SetInstrument(int program) => Send(0xC0 | (program << 8));

    public void SetVolume(int volume) => Send(0xB0 | (7 << 8) | (Math.Clamp(volume, 0, 127) << 16));

    public void NoteOn(int pitch, int velocity) => Send(0x90 | (pitch << 8) | (velocity << 16));

    public void NoteOff(int pitch) => Send(0x80 | (pitch << 8));

    public void AllNotesOff() => Send(0xB0 | (123 << 8));

    public void Dispose()
    {
        if (_handle == IntPtr.Zero)
        {
            return;
        }

        AllNotesOff();
        MidiOutClose(_handle);
        _handle = IntPtr.Zero;
    }

    private void Send(int message)
    {
        if (_handle != IntPtr.Zero)
        {
            MidiOutShortMsg(_handle, message);
        }
    }

    [DllImport("winmm.dll", EntryPoint = "midiOutOpen")]
    private static extern uint MidiOutOpen(
        out IntPtr handle,
        uint deviceId,
        IntPtr callback,
        IntPtr instance,
        uint flags);

    [DllImport("winmm.dll", EntryPoint = "midiOutShortMsg")]
    private static extern uint MidiOutShortMsg(IntPtr handle, int message);

    [DllImport("winmm.dll", EntryPoint = "midiOutClose")]
    private static extern uint MidiOutClose(IntPtr handle);
}
