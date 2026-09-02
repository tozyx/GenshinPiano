namespace GenshinPiano.Application.Abstractions;

public interface ISampleAuditionOutput
{
    void SetVolume(int volume);
    void NoteOn(int instrument, int pitch, int velocity);
    void NoteOff(int pitch);
    void AllNotesOff();
}
