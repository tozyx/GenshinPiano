namespace GenshinPiano.Application.Abstractions;

public interface IPlaybackFocusGuard
{
    bool IsPlaybackTargetFocused();

    bool TryFocusFirstPlaybackTarget() => false;
}
