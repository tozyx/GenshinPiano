using System.Collections.Concurrent;
using System.IO;
using System.Media;

namespace GenshinPiano.App.Services;

internal static class PracticeHitSound
{
    private static int _volume = 75;
    private static readonly ConcurrentDictionary<int, byte[]> Waves = new();

    public static void SetVolume(int volume) => Volatile.Write(ref _volume, Math.Clamp(volume, 0, 100));

    public static async void Play()
    {
        try
        {
            var volume = Volatile.Read(ref _volume);
            if (volume <= 0) return;
            var wave = Waves.GetOrAdd(volume, value => CreateWave(value / 100d));
            var stream = new MemoryStream(wave, writable: false);
            var player = new SoundPlayer(stream);
            player.Play();
            await Task.Delay(150);
            player.Dispose();
            stream.Dispose();
        }
        catch (Exception exception)
        {
            AppLogger.Warning($"Practice hit sound failed: {exception.Message}");
        }
    }

    private static byte[] CreateWave(double volume)
    {
        const int rate = 44100;
        const int samples = 5292;
        using var stream = new MemoryStream();
        using (var writer = new BinaryWriter(stream, System.Text.Encoding.ASCII, true))
        {
            writer.Write("RIFF"u8.ToArray());
            writer.Write(36 + samples * 2);
            writer.Write("WAVEfmt "u8.ToArray());
            writer.Write(16);
            writer.Write((short)1);
            writer.Write((short)1);
            writer.Write(rate);
            writer.Write(rate * 2);
            writer.Write((short)2);
            writer.Write((short)16);
            writer.Write("data"u8.ToArray());
            writer.Write(samples * 2);
            for (var index = 0; index < samples; index++)
            {
                var time = index / (double)rate;
                var attack = Math.Min(1, index / 32d);
                var fundamental = Math.Sin(2 * Math.PI * 880 * time) * Math.Exp(-time * 34);
                var overtone = Math.Sin(2 * Math.PI * 1760 * time + .35) * Math.Exp(-time * 58) * .34;
                var sparkle = Math.Sin(2 * Math.PI * 2640 * time + .8) * Math.Exp(-time * 92) * .13;
                var transient = index < 180
                    ? (((index * 1103515245L + 12345) & 0xffff) / 32767.5 - 1) *
                      Math.Exp(-time * 180) * .08
                    : 0;
                var perceptualGain = Math.Pow(Math.Clamp(volume, 0, 1), .55);
                var sample = (fundamental + overtone + sparkle + transient) * attack * 15500 * perceptualGain;
                writer.Write((short)Math.Clamp(sample, short.MinValue, short.MaxValue));
            }
        }
        return stream.ToArray();
    }
}
