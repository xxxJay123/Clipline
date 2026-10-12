namespace Clipline.Core;

/// <summary>What can hang on the line, by file extension.</summary>
public static class MediaKind
{
    static readonly HashSet<string> Images = new(StringComparer.OrdinalIgnoreCase)
    {
        ".png", ".jpg", ".jpeg", ".bmp", ".gif", ".tif", ".tiff", ".webp", ".heic", ".heif", ".jxr",
    };

    static readonly HashSet<string> Recordings = new(StringComparer.OrdinalIgnoreCase)
    {
        ".mp4", ".mov", ".m4v", ".wmv", ".mkv", ".webm",
    };

    public static bool IsImage(string path) => Images.Contains(Path.GetExtension(path));
    public static bool IsRecording(string path) => Recordings.Contains(Path.GetExtension(path));
    public static bool IsSupported(string path) => IsImage(path) || IsRecording(path);

    /// <summary>Half-written files and the temporary names apps save through.</summary>
    public static bool IsTemporary(string path)
    {
        var name = Path.GetFileName(path);
        return name.StartsWith('.') || name.StartsWith("~$", StringComparison.Ordinal)
            || name.EndsWith(".tmp", StringComparison.OrdinalIgnoreCase)
            || name.EndsWith(".partial", StringComparison.OrdinalIgnoreCase)
            || name.EndsWith(".crdownload", StringComparison.OrdinalIgnoreCase);
    }
}

/// <summary>
/// Clipline's own folder. Images copied to the clipboard are saved here, and
/// only files here are sent to the Recycle Bin by the corner cross: anything
/// elsewhere just leaves the line.
/// </summary>
public static class Inbox
{
    /// <summary>
    /// A file belongs to the inbox only if it sits directly inside it. Files in
    /// subfolders, or in a folder whose name merely starts the same, do not.
    /// </summary>
    public static bool Owns(string inbox, string path)
    {
        try
        {
            var parent = Path.GetDirectoryName(Path.GetFullPath(path));
            return parent != null && string.Equals(Trim(parent), Trim(Path.GetFullPath(inbox)), StringComparison.OrdinalIgnoreCase);
        }
        catch (Exception e) when (e is ArgumentException or NotSupportedException or PathTooLongException)
        {
            return false;
        }
    }

    static string Trim(string path) => path.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);

    /// <summary>A name in <paramref name="folder"/> that is not taken yet: "name.png", "name 2.png" and so on.</summary>
    public static string UniquePath(string folder, string fileName)
    {
        var stem = Path.GetFileNameWithoutExtension(fileName);
        var ext = Path.GetExtension(fileName);
        var candidate = Path.Combine(folder, fileName);
        for (var n = 2; File.Exists(candidate) || Directory.Exists(candidate); n++)
            candidate = Path.Combine(folder, $"{stem} {n}{ext}");
        return candidate;
    }

    /// <summary>"Clipboard 2026-10-12 at 20.15.30.png", in the style Windows names screenshots.</summary>
    public static string ClipboardName(DateTime now) => $"Clipboard {now:yyyy-MM-dd} at {now:HH.mm.ss}.png";
}

/// <summary>
/// Decides whether a clipboard change is a picture worth hanging. Only images:
/// anything copied along with text or a file, anything an app marks as
/// private, and Clipline's own copies are left alone.
/// </summary>
public static class ClipboardPolicy
{
    /// <summary>Put on every copy Clipline makes, so it never hangs its own copies again.</summary>
    public const string SelfFormat = "Clipline.Copy";
    public const long MaxBytes = 64L * 1024 * 1024;
    public const long MaxPixels = 80_000_000;

    /// <summary>Password managers and similar apps mark private copies with these.</summary>
    static readonly string[] PrivateFormats =
    [
        "ExcludeClipboardContentFromMonitorProcessing",
        "Clipboard Viewer Ignore",
        SelfFormat,
    ];

    /// <summary>
    /// A picture that comes with these is not just a picture: text from a
    /// document, a file from Explorer, a link from a browser.
    /// </summary>
    static readonly string[] NotJustAPicture =
    [
        "UnicodeText", "Text", "OEMText", "Rich Text Format", "HTML Format",
        "FileDrop", "FileName", "FileNameW", "FileGroupDescriptor", "FileGroupDescriptorW",
        "Shell IDList Array", "UniformResourceLocator", "UniformResourceLocatorW",
        "Csv", "XML Spreadsheet", "Embed Source", "Object Descriptor",
    ];

    static readonly string[] PictureFormats = ["PNG", "image/png", "DeviceIndependentBitmap", "Format17", "Bitmap"];

    /// <param name="formats">The formats on the clipboard.</param>
    /// <param name="allowsHistory">
    /// The value of CanIncludeInClipboardHistory, when present: 0 means the app
    /// asked Windows to keep this copy out of the clipboard history.
    /// </param>
    public static bool IsPicture(IReadOnlyCollection<string> formats, uint? allowsHistory)
    {
        if (formats.Count == 0) return false;
        if (allowsHistory == 0) return false;
        if (formats.Any(f => PrivateFormats.Contains(f, StringComparer.OrdinalIgnoreCase))) return false;
        if (formats.Any(f => NotJustAPicture.Contains(f, StringComparer.OrdinalIgnoreCase))) return false;
        return formats.Any(f => PictureFormats.Contains(f, StringComparer.OrdinalIgnoreCase));
    }

    public static bool IsSaneSize(long width, long height) =>
        width > 0 && height > 0 && width * height <= MaxPixels;
}
