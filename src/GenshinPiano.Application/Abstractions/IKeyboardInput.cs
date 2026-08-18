using GenshinPiano.Core.Playback;

namespace GenshinPiano.Application.Abstractions;

public interface IKeyboardInput
{
    void KeyDown(IReadOnlyList<GenshinKey> keys);

    void KeyUp(IReadOnlyList<GenshinKey> keys);
}
