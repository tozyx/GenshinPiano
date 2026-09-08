namespace GenshinPiano.Core.Playback;

public static class PracticeTiming
{
    public static TimeSpan Compensate(TimeSpan position, double inputDelayMs, double speed) =>
        position - TimeSpan.FromMilliseconds(inputDelayMs * speed);

    public static bool IsHit(TimeSpan position, TimeSpan onset, double speed) =>
        (position - onset).Duration() <= Window(speed);

    public static bool IsMissed(TimeSpan position, TimeSpan onset, double speed) =>
        position > onset + Window(speed);

    private static TimeSpan Window(double speed) => TimeSpan.FromMilliseconds(180 * speed);
}
