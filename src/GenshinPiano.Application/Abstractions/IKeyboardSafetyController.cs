namespace GenshinPiano.Application.Abstractions;

public interface IKeyboardSafetyController
{
    void ReleasePressedKeys();

    void EmergencyReleaseAllKeys();
}
