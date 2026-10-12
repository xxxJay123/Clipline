using System.Media;
using Microsoft.Win32;

namespace Clipline;

/// <summary>
/// Two tiny sounds, made in code: a light tink when a photo is pegged on the
/// line and a soft pop when one comes off. Sending a file to the Recycle Bin
/// plays the sound Windows uses for emptying it.
/// </summary>
internal static class Sounds
{
    public static bool Enabled { get; set; } = true;

    static readonly SoundPlayer TinkPlayer = new(new MemoryStream(Tone(0.18, t =>
        0.35 * Math.Exp(-t / 0.045) * (0.7 * Math.Sin(2 * Math.PI * 2093 * t) + 0.3 * Math.Sin(2 * Math.PI * 3136 * t)))));

    static readonly SoundPlayer PopPlayer = new(new MemoryStream(Tone(0.12, t =>
    {
        // A short downward sweep, like a clip letting go.
        var phase = 2 * Math.PI * (380 * t - 900 * t * t);
        return 0.28 * Math.Exp(-t / 0.03) * Math.Sin(phase);
    })));

    public static void Tink() => Play(TinkPlayer);
    public static void Pop() => Play(PopPlayer);

    public static void Recycle()
    {
        if (!Enabled) return;
        var file = RecycleSound();
        if (file != null) Native.PlaySound(file, 0, Native.SND_FILENAME | Native.SND_ASYNC | Native.SND_NODEFAULT);
    }

    static void Play(SoundPlayer player)
    {
        if (!Enabled) return;
        try
        {
            player.Play();
        }
        catch (Exception e) when (e is InvalidOperationException or TimeoutException)
        {
            // No audio device: stay quiet.
        }
    }

    static string? RecycleSound()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(@"AppEvents\Schemes\Apps\Explorer\EmptyRecycleBin\.Current");
            var value = key?.GetValue(null) as string;
            if (string.IsNullOrWhiteSpace(value)) return null;
            var path = Environment.ExpandEnvironmentVariables(value);
            return File.Exists(path) ? path : null;
        }
        catch (Exception e) when (e is System.Security.SecurityException or UnauthorizedAccessException or IOException)
        {
            return null;
        }
    }

    /// <summary>A mono 16-bit WAV of the given wave, with a 2 ms fade in so it never clicks.</summary>
    static byte[] Tone(double seconds, Func<double, double> wave)
    {
        const int rate = 44100;
        var samples = (int)(seconds * rate);
        using var stream = new MemoryStream();
        using var w = new BinaryWriter(stream);
        w.Write("RIFF"u8);
        w.Write(36 + samples * 2);
        w.Write("WAVEfmt "u8);
        w.Write(16);
        w.Write((short)1);
        w.Write((short)1);
        w.Write(rate);
        w.Write(rate * 2);
        w.Write((short)2);
        w.Write((short)16);
        w.Write("data"u8);
        w.Write(samples * 2);
        for (var i = 0; i < samples; i++)
        {
            var t = (double)i / rate;
            var fade = Math.Min(1, t / 0.002) * Math.Min(1, (seconds - t) / 0.01);
            w.Write((short)Math.Clamp(wave(t) * fade * short.MaxValue, short.MinValue, short.MaxValue));
        }
        w.Flush();
        return stream.ToArray();
    }
}
