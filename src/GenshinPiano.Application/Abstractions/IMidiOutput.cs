namespace GenshinPiano.Application.Abstractions;

public interface IMidiOutput : IDisposable
{
    void SetInstrument(int program);

    void SetVolume(int volume);

    void NoteOn(int pitch, int velocity);

    void NoteOff(int pitch);

    void AllNotesOff();
}
