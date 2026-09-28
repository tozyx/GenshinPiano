namespace GenshinPiano.Application.Abstractions;

public interface ISampleAuditionOutput
{
    Task PrepareAsync(int instrument, CancellationToken cancellationToken = default) =>
        Task.CompletedTask;

    void SetVolume(int volume);
    void NoteOn(int instrument, int pitch, int velocity);
    void NoteOff(int pitch);
    void AllNotesOff();
}
