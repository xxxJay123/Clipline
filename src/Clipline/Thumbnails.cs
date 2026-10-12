using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Windows.Interop;

namespace Clipline;

/// <summary>
/// Reads pictures small enough to hang. Images are decoded at the size they
/// need and no larger: every photo on the line keeps one in memory.
/// Recordings show the frame Windows uses for their thumbnail.
/// </summary>
internal static class Thumbnails
{
    public static Task<BitmapSource?> LoadAsync(string path, int maxPixels) => Task.Run(() => Load(path, maxPixels));

    public static BitmapSource? Load(string path, int maxPixels)
    {
        if (MediaKind.IsImage(path))
        {
            try
            {
                return Decode(path, maxPixels);
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException or NotSupportedException
                or FileFormatException or COMException or ArgumentException or InvalidOperationException)
            {
                // A format Windows Imaging cannot read, like HEIC without its extension: try the shell.
            }
        }
        return Shell(path, maxPixels);
    }

    /// <summary>Pixel size without decoding the whole image.</summary>
    public static (int Width, int Height)? PixelSize(string path)
    {
        try
        {
            using var stream = Open(path);
            var frame = BitmapDecoder.Create(stream, BitmapCreateOptions.DelayCreation, BitmapCacheOption.None).Frames[0];
            return (frame.PixelWidth, frame.PixelHeight);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or NotSupportedException
            or FileFormatException or COMException or ArgumentException or InvalidOperationException)
        {
            return null;
        }
    }

    static BitmapSource Decode(string path, int maxPixels)
    {
        using var stream = Open(path);
        var frame = BitmapDecoder.Create(stream, BitmapCreateOptions.DelayCreation, BitmapCacheOption.None).Frames[0];
        int w = frame.PixelWidth, h = frame.PixelHeight;
        stream.Position = 0;
        var image = new BitmapImage();
        image.BeginInit();
        image.CacheOption = BitmapCacheOption.OnLoad;
        image.CreateOptions = BitmapCreateOptions.IgnoreColorProfile;
        if (w >= h && w > maxPixels) image.DecodePixelWidth = maxPixels;
        else if (h > w && h > maxPixels) image.DecodePixelHeight = maxPixels;
        image.StreamSource = stream;
        image.EndInit();
        image.Freeze();
        return image;
    }

    /// <summary>Shared with whoever is still writing or about to delete it, so reading never gets in their way.</summary>
    static FileStream Open(string path) =>
        new(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete, 1 << 16, FileOptions.SequentialScan);

    /// <summary>
    /// A fingerprint of the pixels. The same capture saved as a file by
    /// Snipping Tool and put on the clipboard has the same one, so it hangs once.
    /// </summary>
    public static string? Fingerprint(BitmapSource image)
    {
        try
        {
            var converted = new FormatConvertedBitmap(image, PixelFormats.Bgr32, null, 0);
            int w = converted.PixelWidth, h = converted.PixelHeight;
            const int grid = 48;
            var sample = new byte[grid * grid * 4 + 8];
            var pixel = new byte[4];
            BitConverter.TryWriteBytes(sample.AsSpan(0, 4), w);
            BitConverter.TryWriteBytes(sample.AsSpan(4, 4), h);
            var o = 8;
            for (var gy = 0; gy < grid; gy++)
            {
                var y = (int)((gy + 0.5) * h / grid);
                for (var gx = 0; gx < grid; gx++)
                {
                    var x = (int)((gx + 0.5) * w / grid);
                    converted.CopyPixels(new Int32Rect(x, y, 1, 1), pixel, 4, 0);
                    sample[o++] = pixel[0];
                    sample[o++] = pixel[1];
                    sample[o++] = pixel[2];
                    sample[o++] = 0;
                }
            }
            return Convert.ToHexString(SHA256.HashData(sample));
        }
        catch (Exception e) when (e is COMException or ArgumentException or InvalidOperationException or NotSupportedException)
        {
            return null;
        }
    }

    /// <summary>The full image, for a fingerprint. Large, so only read once and let go.</summary>
    public static string? FingerprintOf(string path)
    {
        if (!MediaKind.IsImage(path)) return null;
        try
        {
            using var stream = Open(path);
            var frame = BitmapDecoder.Create(stream, BitmapCreateOptions.IgnoreColorProfile, BitmapCacheOption.OnLoad).Frames[0];
            return Fingerprint(frame);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or NotSupportedException
            or FileFormatException or COMException or ArgumentException or InvalidOperationException)
        {
            return null;
        }
    }

    // The shell's own thumbnails: the first frame of a recording, or any image a codec pack can read.

    [ComImport, Guid("bcc18b79-ba16-442f-80c4-8a59c30c463b"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    interface IShellItemImageFactory
    {
        [PreserveSig]
        int GetImage(SIZE size, int flags, out nint bitmap);
    }

    [StructLayout(LayoutKind.Sequential)]
    struct SIZE
    {
        public int cx, cy;
    }

    [DllImport("shell32.dll", CharSet = CharSet.Unicode, PreserveSig = false)]
    static extern void SHCreateItemFromParsingName(string path, nint bindContext,
        [MarshalAs(UnmanagedType.LPStruct)] Guid riid, [MarshalAs(UnmanagedType.Interface)] out IShellItemImageFactory item);

    const int SIIGBF_RESIZETOFIT = 0x0, SIIGBF_BIGGERSIZEOK = 0x1, SIIGBF_THUMBNAILONLY = 0x8;

    static BitmapSource? Shell(string path, int maxPixels)
    {
        BitmapSource? result = null;
        // Thumbnail providers expect a single-threaded apartment.
        var thread = new Thread(() =>
        {
            try
            {
                SHCreateItemFromParsingName(path, 0, typeof(IShellItemImageFactory).GUID, out var factory);
                try
                {
                    var size = new SIZE { cx = maxPixels, cy = maxPixels };
                    if (factory.GetImage(size, SIIGBF_RESIZETOFIT | SIIGBF_BIGGERSIZEOK | SIIGBF_THUMBNAILONLY, out var hbitmap) != 0) return;
                    try
                    {
                        var source = Imaging.CreateBitmapSourceFromHBitmap(hbitmap, 0, Int32Rect.Empty, BitmapSizeOptions.FromEmptyOptions());
                        // The shell hands back premultiplied pixels with no alpha for videos; keep them opaque.
                        var opaque = new FormatConvertedBitmap(source, PixelFormats.Bgr32, null, 0);
                        var copy = new WriteableBitmap(opaque);
                        copy.Freeze();
                        result = copy;
                    }
                    finally
                    {
                        Native.DeleteObject(hbitmap);
                    }
                }
                finally
                {
                    Marshal.ReleaseComObject(factory);
                }
            }
            catch (Exception e) when (e is COMException or FileNotFoundException or UnauthorizedAccessException or ArgumentException or InvalidCastException)
            {
                result = null;
            }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.IsBackground = true;
        thread.Start();
        thread.Join(TimeSpan.FromSeconds(10));
        return result;
    }
}
