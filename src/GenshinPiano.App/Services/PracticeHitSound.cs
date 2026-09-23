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
        // A short, muted wood-block style hit. Keeping the body below 1 kHz and
        // filtering the transient makes repeated rhythm-game taps clear without
        // the sharp, full bell character of the previous sound.
        const int samples = 3969;
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
            double filteredNoise = 0;
            uint noiseState = 0x6d2b79f5;
            for (var index = 0; index < samples; index++)
            {
                var time = index / (double)rate;
                var attack = Math.Min(1, index / 18d);
                var body = Math.Sin(2 * Math.PI * 410 * time) * Math.Exp(-time * 43) * .72;
                var woodyResonance = Math.Sin(2 * Math.PI * 615 * time + .22) *
                                      Math.Exp(-time * 64) * .24;
                var lowerBody = Math.Sin(2 * Math.PI * 255 * time + .7) *
                                Math.Exp(-time * 38) * .13;

                noiseState = noiseState * 1664525u + 1013904223u;
                var rawNoise = (noiseState / (double)uint.MaxValue) * 2 - 1;
                filteredNoise += (rawNoise - filteredNoise) * .18;
                var transient = filteredNoise * Math.Exp(-time * 190) * .12;
                var perceptualGain = Math.Pow(Math.Clamp(volume, 0, 1), .55);
                var sample = (body + woodyResonance + lowerBody + transient) *
                             attack * 14200 * perceptualGain;
                writer.Write((short)Math.Clamp(sample, short.MinValue, short.MaxValue));
            }
        }
        return stream.ToArray();
    }
}
