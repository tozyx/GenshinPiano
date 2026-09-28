namespace GenshinPiano.Application.Abstractions;

public interface IMidiOutput : IDisposable
{
    Task PrepareAsync(int program, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        SetInstrument(program);
        return Task.CompletedTask;
    }

    void SetInstrument(int program);

    void SetVolume(int volume);

    void NoteOn(int pitch, int velocity);

    void NoteOff(int pitch);

    void AllNotesOff();
}
